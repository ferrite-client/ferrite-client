using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Ferrite.App.Localization;
using Ferrite.App.Services;
using Ferrite.Core.Diagnostics;

namespace Ferrite.App.ViewModels;

/// <summary>
/// The downloads area: what the launcher is doing now, and what it has done. It reads the same
/// operation log the diagnostics section does, so the two can never disagree about what happened.
/// </summary>
public sealed partial class DownloadsViewModel : ObservableObject
{
    private const int HistoryLimit = 200;

    private readonly AppServices _services;
    private readonly MainWindowViewModel _shell;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private ChoiceOption _selectedFilter = new("all", Localizer.Get("L.Downloads.FilterAll"));

    public DownloadsViewModel(AppServices services, MainWindowViewModel shell)
    {
        _services = services;
        _shell = shell;
        Filters =
        [
            new("all", Localizer.Get("L.Downloads.FilterAll")),
            new("succeeded", Localizer.Get("L.Downloads.FilterFinished")),
            new("failed", Localizer.Get("L.Downloads.FilterFailed")),
        ];
    }

    public IReadOnlyList<ChoiceOption> Filters { get; }

    public ObservableCollection<DownloadItemViewModel> Active { get; } = [];

    public ObservableCollection<DownloadItemViewModel> History { get; } = [];

    public bool HasActive => Active.Count > 0;

    public bool HasHistory => History.Count > 0;

    public bool ShowsEmptyHistory => !HasHistory && !IsFiltered;

    public bool IsFiltered =>
        !string.IsNullOrWhiteSpace(SearchText)
        || !string.Equals(SelectedFilter.Value, "all", StringComparison.Ordinal);

    public bool ShowsNoFilterMatches => !HasHistory && IsFiltered;

    /// <summary>How many operations are in flight, for the rail badge and the drawer.</summary>
    public int ActiveCount => Active.Count;

    public string SummaryText => HasActive
        ? Localizer.Format("L.Downloads.SummaryActive", Active.Count)
        : Localizer.Get("L.Downloads.SummaryIdle");

    /// <summary>Rebuilds the view from the shell's live activity and the persisted operation log.</summary>
    public void Refresh()
    {
        Active.Clear();
        if (_shell.IsActivityVisible)
        {
            Active.Add(DownloadItemViewModel.Running(
                name: _shell.ActivityText ?? Localizer.Get("L.Downloads.Working"),
                detail: null,
                fraction: _shell.IsActivityIndeterminate ? 0 : _shell.ActivityFraction,
                transfer: _shell.Transfer));
        }

        var query = SearchText?.Trim() ?? string.Empty;
        History.Clear();
        foreach (var entry in _services.Operations.Recent(HistoryLimit))
        {
            if (!Matches(entry, query))
            {
                continue;
            }

            History.Add(DownloadItemViewModel.FromEntry(entry));
        }

        OnPropertyChanged(nameof(HasActive));
        OnPropertyChanged(nameof(HasHistory));
        OnPropertyChanged(nameof(ShowsEmptyHistory));
        OnPropertyChanged(nameof(ShowsNoFilterMatches));
        OnPropertyChanged(nameof(IsFiltered));
        OnPropertyChanged(nameof(ActiveCount));
        OnPropertyChanged(nameof(SummaryText));
    }

    private bool Matches(OperationEntry entry, string query)
    {
        var filter = SelectedFilter?.Value ?? "all";
        var outcomeMatches = filter switch
        {
            "succeeded" => entry.Outcome == OperationOutcome.Succeeded,
            "failed" => entry.Outcome != OperationOutcome.Succeeded,
            _ => true,
        };

        if (!outcomeMatches)
        {
            return false;
        }

        if (query.Length == 0)
        {
            return true;
        }

        return entry.Operation.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || (entry.Detail?.Contains(query, StringComparison.CurrentCultureIgnoreCase) ?? false);
    }

    partial void OnSearchTextChanged(string value) => Refresh();

    partial void OnSelectedFilterChanged(ChoiceOption value) => Refresh();
}
