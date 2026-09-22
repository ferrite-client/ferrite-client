using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ferrite.App.Localization;
using Ferrite.Core.Content;
using Ferrite.Core.Storage;

namespace Ferrite.App.ViewModels;

/// <summary>
/// Saved projects: the browser remembers a project the user wants to revisit, and the saved view is
/// the same result list filtered to the active provider's saved entries so selecting one still loads
/// its versions and installs it through the normal path.
/// </summary>
public sealed partial class BrowseViewModel
{
    [ObservableProperty]
    private bool _isSavedView;

    /// <summary>Whether the currently selected project is saved, which the toggle button reflects.</summary>
    [ObservableProperty]
    private bool _isSelectedSaved;

    [ObservableProperty]
    private int _savedCount;

    public bool HasSavedProjects => SavedCount > 0;

    /// <summary>The empty-list message, which differs between searching and the saved view.</summary>
    public string EmptyMessage => IsSavedView
        ? Localizer.Get("L.Browse.NoSaved")
        : Localizer.Get("L.Browse.Empty");

    /// <summary>The save toggle's label, so one button serves both directions.</summary>
    public string SavedButtonText => IsSelectedSaved
        ? Localizer.Get("L.Browse.Unsave")
        : Localizer.Get("L.Browse.Save");

    partial void OnIsSavedViewChanged(bool value)
    {
        OnPropertyChanged(nameof(EmptyMessage));
        OnPropertyChanged(nameof(IsSearchView));
    }

    partial void OnIsSelectedSavedChanged(bool value) => OnPropertyChanged(nameof(SavedButtonText));

    /// <summary>True while the search controls should be the ones on screen.</summary>
    public bool IsSearchView => !IsSavedView;

    [RelayCommand]
    private async Task ShowSavedAsync()
    {
        IsSavedView = true;
        OnPropertyChanged(nameof(IsSearchView));
        await RebuildSavedResultsAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ShowSearchAsync()
    {
        IsSavedView = false;
        OnPropertyChanged(nameof(IsSearchView));
        await SearchAsync().ConfigureAwait(true);
    }

    /// <summary>Adds or removes the selected project, and says which way it went.</summary>
    [RelayCommand]
    private async Task ToggleSavedAsync()
    {
        if (SelectedResult is not { } project)
        {
            return;
        }

        try
        {
            var saved = await _services.SavedProjects
                .ToggleAsync(project.Summary, CancellationToken.None)
                .ConfigureAwait(true);
            IsSelectedSaved = saved;
            await RefreshSavedCountAsync().ConfigureAwait(true);
            StatusNote = saved
                ? Localizer.Format("L.Browse.SavedProject", project.Title)
                : Localizer.Format("L.Browse.UnsavedProject", project.Title);

            if (IsSavedView)
            {
                await RebuildSavedResultsAsync().ConfigureAwait(true);
            }
        }
        catch (Exception exception)
        {
            StatusNote = exception.Message;
        }
    }

    /// <summary>
    /// Fills the result list from the saved projects of the active provider. The provider's own
    /// entries only, so selecting one always resolves against the provider that answered.
    /// </summary>
    private async Task RebuildSavedResultsAsync()
    {
        try
        {
            var saved = await _services.SavedProjects.LoadAsync(CancellationToken.None).ConfigureAwait(true);
            SavedCount = saved.Count;
            OnPropertyChanged(nameof(HasSavedProjects));

            var provider = ActiveProvider.Name;
            Results.Clear();
            foreach (var entry in saved.Where(entry =>
                         string.Equals(entry.Provider, provider, StringComparison.OrdinalIgnoreCase)))
            {
            Results.Add(new ContentSummaryViewModel(ToSummary(entry), _services));
            }

            ResultSummary = Localizer.Format("L.Browse.SavedSummary", Results.Count);
            SelectedResult = Results.FirstOrDefault();
            OnPropertyChanged(nameof(HasResults));
        }
        catch (Exception exception)
        {
            StatusNote = exception.Message;
        }
    }

    private async Task RefreshSavedCountAsync()
    {
        var saved = await _services.SavedProjects.LoadAsync(CancellationToken.None).ConfigureAwait(true);
        SavedCount = saved.Count;
        OnPropertyChanged(nameof(HasSavedProjects));
    }

    /// <summary>Re-reads whether the selected project is saved, after the selection changed.</summary>
    private async Task RefreshSelectedSavedAsync()
    {
        if (SelectedResult is not { } project)
        {
            IsSelectedSaved = false;
            return;
        }

        try
        {
            IsSelectedSaved = await _services.SavedProjects
                .ContainsAsync(project.Provider, project.ProjectId, CancellationToken.None)
                .ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            StatusNote = exception.Message;
        }
    }

    private static ContentSummary ToSummary(SavedProject saved) => new(
        saved.Provider,
        saved.ProjectId,
        saved.Slug,
        saved.Title,
        Description: null,
        saved.ProjectType,
        Downloads: 0,
        saved.IconUrl,
        saved.Author,
        Categories: [],
        UpdatedAt: saved.SavedAt);
}
