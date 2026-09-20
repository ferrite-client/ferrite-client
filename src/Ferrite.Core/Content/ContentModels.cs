namespace Ferrite.Core.Content;

/// <summary>One entry in a provider search result list.</summary>
public sealed record ContentSummary(
    string Provider,
    string ProjectId,
    string Slug,
    string Title,
    string? Description,
    ContentProjectType ProjectType,
    long Downloads,
    string? IconUrl,
    string? Author,
    IReadOnlyList<string> Categories,
    DateTimeOffset? UpdatedAt);

/// <summary>A full project description.</summary>
public sealed record ContentProject(
    string Provider,
    string ProjectId,
    string Slug,
    string Title,
    string? Description,
    string? Body,
    ContentProjectType ProjectType,
    long Downloads,
    string? IconUrl,
    string? License,
    IReadOnlyList<string> Categories,
    IReadOnlyList<string> GameVersions,
    IReadOnlyList<string> Loaders,
    string? SourceUrl,
    string? IssuesUrl);

public sealed record ContentFile(
    string FileName,
    string Url,
    long Size,
    bool Primary,
    string? Sha1,
    string? Sha512);

public sealed record ContentDependency(
    string? ProjectId,
    string? VersionId,
    string? FileName,
    /// <summary>"required", "optional", "incompatible", or "embedded".</summary>
    string Kind);

public sealed record ContentVersion(
    string Provider,
    string VersionId,
    string ProjectId,
    string VersionNumber,
    string? Name,
    string? Changelog,
    /// <summary>"release", "beta", or "alpha".</summary>
    string VersionType,
    IReadOnlyList<string> GameVersions,
    IReadOnlyList<string> Loaders,
    IReadOnlyList<ContentFile> Files,
    IReadOnlyList<ContentDependency> Dependencies,
    DateTimeOffset? PublishedAt)
{
    public ContentFile? PrimaryFile => Files.FirstOrDefault(file => file.Primary) ?? Files.FirstOrDefault();

    public bool IsRelease => string.Equals(VersionType, "release", StringComparison.OrdinalIgnoreCase);
}

public sealed record ContentSearchResult(
    long TotalHits,
    int Offset,
    int Limit,
    IReadOnlyList<ContentSummary> Hits);

public sealed record ContentTag(string Name, string? DisplayName, string? IconUrl);

/// <summary>Filters applied to a provider search.</summary>
public sealed record ContentSearchQuery(
    string Query,
    ContentProjectType? ProjectType = null,
    string? GameVersion = null,
    string? Loader = null,
    IReadOnlyList<string>? Categories = null,
    int Limit = 20,
    int Offset = 0,
    string SortBy = "relevance");
