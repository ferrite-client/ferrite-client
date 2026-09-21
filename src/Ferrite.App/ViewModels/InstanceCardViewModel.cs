using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ferrite.App.Localization;
using Ferrite.App.Services;
using Ferrite.Core.Content;
using Ferrite.Core.Loaders;
using Ferrite.Core.Minecraft;
using Ferrite.Core.Rules;
using Ferrite.Core.Storage;
using Ferrite.Core.Util;

namespace Ferrite.App.ViewModels;

/// <summary>One instance in the library, with its live state and actions.</summary>
public sealed partial class InstanceCardViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly MainWindowViewModel _shell;

    public InstanceCardViewModel(InstanceRecord record, AppServices services, MainWindowViewModel shell)
    {
        Record = record;
        _services = services;
        _shell = shell;
    }

    public InstanceRecord Record { get; }

    public string Name => Record.Name;

    public string VersionId => InstanceLauncher.LaunchVersionId(Record);

    public string Subtitle => Record.Loader == LoaderKind.Vanilla
        ? $"Minecraft {Record.MinecraftVersion}"
        : $"Minecraft {Record.MinecraftVersion} · {Record.Loader.ToDisplayName()} {Record.LoaderVersion}";

    public string MemoryText => Record.MemoryMb is { } memory ? $"{memory} MB heap" : "Automatic memory";

    public string LastPlayedText => Record.LastLaunchedAt is { } played
        ? $"Last played {played.LocalDateTime:yyyy-MM-dd HH:mm}"
        : "Never launched";

    public string ModpackText => Record.Modpack is { } pack
        ? $"{pack.Name} {pack.VersionName}".Trim()
        : string.Empty;

    public bool HasModpack => Record.Modpack is not null;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string? _statusNote;

    [ObservableProperty]
    private string _sizeText = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var directory = _services.Paths.InstanceDirectory(Record.Id);
        SizeText = ByteSize.Format(
            await Task.Run(() => InstanceContentManager.GetDirectorySize(directory), cancellationToken)
                .ConfigureAwait(true));
        IsRunning = _services.Launcher.TryGetRunning(Record.Id, out var process) && !process.HasExited;
    }

    [RelayCommand]
    private void Open() => _shell.DetailPage = new InstanceDetailViewModel(Record, _services, _shell);

    [RelayCommand]
    private void OpenFolder() => ShellOpen.Directory(_services.Paths.InstanceGameDirectory(Record.Id));

    [RelayCommand]
    private void Rename() => _shell.Library.BeginRename(this);

    [RelayCommand]
    private void Clone() => _shell.Library.BeginClone(this);

    [RelayCommand]
    private async Task StopAsync() =>
        await _services.Launcher.StopAsync(Record.Id, TimeSpan.FromSeconds(15), CancellationToken.None)
            .ConfigureAwait(true);

    [RelayCommand]
    private async Task DeleteAsync()
    {
        try
        {
            await _services.Instances
                .DeleteAsync(Record.Id, moveToBackups: true, CancellationToken.None)
                .ConfigureAwait(true);
            _shell.ReportStatus(Localizer.Format("L.Library.MovedToBackups", Record.Name));
            await _shell.Library.RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _shell.ReportError(exception.Message);
        }
    }

    [RelayCommand]
    private async Task RepairAsync()
    {
        IsBusy = true;
        try
        {
            _shell.BeginActivity($"Repairing {Record.Name}...");
            var report = await _services.Installer
                .RepairAsync(
                    Record.Id,
                    VersionId,
                    RuleContext.ForHost(),
                    new Progress<InstallProgress>(_shell.ReportActivity),
                    CancellationToken.None)
                .ConfigureAwait(true);
            StatusNote = report.IsHealthy
                ? Localizer.Get("L.Instance.Healthy")
                : Localizer.Format("L.Instance.ProblemsRemain", report.Issues.Count);
            _shell.ReportStatus(StatusNote);
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
}
