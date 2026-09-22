using Ferrite.App.Localization;
using Ferrite.App.Services;
using Ferrite.App.ViewModels;
using Ferrite.Core.Content;
using Ferrite.Core.Platform;
using Microsoft.Extensions.Logging;

namespace Ferrite.App.Tests;

/// <summary>
/// The browser's saved view. The provider is pointed at a dead address so the assertions are about
/// the saved list itself rather than about a live search.
/// </summary>
public sealed class BrowseSavedTests : IDisposable
{
    private readonly string _root;
    private readonly AppServices _services;
    private readonly BrowseViewModel _viewModel;

    public BrowseSavedTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ferrite-saved-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
        _services = new AppServices(
            loggerFactory,
            AppPaths.ForRoot(_root),
            modrinthApiBase: "http://127.0.0.1:9/");
        Localizer.Apply(Avalonia.Application.Current, Localizer.English);
        _viewModel = new BrowseViewModel(_services, new MainWindowViewModel(_services));
    }

    public void Dispose()
    {
        _services.Dispose();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static ContentSummary Project(string id, string title, string provider = "modrinth") => new(
        provider, id, id, title, "A description", ContentProjectType.Mod, 10, null, "An author", [], null);

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task The_saved_view_lists_only_the_active_providers_saved_projects()
    {
        await _services.SavedProjects.ToggleAsync(Project("sodium", "Sodium"), CancellationToken.None);
        await _services.SavedProjects.ToggleAsync(Project("mekanism", "Mekanism"), CancellationToken.None);
        await _services.SavedProjects.ToggleAsync(Project("jei", "JEI", "curseforge"), CancellationToken.None);

        Assert.False(_viewModel.IsSavedView);
        Assert.True(_viewModel.IsSearchView);

        await _viewModel.ShowSavedCommand.ExecuteAsync(null);

        Assert.True(_viewModel.IsSavedView);
        Assert.Equal(2, _viewModel.Results.Count);
        Assert.Contains("2", _viewModel.ResultSummary, StringComparison.Ordinal);
        Assert.All(_viewModel.Results, result => Assert.Equal("modrinth", result.Summary.Provider));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task Unsaving_from_the_saved_view_takes_the_entry_out()
    {
        await _services.SavedProjects.ToggleAsync(Project("sodium", "Sodium"), CancellationToken.None);

        await _viewModel.ShowSavedCommand.ExecuteAsync(null);
        Assert.Single(_viewModel.Results);

        await _viewModel.ToggleSavedCommand.ExecuteAsync(null);

        Assert.Empty(_viewModel.Results);
        Assert.Equal(Localizer.Get("L.Browse.NoSaved"), _viewModel.EmptyMessage);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task Saving_and_unsaving_the_selected_project_is_reported()
    {
        await _viewModel.ShowSavedCommand.ExecuteAsync(null);
        Assert.Empty(_viewModel.Results);

        // Select a project the way a search would, then save it.
        _viewModel.Results.Add(new ContentSummaryViewModel(Project("sodium", "Sodium"), _services));
        _viewModel.SelectedResult = _viewModel.Results[0];
        Assert.Equal(Localizer.Get("L.Browse.Save"), _viewModel.SavedButtonText);

        await _viewModel.ToggleSavedCommand.ExecuteAsync(null);

        Assert.True(_viewModel.IsSelectedSaved);
        Assert.Equal(Localizer.Get("L.Browse.Unsave"), _viewModel.SavedButtonText);
        Assert.Single(await _services.SavedProjects.LoadAsync(CancellationToken.None));

        await _viewModel.ToggleSavedCommand.ExecuteAsync(null);

        Assert.False(_viewModel.IsSelectedSaved);
        Assert.Empty(await _services.SavedProjects.LoadAsync(CancellationToken.None));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task Leaving_the_saved_view_goes_back_to_searching()
    {
        await _viewModel.ShowSavedCommand.ExecuteAsync(null);
        Assert.True(_viewModel.IsSavedView);

        await _viewModel.ShowSearchCommand.ExecuteAsync(null);

        Assert.False(_viewModel.IsSavedView);
        Assert.True(_viewModel.IsSearchView);
    }
}
