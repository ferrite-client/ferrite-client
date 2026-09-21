using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using Ferrite.Core.Content;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ferrite.Core.Tests;

/// <summary>
/// Rescanning a large pack. The mod tab is reopened constantly, and opening hundreds of jars to read
/// descriptors that have not changed is the expensive part of that screen.
/// </summary>
public sealed class ModScanCacheTests : IDisposable
{
    private const int ModCount = 200;

    private readonly string _root;
    private readonly string _mods;
    private readonly ModScanner _scanner = new(NullLogger<ModScanner>.Instance);

    public ModScanCacheTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ferrite-modcache-" + Guid.NewGuid().ToString("N"));
        _mods = Path.Combine(_root, "mods");
        Directory.CreateDirectory(_mods);
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

    private void WriteMod(int index)
    {
        var path = Path.Combine(_mods, $"mod{index:D3}.jar");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        var entry = archive.CreateEntry("fabric.mod.json");
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(
            $$"""
            { "schemaVersion": 1, "id": "mod{{index}}", "name": "Mod {{index}}", "version": "1.0.{{index}}" }
            """);
    }

    [Fact]
    public async Task A_rescan_of_unchanged_files_does_not_open_them_again()
    {
        for (var index = 0; index < ModCount; index++)
        {
            WriteMod(index);
        }

        _scanner.Cache.Clear();
        _scanner.Cache.ResetCounters();
        var first = Stopwatch.StartNew();
        var mods = await _scanner.ScanAsync(_mods, TestContext.Current.CancellationToken);
        first.Stop();

        Assert.Equal(ModCount, mods.Count);
        Assert.Equal(ModCount, _scanner.Cache.Misses);
        Assert.Equal(0, _scanner.Cache.Hits);

        // Second scan: same files, so the descriptors come from the cache and nothing is reopened.
        _scanner.Cache.ResetCounters();
        var cached = Stopwatch.StartNew();
        var again = await _scanner.ScanAsync(_mods, TestContext.Current.CancellationToken);
        cached.Stop();

        Assert.Equal(ModCount, again.Count);
        Assert.Equal(0, _scanner.Cache.Misses);
        Assert.Equal(ModCount, _scanner.Cache.Hits);
        Assert.Equal(
            mods.Select(mod => mod.DisplayName).OrderBy(name => name, StringComparer.Ordinal),
            again.Select(mod => mod.DisplayName).OrderBy(name => name, StringComparer.Ordinal));

        // The cached scan does not open a file per mod, so it is faster than the first one.
        Assert.True(
            cached.Elapsed < first.Elapsed,
            $"cached scan {cached.ElapsedMilliseconds}ms was not faster than the first {first.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task A_changed_file_is_read_again_and_the_rest_are_not()
    {
        for (var index = 0; index < 20; index++)
        {
            WriteMod(index);
        }

        await _scanner.ScanAsync(_mods, TestContext.Current.CancellationToken);
        _scanner.Cache.ResetCounters();

        // Replace one jar, which changes both its size and its write time.
        var replaced = Path.Combine(_mods, "mod007.jar");
        File.Delete(replaced);
        WriteMod(7);
        await File.AppendAllTextAsync(replaced, "padding", TestContext.Current.CancellationToken);

        var mods = await _scanner.ScanAsync(_mods, TestContext.Current.CancellationToken);

        Assert.Equal(20, mods.Count);
        Assert.Equal(1, _scanner.Cache.Misses);
        Assert.Equal(19, _scanner.Cache.Hits);
    }

    [Fact]
    public async Task Disabling_a_mod_is_a_different_file_with_its_own_entry()
    {
        WriteMod(1);
        var enabled = await _scanner.ScanAsync(_mods, TestContext.Current.CancellationToken);
        Assert.True(Assert.Single(enabled).Enabled);

        // Disabling renames, so the next scan sees a path it has no entry for.
        var path = Assert.Single(enabled).FilePath;
        InstanceContentManager.SetEnabled(path, enabled: false);
        _scanner.Cache.ResetCounters();

        var disabled = await _scanner.ScanAsync(_mods, TestContext.Current.CancellationToken);

        var entry = Assert.Single(disabled);
        Assert.False(entry.Enabled);
        Assert.EndsWith(".disabled", entry.FileName, StringComparison.Ordinal);
        Assert.Equal(1, _scanner.Cache.Misses);
        Assert.Equal("mod1", entry.ModId);
    }
}
