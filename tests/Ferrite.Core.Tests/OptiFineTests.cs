using System.IO.Compression;
using System.Text;
using Ferrite.Core.Loaders;
using Ferrite.Core.Platform;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ferrite.Core.Tests;

/// <summary>
/// OptiFine support: recognising the installer OptiFine publishes, and adopting a version it
/// installed into the launcher's own store.
/// </summary>
public sealed class OptiFineTests : IDisposable
{
    private readonly string _root;
    private readonly AppPaths _paths;

    public OptiFineTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ferrite-optifine-" + Guid.NewGuid().ToString("N"));
        _paths = AppPaths.ForRoot(_root);
        _paths.EnsureCreated();
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void An_installer_jar_is_recognised_by_its_manifest_and_name()
    {
        var path = CreateJar(
            "OptiFine_1.21.1_HD_U_I6.jar",
            manifest: "Manifest-Version: 1.0\r\nMain-Class: optifine.InstallerFrame\r\n\r\n");

        var info = OptiFineInstaller.Inspect(path);

        Assert.NotNull(info);
        Assert.Equal("HD_U_I6", info!.OptiFineVersion);
        Assert.Equal("1.21.1", info.MinecraftVersion);
        Assert.Equal("1.21.1-OptiFine_HD_U_I6", info.VersionId);
        Assert.Contains("OptiFine HD_U_I6", info.DisplayName, StringComparison.Ordinal);
    }

    [Fact]
    public void A_partial_download_name_is_normalised()
    {
        var path = CreateJar(
            "OptiFine_1.8.9_HD_U_M6_pre2.jar.temp",
            manifest: "Manifest-Version: 1.0\r\nMain-Class: optifine.InstallerFrame\r\n\r\n");

        var info = OptiFineInstaller.Inspect(path);

        Assert.NotNull(info);
        Assert.Equal("1.8.9", info!.MinecraftVersion);
        Assert.Equal("HD_U_M6_pre2", info.OptiFineVersion);
    }

    /// <summary>
    /// OptiFine files in the wild carry a <c>preview_</c> prefix: that is how the file on this machine
    /// is named. The version is read from the tokens after the OptiFine marker, not from the start.
    /// </summary>
    [Fact]
    public void A_prefixed_file_name_is_understood()
    {
        var path = CreateJar(
            "preview_OptiFine_1.8.9_HD_U_M6_pre2.jar",
            manifest: "Manifest-Version: 1.0\r\nMain-Class: optifine.InstallerFrame\r\n\r\n");

        var info = OptiFineInstaller.Inspect(path);

        Assert.NotNull(info);
        Assert.Equal("1.8.9", info!.MinecraftVersion);
        Assert.Equal("HD_U_M6_pre2", info.OptiFineVersion);
        Assert.Equal("1.8.9-OptiFine_HD_U_M6_pre2", info.VersionId);
    }

    /// <summary>A marker in the middle of the name is also just a partial-download marker.</summary>
    [Fact]
    public void A_marker_before_the_extension_is_ignored()
    {
        var path = CreateJar(
            "preview_OptiFine_1.8.9_HD_U_M6_pre2.temp.jar",
            manifest: "Manifest-Version: 1.0\r\nMain-Class: optifine.InstallerFrame\r\n\r\n");

        var info = OptiFineInstaller.Inspect(path);

        Assert.NotNull(info);
        Assert.Equal("HD_U_M6_pre2", info!.OptiFineVersion);
        Assert.Equal("1.8.9-OptiFine_HD_U_M6_pre2", info.VersionId);
    }

    [Fact]
    public void A_jar_that_is_not_named_like_OptiFine_is_not_treated_as_one()
    {
        var path = CreateJar("sodium-fabric-0.6.0.jar", manifest: "Manifest-Version: 1.0\r\n\r\n");

        Assert.Null(OptiFineInstaller.Inspect(path));
        Assert.Null(OptiFineInstaller.Inspect(Path.Combine(_root, "missing.jar")));
    }

    /// <summary>
    /// A mod JAR of the same vintage carries OptiFine classes but no installer entry point. Only the
    /// installer can be run, so the default check refuses the mod JAR.
    /// </summary>
    [Fact]
    public void An_OptiFine_jar_without_the_installer_entry_point_is_not_an_installer()
    {
        var path = CreateJar("OptiFine_1.20.1_HD_U_I6.jar", manifest: "Manifest-Version: 1.0\r\n\r\n");

        Assert.Null(OptiFineInstaller.Inspect(path));
        Assert.NotNull(OptiFineInstaller.Inspect(path, requireInstallerMainClass: false));
    }

    [Fact]
    public void Installing_versions_are_found_in_a_game_directory()
    {
        var gameDirectory = Path.Combine(_root, ".minecraft");
        Directory.CreateDirectory(Path.Combine(gameDirectory, "versions", "1.8.9-OptiFine_1.8.9_HD_U_M6_pre2"));
        Directory.CreateDirectory(Path.Combine(gameDirectory, "versions", "1.21.1-OptiFine_HD_U_I6"));
        Directory.CreateDirectory(Path.Combine(gameDirectory, "versions", "1.21.1"));

        var found = OptiFineInstaller.FindInstalledVersions(gameDirectory);

        Assert.Equal(2, found.Count);
        Assert.Contains("1.21.1-OptiFine_HD_U_I6", found);
        Assert.DoesNotContain("1.21.1", found);
    }

    [Fact]
    public void Adopting_copies_the_version_document_and_its_library_into_the_store()
    {
        var gameDirectory = Path.Combine(_root, "source-minecraft");
        const string versionId = "1.21.1-OptiFine_HD_U_I6";
        var installer = new OptiFineInstaller(_paths, NullLogger<OptiFineInstaller>.Instance);

        WriteInstalledVersion(gameDirectory, versionId, libraryBytes: "optifine library");

        var result = installer.Adopt(gameDirectory, versionId, TestContext.Current.CancellationToken);

        Assert.Equal(versionId, result.VersionId);
        Assert.Equal(1, result.LibraryFiles);
        Assert.Empty(result.Warnings);

        Assert.True(File.Exists(_paths.VersionJsonFile(versionId)));
        var library = Path.Combine(
            _paths.LibrariesDirectory,
            "optifine",
            "OptiFine",
            "HD_U_I6",
            "OptiFine-HD_U_I6.jar");
        Assert.True(File.Exists(library), $"expected the OptiFine library at {library}");
        Assert.Equal("optifine library", File.ReadAllText(library));
    }

    [Fact]
    public void Adopting_reports_a_missing_library_instead_of_claiming_success()
    {
        var gameDirectory = Path.Combine(_root, "source-incomplete");
        const string versionId = "1.21.1-OptiFine_HD_U_I6";
        var installer = new OptiFineInstaller(_paths, NullLogger<OptiFineInstaller>.Instance);

        WriteInstalledVersion(gameDirectory, versionId, libraryBytes: null);

        var result = installer.Adopt(gameDirectory, versionId, TestContext.Current.CancellationToken);

        Assert.Equal(0, result.LibraryFiles);
        Assert.Contains(result.Warnings, warning =>
            warning.Contains("No OptiFine library", StringComparison.Ordinal));
    }

    [Fact]
    public void Adopting_a_version_that_is_not_installed_fails_with_a_clear_message()
    {
        var installer = new OptiFineInstaller(_paths, NullLogger<OptiFineInstaller>.Instance);

        var exception = Assert.Throws<LoaderException>(() => installer.Adopt(
            Path.Combine(_root, "no-such-game"),
            "1.21.1-OptiFine_HD_U_I6",
            TestContext.Current.CancellationToken));

        Assert.Contains("OptiFine_HD_U_I6", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Writes the version layout OptiFine's installer produces.</summary>
    private static void WriteInstalledVersion(string gameDirectory, string versionId, string? libraryBytes)
    {
        var versionDirectory = Path.Combine(gameDirectory, "versions", versionId);
        Directory.CreateDirectory(versionDirectory);
        File.WriteAllText(
            Path.Combine(versionDirectory, versionId + ".json"),
            $$"""
            {
              "id": "{{versionId}}",
              "inheritsFrom": "1.21.1",
              "mainClass": "net.minecraft.launchwrapper.Launch",
              "libraries": [
                { "name": "optifine:OptiFine:HD_U_I6" },
                { "name": "net.minecraft:launchwrapper:1.12" }
              ]
            }
            """);

        if (libraryBytes is null)
        {
            return;
        }

        var library = Path.Combine(
            gameDirectory,
            "libraries",
            "optifine",
            "OptiFine",
            "HD_U_I6",
            "OptiFine-HD_U_I6.jar");
        Directory.CreateDirectory(Path.GetDirectoryName(library)!);
        File.WriteAllText(library, libraryBytes);
    }

    private string CreateJar(string fileName, string? manifest)
    {
        var path = Path.Combine(_root, fileName);
        using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        if (manifest is not null)
        {
            var entry = archive.CreateEntry("META-INF/MANIFEST.MF");
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(manifest);
        }

        var classes = archive.CreateEntry("optifine/Installer.class");
        using var classWriter = new StreamWriter(classes.Open(), new UTF8Encoding(false));
        classWriter.Write("not really a class file");
        return path;
    }
}
