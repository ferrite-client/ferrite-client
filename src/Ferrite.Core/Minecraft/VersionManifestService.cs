using System.Text.Json;
using Ferrite.Core.Json;
using Ferrite.Core.Net;
using Ferrite.Core.Platform;
using Ferrite.Core.Util;
using Microsoft.Extensions.Logging;

namespace Ferrite.Core.Minecraft;

/// <summary>
/// Fetches and caches the Mojang version manifest and individual version documents. A cached
/// manifest is used when the network is unavailable, so an offline launcher can still list and
/// launch versions that are already installed.
/// </summary>
public sealed class VersionManifestService
{
    public const string DefaultManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

    private const int MaxManifestBytes = 16 * 1024 * 1024;
    private const int MaxVersionDocumentBytes = 32 * 1024 * 1024;

    private static readonly TimeSpan DefaultRefreshInterval = TimeSpan.FromMinutes(30);

    private readonly HttpService _http;
    private readonly AppPaths _paths;
    private readonly ILogger<VersionManifestService> _logger;
    private readonly string _manifestUrl;
    private readonly TimeSpan _refreshInterval;

    private VersionManifest? _cachedManifest;
    private DateTimeOffset _cachedAt = DateTimeOffset.MinValue;
    private bool _lastFetchUsedCache;

    public VersionManifestService(
        HttpService http,
        AppPaths paths,
        ILogger<VersionManifestService> logger,
        string manifestUrl = DefaultManifestUrl,
        TimeSpan? refreshInterval = null)
    {
        _http = http;
        _paths = paths;
        _logger = logger;
        _manifestUrl = manifestUrl;
        _refreshInterval = refreshInterval ?? DefaultRefreshInterval;
    }

    /// <summary>True when the most recent manifest read came from disk because the network failed.</summary>
    public bool LastFetchUsedCache => _lastFetchUsedCache;

    public string ManifestCachePath => _paths.CacheFile("version_manifest_v2.json");

    public async Task<VersionManifest> GetManifestAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        if (!forceRefresh && _cachedManifest is not null && DateTimeOffset.UtcNow - _cachedAt < _refreshInterval)
        {
            return _cachedManifest;
        }

        try
        {
            var bytes = await _http.GetBytesAsync(_manifestUrl, MaxManifestBytes, cancellationToken).ConfigureAwait(false);
            var manifest = ParseManifest(bytes, _manifestUrl);
            await AtomicFile.WriteAllBytesAsync(ManifestCachePath, bytes, cancellationToken).ConfigureAwait(false);
            _cachedManifest = manifest;
            _cachedAt = DateTimeOffset.UtcNow;
            _lastFetchUsedCache = false;
            _logger.LogInformation(
                "Version manifest refreshed: {Count} versions, latest release {Release}",
                manifest.Versions.Count,
                manifest.Latest?.Release);
            return manifest;
        }
        catch (Exception exception) when (exception is HttpException or IOException or VersionMetadataException)
        {
            var cached = await TryLoadCachedManifestAsync(cancellationToken).ConfigureAwait(false);
            if (cached is null)
            {
                throw;
            }

            _logger.LogWarning(exception, "Version manifest unavailable; using the cached copy");
            _cachedManifest = cached;
            _cachedAt = DateTimeOffset.UtcNow;
            _lastFetchUsedCache = true;
            return cached;
        }
    }

    public async Task<VersionManifest?> TryLoadCachedManifestAsync(CancellationToken cancellationToken)
    {
        var path = ManifestCachePath;
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var bytes = await AtomicFile.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            return ParseManifest(bytes, path);
        }
        catch (Exception exception) when (exception is JsonException or IOException or VersionMetadataException)
        {
            _logger.LogWarning(exception, "Cached version manifest could not be read");
            return null;
        }
    }

    /// <summary>Downloads a Mojang version document into the shared store and returns it parsed.</summary>
    public async Task<VersionDocument> GetMojangVersionAsync(
        VersionManifestEntry entry,
        CancellationToken cancellationToken)
    {
        var target = _paths.VersionJsonFile(entry.Id);
        if (File.Exists(target))
        {
            var existingHash = string.IsNullOrEmpty(entry.Sha1)
                ? null
                : await Hashing.HashFileSha1Async(target, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(entry.Sha1)
                || string.Equals(existingHash, entry.Sha1, StringComparison.OrdinalIgnoreCase))
            {
                var cached = await TryReadLocalVersionAsync(entry.Id, cancellationToken).ConfigureAwait(false);
                if (cached is not null)
                {
                    return cached;
                }
            }

            _logger.LogInformation("Cached metadata for {Version} is stale; refreshing", entry.Id);
        }

        var bytes = await _http
            .GetBytesAsync(entry.Url, MaxVersionDocumentBytes, cancellationToken)
            .ConfigureAwait(false);
        await AtomicFile.WriteAllBytesAsync(target, bytes, cancellationToken).ConfigureAwait(false);
        return ParseVersionDocument(bytes, entry.Url);
    }

    /// <summary>Reads a version document from the shared store, or null when it is absent.</summary>
    public async Task<VersionDocument?> TryReadLocalVersionAsync(string versionId, CancellationToken cancellationToken)
    {
        var path = _paths.VersionJsonFile(versionId);
        if (!File.Exists(path))
        {
            return null;
        }

        var bytes = await AtomicFile.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        return ParseVersionDocument(bytes, path);
    }

    public async Task WriteLocalVersionAsync(
        string versionId,
        VersionDocument document,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(document, JsonDefaults.Document);
        await AtomicFile
            .WriteAllTextAsync(_paths.VersionJsonFile(versionId), json, cancellationToken)
            .ConfigureAwait(false);
    }

    public static VersionManifest ParseManifest(byte[] bytes, string source)
    {
        try
        {
            var manifest = JsonSerializer.Deserialize<VersionManifest>(bytes, JsonDefaults.Remote);
            if (manifest is null || manifest.Versions.Count == 0)
            {
                throw new VersionMetadataException($"Version manifest from {source} contained no versions.");
            }

            return manifest;
        }
        catch (JsonException exception)
        {
            throw new VersionMetadataException($"Version manifest from {source} is not valid JSON.", exception);
        }
    }

    public static VersionDocument ParseVersionDocument(byte[] bytes, string source)
    {
        try
        {
            var document = JsonSerializer.Deserialize<VersionDocument>(bytes, JsonDefaults.Remote);
            if (document is null)
            {
                throw new VersionMetadataException($"Version document from {source} was empty.");
            }

            return document;
        }
        catch (JsonException exception)
        {
            throw new VersionMetadataException($"Version document from {source} is not valid JSON.", exception);
        }
    }
}
