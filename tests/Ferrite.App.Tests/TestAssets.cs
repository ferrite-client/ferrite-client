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
}
