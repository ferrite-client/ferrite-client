using System.Text.Json;
using Ferrite.Core.Net;
using Microsoft.Extensions.Logging;

namespace Ferrite.Core.Content;

/// <summary>
/// Modrinth API v2 client. Endpoint shapes were read from the live service; see
/// <c>docs/RESEARCH.md</c> section 5.
/// </summary>
public sealed class ModrinthClient : IContentProvider
{
    public const string ProviderName = "modrinth";
    public const string ApiBase = "https://api.modrinth.com/v2";

    private const int MaxResponseBytes = 32 * 1024 * 1024;

    private readonly HttpService _http;
    private readonly ILogger<ModrinthClient> _logger;
    private readonly string _apiBase;

    public ModrinthClient(HttpService http, ILogger<ModrinthClient> logger, string apiBase = ApiBase)
    {
        _http = http;
        _logger = logger;
        _apiBase = apiBase.TrimEnd('/');
    }

    public string Name => ProviderName;

    public bool IsConfigured => true;

    public string? UnavailableReason => null;

    public async Task<ContentSearchResult> SearchAsync(
        ContentSearchQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var facets = new List<string>();
        if (query.ProjectType is { } type)
        {
            facets.Add(JsonSerializer.Serialize(new[] { $"project_type:{type.ToApiValue()}" }));
        }

        if (!string.IsNullOrEmpty(query.GameVersion))
        {
            facets.Add(JsonSerializer.Serialize(new[] { $"versions:{query.GameVersion}" }));
        }

        if (!string.IsNullOrEmpty(query.Loader))
        {
            facets.Add(JsonSerializer.Serialize(new[] { $"categories:{query.Loader}" }));
        }

        foreach (var category in query.Categories ?? [])
        {
            facets.Add(JsonSerializer.Serialize(new[] { $"categories:{category}" }));
        }

        var url = $"{_apiBase}/search?query={Uri.EscapeDataString(query.Query)}"
            + $"&limit={Math.Clamp(query.Limit, 1, 100)}&offset={Math.Max(0, query.Offset)}"
            + $"&index={Uri.EscapeDataString(query.SortBy)}";
        if (facets.Count > 0)
        {
            url += "&facets=" + Uri.EscapeDataString("[" + string.Join(',', facets) + "]");
        }

        _logger.LogDebug("Modrinth search: {Query} (offset {Offset})", query.Query, query.Offset);
        var json = await _http.GetStringAsync(url, MaxResponseBytes, cancellationToken).ConfigureAwait(false);
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var hits = new List<ContentSummary>();
            if (root.TryGetProperty("hits", out var hitArray))
            {
                foreach (var hit in hitArray.EnumerateArray())
                {
                    hits.Add(ReadSummary(hit));
                }
            }

            return new ContentSearchResult(
                GetLong(root, "total_hits") ?? hits.Count,
                (int)(GetLong(root, "offset") ?? query.Offset),
                (int)(GetLong(root, "limit") ?? query.Limit),
                hits);
        }
        catch (JsonException exception)
        {
            throw new ContentProviderException("Modrinth search returned malformed JSON.", exception);
        }
    }

    public async Task<ContentProject?> GetProjectAsync(string idOrSlug, CancellationToken cancellationToken)
    {
        var url = $"{_apiBase}/project/{Uri.EscapeDataString(idOrSlug)}";
        var json = await _http
            .TryGetJsonElementAsync(url, MaxResponseBytes, cancellationToken)
            .ConfigureAwait(false);
        return json is null ? null : ReadProject(json.Value);
    }

    public async Task<IReadOnlyList<ContentVersion>> GetVersionsAsync(
        string idOrSlug,
        string? gameVersion,
        string? loader,
        CancellationToken cancellationToken)
    {
        var url = $"{_apiBase}/project/{Uri.EscapeDataString(idOrSlug)}/version";
        var filters = new List<string>();
        if (!string.IsNullOrEmpty(loader))
        {
            filters.Add("loaders=" + Uri.EscapeDataString(JsonSerializer.Serialize(new[] { loader })));
        }

        if (!string.IsNullOrEmpty(gameVersion))
        {
            filters.Add("game_versions=" + Uri.EscapeDataString(JsonSerializer.Serialize(new[] { gameVersion })));
        }

        if (filters.Count > 0)
        {
            url += "?" + string.Join('&', filters);
        }

        var json = await _http.GetStringAsync(url, MaxResponseBytes, cancellationToken).ConfigureAwait(false);
        try
        {
            using var document = JsonDocument.Parse(json);
            var versions = new List<ContentVersion>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                versions.Add(ReadVersion(element));
            }

            return versions;
        }
        catch (JsonException exception)
        {
            throw new ContentProviderException("Modrinth version list returned malformed JSON.", exception);
        }
    }

    public async Task<ContentVersion?> GetVersionAsync(string versionId, CancellationToken cancellationToken)
    {
        var url = $"{_apiBase}/version/{Uri.EscapeDataString(versionId)}";
        var json = await _http
            .TryGetJsonElementAsync(url, MaxResponseBytes, cancellationToken)
            .ConfigureAwait(false);
        return json is null ? null : ReadVersion(json.Value);
    }

    /// <summary>Modrinth version ids are globally unique, so the project id is not needed.</summary>
    Task<ContentVersion?> IContentProvider.GetVersionAsync(
        string projectId,
        string versionId,
        CancellationToken cancellationToken) => GetVersionAsync(versionId, cancellationToken);

    public async Task<IReadOnlyList<ContentVersion>> GetVersionsByIdsAsync(
        IReadOnlyList<string> versionIds,
        CancellationToken cancellationToken)
    {
        if (versionIds.Count == 0)
        {
            return [];
        }

        var url = $"{_apiBase}/versions?ids=" + Uri.EscapeDataString(JsonSerializer.Serialize(versionIds));
        var json = await _http.GetStringAsync(url, MaxResponseBytes, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(json);
        var versions = new List<ContentVersion>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            versions.Add(ReadVersion(element));
        }

        return versions;
    }

    public async Task<IReadOnlyList<ContentTag>> GetTagsAsync(string kind, CancellationToken cancellationToken)
    {
        var url = $"{_apiBase}/tag/{Uri.EscapeDataString(kind)}";
        var json = await _http.GetStringAsync(url, MaxResponseBytes, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(json);
        var tags = new List<ContentTag>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            // Most facets name their entries with "name"; the game-version facet uses "version"
            // (with "version_type" alongside it), which is why it reads both.
            var name = GetString(element, "name") ?? GetString(element, "version");
            if (!string.IsNullOrEmpty(name))
            {
                tags.Add(new ContentTag(
                    name,
                    GetString(element, "display_name") ?? name,
                    GetString(element, "icon")));
            }
        }

        return tags;
    }

    /// <summary>Selects the best version for an instance, refusing incompatible candidates.</summary>
    public ContentVersion? SelectBestVersion(
        IReadOnlyList<ContentVersion> versions,
        string? gameVersion,
        string? loader,
        bool preferRelease = true) =>
        ContentCompatibility.SelectBestVersion(versions, gameVersion, loader, preferRelease);

    ContentVersion? IContentProvider.SelectBestVersion(
        IReadOnlyList<ContentVersion> versions,
        string? gameVersion,
        string? loader) => SelectBestVersion(versions, gameVersion, loader);

    /// <summary>Never returns true for a version that does not match the instance's game/loader.</summary>
    public static bool IsCompatible(ContentVersion version, string? gameVersion, string? loader) =>
        ContentCompatibility.IsCompatible(version, gameVersion, loader);

    private ContentSummary ReadSummary(JsonElement element)
    {
        var type = ContentProjectTypes.Parse(GetString(element, "project_type"));
        return new ContentSummary(
            ProviderName,
            GetString(element, "project_id") ?? string.Empty,
            GetString(element, "slug") ?? string.Empty,
            GetString(element, "title") ?? "Unknown",
            GetString(element, "description"),
            type,
            GetLong(element, "downloads") ?? 0,
            GetString(element, "icon_url"),
            GetString(element, "author"),
            GetStrings(element, "categories"),
            GetDate(element, "date_modified"));
    }

    private ContentProject ReadProject(JsonElement element)
    {
        var type = ContentProjectTypes.Parse(GetString(element, "project_type"));
        var license = element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty("license", out var licenseElement)
            && licenseElement.ValueKind == JsonValueKind.Object
                ? GetString(licenseElement, "id")
                : null;

        return new ContentProject(
            ProviderName,
            GetString(element, "id") ?? string.Empty,
            GetString(element, "slug") ?? string.Empty,
            GetString(element, "title") ?? "Unknown",
            GetString(element, "description"),
            GetString(element, "body"),
            type,
            GetLong(element, "downloads") ?? 0,
            GetString(element, "icon_url"),
            license,
            GetStrings(element, "categories"),
            GetStrings(element, "game_versions"),
            GetStrings(element, "loaders"),
            GetString(element, "source_url"),
            GetString(element, "issues_url"),
            ReadGallery(element));
    }

    /// <summary>Gallery entries that are images, in the order the project lists them.</summary>
    private static List<string> ReadGallery(JsonElement element)
    {
        var images = new List<string>();
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty("gallery", out var gallery)
            || gallery.ValueKind != JsonValueKind.Array)
        {
            return images;
        }

        foreach (var item in gallery.EnumerateArray())
        {
            // A Modrinth gallery also carries video links, whose URLs are not images.
            var url = GetString(item, "url");
            if (url is { Length: > 0 } && IsImageUrl(url))
            {
                images.Add(url);
            }
        }

        return images;
    }

    private static bool IsImageUrl(string url) =>
        url.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
        || url.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
        || url.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
        || url.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)
        || url.EndsWith(".gif", StringComparison.OrdinalIgnoreCase);

    private ContentVersion ReadVersion(JsonElement element)
    {
        var files = new List<ContentFile>();
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("files", out var fileArray))
        {
            foreach (var file in fileArray.EnumerateArray())
            {
                var hashes = file.TryGetProperty("hashes", out var hashElement) ? hashElement : default;
                files.Add(new ContentFile(
                    GetString(file, "filename") ?? "download.jar",
                    GetString(file, "url") ?? string.Empty,
                    GetLong(file, "size") ?? 0,
                    file.TryGetProperty("primary", out var primary) && primary.ValueKind == JsonValueKind.True,
                    GetString(hashes, "sha1"),
                    GetString(hashes, "sha512")));
            }
        }

        var dependencies = new List<ContentDependency>();
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty("dependencies", out var dependencyArray))
        {
            foreach (var dependency in dependencyArray.EnumerateArray())
            {
                dependencies.Add(new ContentDependency(
                    GetString(dependency, "project_id"),
                    GetString(dependency, "version_id"),
                    GetString(dependency, "file_name"),
                    GetString(dependency, "dependency_type") ?? "required"));
            }
        }

        return new ContentVersion(
            ProviderName,
            GetString(element, "id") ?? string.Empty,
            GetString(element, "project_id") ?? string.Empty,
            GetString(element, "version_number") ?? string.Empty,
            GetString(element, "name"),
            GetString(element, "changelog"),
            GetString(element, "version_type") ?? "release",
            GetStrings(element, "game_versions"),
            GetStrings(element, "loaders"),
            files,
            dependencies,
            GetDate(element, "date_published"));
    }

    private static string? GetString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static long? GetLong(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.Number
            ? value.GetInt64()
            : null;

    private static DateTimeOffset? GetDate(JsonElement element, string property) =>
        GetString(element, property) is { } text
        && DateTimeOffset.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static IReadOnlyList<string> GetStrings(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(property, out var value)
            || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var results = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { } text)
            {
                results.Add(text);
            }
        }

        return results;
    }
}
