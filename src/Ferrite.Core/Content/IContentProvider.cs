namespace Ferrite.Core.Content;

/// <summary>
/// A browsable content source (Modrinth, CurseForge). The browser and the installer work against
/// this shape so both providers share version selection, dependency resolution, and installation.
/// </summary>
public interface IContentProvider
{
    /// <summary>Stable token recorded on installed content, e.g. <c>modrinth</c>.</summary>
    string Name { get; }

    /// <summary>False when the provider cannot be queried yet, such as a missing API key.</summary>
    bool IsConfigured { get; }

    /// <summary>User-facing reason the provider is unusable, or null when it is ready.</summary>
    string? UnavailableReason { get; }

    Task<ContentSearchResult> SearchAsync(ContentSearchQuery query, CancellationToken cancellationToken);

    Task<ContentProject?> GetProjectAsync(string idOrSlug, CancellationToken cancellationToken);

    Task<IReadOnlyList<ContentVersion>> GetVersionsAsync(
        string projectId,
        string? gameVersion,
        string? loader,
        CancellationToken cancellationToken);

    /// <summary>
    /// Loads one specific version. Providers that address files by project (CurseForge) use
    /// <paramref name="projectId"/> to disambiguate; providers with global version ids ignore it.
    /// </summary>
    Task<ContentVersion?> GetVersionAsync(
        string projectId,
        string versionId,
        CancellationToken cancellationToken);

    /// <summary>Selects the newest version compatible with the instance, or null when none is.</summary>
    ContentVersion? SelectBestVersion(
        IReadOnlyList<ContentVersion> versions,
        string? gameVersion,
        string? loader);
}
