using Ferrite.Core.Util;
using Microsoft.Extensions.Logging;

namespace Ferrite.Core.Game;

/// <summary>What a chunk edit did.</summary>
public sealed record ChunkEditResult(
    int ChunksAffected,
    int RegionFilesRewritten,
    string? BackupPath);

/// <summary>
/// Chunk-level edits on a world: deleting chunks, and copying them from one world to another. Every
/// edit rewrites only the region files it touches, and copies each of those into the launcher's
/// backups first, because a region file is where a player's builds live.
/// </summary>
public sealed class WorldChunkEditor
{
    private readonly string _backupsDirectory;
    private readonly ILogger<WorldChunkEditor> _logger;

    public WorldChunkEditor(string backupsDirectory, ILogger<WorldChunkEditor> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupsDirectory);
        _backupsDirectory = backupsDirectory;
        _logger = logger;
    }

    /// <summary>The region files a world holds, as (regionX, regionZ, path).</summary>
    public static IReadOnlyList<(int X, int Z, string Path)> Regions(string worldDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worldDirectory);
        var regionDirectory = Path.Combine(worldDirectory, "region");
        if (!Directory.Exists(regionDirectory))
        {
            return [];
        }

        var regions = new List<(int X, int Z, string Path)>();
        foreach (var path in Directory.EnumerateFiles(regionDirectory, "r.*.*.mca"))
        {
            if (RegionFile.TryParseFileName(path, out var regionX, out var regionZ))
            {
                regions.Add((regionX, regionZ, path));
            }
        }

        return regions;
    }

    /// <summary>Deletes chunks from a world, backing up every region file it touches first.</summary>
    public ChunkEditResult DeleteChunks(
        string worldDirectory,
        IReadOnlyCollection<(int X, int Z)> chunks,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worldDirectory);
        ArgumentNullException.ThrowIfNull(chunks);
        if (chunks.Count == 0)
        {
            return new ChunkEditResult(0, 0, null);
        }

        var byRegion = chunks
            .GroupBy(chunk => RegionFile.Locate(chunk.X, chunk.Z))
            .Select(group => (Region: group.Key, Chunks: group.Select(chunk => (chunk.X, chunk.Z)).ToList()))
            .ToList();

        var backup = BackupDirectory(worldDirectory);
        var removed = 0;
        var rewritten = 0;
        foreach (var (region, regionChunks) in byRegion)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.Combine(
                Path.Combine(worldDirectory, "region"),
                RegionFile.FileName(region.RegionX, region.RegionZ));
            if (!File.Exists(path))
            {
                continue;
            }

            var file = new RegionFile(path);
            var present = regionChunks.Count(chunk => file.ReadChunk(chunk.X, chunk.Z) is not null);
            if (present == 0)
            {
                continue;
            }

            CopyToBackup(path, backup);
            file.Delete(regionChunks);
            removed += present;
            rewritten++;
        }

        _logger.LogInformation(
            "Deleted {Removed} chunk(s) from {World}, rewriting {Files} region file(s)",
            removed,
            worldDirectory,
            rewritten);
        return new ChunkEditResult(removed, rewritten, rewritten == 0 ? null : backup);
    }

    /// <summary>
    /// Copies chunks from one world into another, moved by an offset. The copied chunk's own
    /// coordinates are rewritten, because a chunk that still named its old position would be ignored
    /// or would displace the chunk already there.
    /// </summary>
    public ChunkEditResult CopyChunks(
        string sourceWorldDirectory,
        string targetWorldDirectory,
        IReadOnlyCollection<(int X, int Z)> chunks,
        int offsetX,
        int offsetZ,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceWorldDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetWorldDirectory);
        ArgumentNullException.ThrowIfNull(chunks);
        if (chunks.Count == 0)
        {
            return new ChunkEditResult(0, 0, null);
        }

        var targetRegionDirectory = Path.Combine(targetWorldDirectory, "region");
        Directory.CreateDirectory(targetRegionDirectory);
        var backup = BackupDirectory(targetWorldDirectory);

        var copied = 0;
        var rewritten = 0;
        var touched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (chunkX, chunkZ) in chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (sourceRegionX, sourceRegionZ, _) = RegionFile.Locate(chunkX, chunkZ);
            var sourcePath = Path.Combine(
                Path.Combine(sourceWorldDirectory, "region"),
                RegionFile.FileName(sourceRegionX, sourceRegionZ));
            if (!File.Exists(sourcePath))
            {
                continue;
            }

            var source = new RegionFile(sourcePath).ReadChunk(chunkX, chunkZ);
            if (source is null)
            {
                continue;
            }

            var targetX = chunkX + offsetX;
            var targetZ = chunkZ + offsetZ;
            var moved = Relocate(source, targetX, targetZ);
            if (moved is null)
            {
                continue;
            }

            var (targetRegionX, targetRegionZ, _) = RegionFile.Locate(targetX, targetZ);
            var targetPath = Path.Combine(
                targetRegionDirectory,
                RegionFile.FileName(targetRegionX, targetRegionZ));

            // The target is copied into the backups once, before its first rewrite in this operation.
            if (touched.Add(targetPath) && File.Exists(targetPath))
            {
                CopyToBackup(targetPath, backup);
                rewritten++;
            }

            new RegionFile(targetPath).Write(moved);
            copied++;
        }

        _logger.LogInformation(
            "Copied {Copied} chunk(s) from {Source} into {Target} (offset {OffsetX},{OffsetZ})",
            copied,
            sourceWorldDirectory,
            targetWorldDirectory,
            offsetX,
            offsetZ);
        return new ChunkEditResult(copied, rewritten, rewritten == 0 ? null : backup);
    }

    /// <summary>
    /// Rewrites the chunk's stored position. The coordinates live at the chunk root in 1.18 and
    /// later, and under <c>Level</c> before that, so both are handled.
    /// </summary>
    public static RegionChunk? Relocate(RegionChunk chunk, int targetX, int targetZ)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        NbtTag root;
        try
        {
            root = NbtReader.Read(RegionFile.Decompress(chunk));
        }
        catch (Exception exception) when (exception is NbtException or IOException or InvalidDataException)
        {
            return null;
        }

        if (root["xPos"] is not null && root["zPos"] is not null)
        {
            root = Replace(root, ("xPos", targetX), ("zPos", targetZ));
        }
        else if (root.Path("Level", "xPos") is not null && root["Level"] is { } level)
        {
            root = Replace(root, ("Level", Replace(level, ("xPos", targetX), ("zPos", targetZ))));
        }
        else
        {
            return null;
        }

        return chunk with
        {
            X = targetX,
            Z = targetZ,
            CompressionType = 2,
            Data = RegionFile.Compress(NbtWriter.Write(root, compress: false)),
        };
    }

    /// <summary>A copy of a compound with the named integer children replaced.</summary>
    private static NbtTag Replace(NbtTag compound, params (string Name, int Value)[] replacements)
    {
        var children = new Dictionary<string, NbtTag>(StringComparer.Ordinal);
        foreach (var (name, child) in compound.Compound ?? new Dictionary<string, NbtTag>())
        {
            children[name] = child;
        }

        foreach (var (name, value) in replacements)
        {
            children[name] = new NbtTag { Type = NbtTagType.Int, Value = (long)value, Name = name };
        }

        return new NbtTag
        {
            Type = NbtTagType.Compound,
            Value = children,
            Name = compound.Name,
        };
    }

    private static NbtTag Replace(NbtTag compound, (string Name, NbtTag Child) single)
    {
        var children = new Dictionary<string, NbtTag>(StringComparer.Ordinal);
        foreach (var (name, child) in compound.Compound ?? new Dictionary<string, NbtTag>())
        {
            children[name] = child;
        }

        children[single.Name] = single.Child;
        return new NbtTag
        {
            Type = NbtTagType.Compound,
            Value = children,
            Name = compound.Name,
        };
    }

    private string BackupDirectory(string worldDirectory)
    {
        var name = PathSafety.SanitizeFileName(Path.GetFileName(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(worldDirectory))));
        var directory = Path.Combine(
            _backupsDirectory,
            "world-chunks",
            $"{name}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void CopyToBackup(string path, string backupDirectory)
    {
        var destination = Path.Combine(backupDirectory, Path.GetFileName(path));
        AtomicFile.CopyReplacing(path, destination);
    }
}
