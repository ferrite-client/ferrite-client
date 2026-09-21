using Ferrite.Core.Content;
using Ferrite.Core.Net;
using Ferrite.Core.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ferrite.Core.Tests;

/// <summary>
/// The Feed The Beast provider. The search endpoint answers with ids only, so these tests pin that the
/// documents behind those ids become real results, versions, and a file list.
/// </summary>
public sealed class FtbClientTests : IAsyncLifetime
{
    private const string PackJson = """
        {
          "id": 79,
          "name": "FTB Presents Direwolf20 1.16",
          "synopsis": "A kitchen-sink pack",
          "description": "Longer description",
          "type": "release",
          "installs": 123456,
          "art": [
            { "type": "square", "url": "https://cdn.example/square.png" },
            { "type": "splash", "url": "https://cdn.example/splash.png" }
          ],
          "authors": [ { "name": "FTB Team" } ],
          "versions": [
            {
              "id": 208,
              "name": "1.0.0",
              "type": "release",
              "targets": [
                { "name": "forge", "version": "35.1.0", "type": "modloader" },
                { "name": "minecraft", "version": "1.16.4", "type": "game" }
              ]
            },
            {
              "id": 209,
              "name": "1.1.0",
              "type": "release",
              "targets": [
                { "name": "forge", "version": "35.1.13", "type": "modloader" },
                { "name": "minecraft", "version": "1.16.4", "type": "game" }
              ]
            },
            {
              "id": 300,
              "name": "2.0.0",
              "type": "release",
              "targets": [
                { "name": "forge", "version": "40.1.0", "type": "modloader" },
                { "name": "minecraft", "version": "1.18.2", "type": "game" }
              ]
            }
          ]
        }
        """;

    private const string VersionJson = """
        {
          "id": 209,
          "name": "1.1.0",
          "changelog": "Fixed things",
          "targets": [
            { "name": "forge", "version": "35.1.13", "type": "modloader" },
            { "name": "minecraft", "version": "1.16.4", "type": "game" }
          ],
          "files": [
            { "name": "Bookshelf.jar", "path": "./mods", "url": "https://cdn.example/bookshelf.jar",
              "sha1": "abc", "size": 1234, "type": "mod", "optional": false },
            { "name": "config.toml", "path": "./config", "url": "https://cdn.example/config.toml",
              "sha1": "def", "size": 10, "type": "config", "optional": true }
          ]
        }
        """;

    private TestHttpServer _server = null!;
    private HttpService _http = null!;
    private FtbClient _client = null!;

    public ValueTask InitializeAsync()
    {
        _server = new TestHttpServer();
        _server.AddTextRoute("/public/modpack/search/12", """{ "packs": [79], "total": 1 }""");
        _server.AddTextRoute("/public/modpack/79", PackJson);
        _server.AddTextRoute("/public/modpack/79/209", VersionJson);
        _http = new HttpService(new HttpServiceOptions(), NullLogger<HttpService>.Instance);
        _client = new FtbClient(_http, NullLogger<FtbClient>.Instance, _server.BaseUrl + "/public");
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await _server.DisposeAsync();
    }

    [Fact]
    public async Task A_search_turns_the_id_list_into_packs()
    {
        var result = await _client.SearchAsync(
            new ContentSearchQuery("direwolf", ContentProjectType.Modpack, Limit: 12),
            TestContext.Current.CancellationToken);

        var hit = Assert.Single(result.Hits);
        Assert.Equal("79", hit.ProjectId);
        Assert.Equal("FTB Presents Direwolf20 1.16", hit.Title);
        Assert.Equal(ContentProjectType.Modpack, hit.ProjectType);
        Assert.Equal("https://cdn.example/square.png", hit.IconUrl);
        Assert.Equal("FTB Team", hit.Author);
        Assert.Equal(123456, hit.Downloads);
        Assert.Equal("ftb", hit.Provider);
    }

    [Fact]
    public async Task A_search_for_another_project_type_asks_nothing()
    {
        var result = await _client.SearchAsync(
            new ContentSearchQuery("anything", ContentProjectType.Mod, Limit: 12),
            TestContext.Current.CancellationToken);

        Assert.Empty(result.Hits);
        Assert.Equal(0, _server.RequestCount("/public/modpack/search/12"));
    }

    [Fact]
    public async Task A_project_document_becomes_versions_and_a_loader()
    {
        var project = await _client.GetProjectAsync("79", TestContext.Current.CancellationToken);

        Assert.NotNull(project);
        Assert.Equal("FTB Presents Direwolf20 1.16", project!.Title);
        Assert.Contains("1.16.4", project.GameVersions);
        Assert.Contains("1.18.2", project.GameVersions);
        Assert.Contains("forge", project.Loaders);
        Assert.Contains("FTB Team", project.Authors!);
        Assert.Equal("https://cdn.example/square.png", project.IconUrl);
    }

    [Fact]
    public async Task Versions_are_filtered_by_the_instance_and_the_newest_match_wins()
    {
        var versions = await _client.GetVersionsAsync("79", "1.16.4", "forge", TestContext.Current.CancellationToken);

        Assert.Equal(2, versions.Count);
        Assert.All(versions, version => Assert.Contains("1.16.4", version.GameVersions));
        Assert.All(versions, version => Assert.Contains("forge", version.Loaders));

        var best = _client.SelectBestVersion(versions, "1.16.4", "forge");
        Assert.NotNull(best);
        Assert.Equal("209", best!.VersionId);
    }

    [Fact]
    public async Task A_version_the_instance_cannot_run_selects_nothing()
    {
        var versions = await _client.GetVersionsAsync("79", null, null, TestContext.Current.CancellationToken);

        Assert.Null(_client.SelectBestVersion(versions, "1.12.2", "forge"));
    }

    [Fact]
    public async Task One_version_carries_its_file_list()
    {
        var version = await _client.GetVersionAsync("79", "209", TestContext.Current.CancellationToken);

        Assert.NotNull(version);
        Assert.Equal("1.1.0", version!.VersionNumber);
        Assert.Equal(2, version.Files.Count);
        Assert.Equal("Bookshelf.jar", version.Files[0].FileName);
        Assert.Equal("abc", version.Files[0].Sha1);
    }
}
