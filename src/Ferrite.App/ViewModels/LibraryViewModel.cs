using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ferrite.App.Localization;
using Ferrite.App.Services;
using Ferrite.Core.Loaders;
using Ferrite.Core.Minecraft;
using Ferrite.Core.Storage;

namespace Ferrite.App.ViewModels;

/// <summary>
/// The instance library: the list, the creation form, and the loader catalogue the form uses.
/// </summary>
public sealed partial class LibraryViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly MainWindowViewModel _shell;

    public LibraryViewModel(AppServices services, MainWindowViewModel shell)
    {
        _services = services;
        _shell = shell;
        LoaderOptions =
        [
            LoaderKind.Vanilla,
            LoaderKind.Fabric,
            LoaderKind.Quilt,
            LoaderKind.NeoForge,
            LoaderKind.Forge,
        ];
        SelectedSort = SortChoices[0];
    }

    public ObservableCollection<InstanceCardViewModel> Instances { get; } = [];

    public ObservableCollection<VersionManifestEntry> Versions { get; } = [];

    public ObservableCollection<LoaderVersionInfo> LoaderVersions { get; } = [];

    public IReadOnlyList<LoaderKind> LoaderOptions { get; }

    [ObservableProperty]
    private bool _isCreating;

    [ObservableProperty]
    private string _newName = "New instance";

    [ObservableProperty]
    private VersionManifestEntry? _newVersion;

    [ObservableProperty]
    private LoaderKind _newLoader = LoaderKind.Vanilla;

    [ObservableProperty]
    private LoaderVersionInfo? _newLoaderVersion;

    [ObservableProperty]
    private string? _formError;

    [ObservableProperty]
    private bool _showSnapshots;

    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>How the instance list is ordered. Recently played is the default.</summary>
    [ObservableProperty]
    private ChoiceOption? _selectedSort;

    public IReadOnlyList<ChoiceOption> SortChoices { get; } =
    [
        new("recent", Localizer.Get("L.Library.SortRecent")),
        new("name", Localizer.Get("L.Library.SortName")),
        new("version", Localizer.Get("L.Library.SortVersion")),
        new("size", Localizer.Get("L.Library.SortSize")),
    ];

    [ObservableProperty]
    private bool _isBusy;

    public bool HasInstances => Instances.Count > 0;

    public bool NeedsLoaderVersion => NewLoader != LoaderKind.Vanilla;

    public bool HasFormError => !string.IsNullOrEmpty(FormError);

    /// <summary>The instances that pass the search box, in the order the user chose.</summary>
    public IEnumerable<InstanceCardViewModel> VisibleInstances
    {
        get
        {
            IEnumerable<InstanceCardViewModel> visible = string.IsNullOrWhiteSpace(SearchText)
                ? Instances
                : Instances.Where(instance =>
                    instance.Name.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase)
                    || instance.Subtitle.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase)
                    || (instance.ModpackText ?? string.Empty).Contains(
                        SearchText,
                        StringComparison.CurrentCultureIgnoreCase));

            return (SelectedSort?.Value ?? "recent") switch
            {
                "name" => visible.OrderBy(
                    instance => instance.Name,
                    StringComparer.CurrentCultureIgnoreCase),
                "version" => visible
                    .OrderBy(instance => instance.Record.MinecraftVersion, StringComparer.Ordinal)
                    .ThenBy(instance => instance.Name, StringComparer.CurrentCultureIgnoreCase),
                "size" => visible
                    .OrderByDescending(instance => instance.SizeBytes)
                    .ThenBy(instance => instance.Name, StringComparer.CurrentCultureIgnoreCase),
                _ => visible.OrderByDescending(instance =>
                    instance.Record.LastLaunchedAt ?? instance.Record.CreatedAt),
            };
        }
    }

    partial void OnNewLoaderChanged(LoaderKind value)
    {
        OnPropertyChanged(nameof(NeedsLoaderVersion));
        _ = LoadLoaderVersionsAsync();
    }

    partial void OnNewVersionChanged(VersionManifestEntry? value) => _ = LoadLoaderVersionsAsync();

    partial void OnFormErrorChanged(string? value) => OnPropertyChanged(nameof(HasFormError));

    partial void OnShowSnapshotsChanged(bool value) => _ = LoadVersionsAsync();

    partial void OnSearchTextChanged(string value) => OnPropertyChanged(nameof(VisibleInstances));

    partial void OnSelectedSortChanged(ChoiceOption? value) => OnPropertyChanged(nameof(VisibleInstances));

    public async Task RefreshAsync()
    {
        try
        {
            var records = await _services.Instances.LoadAllAsync(CancellationToken.None).ConfigureAwait(true);
            Instances.Clear();
            foreach (var record in records.OrderByDescending(record => record.LastLaunchedAt ?? record.CreatedAt))
            {
                var card = new InstanceCardViewModel(record, _services, _shell);
                Instances.Add(card);
                await card.RefreshAsync(CancellationToken.None).ConfigureAwait(true);
            }

            OnPropertyChanged(nameof(HasInstances));
            OnPropertyChanged(nameof(VisibleInstances));
        }
        catch (Exception exception)
        {
            _shell.ReportError(exception.Message);
        }
    }

    [RelayCommand]
    private async Task OpenCreateAsync()
    {
        IsCreating = true;
        FormError = null;
        NewName = NextInstanceName();
        NewLoader = LoaderKind.Vanilla;
        NewLoaderVersion = null;
        await LoadVersionsAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private void CancelCreate()
    {
        IsCreating = false;
        FormError = null;
    }

    private string NextInstanceName()
    {
        var index = 1;
        while (Instances.Any(instance => instance.Name == $"Instance {index}"))
        {
            index++;
        }

        return $"Instance {index}";
    }
}
