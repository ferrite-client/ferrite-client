using System.IO.Compression;
using Ferrite.Core.Content;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ferrite.Core.Tests;

public sealed class ModScannerTests : IDisposable
{
    private const string NeoForgeToml = """
    modLoader = "javafml"
    loaderVersion = "[4,)"
    license = "Polyform-Shield-1.0.0"

    [[mods]]
    modId = "sodium"
    version = "0.8.13+mc1.21.1"
    displayName = "Sodium"
    logoFile = "sodium-icon.png" #optional
    authors = "JellySquid (jellysquid3), IMS212"
    description = '''
    Sodium is a powerful rendering engine for Minecraft.
    '''
    provides = ["indium"]

    [[dependencies.sodium]]
    modId = "neoforge"
    type = "required"
    versionRange = "[21.1,)"
    ordering = "NONE"
    side = "BOTH"
    """;

    private const string FabricJson = """
    {
      "schemaVersion": 1,
      "id": "examplemod",
      "name": "Example Mod",
      "version": "1.2.3",
      "description": "An example.",
      "authors": ["Alice", { "name": "Bob" }],
      "contact": { "homepage": "https://example.invalid" },
      "depends": { "fabricloader": ">=0.15.0", "minecraft": "~1.21.1" }
    }
    """;

    private readonly string _workspace;

    public ModScannerTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), "ferrite-mods-" + Guid.NewGuid().ToString("N"));
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
    public void Reads_fabric_metadata()
    {
        var path = CreateJar("example-fabric.jar", ("fabric.mod.json", FabricJson));
        var mod = new ModScanner(NullLogger<ModScanner>.Instance).Read(path, CancellationToken.None);

        Assert.Equal("fabric", mod.Loader);
        Assert.Equal("examplemod", mod.ModId);
        Assert.Equal("Example Mod", mod.Name);
        Assert.Equal("1.2.3", mod.Version);
        Assert.Equal("Alice, Bob", mod.Authors);
        Assert.Equal("https://example.invalid", mod.Homepage);
        Assert.Contains("fabricloader", mod.Dependencies);
        Assert.True(mod.Enabled);
    }

    [Fact]
    public void Reads_neoforge_toml_metadata()
    {
        var path = CreateJar("sodium-neoforge.jar", ("META-INF/neoforge.mods.toml", NeoForgeToml));
        var mod = new ModScanner(NullLogger<ModScanner>.Instance).Read(path, CancellationToken.None);

        Assert.Equal("neoforge", mod.Loader);
        Assert.Equal("sodium", mod.ModId);
        Assert.Equal("Sodium", mod.Name);
        Assert.Equal("0.8.13+mc1.21.1", mod.Version);
        Assert.Equal("JellySquid (jellysquid3), IMS212", mod.Authors);
        Assert.Contains("neoforge", mod.Dependencies);
    }

    [Fact]
    public void Detects_disabled_mods()
    {
        var path = CreateJar("example.jar.disabled", ("fabric.mod.json", FabricJson));
        var mod = new ModScanner(NullLogger<ModScanner>.Instance).Read(path, CancellationToken.None);

        Assert.False(mod.Enabled);
        Assert.Equal("examplemod", mod.ModId);
    }

    [Fact]
    public void Unreadable_jar_still_produces_an_entry()
    {
        var path = Path.Combine(_workspace, "broken.jar");
        File.WriteAllText(path, "this is not a zip");

        var mod = new ModScanner(NullLogger<ModScanner>.Instance).Read(path, CancellationToken.None);

        Assert.Equal("unknown", mod.Loader);
        Assert.Equal("broken.jar", mod.FileName);
    }

    [Fact]
    public void Scan_sorts_by_display_name_and_ignores_non_mod_files()
    {
        CreateJar("zzz.jar", ("fabric.mod.json", FabricJson.Replace("Example Mod", "Zeta", StringComparison.Ordinal)));
        CreateJar("aaa.jar", ("fabric.mod.json", FabricJson.Replace("Example Mod", "Alpha", StringComparison.Ordinal)));
        File.WriteAllText(Path.Combine(_workspace, "notes.txt"), "not a mod");

        var mods = new ModScanner(NullLogger<ModScanner>.Instance).Scan(_workspace, CancellationToken.None);

        Assert.Equal(2, mods.Count);
        Assert.Equal("Alpha", mods[0].Name);
        Assert.Equal("Zeta", mods[1].Name);
    }

    private string CreateJar(string name, params (string EntryName, string Content)[] entries)
    {
        var path = Path.Combine(_workspace, name);
        using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var (entryName, content) in entries)
        {
            var entry = archive.CreateEntry(entryName);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(content);
        }

        return path;
    }
}
