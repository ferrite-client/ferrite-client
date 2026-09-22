using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Ferrite.App.Localization;
using Ferrite.App.Services;
using Ferrite.App.ViewModels;
using Ferrite.App.Views;
using Ferrite.Core.Content;
using Ferrite.Core.Platform;
using Microsoft.Extensions.Logging;

namespace Ferrite.App.Tests;

/// <summary>
/// The browser's facets and the project panel. The facets are what make a search narrowable; the
/// panel is what tells a user what they are about to install.
/// </summary>
public sealed class BrowseFacetTests : IDisposable
{
    private readonly string _root;
    private readonly AppServices _services;
    private readonly BrowseViewModel _viewModel;

    public BrowseFacetTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ferrite-browse-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
        _services = new AppServices(loggerFactory, AppPaths.ForRoot(_root));
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

    /// <summary>A facet combo drives the filter the search sends, and "any" clears it.</summary>
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void Choosing_facets_sets_the_filters_the_search_uses()
    {
        _viewModel.CategoryChoices.Clear();
        _viewModel.CategoryChoices.Add(new ContentTag(BrowseViewModel.AnyFacet, "Any category", null));
        _viewModel.CategoryChoices.Add(new ContentTag("optimization", "Optimization", null));
        _viewModel.GameVersionChoices.Clear();
        _viewModel.GameVersionChoices.Add(new ContentTag(BrowseViewModel.AnyFacet, "Any version", null));
        _viewModel.GameVersionChoices.Add(new ContentTag("1.21.1", "1.21.1", null));
        _viewModel.LoaderChoices.Clear();
        _viewModel.LoaderChoices.Add(new ContentTag(BrowseViewModel.AnyFacet, "Any loader", null));
        _viewModel.LoaderChoices.Add(new ContentTag("fabric", "fabric", null));

        Assert.Equal(0, _viewModel.ActiveFacetCount);

        _viewModel.SelectedCategory = _viewModel.CategoryChoices[1];
        _viewModel.SelectedGameVersion = _viewModel.GameVersionChoices[1];
        _viewModel.SelectedLoader = _viewModel.LoaderChoices[1];

        Assert.Equal("1.21.1", _viewModel.GameVersionFilter);
        Assert.Equal("fabric", _viewModel.LoaderFilter);
        Assert.Equal(3, _viewModel.ActiveFacetCount);

        // "Any" is a real choice that clears the filter rather than an empty selection.
        _viewModel.SelectedCategory = _viewModel.CategoryChoices[0];
        _viewModel.SelectedGameVersion = _viewModel.GameVersionChoices[0];
        _viewModel.SelectedLoader = _viewModel.LoaderChoices[0];

        Assert.Null(_viewModel.GameVersionFilter);
        Assert.Null(_viewModel.LoaderFilter);
        Assert.Equal(0, _viewModel.ActiveFacetCount);
    }

    /// <summary>The panel shows the facts about a project, and says nothing when there are none.</summary>
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void The_project_panel_reports_what_it_has_and_what_it_lacks()
    {
        Assert.False(_viewModel.HasProjectDetails);
        Assert.False(_viewModel.HasProjectBody);
        Assert.False(_viewModel.HasProjectMeta);
        Assert.False(_viewModel.HasGallery);

        _viewModel.ProjectDetails = new ContentProject(
            "modrinth",
            "AANobbMI",
            "sodium",
            "Sodium",
            "Rendering engine",
            "## Sodium",
            ContentProjectType.Mod,
            1,
            null,
            "LGPL-3.0-only",
            ["optimization"],
            ["1.21.1"],
            ["fabric"],
            "https://github.com/CaffeineMC/sodium",
            null);
        _viewModel.ProjectBody = "## Sodium";
        _viewModel.ProjectMeta = "jellysquid3 · LGPL-3.0-only · optimization";

        Assert.True(_viewModel.HasProjectDetails);
        Assert.True(_viewModel.HasProjectBody);
        Assert.True(_viewModel.HasProjectMeta);
    }

    /// <summary>
    /// A provider's description is markdown, and this is a text box rather than a renderer, so the
    /// syntax that would only read as noise is dropped and the prose is kept.
    /// </summary>
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void The_project_body_drops_markdown_noise_but_keeps_the_prose()
    {
        var body = "# Sodium\n\nRenders fast.\n\n---\n\n## Install\n\n- drop it in\n\n```\ncode\n```\n";

        var simplified = BrowseViewModel.SimplifyMarkdownForDisplay(body);

        Assert.Contains("Sodium", simplified, StringComparison.Ordinal);
        Assert.Contains("Renders fast.", simplified, StringComparison.Ordinal);
        Assert.Contains("Install", simplified, StringComparison.Ordinal);
        Assert.Contains("- drop it in", simplified, StringComparison.Ordinal);
        Assert.Contains("code", simplified, StringComparison.Ordinal);
        Assert.DoesNotContain("##", simplified, StringComparison.Ordinal);
        Assert.DoesNotContain("```", simplified, StringComparison.Ordinal);
        Assert.DoesNotContain("---", simplified, StringComparison.Ordinal);
        Assert.DoesNotContain($"{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}", simplified);
    }

    /// <summary>The facet combo boxes and the project panel render with real choices in them.</summary>
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void The_browser_renders_the_facet_choices_it_is_given()
    {
        _viewModel.GameVersionChoices.Clear();
        _viewModel.GameVersionChoices.Add(new ContentTag(BrowseViewModel.AnyFacet, "Any version", null));
        _viewModel.GameVersionChoices.Add(new ContentTag("1.21.1", "1.21.1", null));
        _viewModel.CategoryChoices.Clear();
        _viewModel.CategoryChoices.Add(new ContentTag(BrowseViewModel.AnyFacet, "Any category", null));
        _viewModel.CategoryChoices.Add(new ContentTag("optimization", "Optimization", null));
        _viewModel.SelectedGameVersion = _viewModel.GameVersionChoices[1];
        _viewModel.SelectedCategory = _viewModel.CategoryChoices[1];
        _viewModel.Results.Add(new ContentSummaryViewModel(new ContentSummary(
            "modrinth",
            "238222",
            "jei",
            "Just Enough Items",
            "View items and recipes",
            ContentProjectType.Mod,
            1234567,
            null,
            "mezz",
            ["Map and Information"],
            DateTimeOffset.UtcNow),
            _services));
        _viewModel.SelectedResult = _viewModel.Results[0];
        _viewModel.ProjectMeta = "jellysquid3 · LGPL-3.0-only";
        // The panel shows the description the way the view model prepares it for display.
        _viewModel.ProjectBody = BrowseViewModel.SimplifyMarkdownForDisplay("## Sodium");

        var window = new Window
        {
            Content = new BrowseView { DataContext = _viewModel },
            Width = 1400,
            Height = 900,
        };
        window.Show();

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);

        var texts = window.GetVisualDescendants()
            .OfType<TextBlock>()
            .Select(block => block.Text)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList();
        Assert.Contains("1.21.1", texts);
        Assert.Contains("Optimization", texts);
        Assert.Contains("ABOUT THIS PROJECT", texts);
        Assert.Contains("jellysquid3 · LGPL-3.0-only", texts);
        Assert.Contains("CHANGELOG", texts);
        Assert.Contains("Just Enough Items", texts);

        Save(frame!, "browse-facets");
    }

    private static void Save(Avalonia.Media.Imaging.Bitmap frame, string name)
    {
        var directory = Environment.GetEnvironmentVariable("FERRITE_UI_SHOTS");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        frame.Save(
            Path.Combine(directory, name + ".png"),
            Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }
}
