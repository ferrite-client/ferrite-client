using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ferrite.App.Services;
using Ferrite.Core.Content;
using Ferrite.Core.Diagnostics;
using Ferrite.Core.Minecraft;
using Ferrite.Core.Platform;
using Ferrite.Core.Storage;
using Ferrite.Core.Update;
using Ferrite.Core.Util;

namespace Ferrite.App.ViewModels;

/// <summary>Launcher settings, storage management, and the metadata cache.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly MainWindowViewModel _shell;

    public SettingsViewModel(AppServices services, MainWindowViewModel shell)
    {
        _services = services;
        _shell = shell;
        RefreshCurseForgeKeyStatus();
    }

    public IReadOnlyList<ThemeVariant> Themes { get; } =
    [
        ThemeVariant.Dark,
        ThemeVariant.Light,
        ThemeVariant.System,
    ];

    public IReadOnlyList<string> Languages { get; } = ["en", "pl"];

    [ObservableProperty]
    private ThemeVariant _theme = ThemeVariant.Dark;

    [ObservableProperty]
    private string _language = "en";

    [ObservableProperty]
    private int _maxConcurrentDownloads = 8;

    [ObservableProperty]
    private string? _proxyUrl;

    [ObservableProperty]
    private string? _microsoftClientId;

    [ObservableProperty]
    private bool _showSnapshots;

    /// <summary>A key the user is entering. The stored key is never read back into the UI.</summary>
    [ObservableProperty]
    private string? _newCurseForgeApiKey;

    [ObservableProperty]
    private string _curseForgeKeyStatus = string.Empty;

    /// <summary>Most recent launcher operations, newest first.</summary>
    public ObservableCollection<OperationEntry> RecentOperations { get; } = [];

    [ObservableProperty]
    private string? _diagnosticsNotes;

    [ObservableProperty]
    private string? _diagnosticsStatus;

    [ObservableProperty]
    private string? _updateFeedUrl;

    [ObservableProperty]
    private bool _checkForUpdatesOnStartup;

    [ObservableProperty]
    private string? _updateStatus;

    [ObservableProperty]
    private string? _updateHandoffCommand;

    private UpdateCheckResult? _pendingUpdate;

    [ObservableProperty]
    private string _dataRoot = string.Empty;

    [ObservableProperty]
    private string _cacheSizeText = string.Empty;

    [ObservableProperty]
    private string? _statusNote;

    [ObservableProperty]
    private bool _isBusy;

    public async Task LoadAsync()
    {
        var settings = _services.Settings.Current;
        Theme = settings.Theme;
        Language = settings.Language;
        MaxConcurrentDownloads = settings.MaxConcurrentDownloads;
        ProxyUrl = settings.ProxyUrl;
        MicrosoftClientId = settings.MicrosoftClientId;
        ShowSnapshots = settings.ShowSnapshotsInVersionList;
        UpdateFeedUrl = settings.UpdateFeedUrl;
        CheckForUpdatesOnStartup = settings.CheckForUpdatesOnStartup;
        DataRoot = _services.Paths.Root;
        NewCurseForgeApiKey = null;
        RefreshCurseForgeKeyStatus();
        RefreshOperations();
        await RefreshCacheSizeAsync().ConfigureAwait(true);
    }

    private void RefreshOperations()
    {
        RecentOperations.Clear();
        foreach (var entry in _services.Operations.Recent(12))
        {
            RecentOperations.Add(entry);
        }

        OnPropertyChanged(nameof(HasRecentOperations));
    }

    public bool HasRecentOperations => RecentOperations.Count > 0;

    [RelayCommand]
    private void RefreshOperationsCommand() => RefreshOperations();

    /// <summary>
    /// Writes a support bundle. Called by the view after the user picks a target file, so the view
    /// model never touches platform storage APIs.
    /// </summary>
    public async Task ExportDiagnosticsAsync(string outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return;
        }

        IsBusy = true;
        try
        {
            _shell.BeginActivity("Building diagnostics bundle...");
            var result = await _services.Diagnostics
                .ExportAsync(
                    new DiagnosticsBundleRequest
                    {
                        OutputPath = outputPath,
                        Notes = string.IsNullOrWhiteSpace(DiagnosticsNotes) ? null : DiagnosticsNotes.Trim(),
                    },
                    CancellationToken.None)
                .ConfigureAwait(true);

            DiagnosticsStatus =
                $"Wrote {result.FileCount} file(s), {ByteSize.Format(result.Bytes)} to {Path.GetFileName(result.Path)}";
            _shell.ReportStatus(DiagnosticsStatus);
        }
        catch (Exception exception)
        {
            _shell.ReportError(exception.Message);
        }
        finally
        {
            IsBusy = false;
            _shell.EndActivity();
        }
    }

    private void RefreshCurseForgeKeyStatus()
    {
        if (!_services.Credentials.HasCurseForgeApiKey)
        {
            CurseForgeKeyStatus = "No key stored. CurseForge browsing and modpack installs stay disabled.";
            return;
        }

        CurseForgeKeyStatus = _services.Credentials.IsDegraded
            ? "Key stored with file permissions only (this platform has no OS-backed protection)."
            : "Key stored and protected by the operating system.";
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var settings = _services.Settings.Current;
        settings.Theme = Theme;
        settings.Language = Language;
        settings.MaxConcurrentDownloads = Math.Clamp(MaxConcurrentDownloads, 1, 64);
        settings.ProxyUrl = string.IsNullOrWhiteSpace(ProxyUrl) ? null : ProxyUrl.Trim();
        settings.MicrosoftClientId = string.IsNullOrWhiteSpace(MicrosoftClientId) ? null : MicrosoftClientId.Trim();
        settings.ShowSnapshotsInVersionList = ShowSnapshots;
        settings.UpdateFeedUrl = string.IsNullOrWhiteSpace(UpdateFeedUrl) ? null : UpdateFeedUrl.Trim();
        settings.CheckForUpdatesOnStartup = CheckForUpdatesOnStartup;

        await _services.Settings.SaveAsync(CancellationToken.None).ConfigureAwait(true);

        if (!string.IsNullOrWhiteSpace(NewCurseForgeApiKey))
        {
            _services.Credentials.CurseForgeApiKey = NewCurseForgeApiKey;
            _services.Credentials.Save();
            NewCurseForgeApiKey = null;
            RefreshCurseForgeKeyStatus();
        }

        StatusNote = "Settings saved";
        _shell.ReportStatus(StatusNote);
    }

    [RelayCommand]
    private void ClearCurseForgeKey()
    {
        _services.Credentials.CurseForgeApiKey = null;
        _services.Credentials.Save();
        NewCurseForgeApiKey = null;
        RefreshCurseForgeKeyStatus();
        StatusNote = "CurseForge API key removed";
    }

    [RelayCommand]
    private void OpenDataFolder() => ShellOpen.Directory(_services.Paths.Root);

    /// <summary>
    /// Checks the configured feed. Every failure mode is reported as text: an unsigned feed, an
    /// unreachable host, and a feed that publishes nothing for this machine all stay distinct.
    /// </summary>
    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        _pendingUpdate = null;
        UpdateHandoffCommand = null;

        if (string.IsNullOrWhiteSpace(UpdateFeedUrl))
        {
            UpdateStatus = "No update feed is configured.";
            return;
        }

        IsBusy = true;
        try
        {
            _shell.BeginActivity("Checking for launcher updates...");
            var current = typeof(SettingsViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
            var result = await _services.Updates
                .CheckAsync(UpdateFeedUrl.Trim(), current, PlatformInfo.CurrentRuntimeIdentifier(), CancellationToken.None)
                .ConfigureAwait(true);

            _pendingUpdate = result;
            var version = result.Manifest.Version;
            if (!result.IsNewer)
            {
                UpdateStatus = $"Ferrite {current} is the newest release on this feed.";
            }
            else if (result.Package is null)
            {
                UpdateStatus = $"Ferrite {version} is available, but the feed publishes no build for this machine.";
            }
            else
            {
                UpdateStatus = $"Ferrite {version} is available ({ByteSize.Format(result.Package.Size)}).";
            }

            _shell.ReportStatus(UpdateStatus);
        }
        catch (Exception exception)
        {
            UpdateStatus = exception.Message;
            _shell.ReportError(exception.Message);
        }
        finally
        {
            IsBusy = false;
            _shell.EndActivity();
        }
    }

    /// <summary>
    /// Downloads and verifies the update, then prints the command that applies it. The launcher does
    /// not run the hand-off itself: replacing a running executable is the user's call.
    /// </summary>
    [RelayCommand]
    private async Task StageUpdateAsync()
    {
        if (_pendingUpdate is not { IsNewer: true } check || check.Package is null)
        {
            UpdateStatus = "Check for updates first.";
            return;
        }

        IsBusy = true;
        try
        {
            _shell.BeginActivity($"Downloading Ferrite {check.Manifest.Version}...");
            var stage = await _services.Updates
                .StageAsync(
                    check,
                    new Progress<InstallProgress>(_shell.ReportActivity),
                    CancellationToken.None)
                .ConfigureAwait(true);

            var installDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            UpdateHandoffCommand = UpdateHandoff.BuildCommandLine(
                stage,
                Environment.ProcessId,
                installDirectory);
            UpdateStatus =
                $"Ferrite {stage.Version} is staged and verified ({ByteSize.Format(stage.Bytes)}). "
                + "Close Ferrite, then run the command below.";
            _shell.ReportStatus(UpdateStatus);
        }
        catch (Exception exception)
        {
            UpdateStatus = exception.Message;
            _shell.ReportError(exception.Message);
        }
        finally
        {
            IsBusy = false;
            _shell.EndActivity();
        }
    }

    [RelayCommand]
    private void OpenStagingFolder() => ShellOpen.Directory(_services.Paths.UpdateStagingDirectory);

    [RelayCommand]
    private void OpenLogsFolder() => ShellOpen.Directory(_services.Paths.LauncherLogsDirectory);

    [RelayCommand]
    private async Task RefreshCacheSizeAsync()
    {
        var cache = _services.Paths.CacheDirectory;
        var store = _services.Paths.StoreDirectory;
        CacheSizeText = ByteSize.Format(
            await Task.Run(() =>
                    InstanceContentManager.GetDirectorySize(cache) + InstanceContentManager.GetDirectorySize(store))
                .ConfigureAwait(true));
    }

    [RelayCommand]
    private async Task ClearMetadataCacheAsync()
    {
        IsBusy = true;
        try
        {
            var cache = _services.Paths.CacheDirectory;
            if (Directory.Exists(cache))
            {
                await Task.Run(() => Directory.Delete(cache, recursive: true)).ConfigureAwait(true);
            }

            Directory.CreateDirectory(cache);
            StatusNote = "Metadata cache cleared; it will be fetched again when needed.";
            await RefreshCacheSizeAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _shell.ReportError(exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
