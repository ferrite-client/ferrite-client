using System.IO.Compression;
using System.Text;
using Ferrite.Core.Content;
using Ferrite.Core.Game;
using Ferrite.Core.Util;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ferrite.Core.Tests;

/// <summary>
/// Metadata and archives are the untrusted input to a launcher: they arrive from mod sites, pack
/// authors, and files a user downloaded from anywhere. Every one of these has to be refused with a
/// typed error or degraded to "unreadable", never by writing outside the instance or exhausting
/// memory.
/// </summary>
public sealed class HostileMetadataTests : IDisposable
{
    private readonly string _root;

    public HostileMetadataTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ferrite-hostile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
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

    private string Path_(string name) => Path.Combine(_root, name);

    [Theory]
    [InlineData("../escaped.txt")]
    [InlineData("../../escaped.txt")]
    [InlineData("C:/Windows/Temp/ferrite-escaped.txt")]
    [InlineData("/etc/ferrite-escaped.txt")]
    public async Task An_archive_cannot_write_outside_the_destination(string hostileEntry)
    {
        var archivePath = Path_("hostile-" + Guid.NewGuid().ToString("N") + ".zip");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry(hostileEntry);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write("escaped");
        }

        var destination = Path.Combine(_root, "destination");
        Directory.CreateDirectory(destination);

        var result = await ArchiveExtractor.ExtractZipAsync(
            archivePath,
            destination,
            new ArchiveExtractionOptions(),
            TestContext.Current.CancellationToken);

        // The entry is either refused or clamped inside the destination; either way nothing escaped.
        Assert.All(
            Directory.EnumerateFiles(destination, "*", SearchOption.AllDirectories),
            file => Assert.StartsWith(destination, Path.GetFullPath(file), StringComparison.OrdinalIgnoreCase));
        Assert.False(File.Exists(Path.Combine(_root, "escaped.txt")));
        Assert.False(File.Exists(Path.Combine(_root, "..", "escaped.txt")));
        Assert.False(File.Exists(@"C:\Windows\Temp\ferrite-escaped.txt"));
        Assert.False(File.Exists(@"/etc/ferrite-escaped.txt"));
        Assert.True(result.FilesExtracted >= 0);
    }

    [Fact]
    public async Task An_archive_that_expands_beyond_its_limit_is_refused_with_a_typed_error()
    {
        var archivePath = Path_("bomb.zip");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            // Highly compressible content: small on disk, large when expanded.
            var entry = archive.CreateEntry("big.bin");
            using var stream = entry.Open();
            var block = new byte[1024 * 1024];
            for (var index = 0; index < 8; index++)
            {
                stream.Write(block);
            }
        }

        var destination = Path.Combine(_root, "bomb-destination");
        Directory.CreateDirectory(destination);

        var exception = await Assert.ThrowsAsync<PathSafetyException>(() => ArchiveExtractor.ExtractZipAsync(
            archivePath,
            destination,
            new ArchiveExtractionOptions { MaxTotalBytes = 1024 * 1024 },
            TestContext.Current.CancellationToken));

        Assert.Contains("expanded size", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_mod_descriptor_nested_beyond_the_json_limit_degrades_to_unreadable()
    {
        var mods = Path.Combine(_root, "mods");
        Directory.CreateDirectory(mods);
        var path = Path.Combine(mods, "deep.jar");

        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("fabric.mod.json");
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            // A descriptor nested far past any depth a real one uses.
            writer.Write(new string('[', 5000));
            writer.Write(new string(']', 5000));
        }

        var scanner = new ModScanner(NullLogger<ModScanner>.Instance);
        var metadata = scanner.Read(path, TestContext.Current.CancellationToken);

        // The file is still listed, with no metadata, rather than failing the whole scan.
        Assert.Equal("deep.jar", metadata.FileName);
        Assert.Equal("unknown", metadata.Loader);
        Assert.Null(metadata.ModId);
    }

    [Fact]
    public void A_mod_descriptor_over_the_size_cap_is_not_parsed()
    {
        var mods = Path.Combine(_root, "mods-large");
        Directory.CreateDirectory(mods);
        var path = Path.Combine(mods, "huge.jar");

        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("fabric.mod.json");
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write("{ \"schemaVersion\": 1, \"id\": \"huge\", \"padding\": \"");
            writer.Write(new string('x', 5 * 1024 * 1024));
            writer.Write("\" }");
        }

        var scanner = new ModScanner(NullLogger<ModScanner>.Instance);
        var metadata = scanner.Read(path, TestContext.Current.CancellationToken);

        // Reading the descriptor is bounded, so the oversize one simply does not describe the mod.
        Assert.Equal("unknown", metadata.Loader);
        Assert.Null(metadata.ModId);
    }

    [Fact]
    public void Nbt_nested_past_the_depth_limit_is_a_typed_error()
    {
        var bytes = new List<byte>();
        for (var index = 0; index < 200; index++)
        {
            bytes.Add((byte)NbtTagType.Compound);
            bytes.AddRange([0, 1, (byte)'a']);
        }

        bytes.Add((byte)NbtTagType.End);

        var exception = Assert.Throws<NbtException>(() => NbtReader.Read(bytes.ToArray()));
        Assert.Contains("nesting", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_nbt_list_that_claims_a_gigantic_length_is_refused()
    {
        // A compound holding one list whose declared length is absurd, with no data behind it.
        var bytes = new List<byte>
        {
            (byte)NbtTagType.Compound, 0, 1, (byte)'a',
            (byte)NbtTagType.List, 0, 1, (byte)'l',
            (byte)NbtTagType.Byte,
            0x7F, 0xFF, 0xFF, 0xFF,
            (byte)NbtTagType.End,
        };

        var exception = Assert.Throws<NbtException>(() => NbtReader.Read(bytes.ToArray()));
        Assert.Contains("out of range", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_pack_index_that_is_not_json_is_reported_as_a_provider_error()
    {
        var archivePath = Path_("broken-index.mrpack");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("modrinth.index.json");
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write("{ this is not json");
        }

        var exception = Assert.Throws<ContentProviderException>(() => MrpackInstaller.ReadIndex(archivePath));
        Assert.Contains("index", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
