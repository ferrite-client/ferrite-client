using Ferrite.Core.Game;
using Ferrite.Core.Util;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ferrite.Core.Tests;

/// <summary>
/// Region files, the chunk map, and chunk edits. The fixtures are real Anvil files written by the
/// same format rules the game uses, so the reader is exercised rather than a stub of it.
/// </summary>
public sealed class WorldMapTests : IDisposable
{
    private readonly string _root;

    public WorldMapTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ferrite-worldmap-" + Guid.NewGuid().ToString("N"));
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

    /// <summary>A chunk's NBT with its position and a flat heightmap.</summary>
    private static byte[] ChunkNbt(int chunkX, int chunkZ, int height, bool legacy = false)
    {
        var heightmap = new NbtTag
        {
            Type = NbtTagType.LongArray,
            Name = "WORLD_SURFACE",
            Value = PackHeights(height),
        };
        var heightmaps = new NbtTag
        {
            Type = NbtTagType.Compound,
            Name = "Heightmaps",
            Value = new Dictionary<string, NbtTag>(StringComparer.Ordinal) { ["WORLD_SURFACE"] = heightmap },
        };
        var position = new Dictionary<string, NbtTag>(StringComparer.Ordinal)
        {
            ["xPos"] = new NbtTag { Type = NbtTagType.Int, Name = "xPos", Value = (long)chunkX },
            ["zPos"] = new NbtTag { Type = NbtTagType.Int, Name = "zPos", Value = (long)chunkZ },
        };

        if (legacy)
        {
            position["Heightmaps"] = heightmaps;
            return NbtWriter.Write(
                new NbtTag
                {
                    Type = NbtTagType.Compound,
                    Name = string.Empty,
                    Value = new Dictionary<string, NbtTag>(StringComparer.Ordinal)
                    {
                        ["Level"] = new NbtTag
                        {
                            Type = NbtTagType.Compound,
                            Name = "Level",
                            Value = position,
                        },
                    },
                },
                compress: false);
        }

        position["Heightmaps"] = heightmaps;
        return NbtWriter.Write(
            new NbtTag { Type = NbtTagType.Compound, Name = string.Empty, Value = position },
            compress: false);
    }

    /// <summary>256 nine-bit heights packed seven per long, which is how the game writes them.</summary>
    private static long[] PackHeights(int height)
    {
        var longs = new long[37];
        for (var index = 0; index < 256; index++)
        {
            var slot = index / 7;
            var shift = (index % 7) * 9;
            longs[slot] |= (long)(height & 0x1FF) << shift;
        }

        return longs;
    }

    private string WriteRegion(string worldDirectory, int regionX, int regionZ, params (int X, int Z, int Height)[] chunks)
    {
        var regionDirectory = Path.Combine(worldDirectory, "region");
        Directory.CreateDirectory(regionDirectory);
        var path = Path.Combine(regionDirectory, RegionFile.FileName(regionX, regionZ));
        var file = new RegionFile(path);
        file.Rewrite(chunks
            .Select(chunk => new RegionChunk(
                chunk.X,
                chunk.Z,
                CompressionType: 2,
                RegionFile.Compress(ChunkNbt(chunk.X, chunk.Z, chunk.Height)),
                Timestamp: 1000))
            .ToList());
        return path;
    }

    [Fact]
    public void A_region_file_holds_the_chunks_written_to_it()
    {
        var world = Path.Combine(_root, "world");
        var path = WriteRegion(world, 0, 0, (0, 0, 64), (1, 0, 200));

        var file = new RegionFile(path);
        var chunks = file.ReadAll();

        Assert.Equal(2, chunks.Count);
        Assert.Contains(chunks, chunk => chunk is { X: 0, Z: 0 });
        Assert.Contains(chunks, chunk => chunk is { X: 1, Z: 0 });
        Assert.NotNull(file.ReadChunk(1, 0));
        Assert.Null(file.ReadChunk(5, 5));
        Assert.Equal(1000, file.ReadChunk(0, 0)!.Timestamp);
    }

    [Fact]
    public void Negative_chunk_coordinates_land_in_the_region_the_game_uses()
    {
        Assert.Equal((-1, -1, 31 + (31 * 32)), RegionFile.Locate(-1, -1));
        Assert.Equal((0, 0, 0), RegionFile.Locate(0, 0));
        Assert.Equal((1, 1, 0 + (0 * 32)), RegionFile.Locate(32, 32));
        Assert.Equal((-1, 0, 31 + (5 * 32)), RegionFile.Locate(-1, 5));
    }

    [Fact]
    public void Deleting_a_chunk_rewrites_the_file_without_it()
    {
        var world = Path.Combine(_root, "world");
        var path = WriteRegion(world, 0, 0, (0, 0, 64), (1, 0, 200));

        var file = new RegionFile(path);
        file.Delete([(0, 0), (20, 20)]);

        var remaining = new RegionFile(path).ReadAll();
        Assert.Single(remaining);
        Assert.Equal(1, remaining[0].X);
        // Still a readable file: the header and the data agree after the rewrite.
        Assert.NotNull(new RegionFile(path).ReadChunk(1, 0));
    }

    [Fact]
    public void Writing_a_chunk_replaces_one_at_the_same_coordinates()
    {
        var world = Path.Combine(_root, "world");
        var path = WriteRegion(world, 0, 0, (0, 0, 64));
        var file = new RegionFile(path);

        file.Write(new RegionChunk(
            0,
            0,
            2,
            RegionFile.Compress(ChunkNbt(0, 0, 250)),
            Timestamp: 2000));

        var chunks = new RegionFile(path).ReadAll();
        Assert.Single(chunks);
        Assert.Equal(2000, chunks[0].Timestamp);
        Assert.Equal(250, WorldMapService.HeightColumns(chunks[0])[0]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void A_chunk_payload_round_trips_through_compression(byte compression)
    {
        var payload = ChunkNbt(3, -4, 77);
        var stored = compression == 1
            ? gzip(payload)
            : RegionFile.Compress(payload);
        var chunk = new RegionChunk(3, -4, compression, stored, 0);

        Assert.Equal(payload, RegionFile.Decompress(chunk));
        Assert.Equal(77, WorldMapService.HeightColumns(chunk)[128]);

        static byte[] gzip(byte[] bytes)
        {
            using var output = new MemoryStream();
            using (var stream = new System.IO.Compression.GZipStream(
                       output, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
            {
                stream.Write(bytes);
            }

            return output.ToArray();
        }
    }

    [Fact]
    public void The_map_is_shaded_by_the_worlds_own_heightmap()
    {
        var world = Path.Combine(_root, "world");
        WriteRegion(world, 0, 0, (0, 0, 40), (1, 0, 200));

        var map = new WorldMapService().Render(world, TestContext.Current.CancellationToken);

        Assert.Equal(32, map.Width);
        Assert.Equal(16, map.Height);
        Assert.Equal(2, map.PresentChunks);
        Assert.Equal(0, map.MinChunkX);
        Assert.Equal(1, map.MaxChunkX);
        Assert.Equal(32 * 16 * 4, map.Bgra.Length);

        // The higher chunk is drawn brighter than the lower one.
        var low = map.Bgra[(0 + (0 * map.Width)) * 4];
        var high = map.Bgra[(16 + (0 * map.Width)) * 4];
        Assert.True(high > low, $"high chunk blue {high} should exceed low chunk blue {low}");
    }

    [Fact]
    public void A_world_with_no_regions_renders_empty()
    {
        var world = Path.Combine(_root, "empty");
        Directory.CreateDirectory(world);

        var map = new WorldMapService().Render(world, TestContext.Current.CancellationToken);

        Assert.Equal(0, map.Width);
        Assert.Equal(0, map.PresentChunks);
    }

    [Fact]
    public void Deleting_chunks_backs_the_region_up_first()
    {
        var world = Path.Combine(_root, "world");
        WriteRegion(world, 0, 0, (0, 0, 64), (1, 0, 64));
        var backups = Path.Combine(_root, "backups");
        var editor = new WorldChunkEditor(backups, NullLogger<WorldChunkEditor>.Instance);

        var result = editor.DeleteChunks(
            world,
            [(0, 0)],
            TestContext.Current.CancellationToken);

        Assert.Equal(1, result.ChunksAffected);
        Assert.Equal(1, result.RegionFilesRewritten);
        Assert.NotNull(result.BackupPath);
        var backup = Assert.Single(Directory.EnumerateFiles(result.BackupPath!));
        Assert.Equal("r.0.0.mca", Path.GetFileName(backup));
        // The backup still holds both chunks, so the deleted one is recoverable.
        Assert.Equal(2, new RegionFile(backup).ReadAll().Count);
        Assert.Single(new RegionFile(Path.Combine(world, "region", "r.0.0.mca")).ReadAll());
    }

    [Fact]
    public void Deleting_a_chunk_that_is_not_there_is_not_an_error()
    {
        var world = Path.Combine(_root, "world");
        WriteRegion(world, 0, 0, (0, 0, 64));
        var editor = new WorldChunkEditor(Path.Combine(_root, "backups"), NullLogger<WorldChunkEditor>.Instance);

        var result = editor.DeleteChunks(world, [(7, 7)], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ChunksAffected);
        Assert.Null(result.BackupPath);
    }

    [Fact]
    public void A_copied_chunk_carries_its_new_coordinates()
    {
        var source = new RegionChunk(
            2,
            3,
            2,
            RegionFile.Compress(ChunkNbt(2, 3, 90)),
            Timestamp: 5);

        var moved = WorldChunkEditor.Relocate(source, 9, -4);

        Assert.NotNull(moved);
        Assert.Equal(9, moved!.X);
        Assert.Equal(-4, moved.Z);
        var root = NbtReader.Read(RegionFile.Decompress(moved));
        Assert.Equal(9, root["xPos"]!.AsInt());
        Assert.Equal(-4, root["zPos"]!.AsInt());
        // The heightmap the chunk carried is still there.
        Assert.Equal(90, WorldMapService.HeightColumns(moved)[0]);
    }

    [Fact]
    public void A_pre_1_18_chunk_is_relocated_under_its_level_tag()
    {
        var source = new RegionChunk(
            0,
            0,
            2,
            RegionFile.Compress(ChunkNbt(0, 0, 70, legacy: true)),
            Timestamp: 0);

        var moved = WorldChunkEditor.Relocate(source, 1, 1);

        Assert.NotNull(moved);
        var root = NbtReader.Read(RegionFile.Decompress(moved!));
        Assert.Equal(1, root.Path("Level", "xPos")!.AsInt());
        Assert.Equal(1, root.Path("Level", "zPos")!.AsInt());
    }

    [Fact]
    public void Copying_chunks_writes_them_into_the_other_world_at_the_offset()
    {
        var from = Path.Combine(_root, "from");
        var to = Path.Combine(_root, "to");
        WriteRegion(from, 0, 0, (0, 0, 64), (1, 0, 128));
        Directory.CreateDirectory(to);
        var editor = new WorldChunkEditor(Path.Combine(_root, "backups"), NullLogger<WorldChunkEditor>.Instance);

        var result = editor.CopyChunks(
            from,
            to,
            [(0, 0), (1, 0)],
            offsetX: 0,
            offsetZ: 32,
            TestContext.Current.CancellationToken);

        Assert.Equal(2, result.ChunksAffected);
        var target = new RegionFile(Path.Combine(to, "region", RegionFile.FileName(0, 1)));
        var chunks = target.ReadAll();
        Assert.Equal(2, chunks.Count);
        Assert.Contains(chunks, chunk => chunk is { X: 0, Z: 32 });
        Assert.Contains(chunks, chunk => chunk is { X: 1, Z: 32 });
        // The copied chunk really names its new position, which is what the game reads.
        var copied = target.ReadChunk(0, 32)!;
        Assert.Equal(32, NbtReader.Read(RegionFile.Decompress(copied))["zPos"]!.AsInt());
    }
}
