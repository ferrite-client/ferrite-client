using Ferrite.Core.Minecraft;
using Ferrite.Core.Platform;
using Ferrite.Core.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ferrite.Core.Tests;

/// <summary>
/// Instance folders. They are pure metadata, so the tests assert both that the assignment survives a
/// reload and that nothing about the game directory changes.
/// </summary>
public sealed class InstanceGroupTests : IDisposable
{
    private readonly string _root;
    private readonly AppPaths _paths;
    private readonly InstanceStore _store;
    private readonly InstanceManager _manager;

    public InstanceGroupTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ferrite-groups-" + Guid.NewGuid().ToString("N"));
        _paths = AppPaths.ForRoot(_root);
        _paths.EnsureCreated();
        _store = new InstanceStore(_paths, NullLogger<InstanceStore>.Instance);
        _manager = new InstanceManager(_store, _paths, NullLogger<InstanceManager>.Instance);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task Assigning_a_folder_survives_a_reload()
    {
        var instance = await CreateAsync("Grouped");
        Assert.Null(instance.Group);

        await _manager.SetGroupAsync(instance.Id, "  Survival  ", TestContext.Current.CancellationToken);

        var reloaded = await _store.LoadAsync(instance.Id, TestContext.Current.CancellationToken);
        Assert.Equal("Survival", reloaded.Group);
    }

    [Fact]
    public async Task A_blank_folder_clears_the_assignment()
    {
        var instance = await CreateAsync("Cleared");
        await _manager.SetGroupAsync(instance.Id, "Modded", TestContext.Current.CancellationToken);

        await _manager.SetGroupAsync(instance.Id, "   ", TestContext.Current.CancellationToken);

        var reloaded = await _store.LoadAsync(instance.Id, TestContext.Current.CancellationToken);
        Assert.Null(reloaded.Group);
    }

    [Fact]
    public void NormalizeGroup_trims_and_treats_blank_as_absent()
    {
        Assert.Equal("Tech", InstanceManager.NormalizeGroup("  Tech "));
        Assert.Null(InstanceManager.NormalizeGroup(null));
        Assert.Null(InstanceManager.NormalizeGroup(""));
        Assert.Null(InstanceManager.NormalizeGroup("   "));
    }

    [Fact]
    public async Task Filing_an_instance_does_not_touch_its_game_directory()
    {
        var instance = await CreateAsync("Untouched");
        var gameDirectory = _paths.InstanceGameDirectory(instance.Id);
        var marker = Path.Combine(gameDirectory, "options.txt");
        await File.WriteAllTextAsync(marker, "fov:70", TestContext.Current.CancellationToken);

        await _manager.SetGroupAsync(instance.Id, "Kept", TestContext.Current.CancellationToken);

        Assert.Equal("fov:70", await File.ReadAllTextAsync(marker, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_clone_inherits_the_folder()
    {
        var instance = await CreateAsync("Source");
        await _manager.SetGroupAsync(instance.Id, "Family", TestContext.Current.CancellationToken);

        var clone = await _manager.CloneAsync(instance.Id, null, TestContext.Current.CancellationToken);

        Assert.Equal("Family", clone.Group);
    }

    private async Task<InstanceRecord> CreateAsync(string name) =>
        await _store.CreateAsync(
            new InstanceRecord
            {
                Id = Guid.NewGuid(),
                Name = name,
                MinecraftVersion = "1.21.1",
                Loader = LoaderKind.Vanilla,
            },
            TestContext.Current.CancellationToken);
}
