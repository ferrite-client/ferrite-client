using Ferrite.Core.Game;

namespace Ferrite.Verify;

/// <summary>Reading a real world's chunks, then editing a copy of it.</summary>
internal static partial class Scenarios
{
    /// <summary>
    /// Renders a real world, then edits a copy of it: the source is never written to. The copy is
    /// deleted from and copied out of, so the run is evidence about the reader, the renderer, and the
    /// editor without risking the world that was handed in.
    /// </summary>
    public static async Task<int> WorldMapAsync(
        VerifyServices services,
        string? worldDirectory,
        int deleteCount,
        CancellationToken cancellationToken)
    {
        if (worldDirectory is not { Length: > 0 } || !Directory.Exists(worldDirectory))
        {
            Console.WriteLine("Pass --world <path to a world folder>.");
            return 64;
        }

        var regions = WorldChunkEditor.Regions(worldDirectory);
        Console.WriteLine($"World: {worldDirectory}");
        Console.WriteLine($"Region files: {regions.Count}");

        var map = await Task.Run(
                () => services.WorldMaps.Render(worldDirectory, cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
        Console.WriteLine(
            $"Map: {map.Width}x{map.Height} pixels, step {map.Step}, "
            + $"{map.PresentChunks} of {map.TotalChunks} possible chunk(s) present");
        Console.WriteLine(
            $"Chunk range: x {map.MinChunkX}..{map.MaxChunkX}, z {map.MinChunkZ}..{map.MaxChunkZ}");

        // A spot check that the shading came from real heights rather than a constant.
        var distinct = map.Bgra
            .Where((_, index) => index % 4 == 0)
            .Distinct()
            .Take(6)
            .ToList();
        Console.WriteLine($"Distinct blue values in the render (first 6): {string.Join(", ", distinct)}");

        if (map.PresentChunks == 0)
        {
            Console.WriteLine("No chunks to work with.");
            return 2;
        }

        var working = Path.Combine(services.Paths.Root, "world-map-copy");
        if (Directory.Exists(working))
        {
            Directory.Delete(working, recursive: true);
        }

        await Task.Run(() => CopyDirectory(worldDirectory, working), cancellationToken).ConfigureAwait(false);
        Console.WriteLine();
        Console.WriteLine($"Copied the world to {working} so the original is untouched.");

        var toDelete = deleteCount <= 0 ? 3 : deleteCount;
        var present = new List<(int X, int Z)>();
        foreach (var region in WorldChunkEditor.Regions(working))
        {
            foreach (var chunk in new RegionFile(region.Path).ReadAll())
            {
                present.Add((chunk.X, chunk.Z));
            }
        }

        var sample = present.Take(toDelete).ToList();
        // The copy uses different chunks from the ones deleted, so it copies something that exists.
        var copySample = present.TakeLast(Math.Min(2, present.Count)).ToList();
        if (sample.Count == 0)
        {
            Console.WriteLine("The copy holds no chunks to delete.");
            return 3;
        }

        var before = WorldChunkEditor.Regions(working)
            .Sum(region => new RegionFile(region.Path).ReadAll().Count);
        var deleted = await Task.Run(
                () => services.WorldChunks.DeleteChunks(working, sample, cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
        var after = WorldChunkEditor.Regions(working)
            .Sum(region => new RegionFile(region.Path).ReadAll().Count);

        Console.WriteLine(
            $"Deleted {deleted.ChunksAffected} chunk(s) from {deleted.RegionFilesRewritten} region file(s): "
            + $"{before} -> {after} chunk(s) on disk");
        Console.WriteLine($"Region backup: {deleted.BackupPath}");
        var backedUp = deleted.BackupPath is { Length: > 0 } backup
            ? Directory.EnumerateFiles(backup).Select(path => new RegionFile(path).ReadAll().Count).Sum()
            : 0;
        Console.WriteLine($"Region files in the backup hold {backedUp} chunk(s)");

        var target = Path.Combine(services.Paths.Root, "world-map-target");
        Directory.CreateDirectory(target);

        var copied = await Task.Run(
                () => services.WorldChunks.CopyChunks(
                    working,
                    target,
                    copySample,
                    offsetX: 32,
                    offsetZ: 32,
                    cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
        var copiedOnDisk = WorldChunkEditor.Regions(target)
            .SelectMany(region => new RegionFile(region.Path).ReadAll())
            .ToList();
        Console.WriteLine(
            $"Copied {copied.ChunksAffected} chunk(s) into a new world, offset by 32,32: "
            + $"{copiedOnDisk.Count} chunk(s) on disk at "
            + $"{string.Join(", ", copiedOnDisk.Take(3).Select(chunk => $"({chunk.X},{chunk.Z})"))}");

        var expectedCopy = copySample.Select(chunk => (X: chunk.X + 32, Z: chunk.Z + 32)).ToHashSet();
        var actualCopy = copiedOnDisk.Select(chunk => (chunk.X, chunk.Z)).ToHashSet();
        if (after != before - deleted.ChunksAffected
            || copiedOnDisk.Count != copySample.Count
            || !actualCopy.SetEquals(expectedCopy))
        {
            Console.WriteLine("FAIL: the counts do not add up.");
            return 3;
        }

        Console.WriteLine("PASS: a real world was read, rendered, and edited on a copy.");
        return 0;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }
}
