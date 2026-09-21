using Ferrite.Core.Util;

namespace Ferrite.Core.Game;

/// <summary>One chunk inside a region file, with its payload exactly as it was stored.</summary>
public sealed record RegionChunk(int X, int Z, byte CompressionType, byte[] Data, int Timestamp);

/// <summary>
/// A Minecraft Anvil region file: an 8 KiB header of sector offsets and timestamps, then 4 KiB-aligned
/// chunks. Mutations are performed by rewriting the file through a temporary, which keeps sector
/// allocation trivially correct instead of leaving the header and the data free to disagree.
/// </summary>
public sealed class RegionFile
{
    /// <summary>Both the header's granularity and the alignment of every chunk in the file.</summary>
    public const int SectorSize = 4096;

    /// <summary>Chunks per region edge; a region holds 32 x 32 of them.</summary>
    public const int ChunksPerRegion = 32;

    /// <summary>The region a chunk belongs to, and its slot inside that region's header.</summary>
    public static (int RegionX, int RegionZ, int Index) Locate(int chunkX, int chunkZ)
    {
        var regionX = FloorDiv(chunkX, ChunksPerRegion);
        var regionZ = FloorDiv(chunkZ, ChunksPerRegion);
        var index = PositiveModulo(chunkX, ChunksPerRegion)
            + (PositiveModulo(chunkZ, ChunksPerRegion) * ChunksPerRegion);
        return (regionX, regionZ, index);
    }

    public static string FileName(int regionX, int regionZ) => $"r.{regionX}.{regionZ}.mca";

    /// <summary>
    /// Reads the region origin out of a file name. A region file's own header does not record which
    /// region it is, so the name is the only place that information exists.
    /// </summary>
    public static bool TryParseFileName(string path, out int regionX, out int regionZ)
    {
        regionX = 0;
        regionZ = 0;
        var name = System.IO.Path.GetFileNameWithoutExtension(path);
        var parts = name.Split('.');
        return parts.Length == 3
            && parts[0] == "r"
            && int.TryParse(parts[1], System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out regionX)
            && int.TryParse(parts[2], System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out regionZ);
    }

    public RegionFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = path;
        if (!TryParseFileName(path, out var regionX, out var regionZ))
        {
            throw new ArgumentException(
                $"'{System.IO.Path.GetFileName(path)}' is not a Minecraft region file name.",
                nameof(path));
        }

        RegionX = regionX;
        RegionZ = regionZ;
    }

    public string Path { get; }

    /// <summary>Which 32x32 block of chunks this file holds, from its name.</summary>
    public int RegionX { get; }

    public int RegionZ { get; }

    public bool Exists => File.Exists(Path);

    /// <summary>Every chunk the file actually holds, skipping the empty header slots.</summary>
    public IReadOnlyList<RegionChunk> ReadAll()
    {
        if (!File.Exists(Path))
        {
            return [];
        }

        var bytes = File.ReadAllBytes(Path);
        if (bytes.Length < SectorSize * 2)
        {
            return [];
        }

        var chunks = new List<RegionChunk>();
        for (var index = 0; index < ChunksPerRegion * ChunksPerRegion; index++)
        {
            var offset = ReadOffset(bytes, index);
            if (offset is null)
            {
                continue;
            }

            var chunk = ReadChunkAt(bytes, index, offset.Value.Offset, offset.Value.Sectors);
            if (chunk is not null)
            {
                chunks.Add(chunk);
            }
        }

        return chunks;
    }

    public RegionChunk? ReadChunk(int chunkX, int chunkZ)
    {
        if (!File.Exists(Path))
        {
            return null;
        }

        var bytes = File.ReadAllBytes(Path);
        if (bytes.Length < SectorSize * 2)
        {
            return null;
        }

        var (_, _, index) = Locate(chunkX, chunkZ);
        var offset = ReadOffset(bytes, index);
        return offset is null ? null : ReadChunkAt(bytes, index, offset.Value.Offset, offset.Value.Sectors);
    }

    /// <summary>
    /// Removes the given chunks by rewriting the file with the rest. A chunk that is not there is not
    /// an error: deleting a selection twice must not fail.
    /// </summary>
    public void Delete(IEnumerable<(int X, int Z)> chunks)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        var removed = chunks
            .Select(chunk => (chunk.X, chunk.Z))
            .ToHashSet();
        if (removed.Count == 0)
        {
            return;
        }

        var kept = ReadAll()
            .Where(chunk => !removed.Contains((chunk.X, chunk.Z)))
            .ToList();
        Rewrite(kept);
    }

    /// <summary>
    /// Writes a chunk into this region, replacing one at the same coordinates. The payload is stored
    /// exactly as given, so a copied chunk keeps whatever compression it arrived with.
    /// </summary>
    public void Write(RegionChunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        var chunks = ReadAll()
            .Where(existing => existing.X != chunk.X || existing.Z != chunk.Z)
            .ToList();
        chunks.Add(chunk);
        Rewrite(chunks);
    }

    /// <summary>Writes a fresh file containing exactly these chunks.</summary>
    public void Rewrite(IReadOnlyList<RegionChunk> chunks)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);

        var temporary = Path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 64 * 1024))
            {
                var header = new byte[SectorSize * 2];
                // The header is two sectors long, so the first chunk starts at sector 2. Writing the
                // placeholder first is what puts the data at the offset the header will name.
                stream.Write(header);
                var sector = 2;
                foreach (var chunk in chunks)
                {
                    var (_, _, index) = Locate(chunk.X, chunk.Z);
                    var length = chunk.Data.Length + 1;
                    var sectorCount = (length + 4 + SectorSize - 1) / SectorSize;
                    if (sectorCount > 255)
                    {
                        throw new InvalidDataException(
                            $"Chunk {chunk.X},{chunk.Z} is larger than a region file entry can describe.");
                    }

                    WriteOffset(header, index, sector, sectorCount);
                    WriteTimestamp(header, index, chunk.Timestamp);

                    var record = new byte[sectorCount * SectorSize];
                    WriteInt32(record, 0, length);
                    record[4] = chunk.CompressionType;
                    chunk.Data.CopyTo(record, 5);
                    stream.Write(record);
                    sector += sectorCount;
                }

                stream.Position = 0;
                stream.Write(header);
            }

            File.Move(temporary, Path, overwrite: true);
        }
        catch
        {
            AtomicFile.TryDelete(temporary);
            throw;
        }
    }

    /// <summary>The chunk's payload decompressed, so it can be parsed as NBT.</summary>
    public static byte[] Decompress(RegionChunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        return chunk.CompressionType switch
        {
            1 => Inflate(chunk.Data, gzip: true),
            2 => Inflate(chunk.Data, gzip: false),
            // Type 3 is stored uncompressed.
            3 => chunk.Data,
            _ => throw new InvalidDataException(
                $"Chunk {chunk.X},{chunk.Z} uses compression type {chunk.CompressionType}, which is not "
                + "an Anvil compression."),
        };
    }

    /// <summary>Compresses chunk NBT the way the game writes it (zlib).</summary>
    public static byte[] Compress(byte[] nbt)
    {
        ArgumentNullException.ThrowIfNull(nbt);
        using var output = new MemoryStream();
        using (var deflate = new System.IO.Compression.ZLibStream(
                   output,
                   System.IO.Compression.CompressionLevel.Optimal,
                   leaveOpen: true))
        {
            deflate.Write(nbt);
        }

        return output.ToArray();
    }

    private RegionChunk? ReadChunkAt(byte[] bytes, int index, int sector, int sectors)
    {
        var start = sector * SectorSize;
        if (sector < 2 || start + 5 > bytes.Length)
        {
            return null;
        }

        var length = ReadInt32(bytes, start);
        if (length <= 1 || start + 4 + length > Math.Min(bytes.Length, (sector + sectors) * SectorSize))
        {
            return null;
        }

        var compression = bytes[start + 4];
        var data = new byte[length - 1];
        Array.Copy(bytes, start + 5, data, 0, data.Length);

        return new RegionChunk(WorldX(index), WorldZ(index), compression, data, ReadTimestamp(bytes, index));
    }

    private int WorldX(int index) => (RegionX * ChunksPerRegion) + (index % ChunksPerRegion);

    private int WorldZ(int index) => (RegionZ * ChunksPerRegion) + (index / ChunksPerRegion);

    private static (int Offset, int Sectors)? ReadOffset(byte[] bytes, int index)
    {
        var position = index * 4;
        var value = ReadInt32(bytes, position);
        var offset = (value >> 8) & 0xFFFFFF;
        var sectors = value & 0xFF;
        return offset == 0 || sectors == 0 ? null : (offset, sectors);
    }

    private static int ReadTimestamp(byte[] bytes, int index) =>
        ReadInt32(bytes, SectorSize + (index * 4));

    private static void WriteOffset(byte[] header, int index, int sector, int sectors) =>
        WriteInt32(header, index * 4, ((sector & 0xFFFFFF) << 8) | (sectors & 0xFF));

    private static void WriteTimestamp(byte[] header, int index, int timestamp) =>
        WriteInt32(header, SectorSize + (index * 4), timestamp);

    private static int ReadInt32(byte[] bytes, int position) =>
        (bytes[position] << 24)
        | (bytes[position + 1] << 16)
        | (bytes[position + 2] << 8)
        | bytes[position + 3];

    private static void WriteInt32(byte[] bytes, int position, int value)
    {
        bytes[position] = (byte)(value >> 24);
        bytes[position + 1] = (byte)(value >> 16);
        bytes[position + 2] = (byte)(value >> 8);
        bytes[position + 3] = (byte)value;
    }

    private static byte[] Inflate(byte[] data, bool gzip)
    {
        using var input = new MemoryStream(data);
        using Stream decompressor = gzip
            ? new System.IO.Compression.GZipStream(input, System.IO.Compression.CompressionMode.Decompress)
            : new System.IO.Compression.ZLibStream(input, System.IO.Compression.CompressionMode.Decompress);
        using var output = new MemoryStream();
        decompressor.CopyTo(output);
        return output.ToArray();
    }

    private static int FloorDiv(int value, int divisor) =>
        value >= 0 ? value / divisor : ((value - divisor + 1) / divisor);

    private static int PositiveModulo(int value, int divisor) => ((value % divisor) + divisor) % divisor;
}
