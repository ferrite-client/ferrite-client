using Ferrite.Core.Net;
using Microsoft.Extensions.Logging;

namespace Ferrite.Core.Content;

/// <summary>
/// Feed The Beast's public pack API. It answers a search with pack ids only, so a search fetches the
/// documents for the ids it returns - bounded, and concurrent so the list is not built one request at
/// a time. FTB needs no key or account.
/// </summary>
public sealed class FtbClient : IContentProvider
{
    public const string ProviderName = "ftb";
    public const string ApiBase = "https://api.modpacks.ch/public";
    public const string WebBase = "https://www.feed-the-beast.com/modpacks";

    /// <summary>Documents fetched per search. The API answers with ids, so each one costs a request.</summary>
    private const int MaxDetailFetches = 12;

    private const int MaxDocumentBytes = 8 * 1024 * 1024;

    private readonly HttpService _http;
    private readonly ILogger<FtbClient> _logger;
    private readonly string _apiBase;

    public FtbClient(HttpService http, ILogger<FtbClient> logger, string? apiBase = null)
    {
        _http = http;
        _logger = logger;
        _apiBase = (apiBase ?? ApiBase).TrimEnd('/');
    }

    public string Name => ProviderName;

    public bool IsConfigured => true;

    public string? UnavailableReason => null;

    public async Task<ContentSearchResult> SearchAsync(
        ContentSearchQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // FTB publishes packs only; an explicit request for another type has no answer here.
        if (query.ProjectType is { } type && type != ContentProjectType.Modpack)
        {
            return new ContentSearchResult(0, 0, query.Limit, []);
        }

        var limit = Math.Clamp(query.Limit <= 0 ? 12 : query.Limit, 1, MaxDetailFetches);
        var term = Uri.EscapeDataString(query.Query ?? string.Empty);
        var search = await _http
            .TryGetJsonAsync<FtbSearchResult>(
                $"{_apiBase}/modpack/search/{limit}?term={term}",
                MaxDocumentBytes,
                cancellationToken)
            .ConfigureAwait(false);

        var ids = (search?.Packs ?? []).Take(limit).ToList();
        if (ids.Count == 0)
        {
            return new ContentSearchResult(search?.Total ?? 0, 0, limit, []);
        }

        var documents = await Task.WhenAll(ids.Select(id => TryGetPackAsync(id, cancellationToken)))
            .ConfigureAwait(false);
        var hits = documents
            .Where(pack => pack is not null)
            .Select(pack => ToSummary(pack!))
            .OrderByDescending(summary => summary.Downloads)
            .ToList();

        return new ContentSearchResult(search?.Total ?? hits.Count, 0, limit, hits);
    }

    public async Task<ContentProject?> GetProjectAsync(string idOrSlug, CancellationToken cancellationToken)
    {
        if (!TryParseId(idOrSlug, out var id))
        {
            return null;
        }

        var pack = await TryGetPackAsync(id, cancellationToken).ConfigureAwait(false);
        if (pack is null)
        {
            return null;
        }

        return new ContentProject(
            ProviderName,
            pack.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            pack.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            pack.Name ?? $"FTB pack {pack.Id}",
            pack.Synopsis,
            pack.Description,
            ContentProjectType.Modpack,
            pack.Installs,
            IconFor(pack),
            License: null,
            Categories: [],
            GameVersions: pack.Versions
                .SelectMany(version => version.Targets)
                .Where(target => string.Equals(target.Type, "game", StringComparison.OrdinalIgnoreCase))
                .Select(target => target.Version ?? string.Empty)
                .Where(version => version.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Loaders: pack.Versions
                .SelectMany(version => version.Targets)
                .Where(target => string.Equals(target.Type, "modloader", StringComparison.OrdinalIgnoreCase))
                .Select(target => target.Name ?? string.Empty)
                .Where(loader => loader.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            SourceUrl: $"{WebBase}/{pack.Id}",
            IssuesUrl: null,
            Authors: pack.Authors
                .Select(author => author.Name ?? string.Empty)
                .Where(name => name.Length > 0)
                .ToList());
    }

    /// <summary>
    /// FTB publishes no facet vocabulary, so every facet gets an empty list and the browser offers
    /// only its own "any".
    /// </summary>
    public Task<IReadOnlyList<ContentTag>> GetTagsAsync(string kind, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ContentTag>>([]);

    /// <summary>
    /// FTB versions come from the pack document, so this needs no extra request per version and can
    /// filter on the instance's version without a file lookup.
    /// </summary>
    public async Task<IReadOnlyList<ContentVersion>> GetVersionsAsync(
        string projectId,
        string? gameVersion,
        string? loader,
        CancellationToken cancellationToken)
    {
        if (!TryParseId(projectId, out var id))
        {
            return [];
        }

        var pack = await TryGetPackAsync(id, cancellationToken).ConfigureAwait(false);
        if (pack is null)
        {
            return [];
        }

        return pack.Versions
            .Select(version => ToContentVersion(pack, version))
            .Where(version => gameVersion is null
                || version.GameVersions.Contains(gameVersion, StringComparer.OrdinalIgnoreCase))
            .Where(version => loader is null
                || version.Loaders.Contains(loader, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Loads one version's file list. The browser calls this when it installs, so the cost of the file
    /// list is paid only for the version actually chosen.
    /// </summary>
    public async Task<ContentVersion?> GetVersionAsync(
        string projectId,
        string versionId,
        CancellationToken cancellationToken)
    {
        if (!TryParseId(projectId, out var packId) || !TryParseId(versionId, out var versionNumber))
        {
            return null;
        }

        var files = await TryGetVersionAsync(packId, versionNumber, cancellationToken).ConfigureAwait(false);
        if (files is null)
        {
            return null;
        }

        return ToContentVersion(packId, files);
    }

    public ContentVersion? SelectBestVersion(
        IReadOnlyList<ContentVersion> versions,
        string? gameVersion,
        string? loader)
    {
        ArgumentNullException.ThrowIfNull(versions);
        if (versions.Count == 0)
        {
            return null;
        }

        var matchingVersion = gameVersion is { Length: > 0 }
            ? versions.Where(version => version.GameVersions.Contains(gameVersion, StringComparer.OrdinalIgnoreCase))
                .ToList()
            : versions.ToList();
        if (matchingVersion.Count == 0)
        {
            return null;
        }

        if (loader is { Length: > 0 })
        {
            var matchingLoader = matchingVersion
                .Where(version => version.Loaders.Contains(loader, StringComparer.OrdinalIgnoreCase))
                .ToList();
            if (matchingLoader.Count > 0)
            {
                matchingVersion = matchingLoader;
            }
        }

        // FTB orders versions oldest first, so the newest is the last one.
        return matchingVersion[^1];
    }

    /// <summary>The version's files, for the installer. Package-visible so the installer reuses it.</summary>
    internal async Task<FtbVersionFiles?> TryGetVersionAsync(
        int packId,
        int versionId,
        CancellationToken cancellationToken)
    {
        var files = await _http
            .TryGetJsonAsync<FtbVersionFiles>(
                $"{_apiBase}/modpack/{packId}/{versionId}",
                MaxDocumentBytes,
                cancellationToken)
            .ConfigureAwait(false);
        if (files is null)
        {
            _logger.LogInformation("FTB version {Pack}/{Version} could not be read", packId, versionId);
        }

        return files;
    }

    private async Task<FtbPack?> TryGetPackAsync(int id, CancellationToken cancellationToken)
    {
        try
        {
            return await _http
                .TryGetJsonAsync<FtbPack>($"{_apiBase}/modpack/{id}", MaxDocumentBytes, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpException exception)
        {
            // One unreadable pack in a result list must not fail the whole search.
            _logger.LogInformation("FTB pack {Id} could not be read: {Reason}", id, exception.Message);
            return null;
        }
    }

    private static bool TryParseId(string? value, out int id) =>
        int.TryParse(value, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out id) && id > 0;

    private static string? IconFor(FtbPack pack) =>
        pack.Art.FirstOrDefault(art => string.Equals(art.Type, "square", StringComparison.OrdinalIgnoreCase))?.Url
        ?? pack.Art.FirstOrDefault()?.Url;

    private static string? LoaderOf(FtbVersion version) => version.Targets
        .FirstOrDefault(target => string.Equals(target.Type, "modloader", StringComparison.OrdinalIgnoreCase))
        ?.Name;

    private static string? GameVersionOf(FtbVersion version) => version.Targets
        .FirstOrDefault(target => string.Equals(target.Type, "game", StringComparison.OrdinalIgnoreCase))
        ?.Version;

    private ContentSummary ToSummary(FtbPack pack) => new(
        ProviderName,
        pack.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
        pack.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
        pack.Name ?? $"FTB pack {pack.Id}",
        pack.Synopsis,
        ContentProjectType.Modpack,
        pack.Installs,
        IconFor(pack),
        pack.Authors.FirstOrDefault()?.Name,
        [],
        UpdatedAt: null);

    private static ContentVersion ToContentVersion(FtbPack pack, FtbVersion version) => new(
        ProviderName,
        version.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
        pack.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
        version.Name ?? version.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
        version.Name,
        Changelog: null,
        VersionType: string.Equals(version.Type, "release", StringComparison.OrdinalIgnoreCase)
            ? "release"
            : version.Type ?? "release",
        GameVersions: GameVersionOf(version) is { Length: > 0 } game ? [game] : [],
        Loaders: LoaderOf(version) is { Length: > 0 } loader ? [loader] : [],
        Files: [],
        Dependencies: [],
        PublishedAt: null);

    /// <summary>A version with its file list, which is what the installer needs.</summary>
    private static ContentVersion ToContentVersion(int packId, FtbVersionFiles files) => new(
        ProviderName,
        files.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
        packId.ToString(System.Globalization.CultureInfo.InvariantCulture),
        files.Name ?? files.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
        files.Name,
        files.Changelog,
        VersionType: "release",
        GameVersions: files.Targets
            .Where(target => string.Equals(target.Type, "game", StringComparison.OrdinalIgnoreCase))
            .Select(target => target.Version ?? string.Empty)
            .Where(version => version.Length > 0)
            .ToList(),
        Loaders: files.Targets
            .Where(target => string.Equals(target.Type, "modloader", StringComparison.OrdinalIgnoreCase))
            .Select(target => target.Name ?? string.Empty)
            .Where(loader => loader.Length > 0)
            .ToList(),
        Files: files.Files.Select(file => new ContentFile(
            file.Name ?? string.Empty,
            file.Url ?? string.Empty,
            file.Size,
            Primary: false,
            Sha1: file.Sha1,
            Sha512: null)).ToList(),
        Dependencies: [],
        PublishedAt: null);
}
