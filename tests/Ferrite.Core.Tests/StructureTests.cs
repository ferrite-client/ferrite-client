using System.IO.Compression;
using Ferrite.Core.Game;

namespace Ferrite.Core.Tests;

/// <summary>
/// Structure files: the format, the preview, and the compatibility comparison. The fixtures are
/// written through the same NBT writer the game's format uses, so the reader is exercised for real.
/// </summary>
public sealed class StructureTests : IDisposable
{
    private readonly string _workspace;

    public StructureTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), "ferrite-structure-" + Guid.NewGuid().ToString("N"));
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

    private static NbtTag Int(string name, int value) =>
        new() { Type = NbtTagType.Int, Name = name, Value = (long)value };

    private static NbtTag IntList(string name, params int[] values) => new()
    {
        Type = NbtTagType.List,
        Name = name,
        Value = values.Select(value => new NbtTag { Type = NbtTagType.Int, Value = (long)value }).ToList(),
    };

    private static NbtTag Text(string name, string value) =>
        new() { Type = NbtTagType.String, Name = name, Value = value };

    private static NbtTag Compound(string name, params (string Key, NbtTag Tag)[] children)
    {
        var map = new Dictionary<string, NbtTag>(StringComparer.Ordinal);
        foreach (var (key, tag) in children)
        {
            map[key] = tag;
        }

        return new NbtTag { Type = NbtTagType.Compound, Name = name, Value = map };
    }

    /// <summary>A structure: three palette entries, and blocks at known positions.</summary>
    private static byte[] StructureBytes(int dataVersion, params (int X, int Y, int Z, int State)[] blocks)
    {
        var palette = new NbtTag
        {
            Type = NbtTagType.List,
            Name = "palette",
            Value = new List<NbtTag>
            {
                Compound(
                    string.Empty,
                    ("Name", Text("Name", "minecraft:oak_planks")),
                    ("Properties", Compound("Properties", ("axis", Text("axis", "y"))))),
                Compound(string.Empty, ("Name", Text("Name", "minecraft:air"))),
                Compound(string.Empty, ("Name", Text("Name", "minecraft:glass_pane"))),
            },
        };

        var blockList = new NbtTag
        {
            Type = NbtTagType.List,
            Name = "blocks",
            Value = blocks.Select(block => (NbtTag)Compound(
                string.Empty,
                ("pos", IntList("pos", block.X, block.Y, block.Z)),
                ("state", Int("state", block.State)))).ToList(),
        };

        return NbtWriter.Write(
            Compound(
                string.Empty,
                ("DataVersion", Int("DataVersion", dataVersion)),
                ("size", IntList("size", 5, 3, 4)),
                ("palette", palette),
                ("blocks", blockList)),
            compress: true);
    }

    [Fact]
    public void A_structure_is_read_with_its_size_palette_and_positions()
    {
        var layout = StructureReader.Read(StructureBytes(
            3955,
            (0, 0, 0, 0),
            (1, 0, 0, 0),
            (1, 1, 0, 1),
            (2, 0, 3, 2)));

        Assert.Equal(5, layout.SizeX);
        Assert.Equal(3, layout.SizeY);
        Assert.Equal(4, layout.SizeZ);
        Assert.Equal(3955, layout.DataVersion);
        Assert.Equal(4, layout.Blocks.Count);

        var first = layout.Blocks[0];
        Assert.Equal((0, 0, 0), (first.X, first.Y, first.Z));
        Assert.Equal("minecraft:oak_planks", first.BlockName);
        Assert.Equal("y", first.Properties!["axis"]);
        Assert.Equal("minecraft:glass_pane", layout.Blocks[3].BlockName);
        Assert.Null(layout.Blocks[3].Properties);
    }

    [Fact]
    public void Materials_are_counted_and_ordered_by_how_much_is_used()
    {
        var layout = StructureReader.Read(StructureBytes(
            3955,
            (0, 0, 0, 0),
            (1, 0, 0, 0),
            (2, 0, 0, 1),
            (3, 0, 0, 2)));

        var materials = layout.Materials();

        Assert.Equal(3, materials.Count);
        Assert.Equal(("minecraft:oak_planks", 2), materials[0]);
        Assert.Contains(materials, entry => entry.Name == "minecraft:air");
        Assert.Contains(materials, entry => entry.Name == "minecraft:glass_pane");
    }

    [Fact]
    public void Air_is_recognised_so_a_preview_does_not_draw_it()
    {
        Assert.True(StructureReader.IsAir("minecraft:air"));
        Assert.True(StructureReader.IsAir("air"));
        Assert.True(StructureReader.IsAir("minecraft:cave_air"));
        Assert.False(StructureReader.IsAir("minecraft:glass"));
        Assert.Equal("oak_planks", StructureReader.StripNamespace("minecraft:oak_planks"));
    }

    [Fact]
    public void A_file_that_is_not_a_structure_is_refused()
    {
        var notAStructure = NbtWriter.Write(
            Compound(string.Empty, ("something", Int("something", 1))),
            compress: false);

        Assert.Throws<NbtException>(() => StructureReader.Read(notAStructure));
    }

    [Fact]
    public void The_preview_draws_the_blocks_and_not_the_air()
    {
        var layout = StructureReader.Read(StructureBytes(
            3955,
            (0, 0, 0, 0),
            (1, 0, 0, 0),
            (2, 0, 0, 1)));

        var image = StructurePreview.Render(layout);

        Assert.True(image.Width > 0 && image.Height > 0);
        Assert.Equal(image.Width * image.Height * 4, image.Bgra.Length);
        var drawn = 0;
        for (var index = 3; index < image.Bgra.Length; index += 4)
        {
            if (image.Bgra[index] == 255)
            {
                drawn++;
            }
        }

        Assert.True(drawn > 0, "nothing was drawn");
        Assert.True(drawn < image.Width * image.Height, "the whole buffer was filled");
    }

    [Fact]
    public void A_structure_of_only_air_renders_nothing()
    {
        var layout = StructureReader.Read(StructureBytes(3955, (0, 0, 0, 1), (1, 0, 0, 1)));

        var image = StructurePreview.Render(layout);

        Assert.Equal(0, image.Width);
        Assert.Empty(image.Bgra);
    }

    [Fact]
    public void A_large_structure_is_downscaled_to_fit_the_preview()
    {
        var blocks = new List<(int X, int Y, int Z, int State)>();
        for (var index = 0; index < 200; index++)
        {
            blocks.Add((index % 20, index / 20, 0, 0));
        }

        var layout = StructureReader.Read(StructureBytes(3955, [.. blocks]));
        var image = StructurePreview.Render(layout, maxSize: 64);

        Assert.True(image.Width <= 64, $"width was {image.Width}");
        Assert.True(image.Height <= 64, $"height was {image.Height}");
    }

    [Fact]
    public void A_named_block_has_its_own_colour_and_others_are_stable()
    {
        Assert.Equal((125, 125, 125), StructurePreview.ColourFor("minecraft:stone"));
        Assert.Equal((106, 150, 76), StructurePreview.ColourFor("grass_block"));

        // An unknown block keeps the same colour between calls, which is what makes a preview readable.
        var first = StructurePreview.ColourFor("minecraft:some_mod_block");
        Assert.Equal(first, StructurePreview.ColourFor("some_mod_block"));
    }

    [Fact]
    public void The_data_version_of_a_client_jar_is_read_from_its_version_file()
    {
        var jar = Path.Combine(_workspace, "client.jar");
        using (var archive = ZipFile.Open(jar, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("version.json");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("""{ "id": "1.21.1", "world_version": 3955 }""");
        }

        Assert.Equal(3955, StructureCompatibilityCheck.ReadClientDataVersion(jar));
        Assert.Null(StructureCompatibilityCheck.ReadClientDataVersion(
            Path.Combine(_workspace, "absent.jar")));
    }

    [Fact]
    public void A_jar_without_a_data_version_reports_none()
    {
        var jar = Path.Combine(_workspace, "odd.jar");
        using (var archive = ZipFile.Open(jar, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("version.json");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("""{ "id": "1.21.1" }""");
        }

        Assert.Null(StructureCompatibilityCheck.ReadClientDataVersion(jar));
    }

    [Theory]
    [InlineData(3955, 3955, StructureCompatibility.SameVersion)]
    [InlineData(3700, 3955, StructureCompatibility.StructureIsOlder)]
    [InlineData(4000, 3955, StructureCompatibility.StructureIsNewer)]
    [InlineData(null, 3955, StructureCompatibility.NoStructureVersion)]
    [InlineData(3955, null, StructureCompatibility.NoGameVersion)]
    public void Compatibility_compares_the_two_real_numbers(
        int? structure,
        int? game,
        StructureCompatibility expected)
    {
        Assert.Equal(expected, StructureCompatibilityCheck.Evaluate(structure, game));
    }
}
