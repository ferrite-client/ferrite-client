using Ferrite.Core.Minecraft;
using Ferrite.Core.Platform;
using Ferrite.Core.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ferrite.Core.Tests;

public sealed class InstanceManagerTests : IDisposable
{
    private readonly string _root;
    private readonly AppPaths _paths;
    private readonly InstanceStore _store;
    private readonly InstanceManager _manager;

    public InstanceManagerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ferrite-instances-" + Guid.NewGuid().ToString("N"));
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
    public async Task Clone_copies_metadata_and_content_into_an_independent_instance()
    {
        var source = await CreateInstanceAsync("Original");
        var sourceGame = _paths.InstanceGameDirectory(source.Id);
        await File.WriteAllTextAsync(Path.Combine(sourceGame, "mods", "example.jar"), "mod bytes");
        await File.WriteAllTextAsync(Path.Combine(sourceGame, "options.txt"), "fov:70");

        var clone = await _manager.CloneAsync(source.Id, null, TestContext.Current.CancellationToken);

        Assert.NotEqual(source.Id, clone.Id);
        Assert.Equal("Original (copy)", clone.Name);
        Assert.Equal(source.MinecraftVersion, clone.MinecraftVersion);
        Assert.Equal(source.LoaderVersion, clone.LoaderVersion);

        var cloneGame = _paths.InstanceGameDirectory(clone.Id);
        Assert.Equal("mod bytes", await File.ReadAllTextAsync(Path.Combine(cloneGame, "mods", "example.jar")));

        // The clone must be independent: changing one must not affect the other.
        await File.WriteAllTextAsync(Path.Combine(cloneGame, "options.txt"), "fov:100");
        Assert.Equal("fov:70", await File.ReadAllTextAsync(Path.Combine(sourceGame, "options.txt")));
    }

    [Fact]
    public async Task Clone_accepts_an_explicit_name()
    {
        var source = await CreateInstanceAsync("Original");
        var clone = await _manager.CloneAsync(source.Id, "  Renamed clone  ", TestContext.Current.CancellationToken);
        Assert.Equal("Renamed clone", clone.Name);
    }

    [Fact]
    public async Task Rename_changes_only_the_display_name()
    {
        var instance = await CreateInstanceAsync("Before");
        var gameDirectory = _paths.InstanceGameDirectory(instance.Id);
        await File.WriteAllTextAsync(Path.Combine(gameDirectory, "options.txt"), "fov:80");

        var renamed = await _manager.RenameAsync(instance.Id, "After", TestContext.Current.CancellationToken);

        Assert.Equal("After", renamed.Name);
        Assert.Equal(instance.Id, renamed.Id);
        Assert.True(Directory.Exists(gameDirectory));
        Assert.Equal("fov:80", await File.ReadAllTextAsync(Path.Combine(gameDirectory, "options.txt")));
    }

    [Fact]
    public async Task Rename_rejects_an_empty_name()
    {
        var instance = await CreateInstanceAsync("Keeps its name");
        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            _manager.RenameAsync(instance.Id, "   ", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Archive_zips_the_whole_instance_without_modifying_it()
    {
        var instance = await CreateInstanceAsync("Archived");
        var gameDirectory = _paths.InstanceGameDirectory(instance.Id);
        await File.WriteAllTextAsync(Path.Combine(gameDirectory, "options.txt"), "fov:90");
        await File.WriteAllTextAsync(Path.Combine(gameDirectory, "logs", "latest.log"), "log line");

        var outputPath = Path.Combine(_root, "out", "instance.zip");
        var archivePath = await _manager.ArchiveAsync(
            instance.Id,
            outputPath,
            TestContext.Current.CancellationToken);

        Assert.True(File.Exists(archivePath));
        var entries = Ferrite.Core.Util.ArchiveExtractor.ListEntries(archivePath);
        Assert.Contains(entries, name => name.EndsWith("instance.json", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(entries, name => name.EndsWith("latest.log", StringComparison.OrdinalIgnoreCase));

        Assert.True(Directory.Exists(gameDirectory));
        Assert.True(File.Exists(Path.Combine(gameDirectory, "options.txt")));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(archivePath)!, "*.tmp-*"));
    }

    [Fact]
    public async Task Archiving_an_unknown_instance_is_reported()
    {
        await Assert.ThrowsAsync<InstanceNotFoundException>(() =>
            _manager.ArchiveAsync(
                Guid.NewGuid(),
                Path.Combine(_root, "missing.zip"),
                TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Deleting an instance must not destroy it: the whole directory moves into the launcher's
    /// backups folder, with the user's files inside it.
    /// </summary>
    [Fact]
    public async Task Deleting_an_instance_keeps_it_in_backups()
    {
        var record = await CreateInstanceAsync("Precious");
        var gameDirectory = _paths.InstanceGameDirectory(record.Id);
        await File.WriteAllTextAsync(Path.Combine(gameDirectory, "options.txt"), "fov:95");
        var worldDirectory = Path.Combine(gameDirectory, "saves", "world");
        Directory.CreateDirectory(worldDirectory);
        await File.WriteAllTextAsync(Path.Combine(worldDirectory, "level.dat"), "nbt");

        await _store.DeleteAsync(record.Id, moveToBackups: true, TestContext.Current.CancellationToken);

        Assert.False(Directory.Exists(_paths.InstanceDirectory(record.Id)));
        var moved = Directory.EnumerateDirectories(_paths.BackupsDirectory)
            .Where(directory => Path.GetFileName(directory).StartsWith("instance-Precious-", StringComparison.Ordinal))
            .ToList();
        var backup = Assert.Single(moved);
        Assert.Equal(
            "fov:95",
            await File.ReadAllTextAsync(Path.Combine(backup, "minecraft", "options.txt")));

        // It is gone from the library as well, so the list and the disk agree.
        Assert.DoesNotContain(
            await _store.LoadAllAsync(TestContext.Current.CancellationToken),
            candidate => candidate.Id == record.Id);
    }

    private async Task<InstanceRecord> CreateInstanceAsync(string name) =>
        await _store.CreateAsync(
            new InstanceRecord
            {
                Id = Guid.NewGuid(),
                Name = name,
                MinecraftVersion = "1.21.1",
                Loader = LoaderKind.Fabric,
                LoaderVersion = "0.19.5",
            },
            TestContext.Current.CancellationToken);
}
