namespace Ferrite.Core.Game;

/// <summary>A rendered structure, ready to become a bitmap.</summary>
public sealed record StructureImage(int Width, int Height, byte[] Bgra);

/// <summary>
/// Draws a structure as an isometric view. This is a software projection rather than a 3D scene: each
/// block becomes a top face and two side faces, painted back to front, which is enough to see the
/// shape and size of something before placing it. Colour comes from the block's name, so a stone
/// house does not read as a random assortment of hues.
/// </summary>
public static class StructurePreview
{
    /// <summary>Half-width of a block's top face, in pixels before any downscaling.</summary>
    private const int HalfWidth = 6;

    /// <summary>Half-height of a block's top face, which makes the diamond twice as wide as it is tall.</summary>
    private const int HalfHeight = 3;

    /// <summary>How far a block's faces drop, so height is visible in the projection.</summary>
    private const int BlockHeight = 5;

    private const int Margin = 4;

    /// <summary>Blocks with a colour worth naming, so familiar materials look like themselves.</summary>
    private static readonly Dictionary<string, (byte R, byte G, byte B)> KnownBlocks =
        new(StringComparer.Ordinal)
        {
            ["stone"] = (125, 125, 125),
            ["cobblestone"] = (110, 110, 110),
            ["deepslate"] = (80, 80, 84),
            ["dirt"] = (134, 96, 67),
            ["grass_block"] = (106, 150, 76),
            ["sand"] = (219, 207, 163),
            ["sandstone"] = (216, 203, 155),
            ["oak_log"] = (162, 130, 78),
            ["oak_planks"] = (162, 130, 78),
            ["oak_leaves"] = (58, 118, 44),
            ["water"] = (51, 91, 200),
            ["lava"] = (220, 110, 30),
            ["snow_block"] = (240, 245, 248),
            ["ice"] = (150, 190, 235),
            ["glass"] = (200, 220, 230),
            ["bricks"] = (150, 97, 83),
            ["white_wool"] = (232, 236, 236),
            ["black_wool"] = (32, 34, 38),
            ["red_wool"] = (161, 39, 34),
            ["gold_block"] = (246, 208, 61),
            ["iron_block"] = (220, 220, 220),
            ["netherrack"] = (112, 47, 47),
            ["obsidian"] = (20, 18, 29),
            ["bedrock"] = (85, 85, 85),
        };

    /// <summary>
    /// Renders a structure into a BGRA image, at most <paramref name="maxSize"/> pixels on its longer
    /// axis. Air is skipped, and so is anything outside the structure's declared box.
    /// </summary>
    public static StructureImage Render(StructureLayout layout, int maxSize = 640)
    {
        ArgumentNullException.ThrowIfNull(layout);

        var blocks = layout.Blocks
            .Where(block => !StructureReader.IsAir(block.BlockName))
            .ToList();
        if (blocks.Count == 0)
        {
            return new StructureImage(0, 0, []);
        }

        // Downscale the projection rather than the canvas, so a large structure still fits.
        int halfWidth = 0, halfHeight = 0, blockHeight = 0, width = 0, height = 0;
        int minX = 0, minY = 0;
        for (var scale = 1; scale <= 64; scale++)
        {
            halfWidth = Math.Max(1, HalfWidth / scale);
            halfHeight = Math.Max(1, HalfHeight / scale);
            blockHeight = Math.Max(1, BlockHeight / scale);

            Project(blocks, halfWidth, halfHeight, blockHeight, out minX, out minY, out var maxX, out var maxY);
            width = (maxX - minX) + (Margin * 2);
            height = (maxY - minY) + (Margin * 2);
            if (width <= maxSize && height <= maxSize)
            {
                break;
            }
        }

        var pixels = new byte[Math.Max(1, width) * Math.Max(1, height) * 4];
        var offsetX = Margin - minX;
        var offsetY = Margin - minY;

        // Back to front: blocks further from the viewer are painted first, and within a column the
        // lower blocks first, so a block above another covers it.
        foreach (var block in blocks
                     .OrderBy(block => (block.X + block.Z))
                     .ThenBy(block => block.Y))
        {
            var centreX = ((block.X - block.Z) * halfWidth) + offsetX;
            var centreY = ((block.X + block.Z) * halfHeight) - (block.Y * blockHeight) + offsetY;
            var (red, green, blue) = ColourFor(block.BlockName);

            // The two side faces first, then the top, so the top face closes the shape.
            FillFace(
                pixels, width, height,
                centreX - halfWidth, centreY,
                centreX, centreY + halfHeight,
                blockHeight,
                (byte)(red * 72 / 100), (byte)(green * 72 / 100), (byte)(blue * 72 / 100));
            FillFace(
                pixels, width, height,
                centreX, centreY + halfHeight,
                centreX + halfWidth, centreY,
                blockHeight,
                (byte)(red * 55 / 100), (byte)(green * 55 / 100), (byte)(blue * 55 / 100));
            FillDiamond(pixels, width, height, centreX, centreY, halfWidth, halfHeight, red, green, blue);
        }

        return new StructureImage(width, height, pixels);
    }

    private static void Project(
        IReadOnlyList<StructureBlock> blocks,
        int halfWidth,
        int halfHeight,
        int blockHeight,
        out int minX,
        out int minY,
        out int maxX,
        out int maxY)
    {
        minX = int.MaxValue;
        minY = int.MaxValue;
        maxX = int.MinValue;
        maxY = int.MinValue;

        foreach (var block in blocks)
        {
            var centreX = (block.X - block.Z) * halfWidth;
            var centreY = ((block.X + block.Z) * halfHeight) - (block.Y * blockHeight);
            minX = Math.Min(minX, centreX - halfWidth);
            maxX = Math.Max(maxX, centreX + halfWidth);
            minY = Math.Min(minY, centreY - halfHeight);
            maxY = Math.Max(maxY, centreY + halfHeight + blockHeight);
        }
    }

    /// <summary>The top face: a diamond centred on the block's projection.</summary>
    private static void FillDiamond(
        byte[] pixels,
        int width,
        int height,
        int centreX,
        int centreY,
        int halfWidth,
        int halfHeight,
        byte red,
        byte green,
        byte blue)
    {
        for (var offsetY = -halfHeight; offsetY <= halfHeight; offsetY++)
        {
            var span = (int)(halfWidth * (1.0 - (Math.Abs(offsetY) / (double)halfHeight)));
            FillRow(pixels, width, height, centreY + offsetY, centreX - span, centreX + span, red, green, blue);
        }
    }

    /// <summary>
    /// One side face: a parallelogram made by sliding an edge from its top vertex to its bottom
    /// vertex and dropping it by the block's height.
    /// </summary>
    private static void FillFace(
        byte[] pixels,
        int width,
        int height,
        int topX,
        int topY,
        int bottomX,
        int bottomY,
        int drop,
        byte red,
        byte green,
        byte blue)
    {
        var edgeHeight = bottomY - topY;
        if (edgeHeight <= 0)
        {
            return;
        }

        for (var offset = 0; offset <= edgeHeight + drop; offset++)
        {
            var upper = topX + (int)((bottomX - topX) * Math.Clamp(offset / (double)edgeHeight, 0, 1));
            var lower = topX + (int)((bottomX - topX) * Math.Clamp((offset - drop) / (double)edgeHeight, 0, 1));
            var row = topY + offset;
            var from = Math.Min(upper, lower);
            var to = Math.Max(upper, lower);
            FillRow(pixels, width, height, row, from, to, red, green, blue);
        }
    }

    private static void FillRow(
        byte[] pixels,
        int width,
        int height,
        int row,
        int fromX,
        int toX,
        byte red,
        byte green,
        byte blue)
    {
        if (row < 0 || row >= height)
        {
            return;
        }

        var start = Math.Max(0, fromX);
        var end = Math.Min(width - 1, toX);
        for (var x = start; x <= end; x++)
        {
            var index = ((row * width) + x) * 4;
            pixels[index] = blue;
            pixels[index + 1] = green;
            pixels[index + 2] = red;
            pixels[index + 3] = 255;
        }
    }

    /// <summary>
    /// A block's colour: the named table when the block is familiar, otherwise a stable colour derived
    /// from the block's name so the same material always reads the same.
    /// </summary>
    public static (byte Red, byte Green, byte Blue) ColourFor(string blockName)
    {
        ArgumentNullException.ThrowIfNull(blockName);
        var name = StructureReader.StripNamespace(blockName);
        if (KnownBlocks.TryGetValue(name, out var known))
        {
            return known;
        }

        // A stable, muted hue per name: readable without a texture atlas.
        var hash = 17;
        foreach (var character in name)
        {
            hash = (hash * 31) + character;
        }

        var red = (byte)(70 + (Math.Abs(hash) % 120));
        var green = (byte)(70 + (Math.Abs(hash / 7) % 120));
        var blue = (byte)(70 + (Math.Abs(hash / 13) % 120));
        return (red, green, blue);
    }
}
