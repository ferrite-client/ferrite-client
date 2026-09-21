using Ferrite.Core.Net;
using Microsoft.Extensions.Logging;

namespace Ferrite.Core.Content;

/// <summary>
/// Network-first cache in front of a provider. A successful call refreshes the cache; a failed call
/// falls back to whatever was cached and records that fact, so the browser can keep working offline
/// and say how old the data is instead of showing an empty list.
/// </summary>
public sealed class CachedContentProvider : IContentProvider
{
    private readonly IContentProvider _inner;
    private readonly ContentCache _cache;
    private readonly ILogger<CachedContentProvider> _logger;

    public CachedContentProvider(
        IContentProvider inner,
        ContentCache cache,
        ILogger<CachedContentProvider> logger)
    {
        _inner = inner;
        _cache = cache;
        _logger = logger;
    }

    public string Name => _inner.Name;

    public bool IsConfigured => _inner.IsConfigured;

    public string? UnavailableReason => _inner.UnavailableReason;

    /// <summary>
    /// Set when the most recent call was served from the cache. The browser reads it immediately
    /// after awaiting a call; it is not meant to be observed concurrently.
    /// </summary>
    public CacheHit? LastCacheHit { get; private set; }

    public async Task<ContentSearchResult> SearchAsync(
        ContentSearchQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var key = ContentCache.KeyFor(
            Name,
            "search",
            query.Query,
            query.ProjectType,
            query.GameVersion,
            query.Loader,
            query.Limit,
            query.Offset,
            query.SortBy);

        return await LoadAsync(
                key,
                () => _inner.SearchAsync(query, cancellationToken),
                static result => result.Hits.Count > 0,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ContentProject?> GetProjectAsync(string idOrSlug, CancellationToken cancellationToken)
    {
        var key = ContentCache.KeyFor(Name, "project", idOrSlug);
        var envelope = await LoadAsync(
                key,
                async () => new ProjectEnvelope(await _inner.GetProjectAsync(idOrSlug, cancellationToken)
                    .ConfigureAwait(false)),
                static value => value.Project is not null,
                cancellationToken)
            .ConfigureAwait(false);
        return envelope.Project;
    }

    public async Task<IReadOnlyList<ContentVersion>> GetVersionsAsync(
        string projectId,
        string? gameVersion,
        string? loader,
        CancellationToken cancellationToken)
    {
        var key = ContentCache.KeyFor(Name, "versions", projectId, gameVersion, loader);
        return await LoadAsync(
                key,
                () => _inner.GetVersionsAsync(projectId, gameVersion, loader, cancellationToken),
                static versions => versions.Count > 0,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ContentVersion?> GetVersionAsync(
        string projectId,
        string versionId,
        CancellationToken cancellationToken)
    {
        var key = ContentCache.KeyFor(Name, "version", projectId, versionId);
        var envelope = await LoadAsync(
                key,
                async () => new VersionEnvelope(await _inner
                    .GetVersionAsync(projectId, versionId, cancellationToken)
                    .ConfigureAwait(false)),
                static value => value.Version is not null,
                cancellationToken)
            .ConfigureAwait(false);
        return envelope.Version;
    }

    public ContentVersion? SelectBestVersion(
        IReadOnlyList<ContentVersion> versions,
        string? gameVersion,
        string? loader) => _inner.SelectBestVersion(versions, gameVersion, loader);

    private async Task<T> LoadAsync<T>(
        string key,
        Func<Task<T>> fetch,
        Func<T, bool> isUsable,
        CancellationToken cancellationToken)
        where T : class
    {
        LastCacheHit = null;
        try
        {
            var value = await fetch().ConfigureAwait(false);
            if (isUsable(value))
            {
                _cache.Write(key, value);
            }

            return value;
        }
        catch (Exception exception) when (IsOffline(exception, cancellationToken))
        {
            if (_cache.TryRead<T>(key, out var cached, out var hit) && isUsable(cached))
            {
                LastCacheHit = hit;
                _logger.LogInformation(
                    "{Provider} served {Key} from cache (age {Age}) after {Error}",
                    Name,
                    key,
                    hit.Age,
                    exception.Message);
                return cached;
            }

            throw new ContentProviderException(
                $"{Name} could not be reached and nothing is cached for this request. "
                + "Check your connection and try again.",
                exception);
        }
    }

    /// <summary>
    /// Only connectivity failures and service-side errors fall back to the cache. A 404, a malformed
    /// response, or a rejected key is a real answer, and hiding it behind stale data would be
    /// misleading.
    /// </summary>
    private static bool IsOffline(Exception exception, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested
        && exception switch
        {
            HttpRequestException => true,
            TaskCanceledException => true,
            TimeoutException => true,
            // No status code means the transfer itself failed; 408, 429, and 5xx mean try later.
            HttpException http => http.StatusCode is null or 408 or 429 or >= 500,
            _ => false,
        };

    /// <summary>
    /// The cache stores objects, so a provider answer that may legitimately be null is wrapped.
    /// </summary>
    private sealed record ProjectEnvelope(ContentProject? Project);

    private sealed record VersionEnvelope(ContentVersion? Version);
}
