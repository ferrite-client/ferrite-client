using System.Text.Json;
using Ferrite.Core.Download;
using Ferrite.Core.Json;
using Ferrite.Core.Loaders;
using Ferrite.Core.Minecraft;
using Ferrite.Core.Net;
using Ferrite.Core.Platform;
using Ferrite.Core.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ferrite.Core.Tests;

/// <summary>
/// Merging LabyMod's published metadata into a version profile. The published document is already
/// complete, so what is pinned here is that the merge adds the right libraries and nothing else.
/// </summary>
public sealed class LabyModInstallerTests : IAsyncLifetime
{
    private const string ManifestJson = """
        {
          "labyModVersion": "4.6.21",
          "commitReference": "abcdef12",
          "sha1": "0123456789abcdef0123456789abcdef01234567",
          "size": 30000000,
          "assets": { "shader": "aa", "fonts": "bb" },
          "minecraftVersions": [
            { "tag": "1.21.1", "customManifestUrl": "PLACEHOLDER_VERSION_URL" }
          ]
        }
        """;

    private const string LibrariesJson = """
        {
          "libraries": [
            { "name": "org.ow2.asm:asm-util:9.9.1", "url": "https://cdn.example/asm-util.jar",
              "minecraftVersion": "all", "sha1": "aa", "size": 100 },
            { "name": "net.labymod:helper:1.0", "url": "https://cdn.example/helper.jar",
              "minecraftVersion": "1.21.1", "sha1": "bb", "size": 200 },
            { "name": "net.other:only-old:1.0", "url": "https://cdn.example/old.jar",
              "minecraftVersion": "1.20.1", "sha1": "cc", "size": 300 }
          ]
        }
        """;

    private const string VersionJson = """
        {
          "id": "1.21.1",
          "type": "release",
          "mainClass": "net.minecraft.launchwrapper.Launch",
          "assets": "17",
          "assetIndex": { "id": "17", "url": "https://piston-meta.example/17.json" },
          "downloads": {
            "client": { "sha1": "dd", "size": 10, "url": "https://piston-data.example/client.jar" }
          },
          "libraries": [
            { "name": "net.minecraft:launchwrapper:1.12" }
          ],
          "arguments": { "game": [], "jvm": [] }
        }
        """;

    private readonly string _workspace;
    private TestHttpServer _server = null!;
    private HttpService _http = null!;
    private AppPaths _paths = null!;
    private LabyModInstaller _installer = null!;

    public LabyModInstallerTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), "ferrite-labymod-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspace);
    }

    public ValueTask InitializeAsync()
    {
        _server = new TestHttpServer();
        _server.AddTextRoute("/manifest.json", ManifestJson);
        _server.AddTextRoute("/version.json", VersionJson);
        _server.AddTextRoute("/libraries.json", LibrariesJson);
        _http = new HttpService(new HttpServiceOptions(), NullLogger<HttpService>.Instance);
        _paths = AppPaths.ForRoot(Path.Combine(_workspace, "root"));
        _paths.EnsureCreated();
        _installer = new LabyModInstaller(
            _http,
            new DownloadEngine(_http, new DownloadEngineOptions(), NullLogger<DownloadEngine>.Instance),
            installer: null!,
            _paths,
            NullLogger<LabyModInstaller>.Instance,
            manifestUrl: _server.BaseUrl + "/manifest.json",
            librariesUrl: _server.BaseUrl + "/libraries.json",
            downloadBase: _server.BaseUrl);
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

    /// <summary>The manifest names its own version-document URL, so it is patched to the test server.</summary>
    private async Task<LabyModManifest> ManifestAsync()
    {
        _server.AddTextRoute(
            "/manifest.json",
            ManifestJson.Replace(
                "PLACEHOLDER_VERSION_URL",
                _server.BaseUrl + "/version.json",
                StringComparison.Ordinal));
        return await _installer.GetManifestAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task The_version_id_is_the_published_build()
    {
        var manifest = await ManifestAsync();
        Assert.Equal("4.6.21", manifest.LabyModVersion);
        Assert.Equal("1.21.1-LabyMod-4-abcdef12", LabyModInstaller.VersionIdFor(manifest, "1.21.1"));
    }

    [Fact]
    public async Task The_profile_adds_labymods_libraries_for_this_version_and_the_client()
    {
        var manifest = await ManifestAsync();

        var profile = await _installer.WriteProfileAsync(
            manifest,
            "1.21.1",
            TestContext.Current.CancellationToken);

        Assert.Equal("1.21.1-LabyMod-4-abcdef12", profile.VersionId);
        var path = _paths.VersionJsonFile(profile.VersionId);
        Assert.True(File.Exists(path));

        var document = JsonSerializer.Deserialize<VersionDocument>(
            await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken),
            JsonDefaults.Document);
        Assert.NotNull(document);
        var names = document!.Libraries.Select(library => library.Name).ToList();

        // The published document's own library is kept.
        Assert.Contains("net.minecraft:launchwrapper:1.12", names);
        // LabyMod's libraries for this version, and the ones marked "all", are added.
        Assert.Contains("org.ow2.asm:asm-util:9.9.1", names);
        Assert.Contains("net.labymod:helper:1.0", names);
        // A library for another Minecraft version is not.
        Assert.DoesNotContain("net.other:only-old:1.0", names);
        // The client jar itself is added with its published hash.
        var client = Assert.Single(document.Libraries, library => library.Name!.StartsWith("net.labymod:LabyMod:", StringComparison.Ordinal));
        Assert.Contains("abcdef12.jar", client.Downloads!.Artifact!.Url!, StringComparison.Ordinal);
        Assert.Equal(manifest.Sha1, client.Downloads.Artifact.Sha1);
        // The URL is the file's own address, so the entry needs the maven path beside it. Leaving it
        // as a repository base made the planner append the path to the URL itself, and every library
        // 404ed in the live run.
        Assert.Equal(
            "net/labymod/LabyMod/4.6.21/LabyMod-4.6.21.jar",
            client.Downloads.Artifact.Path);

        var asmLibrary = Assert.Single(document.Libraries, library => library.Name == "org.ow2.asm:asm-util:9.9.1");
        Assert.Equal("https://cdn.example/asm-util.jar", asmLibrary.Downloads!.Artifact!.Url);
        Assert.Equal("org/ow2/asm/asm-util/9.9.1/asm-util-9.9.1.jar", asmLibrary.Downloads.Artifact.Path);

        // The document keeps everything the version needs to launch.
        Assert.Equal("net.minecraft.launchwrapper.Launch", document.MainClass);
        Assert.Equal("17", document.Assets);
        Assert.NotNull(document.Downloads?.Client);
        // The reported library count is the profile's, not the asset count.
        Assert.Equal(names.Count, profile.LibraryCount);
    }

    [Fact]
    public async Task A_minecraft_version_labymod_does_not_publish_is_refused()
    {
        var manifest = await ManifestAsync();

        await Assert.ThrowsAsync<LoaderException>(() => _installer.WriteProfileAsync(
            manifest,
            "1.12.2",
            TestContext.Current.CancellationToken));
    }
}
