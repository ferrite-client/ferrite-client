namespace Ferrite.Core.Game;

/// <summary>
/// A rendered view of the chunks a world actually has. One pixel per block column, shaded by the
/// column's height, which is the world's own heightmap rather than a guess: the point is to see what
/// exists and where before launching, not to reproduce the in-game renderer.
/// </summary>
public sealed record WorldMap(
    int Width,
    int Height,
    byte[] Bgra,
    /// <summary>How many block columns each rendered pixel covers, so a pixel maps back to a chunk.</summary>
    int Step,
    int MinChunkX,
    int MaxChunkX,
    int MinChunkZ,
    int MaxChunkZ,
    int PresentChunks,
    int TotalChunks)
{
    /// <summary>The chunk a rendered pixel belongs to, or null when the pixel is outside the map.</summary>
    public (int X, int Z)? ChunkAt(int pixelX, int pixelZ)
    {
        if (pixelX < 0 || pixelZ < 0 || pixelX >= Width || pixelZ >= Height)
        {
            return null;
        }

        var blockX = MinChunkX * 16 + (pixelX * Step);
        var blockZ = MinChunkZ * 16 + (pixelZ * Step);
        return (blockX >> 4, blockZ >> 4);
    }
}

/// <summary>Renders a world's region files into a bitmap.</summary>
public sealed class WorldMapService
{
    /// <summary>How many block columns the renderer will produce at most, per axis.</summary>
    public const int MaxPixelsPerAxis = 1024;

    /// <summary>The height used when a chunk declares no heightmap.</summary>
    private const int UnknownHeight = 64;

    /// <summary>Height range the shading is spread over, matching the world's own build limits.</summary>
    private const int MinShade = -64;

    private const int MaxShade = 320;

    public WorldMap Render(string worldDirectory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worldDirectory);

        var regionDirectory = Path.Combine(worldDirectory, "region");
        var chunks = new List<(RegionChunk Chunk, int[] Heights)>();
        var total = 0;
        if (Directory.Exists(regionDirectory))
        {
            foreach (var path in Directory
                         .EnumerateFiles(regionDirectory, "r.*.*.mca")
                         .OrderBy(entry => entry, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                total += RegionFile.ChunksPerRegion * RegionFile.ChunksPerRegion;

                var region = new RegionFile(path);
                foreach (var chunk in region.ReadAll())
                {
                    chunks.Add((chunk, HeightColumns(chunk)));
                }
            }
        }

        if (chunks.Count == 0)
        {
            return new WorldMap(0, 0, [], 1, 0, 0, 0, 0, 0, total);
        }

        var minChunkX = chunks.Min(entry => entry.Chunk.X);
        var maxChunkX = chunks.Max(entry => entry.Chunk.X);
        var minChunkZ = chunks.Min(entry => entry.Chunk.Z);
        var maxChunkZ = chunks.Max(entry => entry.Chunk.Z);

        // One pixel per block, sampled down when the world is wider than the cap so a large world
        // still renders in bounded time and memory.
        var width = ((maxChunkX - minChunkX + 1) * 16);
        var height = ((maxChunkZ - minChunkZ + 1) * 16);
        var step = Math.Max(1, (int)Math.Ceiling(Math.Max(width, height) / (double)MaxPixelsPerAxis));
        var pixelWidth = Math.Max(1, width / step);
        var pixelHeight = Math.Max(1, height / step);

        var pixels = new byte[pixelWidth * pixelHeight * 4];
        foreach (var (chunk, heights) in chunks)
        {
            for (var columnX = 0; columnX < 16; columnX++)
            {
                for (var columnZ = 0; columnZ < 16; columnZ++)
                {
                    var worldX = ((chunk.X - minChunkX) * 16) + columnX;
                    var worldZ = ((chunk.Z - minChunkZ) * 16) + columnZ;
                    var pixelX = worldX / step;
                    var pixelZ = worldZ / step;
                    if (pixelX >= pixelWidth || pixelZ >= pixelHeight)
                    {
                        continue;
                    }

                    var (red, green, blue) = Shade(heights[columnX + (columnZ * 16)]);
                    var index = ((pixelZ * pixelWidth) + pixelX) * 4;
                    pixels[index] = blue;
                    pixels[index + 1] = green;
                    pixels[index + 2] = red;
                    pixels[index + 3] = 255;
                }
            }
        }

        return new WorldMap(
            pixelWidth,
            pixelHeight,
            pixels,
            step,
            minChunkX,
            maxChunkX,
            minChunkZ,
            maxChunkZ,
            chunks.Count,
            total);
    }

    /// <summary>
    /// The heightmap of one chunk, as 256 column heights in x-major order. A chunk whose heightmap
    /// cannot be read contributes a flat tile rather than failing the whole render.
    /// </summary>
    public static int[] HeightColumns(RegionChunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        var heights = new int[256];
        Array.Fill(heights, UnknownHeight);

        NbtTag? root;
        try
        {
            root = NbtReader.Read(RegionFile.Decompress(chunk));
        }
        catch (Exception exception) when (exception is NbtException or IOException or InvalidDataException)
        {
            return heights;
        }

        var heightmaps = root["Heightmaps"] ?? root.Path("Level", "Heightmaps");
        var surface = heightmaps?["WORLD_SURFACE"] ?? heightmaps?["MOTION_BLOCKING"];
        var packed = PackedLongs(surface);
        if (packed is not null)
        {
            for (var index = 0; index < heights.Length; index++)
            {
                heights[index] = DecodePackedHeight(packed, index);
            }
        }

        return heights;
    }

    /// <summary>
    /// The heightmap's longs. It is a LongArray, whose value is a <c>long[]</c>; the list accessor only
    /// serves NBT lists, so reading it that way silently found nothing.
    /// </summary>
    private static long[]? PackedLongs(NbtTag? tag) => tag?.Value switch
    {
        long[] longs => longs,
        IReadOnlyList<NbtTag> list => list.Select(entry => entry.AsLong() ?? 0).ToArray(),
        _ => null,
    };

    /// <summary>
    /// Heightmaps are 9-bit values packed seven per long, least significant bit first, which is what
    /// the game writes and what the version's own format documents.
    /// </summary>
    private static int DecodePackedHeight(IReadOnlyList<long> values, int index)
    {
        var perLong = 7;
        var slot = index / perLong;
        if (slot >= values.Count)
        {
            return UnknownHeight;
        }

        var shift = (index % perLong) * 9;
        return (int)((values[slot] >> shift) & 0x1FF);
    }

    /// <summary>A height mapped to a colour: low is dark blue-green, high is bright grey.</summary>
    private static (byte Red, byte Green, byte Blue) Shade(int height)
    {
        var clamped = Math.Clamp(height, MinShade, MaxShade);
        var fraction = (clamped - MinShade) / (double)(MaxShade - MinShade);

        // Water and low ground read cooler; high ground reads lighter. Two stops are enough for a
        // map whose job is orientation rather than decoration.
        var red = (byte)(40 + (fraction * 200));
        var green = (byte)(70 + (fraction * 150));
        var blue = (byte)(90 + (fraction * 110));
        return (red, green, blue);
    }
}
