using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ferrite.App.Localization;
using Ferrite.Core.Java;
using Ferrite.Core.Minecraft;
using Ferrite.Core.Rules;

namespace Ferrite.App.ViewModels;

/// <summary>
/// The instance's local server: prepare it, start it, stop it, and export it. Preparing downloads the
/// server jar for the instance's own Minecraft version, so a modded instance still gets the version
/// its mods were built for.
/// </summary>
public sealed partial class InstanceDetailViewModel
{
    private LocalServerLayout? _serverLayout;

    [ObservableProperty]
    private string _serverLevelName = "world";

    [ObservableProperty]
    private int? _serverMaxPlayers = 20;

    [ObservableProperty]
    private int? _serverPort = 25565;

    [ObservableProperty]
    private bool _serverOnlineMode = true;

    [ObservableProperty]
    private string _serverMotd = "A Ferrite server";

    [ObservableProperty]
    private int? _serverMemoryMb = 2048;

    [ObservableProperty]
    private bool _serverAcceptEula;

    [ObservableProperty]
    private string? _serverStatus;

    [ObservableProperty]
    private bool _isServerRunning;

    [ObservableProperty]
    private bool _isServerBusy;

    public bool HasServerStatus => !string.IsNullOrEmpty(ServerStatus);

    public bool CanStartServer => !IsServerRunning && !IsServerBusy;

    partial void OnServerStatusChanged(string? value) => OnPropertyChanged(nameof(HasServerStatus));

    partial void OnIsServerRunningChanged(bool value) => OnPropertyChanged(nameof(CanStartServer));

    partial void OnIsServerBusyChanged(bool value) => OnPropertyChanged(nameof(CanStartServer));

    /// <summary>Reads the server's own file, so the page shows what the server would actually use.</summary>
    private void LoadServerOptions()
    {
        var propertiesPath = Path.Combine(
            _services.LocalServers.ServerDirectory(Record.Id),
            LocalServerService.PropertiesName);
        if (!File.Exists(propertiesPath))
        {
            ServerMemoryMb = Record.MemoryMb ?? 2048;
            return;
        }

        try
        {
            var options = LocalServerService.ReadOptions(
                ServerPropertiesDocument.Parse(File.ReadAllText(propertiesPath)));
            ServerLevelName = options.LevelName;
            ServerMaxPlayers = options.MaxPlayers;
            ServerPort = options.Port;
            ServerOnlineMode = options.OnlineMode;
            ServerMotd = options.Motd;
            ServerMemoryMb = Record.MemoryMb ?? 2048;
            ServerAcceptEula = File.Exists(Path.Combine(
                _services.LocalServers.ServerDirectory(Record.Id),
                LocalServerService.EulaName))
                && File.ReadAllText(Path.Combine(
                    _services.LocalServers.ServerDirectory(Record.Id),
                    LocalServerService.EulaName))
                    .Contains("eula=true", StringComparison.OrdinalIgnoreCase);
        }
        catch (IOException exception)
        {
            ServerStatus = exception.Message;
        }
    }

    private LocalServerOptions CurrentServerOptions() => new()
    {
        LevelName = string.IsNullOrWhiteSpace(ServerLevelName) ? "world" : ServerLevelName.Trim(),
        MaxPlayers = Math.Clamp(ServerMaxPlayers ?? 20, 1, 1000),
        Port = Math.Clamp(ServerPort ?? 25565, 1, 65535),
        OnlineMode = ServerOnlineMode,
        Motd = ServerMotd ?? string.Empty,
        MemoryMb = Math.Clamp(ServerMemoryMb ?? 2048, 512, 65536),
        AcceptEula = ServerAcceptEula,
    };

    /// <summary>Downloads the server jar and writes the settings, without starting anything.</summary>
    [RelayCommand]
    private async Task PrepareServerAsync()
    {
        if (IsServerBusy)
        {
            return;
        }

        IsServerBusy = true;
        _shell.BeginActivity(Localizer.Get("L.Instance.ServerPreparing"));
        try
        {
            var document = await _services.Resolver
                .ResolveAsync(Record.MinecraftVersion, CancellationToken.None)
                .ConfigureAwait(true);
            _serverLayout = await _services.LocalServers
                .PrepareAsync(
                    Record.Id,
                    document,
                    CurrentServerOptions(),
                    new Progress<InstallProgress>(_shell.ReportActivity),
                    CancellationToken.None)
                .ConfigureAwait(true);
            ServerStatus = Localizer.Format("L.Instance.ServerPrepared", _serverLayout.Directory);
        }
        catch (Exception exception)
        {
            ServerStatus = exception.Message;
        }
        finally
        {
            IsServerBusy = false;
            _shell.EndActivity();
        }
    }

    [RelayCommand]
    private async Task StartServerAsync()
    {
        if (IsServerBusy || IsServerRunning)
        {
            return;
        }

        IsServerBusy = true;
        try
        {
            // Always prepare first: the jar may be missing, and the settings may have been edited
            // since the last prepare.
            var document = await _services.Resolver
                .ResolveAsync(Record.MinecraftVersion, CancellationToken.None)
                .ConfigureAwait(true);
            var options = CurrentServerOptions();
            _serverLayout = await _services.LocalServers
                .PrepareAsync(
                    Record.Id,
                    document,
                    options,
                    new Progress<InstallProgress>(_shell.ReportActivity),
                    CancellationToken.None)
                .ConfigureAwait(true);

            var required = JavaCompatibility.RequiredMajorFor(document, Record.MinecraftVersion);
            var runtimes = await _services.Java.DetectAsync(CancellationToken.None).ConfigureAwait(true);
            var java = SelectedJava ?? JavaSelection.SelectBest(runtimes, required);
            if (java is null)
            {
                ServerStatus = Localizer.Get("L.Instance.ServerNoJava");
                return;
            }

            var command = LocalServerService.BuildCommand(_serverLayout, java, options, Record.JvmArguments);
            var process = await _services.Launcher
                .StartAsync(Record.Id, command, CancellationToken.None)
                .ConfigureAwait(true);

            IsServerRunning = true;
            ServerStatus = Localizer.Format("L.Instance.ServerRunning", process.ProcessId);
            process.Exited += exitCode =>
            {
                IsServerRunning = false;
                ServerStatus = Localizer.Format("L.Instance.ServerExited", exitCode);
            };
        }
        catch (Exception exception)
        {
            ServerStatus = exception.Message;
        }
        finally
        {
            IsServerBusy = false;
        }
    }

    [RelayCommand]
    private async Task StopServerAsync()
    {
        try
        {
            await _services.Launcher
                .StopAsync(Record.Id, TimeSpan.FromSeconds(20), CancellationToken.None)
                .ConfigureAwait(true);
            IsServerRunning = false;
            ServerStatus = Localizer.Get("L.Instance.ServerStopped");
        }
        catch (Exception exception)
        {
            ServerStatus = exception.Message;
        }
    }

    /// <summary>Zips the prepared server, world included, to the path the picker returned.</summary>
    public async Task<bool> ExportServerAsync(string? outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return false;
        }

        try
        {
            _serverLayout ??= new LocalServerLayout(
                _services.LocalServers.ServerDirectory(Record.Id),
                Path.Combine(_services.LocalServers.ServerDirectory(Record.Id), LocalServerService.ServerJarName),
                Path.Combine(_services.LocalServers.ServerDirectory(Record.Id), LocalServerService.PropertiesName),
                Path.Combine(_services.LocalServers.ServerDirectory(Record.Id), LocalServerService.EulaName),
                ServerLevelName ?? "world");

            var produced = await _services.LocalServers
                .ExportAsync(_serverLayout, outputPath, CancellationToken.None)
                .ConfigureAwait(true);
            ServerStatus = Localizer.Format("L.Instance.ServerExported", produced);
            return true;
        }
        catch (Exception exception)
        {
            ServerStatus = exception.Message;
            return false;
        }
    }
}
