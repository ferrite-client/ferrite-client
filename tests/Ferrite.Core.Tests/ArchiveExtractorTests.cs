using System.IO.Compression;
using Ferrite.Core.Util;

namespace Ferrite.Core.Tests;

public sealed class ArchiveExtractorTests : IDisposable
{
    private readonly string _workspace;

    public ArchiveExtractorTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), "ferrite-archive-" + Guid.NewGuid().ToString("N"));
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
    public async Task ExtractZip_writes_regular_entries()
    {
        var archive = CreateArchive(("config/mod.toml", "enabled = true"), ("overrides/readme.txt", "hello"));
        var destination = Path.Combine(_workspace, "out");

        var result = await ArchiveExtractor.ExtractZipAsync(
            archive,
            destination,
            new ArchiveExtractionOptions(),
            TestContext.Current.CancellationToken);

        Assert.Equal(2, result.FilesExtracted);
        Assert.Equal("enabled = true", await File.ReadAllTextAsync(Path.Combine(destination, "config", "mod.toml")));
        Assert.Empty(result.SkippedEntries);
    }

    [Theory]
    [InlineData("../escaped.txt")]
    [InlineData("nested/../../escaped.txt")]
    [InlineData("/absolute.txt")]
    [InlineData("C:\\windows\\escaped.txt")]
    public async Task ExtractZip_never_writes_outside_destination(string hostileEntry)
    {
        var archive = CreateArchive((hostileEntry, "owned"), ("safe.txt", "ok"));
        var destination = Path.Combine(_workspace, "hostile-out");

        var result = await ArchiveExtractor.ExtractZipAsync(
            archive,
            destination,
            new ArchiveExtractionOptions(),
            TestContext.Current.CancellationToken);

        Assert.Equal(1, result.FilesExtracted);
        Assert.Contains(result.SkippedEntries, entry => entry.Replace('\\', '/').Contains("escaped", StringComparison.Ordinal)
            || entry.Contains("absolute", StringComparison.Ordinal));

        var escapedCandidates = Directory
            .EnumerateFiles(_workspace, "*escaped*", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(_workspace, "*absolute*", SearchOption.AllDirectories))
            .ToList();
        Assert.Empty(escapedCandidates);
    }

    [Fact]
    public async Task ExtractZip_skips_symbolic_links()
    {
        var archive = Path.Combine(_workspace, "symlink.zip");
        using (var stream = File.Create(archive))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            var link = zip.CreateEntry("link");
            link.ExternalAttributes = unchecked(0xA000 << 16);
            await using var entryStream = link.Open();
            await entryStream.WriteAsync("../../etc/passwd"u8.ToArray());
        }

        var destination = Path.Combine(_workspace, "symlink-out");
        var result = await ArchiveExtractor.ExtractZipAsync(
            archive,
            destination,
            new ArchiveExtractionOptions(),
            TestContext.Current.CancellationToken);

        Assert.Equal(0, result.FilesExtracted);
        Assert.Contains("link", result.SkippedEntries);
        Assert.False(File.Exists(Path.Combine(destination, "link")));
    }

    [Fact]
    public async Task ExtractZip_strips_prefix_and_honours_include_filter()
    {
        var archive = CreateArchive(("overrides/config/a.txt", "a"), ("overrides/mods/b.jar", "b"), ("other/c.txt", "c"));
        var destination = Path.Combine(_workspace, "filtered");

        var result = await ArchiveExtractor.ExtractZipAsync(
            archive,
            destination,
            new ArchiveExtractionOptions
            {
                StripPrefix = "overrides",
                Include = path => path.StartsWith("mods", StringComparison.Ordinal),
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(1, result.FilesExtracted);
        Assert.True(File.Exists(Path.Combine(destination, "mods", "b.jar")));
        Assert.False(Directory.Exists(Path.Combine(destination, "config")));
    }

    [Fact]
    public async Task ExtractZip_enforces_entry_limit()
    {
        var archive = CreateArchive(("a.txt", "a"), ("b.txt", "b"), ("c.txt", "c"));
        var destination = Path.Combine(_workspace, "limited");

        await Assert.ThrowsAsync<PathSafetyException>(() => ArchiveExtractor.ExtractZipAsync(
            archive,
            destination,
            new ArchiveExtractionOptions { MaxEntries = 2 },
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReadEntry_and_List_work_case_insensitively()
    {
        var archive = CreateArchive(("Modrinth.index.json", "{\"formatVersion\":1}"));

        Assert.True(ArchiveExtractor.ContainsEntry(archive, "modrinth.index.json"));
        var text = ArchiveExtractor.ReadEntryText(archive, "MODRINTH.INDEX.JSON", 4096);
        Assert.Equal("{\"formatVersion\":1}", text);
        Assert.Single(ArchiveExtractor.ListEntries(archive));
    }

    private string CreateArchive(params (string Name, string Content)[] entries)
    {
        var path = Path.Combine(_workspace, "archive-" + Guid.NewGuid().ToString("N") + ".zip");
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
