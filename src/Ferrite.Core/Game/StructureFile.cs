namespace Ferrite.Core.Game;

/// <summary>One block in a structure, at its offset inside the structure's own box.</summary>
public sealed record StructureBlock(
    int X,
    int Y,
    int Z,
    string BlockName,
    IReadOnlyDictionary<string, string>? Properties = null);

/// <summary>A parsed structure file: its size, its blocks, and the data version it was saved with.</summary>
public sealed record StructureLayout(
    int SizeX,
    int SizeY,
    int SizeZ,
    int? DataVersion,
    IReadOnlyList<StructureBlock> Blocks)
{
    /// <summary>Blocks grouped by block type, most used first - what the structure is made of.</summary>
    public IReadOnlyList<(string Name, int Count)> Materials() => Blocks
        .GroupBy(block => block.BlockName, StringComparer.Ordinal)
        .Select(group => (Name: group.Key, Count: group.Count()))
        .OrderByDescending(entry => entry.Count)
        .ThenBy(entry => entry.Name, StringComparer.Ordinal)
        .ToList();
}

/// <summary>
/// Reads Minecraft's structure-block format: a <c>size</c>, a <c>palette</c>, and a list of
/// <c>blocks</c> naming a palette entry and a position. The format is what the game writes, so it is
/// read rather than inferred from file names.
/// </summary>
public static class StructureReader
{
    public static StructureLayout ReadFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Read(File.ReadAllBytes(path));
    }

    public static StructureLayout Read(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var root = NbtReader.Read(bytes);

        var size = root["size"]?.List;
        if (size is null || size.Count < 3)
        {
            throw new NbtException("The file is not a structure: it declares no size.");
        }

        var sizeX = size[0].AsInt() ?? 0;
        var sizeY = size[1].AsInt() ?? 0;
        var sizeZ = size[2].AsInt() ?? 0;

        var palette = (root["palette"]?.List ?? [])
            .Select(DescribePaletteEntry)
            .ToList();

        var blocks = new List<StructureBlock>();
        foreach (var entry in root["blocks"]?.List ?? [])
        {
            var position = entry["pos"]?.List;
            if (position is null || position.Count < 3 || entry["state"]?.AsInt() is not { } state)
            {
                continue;
            }

            var inRange = state >= 0 && state < palette.Count;
            var name = inRange ? palette[state].Name : $"unknown#{state}";
            blocks.Add(new StructureBlock(
                position[0].AsInt() ?? 0,
                position[1].AsInt() ?? 0,
                position[2].AsInt() ?? 0,
                name,
                inRange ? palette[state].Properties : null));
        }

        return new StructureLayout(sizeX, sizeY, sizeZ, root["DataVersion"]?.AsInt(), blocks);
    }

    /// <summary>A palette entry: the block's name and, when it has any, its block-state properties.</summary>
    private static (string Name, IReadOnlyDictionary<string, string>? Properties) DescribePaletteEntry(NbtTag tag)
    {
        var name = tag["Name"]?.AsString() ?? "unknown";
        if (tag["Properties"]?.Compound is not { Count: > 0 } properties)
        {
            return (name, null);
        }

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in properties)
        {
            if (value.AsString() is { } text)
            {
                map[key] = text;
            }
        }

        return (name, map);
    }

    /// <summary>True when a block is one of the air variants, which a preview does not draw.</summary>
    public static bool IsAir(string blockName)
    {
        var name = StripNamespace(blockName);
        return name is "air" or "cave_air" or "void_air" or "structure_void";
    }

    /// <summary>The block's name without its <c>minecraft:</c> namespace.</summary>
    public static string StripNamespace(string blockName)
    {
        ArgumentNullException.ThrowIfNull(blockName);
        var separator = blockName.IndexOf(':');
        return separator >= 0 ? blockName[(separator + 1)..] : blockName;
    }
}
