using System.Globalization;
using System.Text;
using System.Text.Json;
using Ferrite.Core.Net;
using Microsoft.Extensions.Logging;

namespace Ferrite.Core.Content;

/// <summary>
/// CurseForge API client. The complete surface is implemented, but every call needs an API key issued
/// by CurseForge, so live use depends on the user supplying one (see
/// <c>docs/HUMAN_ACTION_REQUIRED.md</c>).
/// </summary>
public sealed class CurseForgeClient : IContentProvider
{
    public const string ProviderName = "curseforge";

    private const int MaxResponseBytes = 32 * 1024 * 1024;
    private const int MaxFilesPerLookup = 200;

    private readonly HttpService _http;
    private readonly ILogger<CurseForgeClient> _logger;
    private readonly Func<string?> _apiKeyAccessor;
    private readonly string _apiBase;

    public CurseForgeClient(
        HttpService http,
        ILogger<CurseForgeClient> logger,
        Func<string?> apiKeyAccessor,
        string apiBase = CurseForgeIds.ApiBase)
    {
        _http = http;
        _logger = logger;
        _apiKeyAccessor = apiKeyAccessor;
        _apiBase = apiBase.TrimEnd('/');
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKeyAccessor());

    public string Name => ProviderName;

    public string? UnavailableReason => IsConfigured
        ? null
        : "CurseForge needs an API key. Add one in Settings; see docs/HUMAN_ACTION_REQUIRED.md.";

    public async Task<ContentSearchResult> SearchAsync(
        ContentSearchQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var parameters = new List<string>
        {
            "gameId=" + CurseForgeIds.MinecraftGameId,
            "searchFilter=" + Uri.EscapeDataString(query.Query ?? string.Empty),
            "index=" + Math.Max(0, query.Offset),
            "pageSize=" + Math.Clamp(query.Limit, 1, 50),
        };

        if (query.ProjectType is { } type && CurseForgeIds.ClassIdFor(type) is { } classId)
        {
            parameters.Add("classId=" + classId);
        }

        if (!string.IsNullOrEmpty(query.GameVersion))
        {
            parameters.Add("gameVersion=" + Uri.EscapeDataString(query.GameVersion));
        }

        if (CurseForgeIds.ModLoaderFor(query.Loader) is { } loaderId)
        {
            parameters.Add("modLoaderType=" + loaderId);
        }

        if (MapSort(query.SortBy) is { } sort)
        {
            parameters.Add("sortField=" + sort.Field);
            parameters.Add("sortOrder=" + sort.Order);
        }

        var json = await GetAsync($"/mods/search?{string.Join('&', parameters)}", cancellationToken)
            .ConfigureAwait(false);
        var data = json.TryGetProperty("data", out var dataElement) ? dataElement : json;

        var hits = new List<ContentSummary>();
        if (data.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in data.EnumerateArray())
            {
                hits.Add(ReadSummary(element));
            }
        }

        var total = json.TryGetProperty("pagination", out var pagination)
            && pagination.TryGetProperty("totalCount", out var totalCount)
            && totalCount.ValueKind == JsonValueKind.Number
                ? totalCount.GetInt64()
                : hits.Count;

        return new ContentSearchResult(total, query.Offset, query.Limit, hits);
    }

    public async Task<ContentProject?> GetProjectAsync(string idOrSlug, CancellationToken cancellationToken)
    {
        if (!int.TryParse(idOrSlug, NumberStyles.Integer, CultureInfo.InvariantCulture, out var modId))
        {
            // CurseForge has no slug lookup, so fall back to an exact-term search.
            var search = await SearchAsync(new ContentSearchQuery(idOrSlug, Limit: 1), cancellationToken)
                .ConfigureAwait(false);
            if (search.Hits.Count == 0
                || !int.TryParse(search.Hits[0].ProjectId, NumberStyles.Integer, CultureInfo.InvariantCulture, out modId))
            {
                return null;
            }
        }

        var json = await GetAsync($"/mods/{modId}", cancellationToken).ConfigureAwait(false);
        return json.TryGetProperty("data", out var data) ? ReadProject(data) : null;
    }

    public async Task<IReadOnlyList<ContentVersion>> GetVersionsAsync(
        string projectId,
        string? gameVersion,
        string? loader,
        CancellationToken cancellationToken)
    {
        if (!int.TryParse(projectId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var modId))
        {
            var project = await GetProjectAsync(projectId, cancellationToken).ConfigureAwait(false);
            if (project is null
                || !int.TryParse(project.ProjectId, NumberStyles.Integer, CultureInfo.InvariantCulture, out modId))
            {
                return [];
            }
        }

        var parameters = new List<string> { "index=0", "pageSize=50" };
        if (!string.IsNullOrEmpty(gameVersion))
        {
            parameters.Add("gameVersion=" + Uri.EscapeDataString(gameVersion));
        }

        if (CurseForgeIds.ModLoaderFor(loader) is { } loaderId)
        {
            parameters.Add("modLoaderType=" + loaderId);
        }

        var json = await GetAsync($"/mods/{modId}/files?{string.Join('&', parameters)}", cancellationToken)
            .ConfigureAwait(false);
        if (!json.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var versions = new List<ContentVersion>();
        foreach (var element in data.EnumerateArray())
        {
            versions.Add(ReadVersion(element));
        }

        return versions;
    }

    /// <summary>Loads one file of one project. CurseForge addresses files per project, not globally.</summary>
    public async Task<ContentVersion?> GetVersionAsync(
        string projectId,
        string versionId,
        CancellationToken cancellationToken)
    {
        if (!int.TryParse(projectId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var modId)
            || !int.TryParse(versionId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var fileId))
        {
            return null;
        }

        var json = await GetAsync($"/mods/{modId}/files/{fileId}", cancellationToken).ConfigureAwait(false);
        return json.TryGetProperty("data", out var data) ? ReadVersion(data) : null;
    }

    /// <summary>
    /// Resolves many files in one request. A modpack manifest lists project/file id pairs and the
    /// bulk endpoint is the documented way to fetch their metadata without a call per file.
    /// </summary>
    public async Task<IReadOnlyList<ContentVersion>> GetFilesAsync(
        IReadOnlyList<int> fileIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fileIds);
        var results = new List<ContentVersion>(fileIds.Count);

        foreach (var chunk in fileIds.Distinct().Chunk(MaxFilesPerLookup))
        {
            var body = JsonSerializer.Serialize(new { fileIds = chunk });
            var json = await PostAsync("/mods/files", body, cancellationToken).ConfigureAwait(false);
            if (!json.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var element in data.EnumerateArray())
            {
                results.Add(ReadVersion(element));
            }
        }

        return results;
    }

    /// <summary>Selects the newest compatible version, preferring full releases.</summary>
    public ContentVersion? SelectBestVersion(
        IReadOnlyList<ContentVersion> versions,
        string? gameVersion,
        string? loader) =>
        ContentCompatibility.SelectBestVersion(versions, gameVersion, loader);

    /// <summary>
    /// Resolves a downloadable URL for a file. CurseForge only returns one when the author allowed
    /// third-party distribution, which is the retail-file restriction.
    /// </summary>
    public async Task<string?> GetDownloadUrlAsync(int modId, int fileId, CancellationToken cancellationToken)
    {
        var json = await GetAsync($"/mods/{modId}/files/{fileId}/download-url", cancellationToken)
            .ConfigureAwait(false);
        return json.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.String
            ? data.GetString()
            : null;
    }

    private async Task<JsonElement> GetAsync(string path, CancellationToken cancellationToken)
    {
        var apiKey = _apiKeyAccessor();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ContentProviderException(
                "CurseForge needs an API key. Add one in Settings; see docs/HUMAN_ACTION_REQUIRED.md.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, _apiBase + path);
        request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");

        var bytes = await _http
            .SendBufferedWithRetryAsync(request, MaxResponseBytes, cancellationToken)
            .ConfigureAwait(false);

        return Parse(bytes);
    }

    private async Task<JsonElement> PostAsync(string path, string body, CancellationToken cancellationToken)
    {
        var apiKey = _apiKeyAccessor();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ContentProviderException(
                "CurseForge needs an API key. Add one in Settings; see docs/HUMAN_ACTION_REQUIRED.md.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, _apiBase + path)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");

        var bytes = await _http
            .SendBufferedWithRetryAsync(request, MaxResponseBytes, cancellationToken)
            .ConfigureAwait(false);

        return Parse(bytes);
    }

    private static JsonElement Parse(byte[] bytes)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes);
            return document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw new ContentProviderException("CurseForge returned malformed JSON.", exception);
        }
    }

    private ContentSummary ReadSummary(JsonElement element)
    {
        var authors = new List<string>();
        if (element.TryGetProperty("authors", out var authorArray) && authorArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var author in authorArray.EnumerateArray())
            {
                if (GetString(author, "name") is { } name)
                {
                    authors.Add(name);
                }
            }
        }

        return new ContentSummary(
            ProviderName,
            Id(element, "id"),
            GetString(element, "slug") ?? string.Empty,
            GetString(element, "name") ?? "Unknown",
            GetString(element, "summary"),
            CurseForgeIds.ProjectTypeFor(GetInt(element, "classId")),
            GetLong(element, "downloadCount") ?? 0,
            GetString(element, "logo", "thumbnailUrl"),
            authors.Count > 0 ? string.Join(", ", authors) : null,
            ReadStringArray(element, "categories"),
            GetDate(element, "dateModified"));
    }

    private ContentProject ReadProject(JsonElement element) => new(
        ProviderName,
        Id(element, "id"),
        GetString(element, "slug") ?? string.Empty,
        GetString(element, "name") ?? "Unknown",
        GetString(element, "summary"),
        GetString(element, "description"),
        CurseForgeIds.ProjectTypeFor(GetInt(element, "classId")),
        GetLong(element, "downloadCount") ?? 0,
        GetString(element, "logo", "thumbnailUrl"),
        null,
        ReadStringArray(element, "categories"),
        ReadStringArray(element, "latestFilesIndexes", "gameVersion"),
        [],
        GetString(element, "links", "websiteUrl"),
        GetString(element, "links", "issuesUrl"));

    private ContentVersion ReadVersion(JsonElement element)
    {
        var files = new List<ContentFile>();
        var fileName = GetString(element, "fileName") ?? "download.jar";
        var downloadUrl = GetString(element, "downloadUrl");
        if (!string.IsNullOrEmpty(downloadUrl))
        {
            files.Add(new ContentFile(
                fileName,
                downloadUrl,
                GetLong(element, "fileLength") ?? 0,
                Primary: true,
                ReadHash(element, algorithm: 1),
                Sha512: null));
        }

        var dependencies = new List<ContentDependency>();
        if (element.TryGetProperty("dependencies", out var dependencyArray)
            && dependencyArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var dependency in dependencyArray.EnumerateArray())
            {
                dependencies.Add(new ContentDependency(
                    Id(dependency, "modId"),
                    null,
                    null,
                    CurseForgeIds.RelationName(GetInt(dependency, "relationType") ?? 3)));
            }
        }

        var (gameVersions, loaders) = ContentCompatibility.SplitVersionTokens(
            ReadStringArray(element, "gameVersions"));

        return new ContentVersion(
            ProviderName,
            Id(element, "id"),
            Id(element, "modId"),
            GetString(element, "displayName") ?? fileName,
            GetString(element, "displayName"),
            null,
            CurseForgeIds.ReleaseTypeName(GetInt(element, "releaseType") ?? 1),
            gameVersions,
            loaders,
            files,
            dependencies,
            GetDate(element, "fileDate"));
    }

    private static string? ReadHash(JsonElement element, int algorithm)
    {
        if (!element.TryGetProperty("hashes", out var hashes) || hashes.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var hash in hashes.EnumerateArray())
        {
            if (GetInt(hash, "algo") == algorithm)
            {
                return GetString(hash, "value");
            }
        }

        return null;
    }

    private static (int Field, string Order)? MapSort(string? sortBy) => sortBy switch
    {
        "downloads" => (6, "desc"),
        "newest" or "updated" => (3, "desc"),
        "follows" => (2, "desc"),
        _ => null,
    };

    private static string Id(JsonElement element, string property) =>
        GetInt(element, property)?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    private static string? GetString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? GetString(JsonElement element, string property, string nested) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value)
            ? GetString(value, nested)
            : null;

    private static int? GetInt(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : null;

    private static long? GetLong(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.Number
            ? value.GetInt64()
            : null;

    private static DateTimeOffset? GetDate(JsonElement element, string property) =>
        GetString(element, property) is { } text
        && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static IReadOnlyList<string> ReadStringArray(JsonElement element, string property)
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

    private static IReadOnlyList<string> ReadStringArray(JsonElement element, string property, string nested)
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
            if (GetString(item, nested) is { } text)
            {
                results.Add(text);
            }
        }

        return results;
    }
}
