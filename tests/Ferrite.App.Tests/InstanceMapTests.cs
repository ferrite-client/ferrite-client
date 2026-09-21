using Ferrite.App.Localization;
using Ferrite.App.Services;
using Ferrite.App.ViewModels;
using Ferrite.Core.Game;
using Ferrite.Core.Minecraft;
using Ferrite.Core.Platform;
using Ferrite.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Ferrite.App.Tests;

/// <summary>
/// The map tab: rendering a world's chunks, selecting them by pixel, and deleting the selection. The
/// region file the test writes is a real Anvil file, so the reader and the writer are both exercised.
/// </summary>
public sealed class InstanceMapTests : IDisposable
{
    private readonly string _root;
    private readonly AppServices _services;

    public InstanceMapTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ferrite-map-ui-" + Guid.NewGuid().ToString("N"));
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

    /// <summary>A world folder holding one region with two chunks at known heights.</summary>
    private string WriteWorld(string name, bool withChunks)
    {
        var world = Path.Combine(_root, "saves", name);
        var regionDirectory = Path.Combine(world, "region");
        Directory.CreateDirectory(regionDirectory);
        if (!withChunks)
        {
            return world;
        }

        new RegionFile(Path.Combine(regionDirectory, "r.0.0.mca")).Rewrite(
        [
            Chunk(0, 0, 40),
            Chunk(1, 0, 200),
        ]);
        return world;
    }

    private static RegionChunk Chunk(int x, int z, int height)
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

        return new RegionChunk(x, z, 2, RegionFile.Compress(NbtWriter.Write(root, compress: false)), 0);
    }

    private async Task<(InstanceRecord Record, InstanceDetailViewModel ViewModel)> CreateAsync(
        params (string Name, bool WithChunks)[] worlds)
    {
        var record = await _services.Instances.CreateAsync(
            new InstanceRecord
            {
                Id = Guid.NewGuid(),
                Name = "Mapper",
                MinecraftVersion = "1.21.1",
                Loader = LoaderKind.Vanilla,
            },
            CancellationToken.None);
        var game = _services.Paths.InstanceGameDirectory(record.Id);
        Directory.CreateDirectory(game);

        var viewModel = new InstanceDetailViewModel(record, _services, new MainWindowViewModel(_services));
        foreach (var (name, withChunks) in worlds)
        {
            var directory = WriteWorld(name, withChunks);
            viewModel.Worlds.Add(new WorldItemViewModel(
                new WorldInfo
                {
                    DirectoryPath = directory,
                    FolderName = name,
                    Name = name,
                },
                _ => Task.CompletedTask,
                _ => Task.CompletedTask,
                _ => Task.CompletedTask,
                _ => Task.CompletedTask));
        }

        return (record, viewModel);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task The_map_renders_the_worlds_chunks()
    {
        var (_, viewModel) = await CreateAsync(("Alpha", true), ("Beta", false));

        Assert.False(viewModel.HasWorldMap);
        await viewModel.RefreshMapCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasWorldMap);
        Assert.NotNull(viewModel.WorldMapImage);
        Assert.Equal(32, viewModel.WorldMapImage!.PixelSize.Width);
        Assert.Equal(16, viewModel.WorldMapImage.PixelSize.Height);
        Assert.Contains("2 of", viewModel.MapStatus!, StringComparison.Ordinal);
        // The pickers default to the first world and a different copy target.
        Assert.Equal("Alpha", viewModel.MapWorld!.Name);
        Assert.Equal("Beta", viewModel.MapCopyTarget!.Name);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task Clicking_the_map_selects_the_chunk_under_the_pointer()
    {
        var (_, viewModel) = await CreateAsync(("Alpha", true), ("Beta", false));
        await viewModel.RefreshMapCommand.ExecuteAsync(null);
        Assert.False(viewModel.HasChunkSelection);

        // One pixel per block, so pixel 0 is chunk 0 and pixel 16 is chunk 1.
        viewModel.ToggleChunkAt(0, 0);
        Assert.True(viewModel.HasChunkSelection);
        Assert.Equal("1 chunk(s) selected", viewModel.MapSelectionText);

        viewModel.ToggleChunkAt(16, 0);
        Assert.Equal("2 chunk(s) selected", viewModel.MapSelectionText);

        // Clicking the same chunk again deselects it.
        viewModel.ToggleChunkAt(16, 0);
        Assert.Equal("1 chunk(s) selected", viewModel.MapSelectionText);

        // A click outside the map changes nothing.
        viewModel.ToggleChunkAt(9999, 9999);
        Assert.Equal("1 chunk(s) selected", viewModel.MapSelectionText);

        viewModel.ClearChunkSelectionCommand.Execute(null);
        Assert.False(viewModel.HasChunkSelection);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task Deleting_the_selection_removes_those_chunks_and_backs_the_region_up()
    {
        var (_, viewModel) = await CreateAsync(("Alpha", true));
        await viewModel.RefreshMapCommand.ExecuteAsync(null);
        viewModel.ToggleChunkAt(0, 0);

        await viewModel.DeleteSelectedChunksCommand.ExecuteAsync(null);

        var region = Path.Combine(viewModel.MapWorld!.World.DirectoryPath, "region", "r.0.0.mca");
        var remaining = new RegionFile(region).ReadAll();
        Assert.Single(remaining);
        Assert.Equal(1, remaining[0].X);
        Assert.Contains("Deleted 1 chunk", viewModel.MapStatus!, StringComparison.Ordinal);
        // A backup of the region file exists, so the deleted chunk is recoverable.
        var backups = Directory.EnumerateFiles(
            _services.Paths.BackupsDirectory,
            "r.0.0.mca",
            SearchOption.AllDirectories);
        Assert.NotEmpty(backups);
        Assert.Equal(2, new RegionFile(backups.First()).ReadAll().Count);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task Deleting_with_nothing_selected_says_so()
    {
        var (_, viewModel) = await CreateAsync(("Alpha", true));
        await viewModel.RefreshMapCommand.ExecuteAsync(null);

        await viewModel.DeleteSelectedChunksCommand.ExecuteAsync(null);

        Assert.Equal(Localizer.Get("L.Instance.MapNoSelection"), viewModel.MapStatus);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task Copying_the_selection_writes_the_chunks_into_the_other_world()
    {
        var (_, viewModel) = await CreateAsync(("Alpha", true), ("Beta", false));
        await viewModel.RefreshMapCommand.ExecuteAsync(null);
        viewModel.ToggleChunkAt(0, 0);

        await viewModel.CopySelectedChunksCommand.ExecuteAsync(null);

        Assert.Contains("Copied 1 chunk", viewModel.MapStatus!, StringComparison.Ordinal);
        var target = Path.Combine(viewModel.MapCopyTarget!.World.DirectoryPath, "region", "r.0.0.mca");
        Assert.True(File.Exists(target));
        var copied = Assert.Single(new RegionFile(target).ReadAll());
        Assert.Equal(0, copied.X);
        Assert.Equal(0, copied.Z);
    }
}

