using System.IO.Compression;
using System.Text;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Ferrite.App.Localization;
using Ferrite.App.Services;
using Ferrite.App.ViewModels;
using Ferrite.Core.Game;
using Ferrite.Core.Platform;
using Ferrite.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Ferrite.App.Tests;

/// <summary>
/// Resource packs, shader packs, per-world datapacks, screenshots, and world icons, all against real
/// files: real zips with real <c>pack.mcmeta</c>, a pack that ships as a folder, a real PNG, and a
/// real <c>level.dat</c>.
/// </summary>
public sealed class InstanceContentTests : IDisposable
{
    private readonly string _root;
    private readonly AppServices _services;
    private readonly InstanceRecord _instance;
    private readonly string _gameDirectory;
    private readonly InstanceDetailViewModel _viewModel;

    public InstanceContentTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ferrite-content-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
        _services = new AppServices(loggerFactory, AppPaths.ForRoot(_root));
        Localizer.Apply(Avalonia.Application.Current, Localizer.English);

        _instance = _services.Instances
            .CreateAsync(
                new InstanceRecord { Id = Guid.NewGuid(), Name = "Content", MinecraftVersion = "1.21.1" },
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        _gameDirectory = _services.Paths.InstanceGameDirectory(_instance.Id);
        Directory.CreateDirectory(_gameDirectory);
        _viewModel = new InstanceDetailViewModel(_instance, _services, new MainWindowViewModel(_services));
    }

    public void Dispose()
    {
        _services.Dispose();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string InGameDirectory(params string[] segments)
    {
        var path = Path.Combine(_gameDirectory, Path.Combine(segments));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }

    /// <summary>
    /// A stand-in client jar carrying the version's pack formats, so pack compatibility is judged
    /// against a version rather than skipped.
    /// </summary>
    private void WriteClientJar(int resourceFormat, int dataFormat)
    {
        var jarPath = _services.Paths.VersionClientJarFile(_instance.MinecraftVersion);
        Directory.CreateDirectory(Path.GetDirectoryName(jarPath)!);
        using var archive = ZipFile.Open(jarPath, ZipArchiveMode.Create);
        var entry = archive.CreateEntry("version.json");
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(
            $$"""
            { "pack_version": { "resource": {{resourceFormat}}, "data": {{dataFormat}} } }
            """);
    }

    /// <summary>A real level.dat, written with the same NBT writer the server list uses.</summary>
    private static void WriteLevelDat(string worldDirectory, string name, long seed)
    {
        Directory.CreateDirectory(worldDirectory);
        var data = new NbtTag
        {
            Type = NbtTagType.Compound,
            Name = "Data",
            Value = new Dictionary<string, NbtTag>(StringComparer.Ordinal)
            {
                ["LevelName"] = new NbtTag { Type = NbtTagType.String, Name = "LevelName", Value = name },
                ["GameType"] = new NbtTag { Type = NbtTagType.Int, Name = "GameType", Value = 1 },
                ["LastPlayed"] = new NbtTag
                {
                    Type = NbtTagType.Long,
                    Name = "LastPlayed",
                    Value = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                },
                ["RandomSeed"] = new NbtTag { Type = NbtTagType.Long, Name = "RandomSeed", Value = seed },
                ["Version"] = new NbtTag
                {
                    Type = NbtTagType.Compound,
                    Name = "Version",
                    Value = new Dictionary<string, NbtTag>(StringComparer.Ordinal)
                    {
                        ["Name"] = new NbtTag { Type = NbtTagType.String, Name = "Name", Value = "1.21.1" },
                        ["Id"] = new NbtTag { Type = NbtTagType.Int, Name = "Id", Value = 3953 },
                    },
                },
            },
        };

        var root = new NbtTag
        {
            Type = NbtTagType.Compound,
            Name = string.Empty,
            Value = new Dictionary<string, NbtTag>(StringComparer.Ordinal) { ["Data"] = data },
        };

        File.WriteAllBytes(Path.Combine(worldDirectory, "level.dat"), NbtWriter.Write(root, compress: true));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task Packs_are_listed_with_their_formats_and_can_be_disabled_and_removed()
    {
        WriteClientJar(resourceFormat: 34, dataFormat: 48);
        var matching = InGameDirectory("resourcepacks", "matching.zip");
        TestAssets.WritePackZip(matching, 34);
        var outdated = InGameDirectory("resourcepacks", "outdated.zip");
        TestAssets.WritePackZip(outdated, 15);
        TestAssets.WritePackFolder(InGameDirectory("resourcepacks", "folder-pack"), 34);
        TestAssets.WritePackZip(InGameDirectory("shaderpacks", "shiny.zip"), 34);

        await _viewModel.RefreshContentAsync();

        // A zip, an old zip, and a pack that ships as a folder are all listed.
        Assert.Equal(3, _viewModel.ResourcePacks.Count);
        var outdatedItem = Assert.Single(
            _viewModel.ResourcePacks,
            item => item.FileName == "outdated.zip");
        Assert.True(outdatedItem.IsPackMismatch);
        Assert.Contains("15", outdatedItem.PackFormatText!, StringComparison.Ordinal);

        var matchingItem = Assert.Single(
            _viewModel.ResourcePacks,
            item => item.FileName == "matching.zip");
        Assert.False(matchingItem.IsPackMismatch);

        // A folder pack is a listed entry like any other, and disabling renames the folder itself.
        var folderPack = Assert.Single(
            _viewModel.ResourcePacks,
            item => item.FileName == "folder-pack");
        Assert.True(folderPack.IsEnabled);
        folderPack.ToggleCommand.Execute(null);
        await _viewModel.RefreshContentAsync();

        Assert.False(Directory.Exists(Path.Combine(_gameDirectory, "resourcepacks", "folder-pack")));
        Assert.True(Directory.Exists(Path.Combine(_gameDirectory, "resourcepacks", "folder-pack.disabled")));
        var disabled = Assert.Single(
            _viewModel.ResourcePacks,
            item => item.FileName == "folder-pack.disabled");
        Assert.False(disabled.IsEnabled);

        // Re-enabling restores it, and removing moves the file out of the instance rather than away.
        disabled.ToggleCommand.Execute(null);
        await _viewModel.RefreshContentAsync();
        Assert.True(Directory.Exists(Path.Combine(_gameDirectory, "resourcepacks", "folder-pack")));

        _viewModel.ResourcePacks.Single(item => item.FileName == "outdated.zip")
            .RemoveCommand.Execute(null);
        await _viewModel.RefreshContentAsync();

        Assert.False(File.Exists(outdated));
        Assert.Equal(2, _viewModel.ResourcePacks.Count);
        Assert.EndsWith(
            "outdated.zip",
            Assert.Single(Directory.EnumerateFiles(_viewModel.ContentBackupDirectory)),
            StringComparison.Ordinal);

        Assert.Equal("shiny.zip", Assert.Single(_viewModel.ShaderPacks).FileName);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task Datapacks_are_listed_per_world_and_a_world_icon_is_decoded()
    {
        WriteClientJar(resourceFormat: 34, dataFormat: 48);
        var world = Path.Combine(_gameDirectory, "saves", "Testworld");
        WriteLevelDat(world, "Testworld", seed: 12345);
        TestAssets.WritePng(Path.Combine(world, "icon.png"), 64, 64);
        TestAssets.WritePackZip(InGameDirectory("saves", "Testworld", "datapacks", "tweaks.zip"), 48);

        await _viewModel.RefreshContentAsync();

        var worldItem = Assert.Single(_viewModel.Worlds);
        Assert.Equal("Testworld", worldItem.Name);
        Assert.True(worldItem.HasIconBitmap, "the world's icon.png should decode onto the card");
        Assert.True(worldItem.IconBitmap!.PixelSize.Width > 0);

        var group = Assert.Single(_viewModel.WorldDatapacks);
        Assert.Equal("Testworld", group.WorldName);
        var item = Assert.Single(group.Datapacks);
        Assert.Equal("tweaks.zip", item.FileName);
        Assert.True(_viewModel.HasWorldDatapacks);

        item.ToggleCommand.Execute(null);
        await _viewModel.RefreshContentAsync();
        Assert.True(File.Exists(Path.Combine(world, "datapacks", "tweaks.zip.disabled")));
        Assert.False(Assert.Single(Assert.Single(_viewModel.WorldDatapacks).Datapacks).IsEnabled);

        Assert.Single(Assert.Single(_viewModel.WorldDatapacks).Datapacks).RemoveCommand.Execute(null);
        await _viewModel.RefreshContentAsync();
        Assert.False(File.Exists(Path.Combine(world, "datapacks", "tweaks.zip.disabled")));
        Assert.Empty(Assert.Single(_viewModel.WorldDatapacks).Datapacks);
        Assert.False(_viewModel.HasWorldDatapacks);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task Screenshots_are_listed_with_a_decoded_thumbnail()
    {
        TestAssets.WritePng(InGameDirectory("screenshots", "2026-01-01_12.00.00.png"), 640, 360);
        // A file that is not an image must not break the gallery.
        File.WriteAllText(InGameDirectory("screenshots", "notes.txt"), "not a screenshot", Encoding.UTF8);

        await _viewModel.RefreshContentAsync();

        Assert.Equal(2, _viewModel.Screenshots.Count);
        var shot = Assert.Single(
            _viewModel.Screenshots,
            item => item.FileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase));
        Assert.True(shot.HasThumbnail, "the PNG should decode");
        Assert.True(
            shot.Thumbnail!.PixelSize.Width <= 320,
            $"thumbnail width was {shot.Thumbnail.PixelSize.Width}");

        var notAnImage = Assert.Single(
            _viewModel.Screenshots,
            item => item.FileName == "notes.txt");
        Assert.False(notAnImage.HasThumbnail);
        Assert.False(string.IsNullOrWhiteSpace(notAnImage.Note));
    }
}
