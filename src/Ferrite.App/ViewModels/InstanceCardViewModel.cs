using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
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

    /// <summary>
    /// The letter shown on a card that has no artwork of its own, so an un-themed instance still has
    /// something recognisable where an image would be.
    /// </summary>
    public string Initial => Name.Trim() is { Length: > 0 } trimmed
        ? char.ToUpperInvariant(trimmed[0]).ToString()
        : "?";

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

    public string LoaderBadge => Record.Loader == LoaderKind.Vanilla
        ? string.Empty
        : Record.Loader.ToDisplayName();

    public bool HasLoaderBadge => Record.Loader != LoaderKind.Vanilla;

    public string VersionBadge => Record.MinecraftVersion;

    public bool HasVersionBadge => !string.IsNullOrWhiteSpace(Record.MinecraftVersion);

    /// <summary>The accessible name of the card's artwork button, which is what opens the instance.</summary>
    public string OpenLabel => Localizer.Format("L.Library.OpenInstance", Name);

    /// <summary>
    /// The label of the folder chip: the folder's name when the instance is filed, or a prompt to
    /// file it. It is the affordance that opens the folder prompt without widening the action row.
    /// </summary>
    public string GroupButtonText => Record.Group is { Length: > 0 } group
        ? Localizer.Format("L.Library.FolderChip", group)
        : Localizer.Get("L.Library.MoveToFolder");

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string? _statusNote;

    [ObservableProperty]
    private string _sizeText = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Artwork chosen for the instance, decoded at card size while the card is on screen.</summary>
    [ObservableProperty]
    private Bitmap? _artwork;

    [ObservableProperty]
    private Bitmap? _icon;

    /// <summary>The instance's own accent, when it set one, used to tint the fallback tile.</summary>
    [ObservableProperty]
    private IBrush? _accentBrush;

    public bool HasArtwork => Artwork is not null;

    public bool HasIcon => Icon is not null;

    public bool HasAccentBrush => AccentBrush is not null;

    private bool _artworkLoaded;

    /// <summary>
    /// Decodes the instance's own artwork. Called when a card's container is prepared rather than for
    /// the whole library, so a collection of hundreds of instances only holds the images for the rows
    /// the viewport actually shows.
    /// </summary>
    public async Task EnsureArtworkAsync()
    {
        if (_artworkLoaded)
        {
            return;
        }

        _artworkLoaded = true;
        var directory = _services.Paths.InstanceDirectory(Record.Id);
        Artwork = await Task.Run(() => Decode(directory, Record.ThemeBackgroundPath, 640)).ConfigureAwait(true);
        Icon = await Task.Run(() => Decode(directory, Record.IconPath, 160)).ConfigureAwait(true);
        AccentBrush = Record.ThemeAccent is { Length: 7 } accent && Color.TryParse(accent, out var colour)
            ? new SolidColorBrush(colour)
            : null;

        OnPropertyChanged(nameof(HasArtwork));
        OnPropertyChanged(nameof(HasIcon));
        OnPropertyChanged(nameof(HasAccentBrush));
    }

    /// <summary>Drops the decoded images when the card scrolls out of view, keeping memory bounded.</summary>
    public void ReleaseArtwork()
    {
        _artworkLoaded = false;
        Artwork?.Dispose();
        Artwork = null;
        Icon?.Dispose();
        Icon = null;
        OnPropertyChanged(nameof(HasArtwork));
        OnPropertyChanged(nameof(HasIcon));
    }

    private static Bitmap? Decode(string directory, string? relative, int width)
    {
        if (relative is not { Length: > 0 })
        {
            return null;
        }

        try
        {
            var path = Path.Combine(directory, relative);
            if (!File.Exists(path))
            {
                return null;
            }

            using var stream = File.OpenRead(path);
            return Bitmap.DecodeToWidth(stream, width);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                          or ArgumentException)
        {
            // A broken image is a missing image, not a broken card.
            return null;
        }
    }

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var directory = _services.Paths.InstanceDirectory(Record.Id);
        SizeBytes = await Task.Run(
                () => InstanceContentManager.GetDirectorySize(directory),
                cancellationToken)
            .ConfigureAwait(true);
        SizeText = ByteSize.Format(SizeBytes);
        IsRunning = _services.Launcher.TryGetRunning(Record.Id, out var process) && !process.HasExited;
    }

    /// <summary>Bytes on disk for the whole instance, so the library can be ordered by size.</summary>
    public long SizeBytes { get; private set; }

    [RelayCommand]
    private void Open() => _shell.ShowInstance(Record);

    [RelayCommand]
    private void OpenFolder() => ShellOpen.Directory(_services.Paths.InstanceGameDirectory(Record.Id));

    [RelayCommand]
    private void Rename() => _shell.Library.BeginRename(this);

    [RelayCommand]
    private void Clone() => _shell.Library.BeginClone(this);

    [RelayCommand]
    private void Group() => _shell.Library.BeginGroup(this);

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
            _shell.ReportSuccess(Localizer.Format("L.Library.MovedToBackups", Record.Name));
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
            if (report.IsHealthy)
            {
                _shell.ReportSuccess(StatusNote);
            }
            else
            {
                _shell.ReportStatus(StatusNote);
            }
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
