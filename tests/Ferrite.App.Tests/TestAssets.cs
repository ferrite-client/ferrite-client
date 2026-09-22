using System.IO.Compression;
using System.Text;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Ferrite.App.Tests;

/// <summary>Real files for tests to read: an encoded PNG and a pack zip with a real pack.mcmeta.</summary>
internal static class TestAssets
{
    /// <summary>A real PNG written by the image encoder, not a byte blob pasted into the test.</summary>
    public static void WritePng(string path, int width, int height)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var bitmap = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);
        bitmap.Save(path, PngBitmapEncoderOptions.Default);
    }

    /// <summary>A resource or data pack zip declaring one pack format.</summary>
    public static void WritePackZip(string path, int packFormat, string description = "fixture")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        var entry = archive.CreateEntry("pack.mcmeta");
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(
            $$"""{ "pack": { "pack_format": {{packFormat}}, "description": "{{description}}" } }""");
    }

    /// <summary>A resource pack that ships as a folder rather than a zip.</summary>
    public static void WritePackFolder(string directory, int packFormat)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, "pack.mcmeta"),
            $$"""{ "pack": { "pack_format": {{packFormat}}, "description": "folder fixture" } }""",
            new UTF8Encoding(false));
    }

    /// <summary>
    /// A horizontal gradient PNG, so a rendered instance card shows artwork with real tonal variation
    /// rather than a flat block. Two corner colours, blended across the width.
    /// </summary>
    public static void WriteGradientPng(string path, int width, int height, uint from, uint to)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var bitmap = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Opaque);

        using (var buffer = bitmap.Lock())
        {
            var row = new byte[buffer.RowBytes];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var t = width <= 1 ? 0d : (double)x / (width - 1);
                    var offset = x * 4;
                    row[offset + 0] = Blend(from, to, t, 0);
                    row[offset + 1] = Blend(from, to, t, 8);
                    row[offset + 2] = Blend(from, to, t, 16);
                    row[offset + 3] = 0xFF;
                }

                System.Runtime.InteropServices.Marshal.Copy(
                    row,
                    0,
                    buffer.Address + (y * buffer.RowBytes),
                    buffer.RowBytes);
            }
        }

        bitmap.Save(path, PngBitmapEncoderOptions.Default);
    }

    private static byte Blend(uint from, uint to, double t, int shift)
    {
        var a = (byte)((from >> shift) & 0xFF);
        var b = (byte)((to >> shift) & 0xFF);
        return (byte)Math.Round(a + ((b - a) * t));
    }
}
