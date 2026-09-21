using Ferrite.Core.Content;
using Ferrite.Core.Net;
using Ferrite.Core.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ferrite.Core.Tests;

/// <summary>
/// The Modrinth client against a scripted boundary: the two tag shapes the API actually uses, the
/// gallery the project document carries, and the facets a filtered search sends.
/// </summary>
public sealed class ModrinthClientTests : IAsyncLifetime
{
    private TestHttpServer _server = null!;
    private HttpService _http = null!;
    private ModrinthClient _client = null!;

    public ValueTask InitializeAsync()
    {
        _server = new TestHttpServer();
        _http = new HttpService(new HttpServiceOptions(), NullLogger<HttpService>.Instance);
        _client = new ModrinthClient(
            _http,
            NullLogger<ModrinthClient>.Instance,
            _server.BaseUrl + "/v2");
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await _server.DisposeAsync();
    }

    /// <summary>
    /// The game-version facet names its entries <c>version</c>, not <c>name</c>. Reading only
    /// <c>name</c> silently produced an empty facet, which is what this pins down.
    /// </summary>
    [Fact]
    public async Task The_game_version_facet_reads_its_own_field_shape()
    {
        _server.AddHandler("GET", "/v2/tag/game_version", _ => new TestResponse(
            200,
            """
            [
              { "version": "1.21.1", "version_type": "release", "date": "2024-08-08T00:00:00Z" },
              { "version": "26.3", "version_type": "release", "date": "2026-01-01T00:00:00Z" }
            ]
            """));

        var tags = await _client.GetTagsAsync("game_version", TestContext.Current.CancellationToken);

        Assert.Equal(2, tags.Count);
        Assert.Equal("1.21.1", tags[0].Name);
        Assert.Equal("1.21.1", tags[0].DisplayName);
        Assert.Equal("26.3", tags[1].Name);
    }

    [Fact]
    public async Task The_category_facet_reads_name_and_display_name()
    {
        _server.AddHandler("GET", "/v2/tag/category", _ => new TestResponse(
            200,
            """
            [ { "name": "optimization", "display_name": "Optimization", "icon": "https://example.invalid/i.svg" } ]
            """));

        var tags = await _client.GetTagsAsync("category", TestContext.Current.CancellationToken);

        var tag = Assert.Single(tags);
        Assert.Equal("optimization", tag.Name);
        Assert.Equal("Optimization", tag.DisplayName);
        Assert.Equal("https://example.invalid/i.svg", tag.IconUrl);
    }

    [Fact]
    public async Task A_filtered_search_sends_the_category_as_a_facet()
    {
        _server.AddHandler("GET", "/v2/search", _ => new TestResponse(200, """{ "hits": [], "total_hits": 0 }"""));

        await _client.SearchAsync(
            new ContentSearchQuery(
                "sodium",
                ContentProjectType.Mod,
                GameVersion: "1.21.1",
                Loader: "fabric",
                Categories: ["optimization"]),
            TestContext.Current.CancellationToken);

        var request = _server.LastRequest("/v2/search");
        Assert.NotNull(request);
        var facets = Uri.UnescapeDataString(request!.Query);
        Assert.Contains("\"categories:optimization\"", facets, StringComparison.Ordinal);
        Assert.Contains("\"versions:1.21.1\"", facets, StringComparison.Ordinal);
        // The loader is a category facet on Modrinth, so both appear as category entries.
        Assert.Contains("\"categories:fabric\"", facets, StringComparison.Ordinal);
        Assert.Contains("project_type:mod", facets, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_project_gallery_keeps_images_and_drops_video_links()
    {
        _server.AddHandler("GET", "/v2/project/sodium", _ => new TestResponse(
            200,
            """
            {
              "id": "AANobbMI",
              "slug": "sodium",
              "title": "Sodium",
              "description": "Rendering engine",
              "body": "## Sodium",
              "project_type": "mod",
              "downloads": 1,
              "license": { "id": "LGPL-3.0-only" },
              "categories": [ "optimization" ],
              "game_versions": [ "1.21.1" ],
              "loaders": [ "fabric" ],
              "source_url": "https://github.com/CaffeineMC/sodium",
              "issues_url": "https://github.com/CaffeineMC/sodium/issues",
              "gallery": [
                { "url": "https://cdn.example.invalid/one.png", "featured": true },
                { "url": "https://www.youtube.com/watch?v=abc", "featured": false },
                { "url": "https://cdn.example.invalid/two.webp", "featured": false }
              ]
            }
            """));

        var project = await _client.GetProjectAsync("sodium", TestContext.Current.CancellationToken);

        Assert.NotNull(project);
        Assert.Equal("LGPL-3.0-only", project!.License);
        Assert.Equal(2, project.Gallery!.Count);
        Assert.DoesNotContain(project.Gallery, url => url.Contains("youtube", StringComparison.Ordinal));
        Assert.Equal("https://cdn.example.invalid/one.png", project.Gallery[0]);
    }
}
