using System.Text;
using Ferrite.Core.Content;
using Ferrite.Core.Download;
using Ferrite.Core.Minecraft;
using Ferrite.Core.Net;
using Ferrite.Core.Platform;
using Ferrite.Core.Storage;
using Ferrite.Core.Tests.Infrastructure;
using Ferrite.Core.Util;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ferrite.Core.Tests;

/// <summary>
/// The update path: what the launcher recorded, what it offers to update, and what happens when a
/// replacement download fails.
/// </summary>
public sealed class ContentUpdateTests : IAsyncLifetime
{
    private const string Provider = "modrinth";

    private readonly string _root;
    private readonly AppPaths _paths;
    private readonly InstanceStore _instances;
    private readonly ContentManifestStore _manifests;
    private ContentUpdater _updater = null!;
    private TestHttpServer _server = null!;
    private HttpService _http = null!;
    private InstanceRecord _instance = null!;

    public ContentUpdateTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ferrite-update-" + Guid.NewGuid().ToString("N"));
        _paths = AppPaths.ForRoot(_root);
        _paths.EnsureCreated();
        _instances = new InstanceStore(_paths, NullLogger<InstanceStore>.Instance);
        _manifests = new ContentManifestStore(NullLogger<ContentManifestStore>.Instance);
    }

    public async ValueTask InitializeAsync()
    {
        _server = new TestHttpServer();
        _http = new HttpService(new HttpServiceOptions(), NullLogger<HttpService>.Instance);
        var downloads = new DownloadEngine(
            _http,
            new DownloadEngineOptions { MaxConcurrency = 2 },
            NullLogger<DownloadEngine>.Instance);
        _updater = new ContentUpdater(
            downloads,
            _manifests,
            _paths,
            NullLogger<ContentUpdater>.Instance);

        _instance = await _instances.CreateAsync(
            new InstanceRecord
            {
                Id = Guid.NewGuid(),
                Name = "Update target",
                MinecraftVersion = "1.21.1",
                Loader = LoaderKind.Fabric,
                LoaderVersion = "0.15.11",
            },
            TestContext.Current.CancellationToken);
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
    public void The_manifest_round_trips_and_matches_paths_case_insensitively()
    {
        var file = _paths.InstanceContentManifestFile(_instance.Id);
        var manifest = new ContentManifest();
        manifest.Upsert(Entry("mods/sodium.jar", "install-1"));
        _manifests.Save(file, manifest);

        var reloaded = _manifests.Load(file);

        var entry = Assert.Single(reloaded.Entries);
        Assert.Equal("mods/sodium.jar", entry.RelativePath);
        Assert.NotNull(reloaded.Find("mods/Sodium.JAR"));
        Assert.True(reloaded.Remove("mods/SODIUM.jar"));
        Assert.Empty(reloaded.Entries);
    }

    [Fact]
    public void A_damaged_manifest_is_reported_as_empty_rather_than_throwing()
    {
        var file = _paths.InstanceContentManifestFile(_instance.Id);
        File.WriteAllText(file, "{ not json");

        Assert.Empty(_manifests.Load(file).Entries);
    }

    [Fact]
    public async Task A_newer_compatible_version_is_offered_and_current_ones_are_not()
    {
        InstallTrackedFile("mods/sodium.jar", "old-version");
        var provider = new StubProvider();
        provider.Add("sodium-project", Version("new-version", "1.21.1", "fabric", name: "Sodium 0.6.0"));

        var report = await _updater.CheckAsync(_instance, provider, TestContext.Current.CancellationToken);

        var update = Assert.Single(report.Updates);
        Assert.Equal("mods/sodium.jar", update.Entry.RelativePath);
        Assert.Equal("new-version", update.Available.VersionId);
        Assert.Equal("Sodium 0.6.0", update.Title);
        Assert.Empty(report.UpToDate);
    }

    [Fact]
    public async Task An_already_current_file_is_reported_as_up_to_date()
    {
        InstallTrackedFile("mods/sodium.jar", "same-version");
        var provider = new StubProvider();
        provider.Add("sodium-project", Version("same-version", "1.21.1", "fabric"));

        var report = await _updater.CheckAsync(_instance, provider, TestContext.Current.CancellationToken);

        Assert.Empty(report.Updates);
        Assert.Equal(["mods/sodium.jar"], report.UpToDate);
    }

    [Fact]
    public async Task An_incompatible_only_project_is_reported_rather_than_offered()
    {
        InstallTrackedFile("mods/sodium.jar", "old-version");
        var provider = new StubProvider();
        provider.Add("sodium-project", Version("other-version", "1.20.1", "fabric"));

        var report = await _updater.CheckAsync(_instance, provider, TestContext.Current.CancellationToken);

        Assert.Empty(report.Updates);
        Assert.Contains(report.Warnings, warning => warning.Contains("No compatible version", StringComparison.Ordinal));
    }

    /// <summary>
    /// A vanilla instance has no mod loader, so a Fabric or NeoForge build must never be offered.
    /// This was found by a live update run that picked a NeoForge build for a vanilla instance.
    /// </summary>
    [Fact]
    public void A_loader_specific_build_is_not_compatible_with_a_vanilla_instance()
    {
        Assert.False(ContentCompatibility.IsCompatible(
            Version("fabric-build", "1.21.1", "fabric"),
            "1.21.1",
            loader: null));
        Assert.False(ContentCompatibility.IsCompatible(
            Version("neoforge-build", "1.21.1", "neoforge"),
            "1.21.1",
            loader: null));
        Assert.True(ContentCompatibility.IsCompatible(
            Version("fabric-build", "1.21.1", "fabric"),
            "1.21.1",
            "fabric"));
    }

    [Fact]
    public void Content_that_targets_the_base_game_is_compatible_with_a_vanilla_instance()
    {
        // Modrinth reports a resource pack's loader as "minecraft", which is not a mod loader.
        var pack = Version("pack", "1.21.1", "minecraft");

        Assert.True(ContentCompatibility.IsCompatible(pack, "1.21.1", loader: null));
        Assert.True(ContentCompatibility.IsCompatible(pack, "1.21.1", "fabric"));
    }
    [Fact]
    public async Task Content_from_another_provider_and_missing_files_are_skipped()
    {
        InstallTrackedFile("mods/sodium.jar", "old-version");
        InstallTrackedFile("mods/gone.jar", "old-version", trackOnly: true);
        InstallTrackedFile("mods/curseforge-mod.jar", "old-version", provider: "curseforge");
        var provider = new StubProvider();
        provider.Add("sodium-project", Version("new-version", "1.21.1", "fabric"));

        var report = await _updater.CheckAsync(_instance, provider, TestContext.Current.CancellationToken);

        Assert.Single(report.Updates);
        Assert.Contains(report.Skipped, entry => entry.Contains("curseforge", StringComparison.Ordinal));
        Assert.Contains(report.Skipped, entry => entry.Contains("no longer installed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Applying_an_update_replaces_the_file_and_the_manifest_entry()
    {
        InstallTrackedFile("mods/sodium.jar", "old-version");
        var bytes = Encoding.UTF8.GetBytes("new sodium jar bytes");
        _server.AddRoute("/mods/new.jar", bytes);
        var provider = new StubProvider();
        provider.Add(
            "sodium-project",
            Version(
                "new-version",
                "1.21.1",
                "fabric",
                url: _server.BaseUrl + "/mods/new.jar",
                sha1: Hashing.Sha1(bytes),
                fileName: "new.jar"));

        var report = await _updater.CheckAsync(_instance, provider, TestContext.Current.CancellationToken);
        var result = await _updater.ApplyAsync(
            _instance,
            report.Updates,
            progress: null,
            TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Updated);
        var gameDirectory = _paths.InstanceGameDirectory(_instance.Id);
        Assert.Equal("new sodium jar bytes", File.ReadAllText(Path.Combine(gameDirectory, "mods", "new.jar")));
        Assert.False(File.Exists(Path.Combine(gameDirectory, "mods", "sodium.jar")));

        var manifest = _manifests.Load(_paths.InstanceContentManifestFile(_instance.Id));
        var entry = Assert.Single(manifest.Entries);
        Assert.Equal("mods/new.jar", entry.RelativePath);
        Assert.Equal("new-version", entry.VersionId);
    }

    [Fact]
    public async Task A_failed_download_leaves_the_installed_file_untouched()
    {
        InstallTrackedFile("mods/sodium.jar", "old-version");
        _server.AddHandler("GET", "/mods/broken.jar", _ => new TestResponse(500, "boom"));
        var provider = new StubProvider();
        provider.Add(
            "sodium-project",
            Version("new-version", "1.21.1", "fabric", url: _server.BaseUrl + "/mods/broken.jar"));

        var report = await _updater.CheckAsync(_instance, provider, TestContext.Current.CancellationToken);
        var result = await _updater.ApplyAsync(
            _instance,
            report.Updates,
            progress: null,
            TestContext.Current.CancellationToken);

        Assert.Equal(0, result.Updated);
        Assert.NotEmpty(result.Warnings);

        var gameDirectory = _paths.InstanceGameDirectory(_instance.Id);
        Assert.Equal("old bytes", File.ReadAllText(Path.Combine(gameDirectory, "mods", "sodium.jar")));

        var entry = Assert.Single(_manifests.Load(_paths.InstanceContentManifestFile(_instance.Id)).Entries);
        Assert.Equal("old-version", entry.VersionId);
    }

    [Fact]
    public async Task A_retail_only_version_is_reported_and_not_downloaded()
    {
        InstallTrackedFile("mods/sodium.jar", "old-version");
        var provider = new StubProvider();
        provider.Add("sodium-project", Version("new-version", "1.21.1", "fabric", url: string.Empty));

        var report = await _updater.CheckAsync(_instance, provider, TestContext.Current.CancellationToken);
        var result = await _updater.ApplyAsync(
            _instance,
            report.Updates,
            progress: null,
            TestContext.Current.CancellationToken);

        Assert.Equal(0, result.Updated);
        Assert.Contains(result.Warnings, warning =>
            warning.Contains("cannot be downloaded", StringComparison.OrdinalIgnoreCase));
        Assert.True(File.Exists(Path.Combine(
            _paths.InstanceGameDirectory(_instance.Id),
            "mods",
            "sodium.jar")));
    }

    private void InstallTrackedFile(
        string relativePath,
        string versionId,
        string provider = Provider,
        bool trackOnly = false)
    {
        var gameDirectory = _paths.InstanceGameDirectory(_instance.Id);
        var full = Path.Combine(gameDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        if (!trackOnly)
        {
            File.WriteAllText(full, "old bytes");
        }

        var file = _paths.InstanceContentManifestFile(_instance.Id);
        var manifest = _manifests.Load(file);
        manifest.Upsert(Entry(relativePath, versionId, provider));
        _manifests.Save(file, manifest);
    }

    private static ContentManifestEntry Entry(string relativePath, string versionId, string provider = Provider) => new()
    {
        RelativePath = relativePath,
        Provider = provider,
        ProjectId = Path.GetFileNameWithoutExtension(relativePath) + "-project",
        VersionId = versionId,
        ProjectType = ContentProjectType.Mod,
    };

    private static ContentVersion Version(
        string versionId,
        string gameVersion,
        string loader,
        string? url = "https://example.invalid/mod.jar",
        string? sha1 = null,
        string? name = null,
        string fileName = "mod.jar") => new(
        Provider,
        versionId,
        "sodium-project",
        versionId,
        name,
        null,
        "release",
        [gameVersion],
        [loader],
        [new ContentFile(fileName, url ?? string.Empty, 0, true, sha1, null)],
        [],
        DateTimeOffset.Parse("2026-09-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>In-memory provider so the check logic is tested without a network boundary.</summary>
    private sealed class StubProvider : IContentProvider
    {
        private readonly Dictionary<string, List<ContentVersion>> _versions = new(StringComparer.Ordinal);

        public string Name => Provider;

        public bool IsConfigured => true;

        public string? UnavailableReason => null;

        public void Add(string projectId, ContentVersion version)
        {
            if (!_versions.TryGetValue(projectId, out var list))
            {
                list = [];
                _versions[projectId] = list;
            }

            list.Add(version);
        }

        public Task<ContentSearchResult> SearchAsync(ContentSearchQuery query, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The update path never searches.");

        public Task<ContentProject?> GetProjectAsync(string idOrSlug, CancellationToken cancellationToken) =>
            Task.FromResult<ContentProject?>(null);

        public Task<IReadOnlyList<ContentTag>> GetTagsAsync(string kind, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ContentTag>>([]);

        public Task<IReadOnlyList<ContentVersion>> GetVersionsAsync(
            string projectId,
            string? gameVersion,
            string? loader,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ContentVersion>>(
                _versions.TryGetValue(projectId, out var list) ? list : []);

        public Task<ContentVersion?> GetVersionAsync(
            string projectId,
            string versionId,
            CancellationToken cancellationToken) =>
            Task.FromResult<ContentVersion?>(
                _versions.TryGetValue(projectId, out var list)
                    ? list.FirstOrDefault(version => version.VersionId == versionId)
                    : null);

        public ContentVersion? SelectBestVersion(
            IReadOnlyList<ContentVersion> versions,
            string? gameVersion,
            string? loader) => ContentCompatibility.SelectBestVersion(versions, gameVersion, loader);
    }
}
