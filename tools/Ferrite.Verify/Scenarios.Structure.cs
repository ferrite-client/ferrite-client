using System.IO.Compression;
using Ferrite.Core.Game;

namespace Ferrite.Verify;

/// <summary>Reading, previewing, and checking a real structure file.</summary>
internal static partial class Scenarios
{
    /// <summary>
    /// Extracts a structure from a client jar - vanilla ships 1180 of them - and reads it, renders it,
    /// lists what it is made of, and compares its data version with the jar's own. Using the jar means
    /// the input is a file Mojang wrote rather than a fixture.
    /// </summary>
    public static async Task<int> StructureAsync(
        VerifyServices services,
        string? jarPath,
        string? entryName,
        string? outputPath,
        CancellationToken cancellationToken)
    {
        if (jarPath is not { Length: > 0 } || !File.Exists(jarPath))
        {
            Console.WriteLine("Pass --jar <path to a client jar>.");
            return 64;
        }

        var gameDataVersion = StructureCompatibilityCheck.ReadClientDataVersion(jarPath);
        Console.WriteLine($"Client jar: {jarPath}");
        Console.WriteLine($"Client world_version: {gameDataVersion?.ToString() ?? "(not declared)"}");

        string entry;
        using (var archive = ZipFile.OpenRead(jarPath))
        {
            var candidates = archive.Entries
                .Where(candidate =>
                    candidate.FullName.StartsWith("data/minecraft/structure/", StringComparison.Ordinal)
                    && candidate.FullName.EndsWith(".nbt", StringComparison.Ordinal))
                .ToList();
            Console.WriteLine($"Structures in the jar: {candidates.Count}");
            if (candidates.Count == 0)
            {
                Console.WriteLine("This jar ships no structures.");
                return 2;
            }

            entry = entryName is { Length: > 0 }
                ? candidates
                    .FirstOrDefault(candidate =>
                        string.Equals(candidate.FullName, entryName, StringComparison.OrdinalIgnoreCase))
                    ?.FullName
                    ?? candidates
                        .FirstOrDefault(candidate =>
                            candidate.FullName.EndsWith(entryName, StringComparison.OrdinalIgnoreCase))
                        ?.FullName
                    ?? candidates
                        .OrderByDescending(candidate => candidate.Length)
                        .First()
                        .FullName
                : candidates.OrderByDescending(candidate => candidate.Length).First().FullName;
        }

        Console.WriteLine($"Reading {entry}");
        var temporary = Path.Combine(services.Paths.TemporaryDirectory, "structure.nbt");
        Directory.CreateDirectory(services.Paths.TemporaryDirectory);
        using (var archive = ZipFile.OpenRead(jarPath))
        {
            var source = archive.GetEntry(entry)
                ?? throw new InvalidDataException($"{entry} is not in the jar.");
            using var input = source.Open();
            await using var output = File.Create(temporary);
            await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        }

        var layout = await Task.Run(() => StructureReader.ReadFile(temporary), cancellationToken)
            .ConfigureAwait(false);
        Console.WriteLine(
            $"Structure: {layout.SizeX} x {layout.SizeY} x {layout.SizeZ}, "
            + $"{layout.Blocks.Count} block(s), DataVersion {layout.DataVersion?.ToString() ?? "(none)"}");

        var materials = layout.Materials();
        Console.WriteLine($"Distinct materials: {materials.Count}");
        foreach (var (name, count) in materials.Take(8))
        {
            Console.WriteLine($"  {count,5}  {name}");
        }

        Console.WriteLine(
            $"Compatibility: {StructureCompatibilityCheck.Evaluate(layout.DataVersion, gameDataVersion)}");

        var image = await Task.Run(() => StructurePreview.Render(layout), cancellationToken)
            .ConfigureAwait(false);
        Console.WriteLine($"Preview: {image.Width}x{image.Height} pixels, {image.Bgra.Length / 4} pixel(s)");

        // A preview that drew nothing would be a flat buffer; count the distinct colours that landed.
        var colours = new HashSet<int>();
        for (var index = 0; index + 3 < image.Bgra.Length; index += 4)
        {
            if (image.Bgra[index + 3] == 0)
            {
                continue;
            }

            colours.Add((image.Bgra[index] << 16) | (image.Bgra[index + 1] << 8) | image.Bgra[index + 2]);
        }

        Console.WriteLine($"Distinct colours drawn: {colours.Count}");

        if (image.Width == 0 || colours.Count == 0 || layout.Blocks.Count == 0)
        {
            Console.WriteLine("FAIL: the structure produced no preview.");
            return 3;
        }

        if (outputPath is { Length: > 0 })
        {
            await File.WriteAllBytesAsync(outputPath, image.Bgra, cancellationToken).ConfigureAwait(false);
            Console.WriteLine($"Wrote the raw BGRA buffer to {outputPath}");
        }

        Console.WriteLine("PASS: a real structure was read, previewed, and checked against the game version.");
        return 0;
    }
}
