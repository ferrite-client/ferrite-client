using Ferrite.Core.Content;
using Ferrite.Core.Net;
using Ferrite.Core.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ferrite.Core.Tests;

/// <summary>
/// Exercises the CurseForge client against a scripted boundary: request shapes, response mapping, the
/// API-key requirement, and the retail-file restriction. Live calls need a user-issued key.
/// </summary>
public sealed class CurseForgeClientTests : IAsyncLifetime
{
    private const string ApiKey = "test-curseforge-key-0123456789";

    private TestHttpServer _server = null!;
    private HttpService _http = null!;
    private CurseForgeClient _client = null!;

    public ValueTask InitializeAsync()
    {
        _server = new TestHttpServer();
        _http = new HttpService(new HttpServiceOptions(), NullLogger<HttpService>.Instance);
        _client = new CurseForgeClient(
            _http,
            NullLogger<CurseForgeClient>.Instance,
            () => ApiKey,
            _server.BaseUrl + "/v1");
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await _server.DisposeAsync();
    }

    [Fact]
    public async Task Search_sends_the_expected_query_and_maps_results()
    {
        _server.AddHandler("GET", "/v1/mods/search", _ => new TestResponse(
            200,
            """
            {
              "data": [
                {
                  "id": 238222,
                  "name": "Just Enough Items",
                  "slug": "jei",
                  "summary": "View items and recipes",
                  "classId": 6,
                  "downloadCount": 1234567,
                  "logo": { "thumbnailUrl": "https://example.invalid/jei.png" },
                  "authors": [ { "name": "mezz" } ],
                  "categories": [ { "name": "Map and Information" } ],
                  "dateModified": "2026-08-01T10:00:00Z"
                }
              ],
              "pagination": { "index": 0, "pageSize": 20, "resultCount": 1, "totalCount": 42 }
            }
            """));

        var result = await _client.SearchAsync(
            new ContentSearchQuery(
                "jei",
                ContentProjectType.Mod,
                GameVersion: "1.21.1",
                Loader: "neoforge",
                Limit: 20),
            TestContext.Current.CancellationToken);

        Assert.Equal(42, result.TotalHits);
        var hit = Assert.Single(result.Hits);
        Assert.Equal("238222", hit.ProjectId);
        Assert.Equal("Just Enough Items", hit.Title);
        Assert.Equal(ContentProjectType.Mod, hit.ProjectType);
        Assert.Equal(1234567, hit.Downloads);
        Assert.Equal("mezz", hit.Author);
        Assert.Equal("https://example.invalid/jei.png", hit.IconUrl);
        Assert.Equal("curseforge", hit.Provider);

        var request = _server.LastRequest("/v1/mods/search");
        Assert.NotNull(request);
        Assert.Equal(ApiKey, request!.Headers["x-api-key"]);
        Assert.Contains("gameId=432", request.Query, StringComparison.Ordinal);
        Assert.Contains("classId=6", request.Query, StringComparison.Ordinal);
        Assert.Contains("modLoaderType=6", request.Query, StringComparison.Ordinal);
        Assert.Contains("gameVersion=1.21.1", request.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Version_listing_maps_files_hashes_and_dependencies()
    {
        _server.AddHandler("GET", "/v1/mods/238222/files", _ => new TestResponse(
            200,
            """
            {
              "data": [
                {
                  "id": 5512345,
                  "modId": 238222,
                  "displayName": "jei-1.21.1-19.0.0.1.jar",
                  "fileName": "jei-1.21.1-19.0.0.1.jar",
                  "releaseType": 1,
                  "fileLength": 1234567,
                  "fileDate": "2026-07-15T12:00:00Z",
                  "downloadUrl": "https://edge.forgecdn.net/files/5512/345/jei.jar",
                  "hashes": [ { "value": "abcdef0123456789", "algo": 1 } ],
                  "gameVersions": [ "1.21.1", "NeoForge" ],
                  "dependencies": [ { "modId": 306612, "relationType": 3 } ]
                }
              ]
            }
            """));

        var versions = await _client.GetVersionsAsync(
            "238222",
            "1.21.1",
            "neoforge",
            TestContext.Current.CancellationToken);

        var version = Assert.Single(versions);
        Assert.Equal("5512345", version.VersionId);
        Assert.Equal("238222", version.ProjectId);
        Assert.Equal("release", version.VersionType);
        Assert.True(version.IsRelease);
        Assert.Contains("1.21.1", version.GameVersions);

        var file = Assert.Single(version.Files);
        Assert.Equal("abcdef0123456789", file.Sha1);
        Assert.Equal(1234567, file.Size);
        Assert.True(file.Primary);

        var dependency = Assert.Single(version.Dependencies);
        Assert.Equal("306612", dependency.ProjectId);
        Assert.Equal("required", dependency.Kind);
    }

    [Fact]
    public async Task Project_lookup_uses_the_numeric_id()
    {
        _server.AddHandler("GET", "/v1/mods/238222", _ => new TestResponse(
            200,
            """
            {
              "data": {
                "id": 238222,
                "name": "Just Enough Items",
                "slug": "jei",
                "summary": "View items and recipes",
                "classId": 6,
                "downloadCount": 999,
                "latestFilesIndexes": [ { "gameVersion": "1.21.1" } ],
                "links": { "websiteUrl": "https://example.invalid/jei" }
              }
            }
            """));

        var project = await _client.GetProjectAsync("238222", TestContext.Current.CancellationToken);

        Assert.NotNull(project);
        Assert.Equal("Just Enough Items", project!.Title);
        Assert.Equal("https://example.invalid/jei", project.SourceUrl);
        Assert.Contains("1.21.1", project.GameVersions);
    }

    [Fact]
    public async Task A_missing_api_key_is_reported_as_configuration()
    {
        var unconfigured = new CurseForgeClient(
            _http,
            NullLogger<CurseForgeClient>.Instance,
            () => null,
            _server.BaseUrl + "/v1");

        Assert.False(unconfigured.IsConfigured);
        var exception = await Assert.ThrowsAsync<ContentProviderException>(() =>
            unconfigured.SearchAsync(
                new ContentSearchQuery("anything"),
                TestContext.Current.CancellationToken));

        Assert.Contains("API key", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, _server.RequestCount("/v1/mods/search"));
    }

    [Fact]
    public async Task A_retail_only_file_reports_no_download_url()
    {
        _server.AddHandler("GET", "/v1/mods/111/files/222/download-url", _ => new TestResponse(
            200,
            """{"data":null}"""));

        var url = await _client.GetDownloadUrlAsync(111, 222, TestContext.Current.CancellationToken);
        Assert.Null(url);
    }

    [Fact]
    public async Task A_distributable_file_returns_its_download_url()
    {
        _server.AddHandler("GET", "/v1/mods/111/files/222/download-url", _ => new TestResponse(
            200,
            """{"data":"https://edge.forgecdn.net/files/111/222/mod.jar"}"""));

        var url = await _client.GetDownloadUrlAsync(111, 222, TestContext.Current.CancellationToken);
        Assert.Equal("https://edge.forgecdn.net/files/111/222/mod.jar", url);
    }

    [Fact]
    public void Class_and_loader_ids_map_both_ways()
    {
        Assert.Equal(6, CurseForgeIds.ClassIdFor(ContentProjectType.Mod));
        Assert.Equal(4471, CurseForgeIds.ClassIdFor(ContentProjectType.Modpack));
        Assert.Equal(ContentProjectType.Shader, CurseForgeIds.ProjectTypeFor(6552));
        Assert.Equal(6, CurseForgeIds.ModLoaderFor("neoforge"));
        Assert.Equal(4, CurseForgeIds.ModLoaderFor("fabric"));
        Assert.Null(CurseForgeIds.ModLoaderFor(null));
        Assert.Equal("required", CurseForgeIds.RelationName(3));
        Assert.Equal("incompatible", CurseForgeIds.RelationName(5));
    }
}
