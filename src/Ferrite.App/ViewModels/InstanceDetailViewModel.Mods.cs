using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ferrite.App.Localization;
using Ferrite.Core.Content;

namespace Ferrite.App.ViewModels;

/// <summary>
/// Searching, filtering, and ordering over the last mod scan. A large pack is hundreds of files, so
/// the visible list is a view over the scan rather than a reason to touch the disk again.
/// </summary>
public sealed partial class InstanceDetailViewModel
{
    /// <summary>The loader filter value that matches every mod.</summary>
    internal const string AllLoaders = "all";

    private readonly List<ModMetadata> _scannedMods = [];

    /// <summary>Loaders present in this instance, with an entry that matches all of them.</summary>
    public ObservableCollection<ChoiceOption> ModLoaderChoices { get; } = [];

    public IReadOnlyList<ChoiceOption> ModSortChoices { get; } =
    [
        new("name", Localizer.Get("L.Instance.SortName")),
        new("size", Localizer.Get("L.Instance.SortSize")),
        new("loader", Localizer.Get("L.Instance.SortLoader")),
    ];

    [ObservableProperty]
    private string _modQuery = string.Empty;

    [ObservableProperty]
    private ChoiceOption? _selectedModLoader;

    [ObservableProperty]
    private ChoiceOption? _selectedModSort;

    [ObservableProperty]
    private string? _modFilterNote;

    /// <summary>True when the instance holds mods at all, whether or not the filter shows them.</summary>
    public bool HasAnyMods => _scannedMods.Count > 0;

    /// <summary>True when the filter removed every mod the instance has.</summary>
    public bool HasNoModMatches => HasAnyMods && Mods.Count == 0;

    public bool HasModFilterNote => !string.IsNullOrEmpty(ModFilterNote);

    partial void OnModQueryChanged(string value) => ApplyModFilter();

    partial void OnSelectedModLoaderChanged(ChoiceOption? value) => ApplyModFilter();

    partial void OnSelectedModSortChanged(ChoiceOption? value) => ApplyModFilter();

    /// <summary>
    /// Replaces the scan behind the list and rebuilds the loader choices from what was actually
    /// found, so the filter can only offer loaders this instance has.
    /// </summary>
    internal void SetScannedMods(IReadOnlyList<ModMetadata> mods)
    {
        _scannedMods.Clear();
        _scannedMods.AddRange(mods);

        var previous = SelectedModLoader?.Value ?? AllLoaders;
        ModLoaderChoices.Clear();
        ModLoaderChoices.Add(new ChoiceOption(AllLoaders, Localizer.Get("L.Instance.ModLoaderAll")));
        foreach (var loader in mods
                     .Select(mod => mod.Loader)
                     .Where(loader => !string.IsNullOrWhiteSpace(loader))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(loader => loader, StringComparer.OrdinalIgnoreCase))
        {
            ModLoaderChoices.Add(new ChoiceOption(loader, loader));
        }

        SelectedModLoader = ModLoaderChoices.FirstOrDefault(choice =>
                string.Equals(choice.Value, previous, StringComparison.OrdinalIgnoreCase))
            ?? ModLoaderChoices[0];
        SelectedModSort ??= ModSortChoices[0];

        ApplyModFilter();
    }

    /// <summary>Recomputes the visible list from the query, the loader filter, and the order.</summary>
    internal void ApplyModFilter()
    {
        IEnumerable<ModMetadata> visible = _scannedMods;

        var loader = SelectedModLoader?.Value ?? AllLoaders;
        if (!string.Equals(loader, AllLoaders, StringComparison.OrdinalIgnoreCase))
        {
            visible = visible.Where(mod =>
                string.Equals(mod.Loader, loader, StringComparison.OrdinalIgnoreCase));
        }

        var query = ModQuery?.Trim();
        if (!string.IsNullOrEmpty(query))
        {
            visible = visible.Where(mod => Matches(mod, query));
        }

        visible = (SelectedModSort?.Value ?? "name") switch
        {
            "size" => visible
                .OrderByDescending(mod => mod.Size)
                .ThenBy(mod => mod.DisplayName, StringComparer.CurrentCultureIgnoreCase),
            "loader" => visible
                .OrderBy(mod => mod.Loader, StringComparer.OrdinalIgnoreCase)
                .ThenBy(mod => mod.DisplayName, StringComparer.CurrentCultureIgnoreCase),
            _ => visible.OrderBy(mod => mod.DisplayName, StringComparer.CurrentCultureIgnoreCase),
        };

        Mods.Clear();
        foreach (var mod in visible)
        {
            Mods.Add(new ModItemViewModel(
                mod,
                _services,
                () => _ = RefreshModsAsync(),
                ModBackupDirectory,
                OnModSelectionChanged));
        }

        ModFilterNote = _scannedMods.Count == 0
            ? null
            : Localizer.Format("L.Instance.ModsShown", Mods.Count, _scannedMods.Count);
        OnPropertyChanged(nameof(HasMods));
        OnPropertyChanged(nameof(HasAnyMods));
        OnPropertyChanged(nameof(HasNoModMatches));
        OnPropertyChanged(nameof(HasModFilterNote));
        OnPropertyChanged(nameof(HasModSelection));
        OnPropertyChanged(nameof(SelectedModSummary));
        OnPropertyChanged(nameof(HasSelectedModSummary));
    }

    /// <summary>Where a removed mod is kept, per instance, so a removal is still recoverable.</summary>
    internal string ModBackupDirectory =>
        Path.Combine(_services.Paths.BackupsDirectory, "removed-content", Record.Id.ToString("N"));

    /// <summary>Selected mods, in the order they are shown. Empty when nothing is ticked.</summary>
    private List<ModItemViewModel> SelectedMods() =>
        Mods.Where(mod => mod.IsSelected).ToList();

    public bool HasModSelection => Mods.Any(mod => mod.IsSelected);

    public bool HasSelectedModSummary => HasModSelection;

    /// <summary>How many mods a bulk action would act on, so the buttons are never ambiguous.</summary>
    public string? SelectedModSummary => HasModSelection
        ? Localizer.Format("L.Instance.ModsSelected", Mods.Count(mod => mod.IsSelected))
        : null;

    private void OnModSelectionChanged()
    {
        OnPropertyChanged(nameof(HasModSelection));
        OnPropertyChanged(nameof(SelectedModSummary));
        OnPropertyChanged(nameof(HasSelectedModSummary));
    }

    /// <summary>Ticks every mod the filter is currently showing, not the ones it hid.</summary>
    [RelayCommand]
    private void SelectAllMods()
    {
        foreach (var mod in Mods)
        {
            mod.IsSelected = true;
        }

        OnModSelectionChanged();
    }

    [RelayCommand]
    private void ClearModSelection()
    {
        foreach (var mod in Mods)
        {
            mod.IsSelected = false;
        }

        OnModSelectionChanged();
    }

    [RelayCommand]
    private Task EnableSelectedModsAsync() => SetSelectedModsEnabledAsync(true);

    [RelayCommand]
    private Task DisableSelectedModsAsync() => SetSelectedModsEnabledAsync(false);

    /// <summary>
    /// Enables or disables every ticked mod in one pass, then rescans once. A file that could not be
    /// renamed is reported by count rather than silently skipped.
    /// </summary>
    private async Task SetSelectedModsEnabledAsync(bool enabled)
    {
        var selected = SelectedMods();
        if (selected.Count == 0)
        {
            return;
        }

        var failed = selected.Count(mod => !mod.TrySetEnabled(enabled));
        await RefreshModsAsync().ConfigureAwait(true);
        StatusNote = failed > 0
            ? Localizer.Format("L.Instance.ModsFailed", failed)
            : enabled
                ? Localizer.Format("L.Instance.EnabledMods", selected.Count)
                : Localizer.Format("L.Instance.DisabledMods", selected.Count);
    }

    /// <summary>Moves every ticked mod out of the instance in one pass. Each file is kept in backups.</summary>
    [RelayCommand]
    private async Task RemoveSelectedModsAsync()
    {
        var selected = SelectedMods();
        if (selected.Count == 0)
        {
            return;
        }

        var removed = selected.Count(mod => mod.TryRemove());
        await RefreshModsAsync().ConfigureAwait(true);
        StatusNote = removed == 0
            ? Localizer.Get("L.Instance.NoModsRemoved")
            : Localizer.Format("L.Instance.RemovedMods", removed);
    }

    private static bool Matches(ModMetadata mod, string query) =>
        Contains(mod.DisplayName, query)
        || Contains(mod.FileName, query)
        || Contains(mod.Version, query)
        || Contains(mod.Loader, query)
        || mod.Dependencies.Any(dependency => Contains(dependency, query));

    private static bool Contains(string? value, string query) =>
        value is not null && value.Contains(query, StringComparison.CurrentCultureIgnoreCase);
}
