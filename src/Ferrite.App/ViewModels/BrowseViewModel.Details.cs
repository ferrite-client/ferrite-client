using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Ferrite.App.Localization;
using Ferrite.Core.Content;
using Ferrite.Core.Net;

namespace Ferrite.App.ViewModels;

/// <summary>
/// Facets and project details for the content browser: the provider's own category and version
/// vocabularies, the project body and licence, its authors, and its gallery.
/// </summary>
public sealed partial class BrowseViewModel
{
    /// <summary>How many gallery frames are fetched, so a large gallery stays cheap to open.</summary>
    private const int MaxGalleryFrames = 6;

    /// <summary>Bodies are shown in full up to this length; beyond it the panel says it truncated.</summary>
    private const int MaxBodyCharacters = 20000;

    /// <summary>The value of a facet that means "do not filter on this".</summary>
    internal const string AnyFacet = "any";

    public ObservableCollection<ContentTag> CategoryChoices { get; } = [];

    public ObservableCollection<ContentTag> GameVersionChoices { get; } = [];

    public ObservableCollection<ContentTag> LoaderChoices { get; } = [];

    [ObservableProperty]
    private ContentTag? _selectedCategory;

    [ObservableProperty]
    private ContentTag? _selectedGameVersion;

    [ObservableProperty]
    private ContentTag? _selectedLoader;

    /// <summary>The full project document for the selected result, when the provider has one.</summary>
    [ObservableProperty]
    private ContentProject? _projectDetails;

    public bool HasProjectDetails => ProjectDetails is not null;

    [ObservableProperty]
    private string? _projectBody;

    public bool HasProjectBody => !string.IsNullOrWhiteSpace(ProjectBody);

    [ObservableProperty]
    private string? _projectMeta;

    public bool HasProjectMeta => !string.IsNullOrWhiteSpace(ProjectMeta);

    public ObservableCollection<GalleryImageViewModel> Gallery { get; } = [];

    public bool HasGallery => Gallery.Count > 0;

    /// <summary>The project's own icon, so the detail view leads with the provider's artwork.</summary>
    [ObservableProperty]
    private Bitmap? _projectIcon;

    public bool HasProjectIcon => ProjectIcon is not null;

    private const int MaxIconBytes = 2 * 1024 * 1024;
    private const int IconDecodeWidth = 256;

    /// <summary>
    /// Fetches and decodes a project icon. A missing or broken icon is simply no icon: the detail view
    /// falls back to a typographic header rather than showing a broken frame.
    /// </summary>
    private async Task<Bitmap?> LoadIconAsync(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        try
        {
            var bytes = await _services.Http
                .GetBytesAsync(url, MaxIconBytes, CancellationToken.None)
                .ConfigureAwait(true);
            using var stream = new MemoryStream(bytes, writable: false);
            return Bitmap.DecodeToWidth(stream, IconDecodeWidth);
        }
        catch (Exception exception) when (exception is IOException or HttpException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Loads the provider's vocabularies for the facets the browser offers. A provider that has no
    /// vocabulary leaves the facet with only "any", which is the honest state rather than a free-text
    /// box that silently sends nothing.
    /// </summary>
    public async Task LoadFacetsAsync()
    {
        var provider = ActiveProvider;
        if (!provider.IsConfigured)
        {
            return;
        }

        try
        {
            var categories = await provider
                .GetTagsAsync("category", CancellationToken.None)
                .ConfigureAwait(true);
            SelectedCategory = FillFacet(
                CategoryChoices,
                categories,
                SelectedCategory,
                Localizer.Get("L.Browse.AnyCategory"));

            var versions = await provider
                .GetTagsAsync("game_version", CancellationToken.None)
                .ConfigureAwait(true);
            SelectedGameVersion = FillFacet(
                GameVersionChoices,
                versions,
                SelectedGameVersion,
                Localizer.Get("L.Browse.AnyVersion"));

            var loaders = await provider
                .GetTagsAsync("loader", CancellationToken.None)
                .ConfigureAwait(true);
            SelectedLoader = FillFacet(
                LoaderChoices,
                loaders,
                SelectedLoader,
                Localizer.Get("L.Browse.AnyLoader"));
        }
        catch (Exception exception)
        {
            StatusNote = exception.Message;
        }
    }

    /// <summary>
    /// Rebuilds one facet from the provider's tags, always offering "any" first, and returns the
    /// choice that should still be selected: the previous one when it survived, otherwise "any".
    /// </summary>
    private static ContentTag FillFacet(
        ObservableCollection<ContentTag> target,
        IReadOnlyList<ContentTag> tags,
        ContentTag? current,
        string anyLabel)
    {
        var keep = current?.Name;
        target.Clear();
        target.Add(new ContentTag(AnyFacet, anyLabel, null));
        foreach (var tag in tags
                     .Where(tag => !string.IsNullOrWhiteSpace(tag.Name))
                     .GroupBy(tag => tag.Name, StringComparer.OrdinalIgnoreCase)
                     .Select(group => group.First())
                     .OrderBy(tag => tag.DisplayName ?? tag.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            target.Add(tag);
        }

        return keep is null
            ? target[0]
            : target.FirstOrDefault(tag => string.Equals(tag.Name, keep, StringComparison.OrdinalIgnoreCase))
              ?? target[0];
    }

    /// <summary>The category to send to the provider, or null when the facet is set to "any".</summary>
    private string? CategoryFilter =>
        SelectedCategory is { } category && !string.Equals(category.Name, AnyFacet, StringComparison.Ordinal)
            ? category.Name
            : null;

    /// <summary>
    /// Points the facet combo boxes at the filters the browser chose from the selected instance, so
    /// the visible facets and the query agree.
    /// </summary>
    private void ApplyFiltersToFacets()
    {
        SelectedGameVersion = MatchFacet(GameVersionChoices, GameVersionFilter);
        SelectedLoader = MatchFacet(LoaderChoices, LoaderFilter);
        OnPropertyChanged(nameof(ActiveFacetCount));
    }

    private static ContentTag? MatchFacet(ObservableCollection<ContentTag> choices, string? value) =>
        value is { Length: > 0 }
            ? choices.FirstOrDefault(choice =>
                string.Equals(choice.Name, value, StringComparison.OrdinalIgnoreCase))
            : choices.FirstOrDefault();

    /// <summary>Number of facets the user has actually narrowed, for the summary line.</summary>
    internal int ActiveFacetCount =>
        (CategoryFilter is null ? 0 : 1)
        + (string.IsNullOrWhiteSpace(GameVersionFilter) ? 0 : 1)
        + (string.IsNullOrWhiteSpace(LoaderFilter) ? 0 : 1);

    partial void OnSelectedCategoryChanged(ContentTag? value) => OnFacetChanged();

    partial void OnSelectedGameVersionChanged(ContentTag? value) => OnFacetChanged();

    partial void OnSelectedLoaderChanged(ContentTag? value) => OnFacetChanged();

    /// <summary>
    /// The combo boxes are the source of the version and loader filters, so a change there has to
    /// reach the query the search sends.
    /// </summary>
    private void OnFacetChanged()
    {
        GameVersionFilter = SelectedGameVersion is { } version
            && !string.Equals(version.Name, AnyFacet, StringComparison.Ordinal)
                ? version.Name
                : null;
        LoaderFilter = SelectedLoader is { } loader
            && !string.Equals(loader.Name, AnyFacet, StringComparison.Ordinal)
                ? loader.Name
                : null;
        OnPropertyChanged(nameof(ActiveFacetCount));
    }

    /// <summary>Loads the project document, its body, and its gallery for the selected result.</summary>
    private async Task LoadProjectDetailsAsync(ContentSummary? summary)
    {
        ProjectDetails = null;
        ProjectBody = null;
        ProjectMeta = null;
        ProjectIcon = null;
        Gallery.Clear();
        OnPropertyChanged(nameof(HasGallery));
        OnPropertyChanged(nameof(HasProjectBody));
        OnPropertyChanged(nameof(HasProjectMeta));
        OnPropertyChanged(nameof(HasProjectDetails));
        OnPropertyChanged(nameof(HasProjectIcon));

        if (summary is null)
        {
            return;
        }

        if (!ActiveProvider.IsConfigured)
        {
            // A provider without a key cannot answer, so there is nothing to look up.
            return;
        }

        try
        {
            var project = await ActiveProvider
                .GetProjectAsync(summary.ProjectId, CancellationToken.None)
                .ConfigureAwait(true);
            if (project is null)
            {
                return;
            }

            ProjectDetails = project;
            ProjectBody = TrimBody(project.Body);
            ProjectMeta = DescribeProject(project, summary);
            ProjectIcon = await LoadIconAsync(project.IconUrl ?? summary.IconUrl).ConfigureAwait(true);
            OnPropertyChanged(nameof(HasProjectDetails));
            OnPropertyChanged(nameof(HasProjectIcon));
            OnPropertyChanged(nameof(HasProjectBody));
            OnPropertyChanged(nameof(HasProjectMeta));

            foreach (var url in (project.Gallery ?? []).Take(MaxGalleryFrames))
            {
                var frame = new GalleryImageViewModel(url, _services.Http);
                Gallery.Add(frame);
                await frame.LoadAsync(CancellationToken.None).ConfigureAwait(true);
            }

            OnPropertyChanged(nameof(HasGallery));
            RefreshCacheNote();
        }
        catch (Exception exception)
        {
            StatusNote = exception.Message;
        }
    }

    private string TrimBody(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        var text = SimplifyMarkdownForDisplay(body.Trim());
        return text.Length <= MaxBodyCharacters
            ? text
            : text[..MaxBodyCharacters] + Environment.NewLine + Localizer.Get("L.Browse.BodyTruncated");
    }

    /// <summary>
    /// A provider's description is markdown. This is not a renderer: it drops the syntax that would
    /// otherwise show up as noise in a plain text box (heading markers, code fences, rule lines) and
    /// leaves the prose, lists, and links as the author wrote them.
    /// </summary>
    internal static string SimplifyMarkdownForDisplay(string body)
    {
        var lines = body.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var result = new List<string>(lines.Length);
        var inFence = false;

        foreach (var line in lines)
        {
            var trimmed = line.TrimEnd();
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
                result.Add(string.Empty);
                continue;
            }

            if (!inFence && trimmed.Length > 0 && trimmed.All(character => character is '#' or '=' or '-' or ' ' or '*'))
            {
                // A heading underline or a horizontal rule carries no text of its own.
                result.Add(string.Empty);
                continue;
            }

            if (!inFence && trimmed.StartsWith('#'))
            {
                result.Add(trimmed.TrimStart('#').TrimStart());
                continue;
            }

            result.Add(trimmed);
        }

        // Collapse the runs of blank lines the replacements can leave behind.
        var collapsed = new List<string>(result.Count);
        foreach (var line in result)
        {
            if (line.Length == 0 && collapsed.Count > 0 && collapsed[^1].Length == 0)
            {
                continue;
            }

            collapsed.Add(line);
        }

        return string.Join(Environment.NewLine, collapsed).Trim();
    }

    /// <summary>
    /// The facts about a project that are worth reading before installing: licence, who made it, what
    /// it is tagged with, and where its source lives.
    /// </summary>
    private static string DescribeProject(ContentProject project, ContentSummary summary)
    {
        var parts = new List<string>();
        var authors = (project.Authors ?? [])
            .Concat(summary.Author is { Length: > 0 } author ? [author] : Array.Empty<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (authors.Count > 0)
        {
            parts.Add(string.Join(", ", authors));
        }

        if (project.License is { Length: > 0 } license)
        {
            parts.Add(license);
        }

        if (project.Categories.Count > 0)
        {
            parts.Add(string.Join(", ", project.Categories));
        }

        if (project.Loaders.Count > 0)
        {
            parts.Add(string.Join(", ", project.Loaders));
        }

        if (project.GameVersions.Count > 0)
        {
            parts.Add(project.GameVersions.Count > 6
                ? string.Join(", ", project.GameVersions.Take(6)) + " …"
                : string.Join(", ", project.GameVersions));
        }

        return string.Join(" · ", parts);
    }
}
