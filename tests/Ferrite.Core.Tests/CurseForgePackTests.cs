using System.IO.Compression;
using Ferrite.Core.Content;
using Ferrite.Core.Minecraft;

namespace Ferrite.Core.Tests;

/// <summary>
/// Covers the CurseForge modpack path: manifest parsing, loader resolution, file planning, and the
/// retail-distribution restriction. Live installation additionally needs a user-issued API key, so
/// only the boundary is proven here.
/// </summary>
public sealed class CurseForgePackTests : IDisposable
{
    private readonly string _workspace;

    public CurseForgePackTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), "ferrite-cfpack-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspace);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_workspace, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void Manifest_is_read_from_the_archive_root()
    {
        var archive = CreateArchive(
            ("manifest.json", Manifest("1.21.1", "neoforge-21.1.72", (238222, 5512345))),
            ("overrides/config/example.toml", "enabled = true"));

        var manifest = CurseForgePackInstaller.ReadManifest(archive);

        Assert.Equal("Example Pack", manifest.Name);
        Assert.Equal("1.21.1", manifest.Minecraft!.Version);
        Assert.Equal("overrides", manifest.Overrides);
        var file = Assert.Single(manifest.Files);
        Assert.Equal(238222, file.ProjectID);
        Assert.Equal(5512345, file.FileID);
    }

    [Fact]
    public void An_archive_without_a_manifest_is_rejected()
    {
        var archive = CreateArchive(("readme.txt", "not a pack"));

        var exception = Assert.Throws<ContentProviderException>(() =>
            CurseForgePackInstaller.ReadManifest(archive));
        Assert.Contains("manifest.json", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_manifest_without_a_minecraft_version_is_rejected()
    {
        var archive = CreateArchive(("manifest.json", """{"manifestType":"minecraftModpack","files":[]}"""));

        var exception = Assert.Throws<ContentProviderException>(() =>
            CurseForgePackInstaller.ReadManifest(archive));
        Assert.Contains("Minecraft version", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("neoforge-21.1.72", LoaderKind.NeoForge, "21.1.72")]
    [InlineData("forge-47.2.0", LoaderKind.Forge, "47.2.0")]
    [InlineData("fabric-0.15.11", LoaderKind.Fabric, "0.15.11")]
    [InlineData("quilt-0.20.0-beta.9", LoaderKind.Quilt, "0.20.0-beta.9")]
    public void Loader_entries_map_onto_loader_kinds(string id, LoaderKind expectedKind, string expectedVersion)
    {
        var manifest = new CurseForgeManifest
        {
            Minecraft = new CurseForgeManifestMinecraft
            {
                Version = "1.21.1",
                ModLoaders = [new CurseForgeManifestLoader { Id = id, Primary = true }],
            },
        };

        var (kind, version) = CurseForgePackInstaller.ResolveLoader(manifest);

        Assert.Equal(expectedKind, kind);
        Assert.Equal(expectedVersion, version);
    }

    [Fact]
    public void A_pack_without_a_loader_entry_is_vanilla()
    {
        var manifest = new CurseForgeManifest
        {
            Minecraft = new CurseForgeManifestMinecraft { Version = "1.21.1" },
        };

        var (kind, version) = CurseForgePackInstaller.ResolveLoader(manifest);

        Assert.Equal(LoaderKind.Vanilla, kind);
        Assert.Null(version);
    }

    [Fact]
    public void Plan_places_available_files_in_mods_and_reports_the_rest()
    {
        var gameDirectory = Path.Combine(_workspace, "minecraft");
        Directory.CreateDirectory(gameDirectory);
        var declared = new List<CurseForgeManifestFile>
        {
            new() { ProjectID = 1, FileID = 100 },
            new() { ProjectID = 2, FileID = 200 },
            new() { ProjectID = 3, FileID = 300 },
        };
        var resolved = new List<ContentVersion>
        {
            Version(1, 100, "available.jar", "https://edge.forgecdn.net/files/1/100/available.jar"),
            // The API withholds the URL when the author disallowed third-party distribution.
            Version(2, 200, "retail.jar", url: null),
        };

        var plan = CurseForgePackInstaller.PlanFiles(declared, resolved, gameDirectory);

        var request = Assert.Single(plan.Requests);
        Assert.Equal("available.jar", Path.GetFileName(request.TargetPath));
        Assert.Equal(
            Path.Combine(gameDirectory, "mods", "available.jar"),
            request.TargetPath);
        Assert.Equal(2, plan.Skipped);
        Assert.Contains(plan.Warnings, warning =>
            warning.Contains("third-party distribution", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(plan.Warnings, warning =>
            warning.Contains("no longer available", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Plan_never_writes_outside_the_instance()
    {
        var gameDirectory = Path.Combine(_workspace, "minecraft-escape");
        Directory.CreateDirectory(gameDirectory);
        var declared = new List<CurseForgeManifestFile> { new() { ProjectID = 9, FileID = 900 } };
        var resolved = new List<ContentVersion>
        {
            Version(9, 900, "../../escaped.jar", "https://edge.forgecdn.net/files/9/900/mod.jar"),
        };

        var plan = CurseForgePackInstaller.PlanFiles(declared, resolved, gameDirectory);

        var request = Assert.Single(plan.Requests);
        Assert.True(
            Path.GetFullPath(request.TargetPath).StartsWith(Path.GetFullPath(gameDirectory), StringComparison.Ordinal),
            $"'{request.TargetPath}' escaped the instance directory.");
        // The hostile name is flattened into a single file name rather than kept as a path.
        Assert.DoesNotContain("/", Path.GetFileName(request.TargetPath), StringComparison.Ordinal);
        Assert.DoesNotContain("\\", Path.GetFileName(request.TargetPath), StringComparison.Ordinal);
        Assert.Equal(
            Path.Combine(gameDirectory, "mods"),
            Path.GetDirectoryName(request.TargetPath));
    }

    [Fact]
    public void Archive_kind_is_detected_from_the_root_entry()
    {
        var modrinth = CreateArchive(("modrinth.index.json", """{"formatVersion":1}"""));
        var curseforge = CreateArchive(("manifest.json", Manifest("1.21.1", "fabric-0.15.11", (1, 2))));
        var neither = CreateArchive(("readme.txt", "nothing"));

        Assert.Equal(ModpackArchiveKind.Modrinth, ModpackArchives.DetectKind(modrinth));
        Assert.Equal(ModpackArchiveKind.CurseForge, ModpackArchives.DetectKind(curseforge));
        Assert.Equal(ModpackArchiveKind.Unknown, ModpackArchives.DetectKind(neither));
    }

    [Fact]
    public void CurseForge_version_tokens_split_into_game_versions_and_loaders()
    {
        var (gameVersions, loaders) = ContentCompatibility.SplitVersionTokens(
            ["1.21.1", "NeoForge", "Client", "Java 21"]);

        Assert.Equal(["1.21.1"], gameVersions);
        Assert.Equal(["NeoForge"], loaders);
    }

    [Fact]
    public void Compatibility_matches_loader_names_regardless_of_case()
    {
        var version = Version(1, 1, "mod.jar", "https://example.invalid/mod.jar") with
        {
            GameVersions = ["1.21.1"],
            Loaders = ["NeoForge"],
        };

        Assert.True(ContentCompatibility.IsCompatible(version, "1.21.1", "neoforge"));
        Assert.False(ContentCompatibility.IsCompatible(version, "1.20.1", "neoforge"));
        Assert.False(ContentCompatibility.IsCompatible(version, "1.21.1", "fabric"));
    }

    [Fact]
    public void Selection_prefers_a_release_over_a_newer_beta()
    {
        var beta = Version(1, 2, "beta.jar", "https://example.invalid/beta.jar") with
        {
            VersionType = "beta",
            PublishedAt = DateTimeOffset.Parse("2026-08-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
        };
        var release = Version(1, 1, "release.jar", "https://example.invalid/release.jar") with
        {
            PublishedAt = DateTimeOffset.Parse("2026-07-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
        };

        var selected = ContentCompatibility.SelectBestVersion([beta, release], "1.21.1", "neoforge");

        Assert.NotNull(selected);
        Assert.Equal("release", selected!.VersionType);
    }

    private static ContentVersion Version(int projectId, int fileId, string fileName, string? url) => new(
        CurseForgeClient.ProviderName,
        fileId.ToString(System.Globalization.CultureInfo.InvariantCulture),
        projectId.ToString(System.Globalization.CultureInfo.InvariantCulture),
        fileName,
        fileName,
        null,
        "release",
        ["1.21.1"],
        ["neoforge"],
        url is null
            ? [new ContentFile(fileName, string.Empty, 0, true, null, null)]
            : [new ContentFile(fileName, url, 1024, true, "sha1value", null)],
        [],
        DateTimeOffset.Parse("2026-07-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));

    private static string Manifest(string minecraftVersion, string loaderId, params (int Project, int File)[] files)
    {
        var fileList = string.Join(
            ',',
            files.Select(file => $$"""{"projectID":{{file.Project}},"fileID":{{file.File}},"required":true}"""));
        return $$"""
        {
          "manifestType": "minecraftModpack",
          "manifestVersion": 1,
          "name": "Example Pack",
          "version": "1.0.0",
          "author": "someone",
          "minecraft": {
            "version": "{{minecraftVersion}}",
            "modLoaders": [ { "id": "{{loaderId}}", "primary": true } ]
          },
          "files": [ {{fileList}} ],
          "overrides": "overrides"
        }
        """;
    }

    private string CreateArchive(params (string Name, string Content)[] entries)
    {
        var path = Path.Combine(_workspace, "pack-" + Guid.NewGuid().ToString("N") + ".zip");
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
