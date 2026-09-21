using Ferrite.Core.Content;
using Ferrite.Core.Net;
using Ferrite.Core.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ferrite.Core.Tests;

/// <summary>
/// The offline fallback in front of a content provider: a successful call is cached, a later
/// network failure serves the cached copy and says so, and a real answer is never hidden by stale
/// data.
/// </summary>
public sealed class ContentCacheTests : IAsyncLifetime
{
    private const string SearchBody = """
        {
          "hits": [
            {
              "project_id": "AANobbMI",
              "slug": "sodium",
              "title": "Sodium",
              "description": "Rendering engine",
              "project_type": "mod",
              "downloads": 42,
              "icon_url": "https://example.invalid/icon.png",
              "author": "jellysquid3",
              "categories": [ "optimization" ],
              "date_modified": "2026-08-01T00:00:00Z"
            }
          ],
          "total_hits": 1,
          "offset": 0,
          "limit": 20
        }
        """;

    private readonly string _workspace;
    private TestHttpServer _server = null!;
    private HttpService _http = null!;
    private ContentCache _cache = null!;

    public ContentCacheTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), "ferrite-cache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspace);
    }

    public ValueTask InitializeAsync()
    {
        _server = new TestHttpServer();
        _http = new HttpService(new HttpServiceOptions(), NullLogger<HttpService>.Instance);
        _cache = new ContentCache(_workspace, NullLogger<ContentCache>.Instance);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await _server.DisposeAsync();
        try
        {
            Directory.Delete(_workspace, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task A_live_answer_is_cached_and_reused_when_the_provider_is_unreachable()
    {
        _server.AddHandler("GET", "/v2/search", _ => new TestResponse(200, SearchBody));
        var provider = CreateProvider();

        var live = await provider.SearchAsync(
            new ContentSearchQuery("sodium"),
            TestContext.Current.CancellationToken);
        Assert.Single(live.Hits);
        Assert.Null(provider.LastCacheHit);

        // Point the client at a port nothing is listening on: the host is now unreachable.
        var offline = CreateProvider(baseUrl: "http://127.0.0.1:1/v2");
        var cached = await offline.SearchAsync(
            new ContentSearchQuery("sodium"),
            TestContext.Current.CancellationToken);

        Assert.Single(cached.Hits);
        Assert.Equal("Sodium", cached.Hits[0].Title);
        Assert.NotNull(offline.LastCacheHit);
        Assert.False(string.IsNullOrWhiteSpace(offline.LastCacheHit!.AgeText));
    }

    [Fact]
    public async Task Nothing_cached_means_a_clear_error_rather_than_an_empty_result()
    {
        var offline = CreateProvider(baseUrl: "http://127.0.0.1:1/v2");

        var exception = await Assert.ThrowsAsync<ContentProviderException>(() => offline.SearchAsync(
            new ContentSearchQuery("never fetched"),
            TestContext.Current.CancellationToken));

        Assert.Contains("could not be reached", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_cached_result_is_not_reused_for_a_different_query()
    {
        _server.AddHandler("GET", "/v2/search", _ => new TestResponse(200, SearchBody));
        await CreateProvider().SearchAsync(
            new ContentSearchQuery("sodium"),
            TestContext.Current.CancellationToken);

        var offline = CreateProvider(baseUrl: "http://127.0.0.1:1/v2");
        await Assert.ThrowsAsync<ContentProviderException>(() => offline.SearchAsync(
            new ContentSearchQuery("something else"),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_server_error_is_not_hidden_behind_stale_data()
    {
        _server.AddHandler("GET", "/v2/search", _ => new TestResponse(200, SearchBody));
        await CreateProvider().SearchAsync(
            new ContentSearchQuery("sodium"),
            TestContext.Current.CancellationToken);

        // A 404 is a real answer. It must surface, not be replaced by the cached success.
        _server.AddHandler("GET", "/v2/search", _ => new TestResponse(404, """{"error":"gone"}"""));
        var provider = CreateProvider();

        await Assert.ThrowsAsync<HttpException>(() => provider.SearchAsync(
            new ContentSearchQuery("sodium"),
            TestContext.Current.CancellationToken));
        Assert.Null(provider.LastCacheHit);
    }

    [Fact]
    public void Cache_keys_separate_providers_and_arguments()
    {
        var first = ContentCache.KeyFor("modrinth", "search", "sodium", null, "1.21.1");
        var second = ContentCache.KeyFor("modrinth", "search", "sodium", null, "1.20.1");
        var other = ContentCache.KeyFor("curseforge", "search", "sodium", null, "1.21.1");

        Assert.NotEqual(first, second);
        Assert.NotEqual(first, other);

        // The same request must produce the same key, or the cache would never hit.
        Assert.Equal(first, ContentCache.KeyFor("modrinth", "search", "sodium", null, "1.21.1"));
    }

    [Fact]
    public void An_unreadable_cache_entry_is_ignored()
    {
        var key = ContentCache.KeyFor("modrinth", "search", "sodium");
        var directory = Path.Combine(_workspace, "content");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, key + ".json"), "{ this is not json");

        Assert.False(_cache.TryRead<ContentSearchResult>(key, out _, out _));
    }

    private CachedContentProvider CreateProvider(string? baseUrl = null) => new(
        new ModrinthClient(
            _http,
            NullLogger<ModrinthClient>.Instance,
            baseUrl ?? _server.BaseUrl + "/v2"),
        _cache,
        NullLogger<CachedContentProvider>.Instance);
}
