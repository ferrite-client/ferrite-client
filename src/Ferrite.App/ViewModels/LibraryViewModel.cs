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
        // The folder filter always offers "all folders", even before the first refresh, so it never
        // renders as an empty box.
        GroupChoices.Add(new ChoiceOption(string.Empty, Localizer.Get("L.Library.GroupAll")));
        SelectedGroup = GroupChoices[0];
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
    private bool _showHistoricalVersions;

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

    /// <summary>The folder filter: "All" first, then every folder in use, re-derived on refresh.</summary>
    public ObservableCollection<ChoiceOption> GroupChoices { get; } = [];

    [ObservableProperty]
    private ChoiceOption? _selectedGroup;

    [ObservableProperty]
    private bool _isBusy;

    public IReadOnlyList<ChoiceOption> ViewChoices { get; } =
    [
        new("grid", Localizer.Get("L.Library.ViewGrid")),
        new("list", Localizer.Get("L.Library.ViewList")),
    ];

    /// <summary>Grid or list. The grid is the default presentation of the library.</summary>
    [ObservableProperty]
    private ChoiceOption _selectedView = new("grid", Localizer.Get("L.Library.ViewGrid"));

    /// <summary>
    /// How many cards fit across the window. The view reports its own width, so the grid reflows with
    /// the window instead of assuming a fixed column count.
    /// </summary>
    [ObservableProperty]
    private int _gridColumns = 4;

    /// <summary>
    /// Rows of cards for the grid view. Rows are what the list virtualises, so a library of hundreds
    /// of instances only ever builds the rows the viewport is showing.
    /// </summary>
    public ObservableCollection<InstanceRowViewModel> VisibleRows { get; } = [];

    public bool IsGridView => string.Equals(SelectedView.Value, "grid", StringComparison.Ordinal);

    public bool IsListView => !IsGridView;

    public bool HasVisibleInstances => VisibleInstances.Any();

    public string CountText => Localizer.Format("L.Library.Count", VisibleInstances.Count());

    /// <summary>Re-chunks the visible instances into rows of <see cref="GridColumns"/>.</summary>
    public void RebuildRows()
    {
        VisibleRows.Clear();
        if (!IsGridView)
        {
            return;
        }

        var columns = Math.Max(1, GridColumns);
        var buffer = new List<InstanceCardViewModel>(columns);
        foreach (var card in VisibleInstances)
        {
            buffer.Add(card);
            if (buffer.Count == columns)
            {
                VisibleRows.Add(new InstanceRowViewModel(buffer.ToArray()));
                buffer.Clear();
            }
        }

        if (buffer.Count > 0)
        {
            VisibleRows.Add(new InstanceRowViewModel(buffer.ToArray()));
        }
    }

    partial void OnSelectedViewChanged(ChoiceOption value)
    {
        OnPropertyChanged(nameof(IsGridView));
        OnPropertyChanged(nameof(IsListView));
        RebuildRows();
    }

    partial void OnGridColumnsChanged(int value) => RebuildRows();

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

            if (SelectedGroup?.Value is { Length: > 0 } group)
            {
                visible = visible.Where(instance =>
                    string.Equals(instance.Record.Group, group, StringComparison.CurrentCultureIgnoreCase));
            }

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

    partial void OnShowHistoricalVersionsChanged(bool value) => _ = LoadVersionsAsync();

    partial void OnSearchTextChanged(string value) => NotifyVisibleChanged();

    partial void OnSelectedSortChanged(ChoiceOption? value) => NotifyVisibleChanged();

    partial void OnSelectedGroupChanged(ChoiceOption? value) => NotifyVisibleChanged();

    /// <summary>One notification path for everything derived from the visible set.</summary>
    private void NotifyVisibleChanged()
    {
        OnPropertyChanged(nameof(VisibleInstances));
        OnPropertyChanged(nameof(HasVisibleInstances));
        OnPropertyChanged(nameof(CountText));
        RebuildRows();
    }

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

            RebuildGroupChoices(records);
            OnPropertyChanged(nameof(HasInstances));
            NotifyVisibleChanged();
        }
        catch (Exception exception)
        {
            _shell.ReportError(exception.Message);
        }
    }

    /// <summary>
    /// Rebuilds the folder filter from the folders actually in use and keeps the selection if that
    /// folder still has instances. An instance moved out of a folder therefore drops out of the
    /// filter rather than leaving an empty one behind.
    /// </summary>
    private void RebuildGroupChoices(IReadOnlyList<InstanceRecord> records)
    {
        var previous = SelectedGroup?.Value ?? string.Empty;
        var groups = records
            .Select(record => record.Group)
            .Where(group => !string.IsNullOrWhiteSpace(group))
            .Select(group => group!)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(group => group, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        GroupChoices.Clear();
        GroupChoices.Add(new ChoiceOption(string.Empty, Localizer.Get("L.Library.GroupAll")));
        foreach (var group in groups)
        {
            GroupChoices.Add(new ChoiceOption(group, group));
        }

        SelectedGroup = GroupChoices.FirstOrDefault(choice =>
                            string.Equals(choice.Value, previous, StringComparison.CurrentCultureIgnoreCase))
                        ?? GroupChoices[0];
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
