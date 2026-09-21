using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Ferrite.Core.Download;
using Ferrite.Core.Net;
using Ferrite.Core.Platform;
using Ferrite.Core.Tests.Infrastructure;
using Ferrite.Core.Update;
using Ferrite.Core.Util;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ferrite.Core.Tests;

/// <summary>
/// The launcher's update path: a signed feed is verified before anything in it is trusted, the
/// package is hash-checked before it is unpacked, and a package that is not a build is refused.
/// </summary>
public sealed class UpdateServiceTests : IAsyncLifetime
{
    private const string CurrentVersion = "1.0.0";
    private const string Runtime = "win-x64";

    private readonly string _root;
    private readonly AppPaths _paths;
    private readonly (string PublicKey, string PrivateKey) _keys = UpdateSignature.CreateKeyPair();
    private readonly (string PublicKey, string PrivateKey) _otherKeys = UpdateSignature.CreateKeyPair();

    private TestHttpServer _server = null!;
    private HttpService _http = null!;
    private UpdateService _service = null!;

    public UpdateServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ferrite-update-svc-" + Guid.NewGuid().ToString("N"));
        _paths = AppPaths.ForRoot(_root);
        _paths.EnsureCreated();
    }

    public ValueTask InitializeAsync()
    {
        _server = new TestHttpServer();
        _http = new HttpService(new HttpServiceOptions(), NullLogger<HttpService>.Instance);
        _service = CreateService(_keys.PublicKey);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await _server.DisposeAsync();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void A_matching_signature_verifies()
    {
        var manifest = Encoding.UTF8.GetBytes("""{"version":"2.0.0","packages":[]}""");
        var signature = UpdateSignature.Sign(manifest, _keys.PrivateKey);

        Assert.True(UpdateSignature.Verify(manifest, signature, _keys.PublicKey));
    }

    [Fact]
    public void A_tampered_manifest_does_not_verify()
    {
        var manifest = Encoding.UTF8.GetBytes("""{"version":"2.0.0","packages":[]}""");
        var signature = UpdateSignature.Sign(manifest, _keys.PrivateKey);
        var tampered = Encoding.UTF8.GetBytes("""{"version":"9.9.9","packages":[]}""");

        Assert.False(UpdateSignature.Verify(tampered, signature, _keys.PublicKey));
    }

    [Fact]
    public void A_signature_from_another_key_does_not_verify()
    {
        var manifest = Encoding.UTF8.GetBytes("""{"version":"2.0.0","packages":[]}""");
        var signature = UpdateSignature.Sign(manifest, _otherKeys.PrivateKey);

        Assert.False(UpdateSignature.Verify(manifest, signature, _keys.PublicKey));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not base64 at all !!")]
    [InlineData("AAAA")]
    public void An_unusable_signature_does_not_verify(string signature)
    {
        var manifest = Encoding.UTF8.GetBytes("""{"version":"2.0.0","packages":[]}""");

        Assert.False(UpdateSignature.Verify(manifest, signature, _keys.PublicKey));
    }

    [Fact]
    public void An_empty_public_key_never_verifies()
    {
        var manifest = Encoding.UTF8.GetBytes("""{"version":"2.0.0","packages":[]}""");
        var signature = UpdateSignature.Sign(manifest, _keys.PrivateKey);

        Assert.False(UpdateSignature.Verify(manifest, signature, publicKeyPem: string.Empty));
    }

    [Fact]
    public void A_manifest_with_missing_fields_is_rejected()
    {
        Assert.Throws<UpdateException>(() => UpdateManifest.Parse("""{"packages":[]}"""));
        Assert.Throws<UpdateException>(() => UpdateManifest.Parse(
            """{"version":"1.0.0","packages":[{"runtime":"win-x64"}]}"""));
    }

    [Fact]
    public void A_manifest_from_a_newer_schema_is_rejected()
    {
        var exception = Assert.Throws<UpdateException>(() => UpdateManifest.Parse(
            """{"schemaVersion":99,"version":"1.0.0","packages":[]}"""));

        Assert.Contains("schema", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("http://example.com/ferrite-2.0.0.zip")]
    [InlineData("ftp://example.com/ferrite-2.0.0.zip")]
    [InlineData("file:///C:/builds/ferrite.zip")]
    [InlineData("../../outside/ferrite.zip")]
    [InlineData("/absolute/ferrite.zip")]
    public void A_package_url_that_is_not_https_is_rejected(string url)
    {
        var json = $$"""
            {"version":"2.0.0","packages":[{"runtime":"win-x64","kind":"self-contained",
             "url":"{{url}}","sha256":"aa","size":1}]}
            """;

        Assert.Throws<UpdateException>(() => UpdateManifest.Parse(json));
    }

    [Fact]
    public void A_loopback_package_url_is_allowed_so_a_feed_can_be_tested_locally()
    {
        var json = """
            {"version":"2.0.0","packages":[{"runtime":"win-x64","kind":"self-contained",
             "url":"http://127.0.0.1:8080/ferrite.zip","sha256":"aa","size":1}]}
            """;

        Assert.Equal("2.0.0", UpdateManifest.Parse(json).Version);
    }

    [Fact]
    public void A_relative_package_url_resolves_against_the_feed()
    {
        var package = new UpdatePackage
        {
            Runtime = Runtime,
            Kind = "self-contained",
            Url = "ferrite-2.0.0-win-x64.zip",
            Sha256 = "aa",
        };

        var resolved = UpdateService.ResolvePackage(package, new Uri("https://updates.example.invalid/stable/"));

        Assert.NotNull(resolved);
        Assert.Equal("https://updates.example.invalid/stable/ferrite-2.0.0-win-x64.zip", resolved!.Url);
    }

    [Fact]
    public void A_relative_package_url_may_not_escape_the_feed()
    {
        var package = new UpdatePackage
        {
            Runtime = Runtime,
            Kind = "self-contained",
            Url = "../secrets.zip",
            Sha256 = "aa",
        };

        Assert.Throws<UpdateException>(() =>
            UpdateService.ResolvePackage(package, new Uri("https://updates.example.invalid/stable/")));
    }

    [Theory]
    [InlineData("1.0.0", "1.0.0", 0)]
    [InlineData("1.0.1", "1.0.0", 1)]
    [InlineData("1.0.0", "1.2.0", -1)]
    [InlineData("1.2", "1.2.0", 0)]
    [InlineData("v2.0.0", "1.9.9", 1)]
    [InlineData("1.2.0-beta", "1.2.0", -1)]
    [InlineData("1.2.0", "1.2.0-beta", 1)]
    public void Versions_compare_the_way_a_release_feed_needs(
        string left,
        string right,
        int expectedSign)
    {
        Assert.Equal(expectedSign, Math.Sign(UpdateService.CompareVersions(left, right)));
    }

    [Fact]
    public async Task A_signed_feed_reports_a_newer_release()
    {
        PublishFeed("2.0.0", [new UpdatePackage
        {
            Runtime = Runtime,
            Kind = "self-contained",
            Url = _server.BaseUrl + "/ferrite-2.0.0.zip",
            Sha256 = "00",
            Size = 10,
        }]);

        var result = await _service.CheckAsync(_server.BaseUrl + "/", CurrentVersion, Runtime, TestContext.Current.CancellationToken);

        Assert.True(result.IsNewer);
        Assert.Equal("2.0.0", result.Manifest.Version);
        Assert.NotNull(result.Package);
    }

    [Fact]
    public async Task A_feed_whose_manifest_was_altered_after_signing_is_refused()
    {
        var manifest = """
            {"version":"9.9.9","packages":[]}
            """;
        var signature = UpdateSignature.Sign(
            Encoding.UTF8.GetBytes("""{"version":"2.0.0","packages":[]}"""),
            _keys.PrivateKey);
        _server.AddHandler("GET", "/manifest.json", _ => new TestResponse(200, manifest));
        _server.AddHandler("GET", "/manifest.json.sig", _ => new TestResponse(200, signature));

        var exception = await Assert.ThrowsAsync<UpdateException>(() => _service.CheckAsync(
            _server.BaseUrl + "/",
            CurrentVersion,
            Runtime,
            TestContext.Current.CancellationToken));

        Assert.Contains("signature", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_build_without_a_feed_key_refuses_to_check()
    {
        var service = CreateService(publicKeyPem: null);

        var exception = await Assert.ThrowsAsync<UpdateException>(() => service.CheckAsync(
            "https://example.invalid/feeds/",
            CurrentVersion,
            Runtime,
            TestContext.Current.CancellationToken));

        Assert.Contains("signing key", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_remote_feed_must_use_https()
    {
        var exception = await Assert.ThrowsAsync<UpdateException>(() => _service.CheckAsync(
            "http://updates.example.invalid/feeds/",
            CurrentVersion,
            Runtime,
            TestContext.Current.CancellationToken));

        Assert.Contains("HTTPS", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Staging_downloads_verifies_and_unpacks_a_build()
    {
        var packagePath = CreatePackage(("Ferrite.exe", "launcher"), ("Ferrite.Core.dll", "core"));
        var bytes = await File.ReadAllBytesAsync(packagePath, TestContext.Current.CancellationToken);
        _server.AddRoute("/ferrite.zip", bytes);
        PublishFeed("2.0.0", [Package(_server.BaseUrl + "/ferrite.zip", bytes)]);

        var check = await _service.CheckAsync(
            _server.BaseUrl + "/",
            CurrentVersion,
            Runtime,
            TestContext.Current.CancellationToken);
        var stage = await _service.StageAsync(check, progress: null, TestContext.Current.CancellationToken);

        Assert.True(File.Exists(Path.Combine(stage.PayloadDirectory, "Ferrite.exe")));
        Assert.True(File.Exists(Path.Combine(stage.PayloadDirectory, "Ferrite.Core.dll")));
        Assert.True(File.Exists(stage.ScriptPath));
        Assert.StartsWith(_paths.UpdateStagingDirectory, stage.StageDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(bytes.Length, stage.Bytes);

        // The staging directory is marked, which is what lets the hand-off script delete it later.
        Assert.True(UpdateHandoff.IsStagingDirectory(stage.StageDirectory));
    }

    [Fact]
    public async Task Staging_refuses_a_package_whose_hash_does_not_match()
    {
        var packagePath = CreatePackage(("Ferrite.exe", "launcher"));
        var bytes = await File.ReadAllBytesAsync(packagePath, TestContext.Current.CancellationToken);
        _server.AddRoute("/ferrite.zip", bytes);

        var declared = Package(_server.BaseUrl + "/ferrite.zip", bytes) with
        {
            Sha256 = new string('0', 64),
        };
        PublishFeed("2.0.0", [declared]);

        var check = await _service.CheckAsync(
            _server.BaseUrl + "/",
            CurrentVersion,
            Runtime,
            TestContext.Current.CancellationToken);
        var exception = await Assert.ThrowsAsync<UpdateException>(() =>
            _service.StageAsync(check, progress: null, TestContext.Current.CancellationToken));

        Assert.Contains("SHA-256", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(
            _paths.UpdateStagingDirectory,
            "2.0.0",
            "payload",
            "Ferrite.exe")));
    }

    [Fact]
    public async Task Staging_refuses_a_package_that_is_not_a_build()
    {
        var packagePath = CreatePackage(("readme.txt", "not a build"));
        var bytes = await File.ReadAllBytesAsync(packagePath, TestContext.Current.CancellationToken);
        _server.AddRoute("/ferrite.zip", bytes);
        PublishFeed("2.0.0", [Package(_server.BaseUrl + "/ferrite.zip", bytes)]);

        var check = await _service.CheckAsync(
            _server.BaseUrl + "/",
            CurrentVersion,
            Runtime,
            TestContext.Current.CancellationToken);
        var exception = await Assert.ThrowsAsync<UpdateException>(() =>
            _service.StageAsync(check, progress: null, TestContext.Current.CancellationToken));

        Assert.Contains("Ferrite.exe", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Package_selection_prefers_the_requested_shape_and_matches_the_runtime()
    {
        var manifest = new UpdateManifest
        {
            Version = "2.0.0",
            Packages =
            [
                new UpdatePackage { Runtime = "linux-x64", Kind = "self-contained", Url = "https://e.invalid/l", Sha256 = "a" },
                new UpdatePackage { Runtime = Runtime, Kind = "framework-dependent", Url = "https://e.invalid/f", Sha256 = "b" },
                new UpdatePackage { Runtime = Runtime, Kind = "self-contained", Url = "https://e.invalid/s", Sha256 = "c" },
            ],
        };

        Assert.Equal(
            "https://e.invalid/f",
            UpdateService.SelectPackage(manifest, Runtime, "framework-dependent")!.Url);
        Assert.Equal("https://e.invalid/s", UpdateService.SelectPackage(manifest, Runtime, "self-contained")!.Url);
        // With no preference the build that runs anywhere wins, regardless of feed order.
        Assert.Equal("https://e.invalid/s", UpdateService.SelectPackage(manifest, Runtime)!.Url);
        Assert.Null(UpdateService.SelectPackage(manifest, "osx-arm64"));
    }

    [Fact]
    public void The_local_build_shape_is_detected_from_the_install_directory()
    {
        var selfContained = Path.Combine(_root, "self-contained");
        var frameworkDependent = Path.Combine(_root, "framework-dependent");
        Directory.CreateDirectory(selfContained);
        Directory.CreateDirectory(frameworkDependent);
        File.WriteAllText(Path.Combine(selfContained, "System.Private.CoreLib.dll"), string.Empty);
        File.WriteAllText(Path.Combine(frameworkDependent, "Ferrite.dll"), string.Empty);

        Assert.Equal(UpdateService.SelfContainedKind, UpdateService.DetectLocalKind(selfContained));
        Assert.Equal(UpdateService.FrameworkDependentKind, UpdateService.DetectLocalKind(frameworkDependent));
    }

    private UpdateService CreateService(string? publicKeyPem) => new(
        _http,
        new DownloadEngine(
            _http,
            new DownloadEngineOptions { MaxConcurrency = 2 },
            NullLogger<DownloadEngine>.Instance),
        _paths,
        NullLogger<UpdateService>.Instance,
        publicKeyPem);

    private UpdatePackage Package(string url, byte[] bytes) => new()
    {
        Runtime = Runtime,
        Kind = "self-contained",
        Url = url,
        Sha256 = Hashing.Sha256(bytes),
        Size = bytes.Length,
    };

    /// <summary>Publishes a signed manifest for the given packages on the test server.</summary>
    private void PublishFeed(string version, IReadOnlyList<UpdatePackage> packages)
    {
        var manifest = new UpdateManifest
        {
            Version = version,
            Channel = "stable",
            PublishedAt = DateTimeOffset.Parse("2026-09-21T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            Notes = "test release",
            Packages = packages,
        };

        var bytes = Encoding.UTF8.GetBytes(manifest.ToJson());
        var signature = UpdateSignature.Sign(bytes, _keys.PrivateKey);
        _server.AddRoute("/manifest.json", bytes);
        _server.AddTextRoute("/manifest.json.sig", signature);
    }

    private string CreatePackage(params (string Name, string Content)[] entries)
    {
        var path = Path.Combine(_root, "package-" + Guid.NewGuid().ToString("N") + ".zip");
        using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            var entry = archive.CreateEntry(name);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(content);
        }

        return path;
    }
}
