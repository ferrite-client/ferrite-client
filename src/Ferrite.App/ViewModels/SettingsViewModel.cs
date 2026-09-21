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
        NewCurseForgeApiKey = null;
        RefreshCurseForgeKeyStatus();
        await RefreshCacheSizeAsync().ConfigureAwait(true);
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
