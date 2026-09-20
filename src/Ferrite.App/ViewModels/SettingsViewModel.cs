using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ferrite.App.Services;
using Ferrite.Core.Content;
using Ferrite.Core.Storage;
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
        DataRoot = _services.Paths.Root;
        await RefreshCacheSizeAsync().ConfigureAwait(true);
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

        await _services.Settings.SaveAsync(CancellationToken.None).ConfigureAwait(true);
        StatusNote = "Settings saved";
        _shell.ReportStatus(StatusNote);
    }

    [RelayCommand]
    private void OpenDataFolder() => ShellOpen.Directory(_services.Paths.Root);

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
