using System.IO.Compression;
using System.Text.Json;
using Ferrite.Core.Content;
using Ferrite.Core.Platform;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ferrite.Core.Tests;

/// <summary>
/// Pack metadata: what <c>pack.mcmeta</c> declares, and how that compares with the format the
/// instance's own client file says it uses.
/// </summary>
public sealed class PackMetadataTests : IDisposable
{
    private readonly string _workspace;

    public PackMetadataTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), "ferrite-pack-" + Guid.NewGuid().ToString("N"));
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
    public void A_single_declared_format_is_read()
    {
        var metadata = PackMetadataReader.Parse("""
            { "pack": { "pack_format": 34, "description": "A pack" } }
            """);

        Assert.NotNull(metadata);
        Assert.Equal(34, metadata!.PackFormat);
        Assert.False(metadata.DeclaresRange);
        Assert.Equal("A pack", metadata.Description);
        Assert.Equal("format 34", metadata.FormatText);
    }

    [Fact]
    public void A_supported_formats_array_becomes_a_range()
    {
        var metadata = PackMetadataReader.Parse("""
            { "pack": { "pack_format": 34, "supported_formats": [34, 46, 48] } }
            """);

        Assert.NotNull(metadata);
        Assert.True(metadata!.DeclaresRange);
        Assert.Equal(34, metadata.EffectiveMin);
        Assert.Equal(48, metadata.EffectiveMax);
        Assert.Equal("formats 34–48", metadata.FormatText);
    }

    [Fact]
    public void A_supported_formats_object_becomes_a_range()
    {
        var metadata = PackMetadataReader.Parse("""
            { "pack": { "pack_format": 46, "supported_formats": { "min_inclusive": 46, "max_inclusive": 55 } } }
            """);

        Assert.NotNull(metadata);
        Assert.Equal(46, metadata.EffectiveMin);
        Assert.Equal(55, metadata.EffectiveMax);
    }

    [Fact]
    public void Pack_filters_are_read()
    {
        var metadata = PackMetadataReader.Parse("""
            {
              "pack": { "pack_format": 34 },
              "filter": { "block": [ { "namespace": "minecraft" } ], "item": [ { "namespace": "example" } ] }
            }
            """);

        Assert.NotNull(metadata);
        Assert.Equal(["minecraft", "example"], metadata!.FilteredNamespaces);
    }

    [Fact]
    public void A_chat_component_description_becomes_plain_text()
    {
        var metadata = PackMetadataReader.Parse("""
            { "pack": { "pack_format": 34, "description": { "text": "Component pack" } } }
            """);

        Assert.Equal("Component pack", metadata!.Description);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{ "pack": 12 }""")]
    public void Malformed_metadata_is_reported_as_absent(string json)
    {
        Assert.Null(PackMetadataReader.Parse(json));
    }

    [Fact]
    public void Metadata_is_read_from_a_pack_zip()
    {
        var path = CreatePackZip("resourcepack-34.zip", """{ "pack": { "pack_format": 34 } }""");

        var metadata = PackMetadataReader.ReadFromZip(path);

        Assert.NotNull(metadata);
        Assert.Equal(34, metadata!.PackFormat);
    }

    [Fact]
    public void A_zip_without_pack_metadata_is_reported_as_absent()
    {
        var path = Path.Combine(_workspace, "plain.zip");
        using (var stream = File.Create(path))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("readme.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("nothing to see");
        }

        Assert.Null(PackMetadataReader.ReadFromZip(path));
    }

    [Fact]
    public void An_unpacked_pack_directory_is_read()
    {
        var directory = Path.Combine(_workspace, "unpacked");
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, PackMetadata.MetaFileName),
            """{ "pack": { "pack_format": 15 } }""");

        Assert.Equal(15, PackMetadataReader.ReadFromDirectory(directory)!.PackFormat);
        Assert.Null(PackMetadataReader.ReadFromDirectory(Path.Combine(_workspace, "missing")));
    }

    [Theory]
    [InlineData(34, "34", PackCompatibility.Compatible)]
    [InlineData(46, "34", PackCompatibility.Mismatch)]
    [InlineData(0, "34", PackCompatibility.Mismatch)]
    public void Declared_formats_are_compared_with_the_instance(
        int declared,
        string instanceFormatText,
        PackCompatibility expected)
    {
        var metadata = PackMetadataReader.Parse($$"""{ "pack": { "pack_format": {{declared}} } }""");
        var instanceFormat = int.Parse(instanceFormatText, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(expected, PackMetadataReader.Evaluate(metadata, instanceFormat));
    }

    [Fact]
    public void A_range_that_contains_the_instance_is_compatible()
    {
        var metadata = PackMetadataReader.Parse("""
            { "pack": { "pack_format": 46, "supported_formats": [34, 48] } }
            """);

        Assert.Equal(PackCompatibility.Compatible, PackMetadataReader.Evaluate(metadata, 40));
        Assert.Equal(PackCompatibility.Mismatch, PackMetadataReader.Evaluate(metadata, 55));
    }

    [Fact]
    public void An_unknown_instance_format_is_reported_as_unknown_not_as_a_mismatch()
    {
        var metadata = PackMetadataReader.Parse("""{ "pack": { "pack_format": 34 } }""");

        Assert.Equal(PackCompatibility.Unknown, PackMetadataReader.Evaluate(metadata, instanceFormat: null));
        Assert.Equal(PackCompatibility.Unknown, PackMetadataReader.Evaluate(metadata: null, 34));
        Assert.Contains(
            "unknown",
            PackMetadataReader.DescribeCompatibility(PackCompatibility.Unknown, null),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Client_pack_formats_are_read_from_the_versions_own_file()
    {
        var json = """
            { "id": "1.21.1", "pack_version": { "resource": 34, "data": 48 } }
            """;

        var formats = ClientPackFormat.Parse(json);

        Assert.NotNull(formats);
        Assert.Equal(34, formats!.Resource);
        Assert.Equal(48, formats.Data);
    }

    [Fact]
    public void An_older_single_number_pack_version_is_read()
    {
        var formats = ClientPackFormat.Parse("""{ "pack_version": 4 }""");

        Assert.NotNull(formats);
        Assert.Equal(4, formats!.Resource);
        Assert.Equal(4, formats.Data);
    }

    /// <summary>
    /// Current clients split the format into a major and a minor: Minecraft 26.3 ships
    /// <c>resource_major 97, resource_minor 1</c>. A pack's <c>pack_format</c> is the major.
    /// </summary>
    [Fact]
    public void A_current_client_pack_version_is_read()
    {
        var json = """
            {
              "id": "26.3",
              "protocol_version": 777,
              "pack_version": {
                "resource_major": 97,
                "resource_minor": 1,
                "data_major": 121,
                "data_minor": 0
              }
            }
            """;

        var formats = ClientPackFormat.Parse(json);

        Assert.NotNull(formats);
        Assert.Equal(97, formats!.Resource);
        Assert.Equal(121, formats.Data);
        Assert.Equal("97.1", formats.ResourceText);
        Assert.Equal("121", formats.DataText);
    }

    [Fact]
    public void A_current_format_is_compared_by_its_major_version()
    {
        var metadata = PackMetadataReader.Parse("""{ "pack": { "pack_format": 97 } }""");

        Assert.Equal(PackCompatibility.Compatible, PackMetadataReader.Evaluate(metadata, 97));
        Assert.Equal(PackCompatibility.Mismatch, PackMetadataReader.Evaluate(metadata, 34));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("not json")]
    [InlineData("""{ "pack_version": "34" }""")]
    public void A_version_file_without_usable_pack_formats_is_reported_as_absent(string json)
    {
        Assert.Null(ClientPackFormat.Parse(json));
    }

    [Fact]
    public void The_client_jar_is_read_when_it_exists_and_reported_absent_when_it_does_not()
    {
        var paths = AppPaths.ForRoot(_workspace);
        paths.EnsureCreated();
        var jarPath = paths.VersionClientJarFile("1.21.1");
        Directory.CreateDirectory(Path.GetDirectoryName(jarPath)!);
        using (var stream = File.Create(jarPath))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("version.json");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("""{ "id": "1.21.1", "pack_version": { "resource": 34, "data": 48 } }""");
        }

        var reader = new ClientPackFormat(paths, NullLogger<ClientPackFormat>.Instance);

        Assert.Equal(34, reader.Read("1.21.1")!.Resource);
        Assert.Null(reader.Read("1.20.1"));
    }

    [Fact]
    public void Listing_a_pack_folder_adds_what_each_pack_declares()
    {
        var gameDirectory = Path.Combine(_workspace, "minecraft");
        var packs = Path.Combine(gameDirectory, "resourcepacks");
        Directory.CreateDirectory(packs);
        CreatePackZip(Path.Combine(packs, "old.zip"), """{ "pack": { "pack_format": 15 } }""");
        CreatePackZip(Path.Combine(packs, "current.zip"), """{ "pack": { "pack_format": 34 } }""");
        File.WriteAllText(Path.Combine(packs, "unknown.zip"), "not a zip at all");

        var entries = InstanceContentManager.ListPacks(gameDirectory, "resourcepacks", instanceFormat: 34);

        var current = entries.First(entry => entry.FileName == "current.zip");
        Assert.Equal("format 34", current.PackFormatText);
        Assert.False(current.IsPackMismatch);

        var old = entries.First(entry => entry.FileName == "old.zip");
        Assert.True(old.IsPackMismatch);
        Assert.Contains("does not list format 34", old.CompatibilityText!, StringComparison.Ordinal);

        // A file that is not a pack is still listed, without a claim about it.
        var unknown = entries.First(entry => entry.FileName == "unknown.zip");
        Assert.Null(unknown.PackFormatText);
        Assert.Null(unknown.CompatibilityText);
    }

    /// <summary>Writes a pack ZIP whose root contains <c>pack.mcmeta</c>.</summary>
    private string CreatePackZip(string path, string metaJson)
    {
        var target = Path.IsPathRooted(path) ? path : Path.Combine(_workspace, path);
        using var stream = File.Create(target);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        var entry = archive.CreateEntry(PackMetadata.MetaFileName);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(JsonSerializer.Deserialize<JsonElement>(metaJson).GetRawText());
        return target;
    }
}
