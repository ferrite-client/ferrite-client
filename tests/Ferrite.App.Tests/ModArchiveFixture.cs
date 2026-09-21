using System.IO.Compression;
using System.Text;

namespace Ferrite.App.Tests;

/// <summary>
/// Writes the archives the mod scanner is meant to read. These are real zips with the descriptor
/// files each loader puts in them, so a test exercises the reader rather than a stub of it.
/// </summary>
internal static class ModArchiveFixture
{
    /// <summary>A Fabric archive: the loader reads <c>fabric.mod.json</c> from the zip.</summary>
    public static void WriteFabric(
        string path,
        string id,
        string name,
        string version,
        int payload,
        string dependency)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        Write(
            archive,
            "fabric.mod.json",
            $$"""
            {
              "schemaVersion": 1,
              "id": "{{id}}",
              "name": "{{name}}",
              "version": "{{version}}",
              "description": "Fixture mod {{id}}",
              "depends": { "fabricloader": ">=0.15.0", "{{dependency}}": "*" }
            }
            """);
        AddPadding(archive, payload);
    }

    /// <summary>A Forge-style archive: the loader reads <c>META-INF/mods.toml</c>.</summary>
    public static void WriteForge(string path, string id, string name, string version, int payload)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        Write(
            archive,
            "META-INF/mods.toml",
            $$"""
            modLoader = "javafml"
            loaderVersion = "[1,)"
            license = "MIT"

            [[mods]]
            modId = "{{id}}"
            version = "{{version}}"
            displayName = "{{name}}"
            description = "Fixture mod {{id}}"
            """);
        AddPadding(archive, payload);
    }

    /// <summary>An archive that declares nothing, which is what part of a real pack looks like.</summary>
    public static void WritePlain(string path, int payload)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        AddPadding(archive, payload);
    }

    private static void Write(ZipArchive archive, string entryName, string contents)
    {
        var entry = archive.CreateEntry(entryName);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(contents);
    }

    private static void AddPadding(ZipArchive archive, int bytes)
    {
        var entry = archive.CreateEntry("payload.bin");
        using var stream = entry.Open();
        stream.Write(new byte[Math.Max(1, bytes)]);
    }
}
