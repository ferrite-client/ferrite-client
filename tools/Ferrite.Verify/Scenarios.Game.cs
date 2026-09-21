using Ferrite.Core.Game;
using Ferrite.Core.Util;

namespace Ferrite.Verify;

/// <summary>World listing and server status scenarios.</summary>
internal static partial class Scenarios
{
    /// <summary>
    /// Lists worlds from an instance, or from an existing Minecraft directory when one is given.
    /// The external directory is only read.
    /// </summary>
    public static async Task<int> ListWorldsAsync(
        VerifyServices services,
        string minecraftVersion,
        string? externalGameDirectory,
        CancellationToken cancellationToken)
    {
        string gameDirectory;
        if (!string.IsNullOrWhiteSpace(externalGameDirectory))
        {
            gameDirectory = externalGameDirectory;
            Console.WriteLine($"Reading worlds from {gameDirectory} (read-only)");
        }
        else
        {
            var instance = await GetOrCreateInstanceAsync(services, minecraftVersion, cancellationToken)
                .ConfigureAwait(false);
            gameDirectory = services.Paths.InstanceGameDirectory(instance.Id);
        }

        var worlds = await Task.Run(
                () => services.Worlds.ListWorlds(gameDirectory, cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);

        Console.WriteLine($"Found {worlds.Count} world(s)");
        foreach (var world in worlds)
        {
            Console.WriteLine(
                $"  {world.Name} [{world.FolderName}] {world.GameMode}"
                + $"{(world.Hardcore ? " hardcore" : string.Empty)}"
                + $" v{world.VersionName ?? "?"} seed={world.Seed?.ToString() ?? "?"}"
                + $" {world.SizeText} last={world.LastPlayedText}");
            Console.WriteLine($"      icon: {(world.HasIcon ? world.IconPath : "none")}");
        }

        return 0;
    }

    /// <summary>Backs up a world, restores it, and reports both paths.</summary>
    public static async Task<int> BackupWorldAsync(
        VerifyServices services,
        string minecraftVersion,
        string? externalGameDirectory,
        CancellationToken cancellationToken)
    {
        var gameDirectory = externalGameDirectory;
        if (string.IsNullOrWhiteSpace(gameDirectory))
        {
            var instance = await GetOrCreateInstanceAsync(services, minecraftVersion, cancellationToken)
                .ConfigureAwait(false);
            gameDirectory = services.Paths.InstanceGameDirectory(instance.Id);
        }

        var worlds = services.Worlds.ListWorlds(gameDirectory, cancellationToken);
        var world = worlds.FirstOrDefault();
        if (world is null)
        {
            Console.WriteLine($"No worlds found in {gameDirectory}");
            return 2;
        }

        var backup = await services.WorldsArchive
            .BackupAsync(world.DirectoryPath, cancellationToken)
            .ConfigureAwait(false);
        Console.WriteLine($"Backed up {world.Name} to {backup}");
        Console.WriteLine($"  size: {ByteSize.Format(new FileInfo(backup).Length)}");

        var verify = await Task.Run(
                () => ArchiveExtractor.ListEntries(backup),
                cancellationToken)
            .ConfigureAwait(false);
        Console.WriteLine($"  entries: {verify.Count}");
        Console.WriteLine($"  contains level.dat: {verify.Any(name => name.EndsWith("level.dat", StringComparison.OrdinalIgnoreCase))}");

        return 0;
    }

    /// <summary>Pings one or more servers and prints their status.</summary>
    public static async Task<int> PingAsync(
        VerifyServices services,
        IReadOnlyList<string> addresses,
        CancellationToken cancellationToken)
    {
        if (addresses.Count == 0)
        {
            Console.WriteLine("No addresses to ping.");
            return 2;
        }

        var failures = 0;
        foreach (var address in addresses)
        {
            var status = await services.Pinger
                .PingAsync(address, null, TimeSpan.FromSeconds(10), cancellationToken)
                .ConfigureAwait(false);

            if (!status.Online)
            {
                failures++;
                Console.WriteLine($"  {status.Address}: offline ({status.Error})");
                continue;
            }

            Console.WriteLine(
                $"  {status.Address}: {status.VersionName} {status.PlayerCountText} {status.LatencyText}");
            Console.WriteLine($"      {status.Motd}");
        }

        return failures == addresses.Count ? 3 : 0;
    }
}
