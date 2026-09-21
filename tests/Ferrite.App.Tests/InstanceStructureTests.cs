using System.IO.Compression;
using Avalonia.Media.Imaging;
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
/// The structure tab: finding a structure the instance already has, previewing it, listing what it is
/// made of, and comparing its data version with the instance's own client.
/// </summary>
public sealed class InstanceStructureTests : IDisposable
{
    private readonly string _root;
    private readonly AppServices _services;

    public InstanceStructureTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ferrite-structure-ui-" + Guid.NewGuid().ToString("N"));
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

    private static NbtTag Int(string name, int value) =>
        new() { Type = NbtTagType.Int, Name = name, Value = (long)value };

    private static NbtTag IntList(string name, params int[] values) => new()
    {
        Type = NbtTagType.List,
        Name = name,
        Value = values.Select(value => new NbtTag { Type = NbtTagType.Int, Value = (long)value }).ToList(),
    };

    private static NbtTag Compound(params (string Key, NbtTag Tag)[] children)
    {
        var map = new Dictionary<string, NbtTag>(StringComparer.Ordinal);
        foreach (var (key, tag) in children)
        {
            map[key] = tag;
        }

        return new NbtTag { Type = NbtTagType.Compound, Name = string.Empty, Value = map };
    }

    /// <summary>A small solid structure: a floor and two walls, all of one block type.</summary>
    private static byte[] StructureBytes(int dataVersion, string blockName)
    {
        var palette = new NbtTag
        {
            Type = NbtTagType.List,
            Name = "palette",
            Value = new List<NbtTag>
            {
                Compound(("Name", new NbtTag { Type = NbtTagType.String, Name = "Name", Value = blockName })),
                Compound(("Name", new NbtTag { Type = NbtTagType.String, Name = "Name", Value = "minecraft:air" })),
            },
        };

        var blocks = new List<NbtTag>();
        for (var x = 0; x < 5; x++)
        {
            for (var z = 0; z < 5; z++)
            {
                blocks.Add(Compound(("pos", IntList("pos", x, 0, z)), ("state", Int("state", 0))));
            }
        }

        for (var y = 1; y < 4; y++)
        {
            for (var x = 0; x < 5; x++)
            {
                blocks.Add(Compound(("pos", IntList("pos", x, y, 0)), ("state", Int("state", 0))));
            }
        }

        return NbtWriter.Write(
            Compound(
                ("DataVersion", Int("DataVersion", dataVersion)),
                ("size", IntList("size", 5, 5, 5)),
                ("palette", palette),
                ("blocks", new NbtTag { Type = NbtTagType.List, Name = "blocks", Value = blocks })),
            compress: true);
    }

    /// <summary>An instance whose client jar declares a data version, plus one structure in a world.</summary>
    private async Task<(InstanceRecord Record, InstanceDetailViewModel ViewModel, string StructurePath)>
        CreateAsync(int structureDataVersion = 3955, bool writeClientJar = true)
    {
        var record = await _services.Instances.CreateAsync(
            new InstanceRecord
            {
                Id = Guid.NewGuid(),
                Name = "Builder",
                MinecraftVersion = "1.21.1",
                Loader = LoaderKind.Vanilla,
            },
            CancellationToken.None);

        var game = _services.Paths.InstanceGameDirectory(record.Id);
        Directory.CreateDirectory(game);

        if (writeClientJar)
        {
            var jarPath = _services.Paths.VersionClientJarFile(record.MinecraftVersion);
            Directory.CreateDirectory(Path.GetDirectoryName(jarPath)!);
            using var archive = ZipFile.Open(jarPath, ZipArchiveMode.Create);
            var entry = archive.CreateEntry("version.json");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("""{ "id": "1.21.1", "world_version": 3955 }""");
        }

        var structureDirectory = Path.Combine(
            game,
            "saves",
            "World",
            "generated",
            "minecraft",
            "structures");
        Directory.CreateDirectory(structureDirectory);
        var structurePath = Path.Combine(structureDirectory, "hut.nbt");
        await File.WriteAllBytesAsync(
            structurePath,
            StructureBytes(structureDataVersion, "minecraft:oak_planks"),
            CancellationToken.None);

        return (record, new InstanceDetailViewModel(record, _services, new MainWindowViewModel(_services)), structurePath);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task A_structure_in_a_world_is_found_and_previewed()
    {
        var (_, viewModel, structurePath) = await CreateAsync();

        await viewModel.RefreshStructuresAsync();

        Assert.True(viewModel.HasStructureFiles);
        Assert.Equal(structurePath, Assert.Single(viewModel.StructureFiles));
        Assert.Equal(structurePath, viewModel.SelectedStructureFile);

        Assert.True(await viewModel.LoadStructureAsync(structurePath));

        Assert.True(viewModel.HasStructureImage);
        Assert.NotNull(viewModel.StructureImage);
        Assert.True(viewModel.StructureImage!.PixelSize.Width > 0);
        Assert.Contains("5 x 5 x 5", viewModel.StructureSummary!, StringComparison.Ordinal);
        Assert.Contains("oak_planks", viewModel.StructureMaterials!, StringComparison.Ordinal);
        // The structure and the client both declare data version 3955.
        Assert.Equal(StructureCompatibility.SameVersion, viewModel.StructureCompatibility);
        Assert.Equal(
            Localizer.Get("L.Instance.StructureSameVersion"),
            viewModel.StructureCompatibilityText);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task A_structure_from_a_newer_game_is_called_out()
    {
        var (_, viewModel, structurePath) = await CreateAsync(structureDataVersion: 4000);
        await viewModel.RefreshStructuresAsync();

        await viewModel.LoadStructureAsync(structurePath);

        Assert.Equal(StructureCompatibility.StructureIsNewer, viewModel.StructureCompatibility);
        Assert.Equal(Localizer.Get("L.Instance.StructureNewer"), viewModel.StructureCompatibilityText);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task Without_a_client_file_compatibility_is_reported_as_unknown()
    {
        var (_, viewModel, structurePath) = await CreateAsync(writeClientJar: false);
        await viewModel.RefreshStructuresAsync();

        await viewModel.LoadStructureAsync(structurePath);

        Assert.Equal(StructureCompatibility.NoGameVersion, viewModel.StructureCompatibility);
        Assert.Equal(Localizer.Get("L.Instance.StructureNoGame"), viewModel.StructureCompatibilityText);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task A_file_that_is_not_a_structure_is_reported_rather_than_throwing()
    {
        var (_, viewModel, _) = await CreateAsync();
        var text = Path.Combine(_root, "notes.nbt");
        await File.WriteAllTextAsync(text, "this is not an NBT file", CancellationToken.None);

        Assert.False(await viewModel.LoadStructureAsync(text));
        Assert.True(viewModel.HasStructureStatus);
        Assert.False(viewModel.HasStructureImage);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task An_empty_instance_says_it_has_no_structures()
    {
        var record = await _services.Instances.CreateAsync(
            new InstanceRecord
            {
                Id = Guid.NewGuid(),
                Name = "Empty",
                MinecraftVersion = "1.21.1",
            },
            CancellationToken.None);
        Directory.CreateDirectory(_services.Paths.InstanceGameDirectory(record.Id));
        var viewModel = new InstanceDetailViewModel(record, _services, new MainWindowViewModel(_services));

        await viewModel.RefreshStructuresAsync();

        Assert.False(viewModel.HasStructureFiles);
        Assert.Equal(Localizer.Get("L.Instance.StructureNoneFound"), viewModel.StructureStatus);
    }

    /// <summary>
    /// Saves the rendered preview so a person can look at it. The render is checked numerically above;
    /// this exists because "it drew something" is not the same claim as "it looks like a structure".
    /// </summary>
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task The_preview_can_be_saved_for_inspection()
    {
        var (_, viewModel, structurePath) = await CreateAsync();
        await viewModel.RefreshStructuresAsync();
        await viewModel.LoadStructureAsync(structurePath);
        Assert.NotNull(viewModel.StructureImage);

        var directory = Environment.GetEnvironmentVariable("FERRITE_UI_SHOTS");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        viewModel.StructureImage!.Save(
            Path.Combine(directory, "structure-preview.png"),
            PngBitmapEncoderOptions.Default);
    }
}
