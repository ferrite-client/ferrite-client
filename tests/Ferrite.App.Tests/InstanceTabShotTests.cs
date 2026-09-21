using System.IO.Compression;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Ferrite.App.Localization;
using Ferrite.App.Services;
using Ferrite.App.ViewModels;
using Ferrite.App.Views;
using Ferrite.Core.Game;
using Ferrite.Core.Minecraft;
using Ferrite.Core.Platform;
using Ferrite.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Ferrite.App.Tests;

/// <summary>
/// Renders the instance page's newer tabs with real data behind them, so the layout can be looked at
/// rather than only asserted. Each tab is selected in turn and captured.
/// </summary>
public sealed class InstanceTabShotTests : IDisposable
{
    private readonly string _root;
    private readonly AppServices _services;

    public InstanceTabShotTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ferrite-tabshots-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
        _services = new AppServices(loggerFactory, AppPaths.ForRoot(_root));
        Localizer.Apply(Avalonia.Application.Current, Localizer.English);
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

    [AvaloniaFact]
    public async Task The_new_tabs_render_with_data()
    {
        var record = await _services.Instances.CreateAsync(
            new InstanceRecord
            {
                Id = Guid.NewGuid(),
                Name = "Shown instance",
                MinecraftVersion = "1.21.1",
                Loader = LoaderKind.LabyMod,
                LoaderVersion = "1.21.1-LabyMod-4-abcdef12",
            },
            CancellationToken.None);
        var game = _services.Paths.InstanceGameDirectory(record.Id);
        Directory.CreateDirectory(game);

        // A world with chunks, so the map has something to draw.
        var world = Path.Combine(game, "saves", "Shown world");
        var regionDirectory = Path.Combine(world, "region");
        Directory.CreateDirectory(regionDirectory);
        var chunks = new List<RegionChunk>();
        for (var x = 0; x < 4; x++)
        {
            for (var z = 0; z < 4; z++)
            {
                chunks.Add(new RegionChunk(
                    x,
                    z,
                    2,
                    RegionFile.Compress(ChunkNbt(x, z, 60 + (x * 20) + (z * 6))),
                    0));
            }
        }

        new RegionFile(Path.Combine(regionDirectory, "r.0.0.mca")).Rewrite(chunks);

        // A structure, so the structure tab has something to draw.
        var structures = Path.Combine(world, "generated", "minecraft", "structures");
        Directory.CreateDirectory(structures);
        await File.WriteAllBytesAsync(
            Path.Combine(structures, "hut.nbt"),
            Structure(),
            CancellationToken.None);

        // A client jar, so the compatibility line has a real number to compare against.
        var jar = _services.Paths.VersionClientJarFile(record.MinecraftVersion);
        Directory.CreateDirectory(Path.GetDirectoryName(jar)!);
        using (var archive = ZipFile.Open(jar, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("version.json");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("""{ "id": "1.21.1", "world_version": 3955 }""");
        }

        var shell = new MainWindowViewModel(_services);
        var viewModel = new InstanceDetailViewModel(record, _services, shell);
        await viewModel.InitializeAsync();

        // The world has no level.dat in this fixture, so it is registered directly; the map tab only
        // needs the folder.
        var worldItem = new WorldItemViewModel(
            new WorldInfo
            {
                DirectoryPath = world,
                FolderName = "Shown world",
                Name = "Shown world",
            },
            _ => Task.CompletedTask,
            _ => Task.CompletedTask,
            _ => Task.CompletedTask,
            _ => Task.CompletedTask);
        viewModel.Worlds.Add(worldItem);
        viewModel.MapWorld = worldItem;
        viewModel.MapCopyTarget = worldItem;

        await viewModel.RefreshMapCommand.ExecuteAsync(null);
        await viewModel.LoadStructureAsync(Path.Combine(structures, "hut.nbt"));

        var window = new Window
        {
            Content = new InstanceDetailView { DataContext = viewModel },
            Width = 1280,
            Height = 820,
        };
        window.Show();

        var tabs = window.GetVisualDescendants().OfType<TabControl>().First();
        var captured = 0;
        for (var index = 0; index < tabs.ItemCount; index++)
        {
            tabs.SelectedIndex = index;
            // A layout pass after switching, so the frame is of the tab that is now selected.
            window.CaptureRenderedFrame();
            var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);

            var header = (tabs.Items[index] as TabItem)?.Header?.ToString() ?? $"tab-{index}";
            Save(frame!, $"instance-tab-{header.ToLowerInvariant()}");
            captured++;
        }

        Assert.Equal(tabs.ItemCount, captured);
        Assert.True(tabs.ItemCount >= 11, $"expected the new tabs to be present, saw {tabs.ItemCount}");
    }

    private static byte[] ChunkNbt(int x, int z, int height)
    {
        var longs = new long[37];
        for (var index = 0; index < 256; index++)
        {
            longs[index / 7] |= (long)height << ((index % 7) * 9);
        }

        var root = new NbtTag
        {
            Type = NbtTagType.Compound,
            Name = string.Empty,
            Value = new Dictionary<string, NbtTag>(StringComparer.Ordinal)
            {
                ["xPos"] = new NbtTag { Type = NbtTagType.Int, Name = "xPos", Value = (long)x },
                ["zPos"] = new NbtTag { Type = NbtTagType.Int, Name = "zPos", Value = (long)z },
                ["Heightmaps"] = new NbtTag
                {
                    Type = NbtTagType.Compound,
                    Name = "Heightmaps",
                    Value = new Dictionary<string, NbtTag>(StringComparer.Ordinal)
                    {
                        ["WORLD_SURFACE"] = new NbtTag
                        {
                            Type = NbtTagType.LongArray,
                            Name = "WORLD_SURFACE",
                            Value = longs,
                        },
                    },
                },
            },
        };

        return NbtWriter.Write(root, compress: false);
    }

    private static byte[] Structure()
    {
        var blocks = new List<NbtTag>();
        var palette = new NbtTag
        {
            Type = NbtTagType.List,
            Name = "palette",
            Value = new List<NbtTag>
            {
                Compound("minecraft:cobblestone"),
                Compound("minecraft:oak_planks"),
                Compound("minecraft:air"),
            },
        };

        for (var x = 0; x < 6; x++)
        {
            for (var z = 0; z < 5; z++)
            {
                blocks.Add(Block(x, 0, z, 0));
            }
        }

        for (var y = 1; y < 4; y++)
        {
            for (var x = 0; x < 6; x++)
            {
                blocks.Add(Block(x, y, 0, 1));
            }
        }

        return NbtWriter.Write(
            new NbtTag
            {
                Type = NbtTagType.Compound,
                Name = string.Empty,
                Value = new Dictionary<string, NbtTag>(StringComparer.Ordinal)
                {
                    ["DataVersion"] = new NbtTag { Type = NbtTagType.Int, Name = "DataVersion", Value = 3955L },
                    ["size"] = IntList("size", 6, 5, 5),
                    ["palette"] = palette,
                    ["blocks"] = new NbtTag { Type = NbtTagType.List, Name = "blocks", Value = blocks },
                },
            },
            compress: true);

        static NbtTag Compound(string name) => new()
        {
            Type = NbtTagType.Compound,
            Name = string.Empty,
            Value = new Dictionary<string, NbtTag>(StringComparer.Ordinal)
            {
                ["Name"] = new NbtTag { Type = NbtTagType.String, Name = "Name", Value = name },
            },
        };
    }

    private static NbtTag Block(int x, int y, int z, int state) => new()
    {
        Type = NbtTagType.Compound,
        Name = string.Empty,
        Value = new Dictionary<string, NbtTag>(StringComparer.Ordinal)
        {
            ["pos"] = IntList("pos", x, y, z),
            ["state"] = new NbtTag { Type = NbtTagType.Int, Name = "state", Value = (long)state },
        },
    };

    private static NbtTag IntList(string name, params int[] values) => new()
    {
        Type = NbtTagType.List,
        Name = name,
        Value = values.Select(value => new NbtTag { Type = NbtTagType.Int, Value = (long)value }).ToList(),
    };

    private static void Save(Bitmap frame, string name)
    {
        var directory = Environment.GetEnvironmentVariable("FERRITE_UI_SHOTS");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        frame.Save(Path.Combine(directory, name + ".png"), new PngBitmapEncoderOptions());
    }
}
