using Ferrite.Core.Game;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ferrite.Core.Tests;

/// <summary>
/// World and server tests use documents produced by the launcher's own NBT writer, which is the same
/// shape the game writes, so the read path is exercised without a game install.
/// </summary>
public sealed class WorldAndServerTests : IDisposable
{
    private readonly string _workspace;
    private readonly string _gameDirectory;

    public WorldAndServerTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), "ferrite-worlds-" + Guid.NewGuid().ToString("N"));
        _gameDirectory = Path.Combine(_workspace, "minecraft");
        Directory.CreateDirectory(_gameDirectory);
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

    [Fact]
    public void Lists_a_world_with_its_level_metadata()
    {
        var worldDirectory = CreateWorld("Survival World", "1.21.1", 767, gameType: 0, hardcore: true, seed: 12345);

        var service = new WorldService(NullLogger<WorldService>.Instance);
        var worlds = service.ListWorlds(_gameDirectory, TestContext.Current.CancellationToken);

        var world = Assert.Single(worlds);
        Assert.Equal("Survival World", world.Name);
        Assert.Equal("survival", world.GameMode);
        Assert.True(world.Hardcore);
        Assert.Equal(12345L, world.Seed);
        Assert.Equal("1.21.1", world.VersionName);
        Assert.Equal(767, world.VersionId);
        Assert.NotNull(world.LastPlayed);
        Assert.True(world.SizeBytes > 0);
        Assert.Equal(Path.GetFileName(worldDirectory), world.FolderName);
    }

    [Fact]
    public void Skips_worlds_without_readable_level_data()
    {
        Directory.CreateDirectory(Path.Combine(_gameDirectory, "saves", "broken"));
        File.WriteAllText(Path.Combine(_gameDirectory, "saves", "broken", "level.dat"), "not nbt");
        CreateWorld("Good World", "1.21.1", 767, gameType: 1, hardcore: false, seed: 1);

        var service = new WorldService(NullLogger<WorldService>.Instance);
        var worlds = service.ListWorlds(_gameDirectory, TestContext.Current.CancellationToken);

        var world = Assert.Single(worlds);
        Assert.Equal("Good World", world.Name);
        Assert.Equal("creative", world.GameMode);
    }

    [Fact]
    public async Task Backs_up_and_restores_a_world()
    {
        var worldDirectory = CreateWorld("Backup Me", "1.21.1", 767, gameType: 0, hardcore: false, seed: 7);
        var archive = new WorldArchive(Path.Combine(_workspace, "backups"), NullLogger<WorldArchive>.Instance);

        var backup = await archive.BackupAsync(worldDirectory, TestContext.Current.CancellationToken);
        Assert.True(File.Exists(backup));

        Directory.Delete(worldDirectory, recursive: true);
        var restored = await archive.RestoreAsync(
            backup,
            _gameDirectory,
            "Backup Me",
            TestContext.Current.CancellationToken);

        var info = new WorldService(NullLogger<WorldService>.Instance)
            .TryRead(restored, TestContext.Current.CancellationToken);
        Assert.NotNull(info);
        Assert.Equal("Backup Me", info!.Name);
    }

    [Fact]
    public async Task Restore_moves_an_existing_world_aside_instead_of_overwriting()
    {
        var worldDirectory = CreateWorld("Contested", "1.21.1", 767, gameType: 0, hardcore: false, seed: 3);
        var backups = Path.Combine(_workspace, "backups");
        var archive = new WorldArchive(backups, NullLogger<WorldArchive>.Instance);
        var backup = await archive.BackupAsync(worldDirectory, TestContext.Current.CancellationToken);

        await archive.RestoreAsync(backup, _gameDirectory, "Contested", TestContext.Current.CancellationToken);

        Assert.True(Directory.Exists(worldDirectory));
        Assert.NotEmpty(Directory.EnumerateDirectories(backups, "replaced-*"));
    }

    [Fact]
    public void Deletes_a_world_by_moving_it_into_backups()
    {
        var worldDirectory = CreateWorld("Delete Me", "1.21.1", 767, gameType: 0, hardcore: false, seed: 4);
        var archive = new WorldArchive(Path.Combine(_workspace, "backups"), NullLogger<WorldArchive>.Instance);

        var moved = archive.Delete(worldDirectory);

        Assert.False(Directory.Exists(worldDirectory));
        Assert.True(Directory.Exists(moved));
        Assert.True(File.Exists(Path.Combine(moved, "level.dat")));
    }

    [Fact]
    public void Round_trips_the_server_list()
    {
        var service = new ServerListService(NullLogger<ServerListService>.Instance);
        service.Write(
            _gameDirectory,
            [
                new ServerEntry { Name = "Alpha", Address = "alpha.example:25565", AcceptTextures = true },
                new ServerEntry { Name = "Beta", Address = "beta.example" },
            ]);

        var servers = service.Read(_gameDirectory);
        Assert.Equal(2, servers.Count);
        Assert.Equal("Alpha", servers[0].Name);
        Assert.True(servers[0].AcceptTextures);
        Assert.False(servers[1].AcceptTextures);
    }

    [Fact]
    public void Adds_and_removes_servers_without_duplicating_addresses()
    {
        var service = new ServerListService(NullLogger<ServerListService>.Instance);
        service.Add(_gameDirectory, new ServerEntry { Name = "One", Address = "play.example:25565" });
        service.Add(_gameDirectory, new ServerEntry { Name = "Renamed", Address = "play.example:25565" });

        var only = Assert.Single(service.Read(_gameDirectory));
        Assert.Equal("Renamed", only.Name);

        service.Remove(_gameDirectory, "play.example:25565");
        Assert.Empty(service.Read(_gameDirectory));
    }

    [Fact]
    public void An_unreadable_server_list_is_treated_as_empty()
    {
        File.WriteAllText(ServerListService.ServerListPath(_gameDirectory), "this is not nbt");

        var service = new ServerListService(NullLogger<ServerListService>.Instance);
        Assert.Empty(service.Read(_gameDirectory));
    }

    private string CreateWorld(
        string name,
        string versionName,
        int versionId,
        int gameType,
        bool hardcore,
        long seed)
    {
        var worldDirectory = Path.Combine(_gameDirectory, "saves", name);
        Directory.CreateDirectory(worldDirectory);

        var data = new NbtTag
        {
            Type = NbtTagType.Compound,
            Name = "Data",
            Value = new Dictionary<string, NbtTag>(StringComparer.Ordinal)
            {
                ["LevelName"] = new NbtTag { Type = NbtTagType.String, Name = "LevelName", Value = name },
                ["LastPlayed"] = new NbtTag
                {
                    Type = NbtTagType.Long,
                    Name = "LastPlayed",
                    Value = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                },
                ["GameType"] = new NbtTag { Type = NbtTagType.Int, Name = "GameType", Value = (long)gameType },
                ["hardcore"] = new NbtTag { Type = NbtTagType.Byte, Name = "hardcore", Value = hardcore ? 1L : 0L },
                ["allowCommands"] = new NbtTag { Type = NbtTagType.Byte, Name = "allowCommands", Value = 0L },
                ["RandomSeed"] = new NbtTag { Type = NbtTagType.Long, Name = "RandomSeed", Value = seed },
                ["Version"] = new NbtTag
                {
                    Type = NbtTagType.Compound,
                    Name = "Version",
                    Value = new Dictionary<string, NbtTag>(StringComparer.Ordinal)
                    {
                        ["Name"] = new NbtTag { Type = NbtTagType.String, Name = "Name", Value = versionName },
                        ["Id"] = new NbtTag { Type = NbtTagType.Int, Name = "Id", Value = (long)versionId },
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
        Directory.CreateDirectory(Path.Combine(worldDirectory, "region"));
        File.WriteAllBytes(Path.Combine(worldDirectory, "region", "r.0.0.mca"), new byte[4096]);
        return worldDirectory;
    }
}
