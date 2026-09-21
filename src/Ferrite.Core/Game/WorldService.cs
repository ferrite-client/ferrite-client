using Ferrite.Core.Content;
using Microsoft.Extensions.Logging;

namespace Ferrite.Core.Game;

/// <summary>
/// Reads saved worlds from an instance. World mutation (backup, restore, delete) lives in
/// <see cref="WorldArchive"/> so the read path stays trivial to test.
/// </summary>
public sealed class WorldService
{
    private static readonly string[] GameModeNames = ["survival", "creative", "adventure", "spectator"];

    private readonly ILogger<WorldService> _logger;

    public WorldService(ILogger<WorldService> logger)
    {
        _logger = logger;
    }

    public static string SavesDirectory(string gameDirectory) => Path.Combine(gameDirectory, "saves");

    /// <summary>Lists worlds, skipping any folder whose level data cannot be read.</summary>
    public IReadOnlyList<WorldInfo> ListWorlds(string gameDirectory, CancellationToken cancellationToken)
    {
        var saves = SavesDirectory(gameDirectory);
        if (!Directory.Exists(saves))
        {
            return [];
        }

        var worlds = new List<WorldInfo>();
        foreach (var directory in Directory.EnumerateDirectories(saves))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = TryRead(directory, cancellationToken);
            if (info is not null)
            {
                worlds.Add(info);
            }
        }

        return worlds
            .OrderByDescending(world => world.LastPlayed ?? DateTimeOffset.MinValue)
            .ToList();
    }

    public WorldInfo? TryRead(string worldDirectory, CancellationToken cancellationToken)
    {
        var levelFile = Path.Combine(worldDirectory, "level.dat");
        if (!File.Exists(levelFile))
        {
            return null;
        }

        try
        {
            var root = NbtReader.ReadFile(levelFile);
            var data = root["Data"] ?? root;
            var folderName = Path.GetFileName(worldDirectory);
            var iconPath = Path.Combine(worldDirectory, "icon.png");
            var gameType = data["GameType"]?.AsInt();
            var version = data["Version"];

            return new WorldInfo
            {
                DirectoryPath = worldDirectory,
                FolderName = folderName,
                Name = data["LevelName"]?.AsString() ?? folderName,
                SizeBytes = InstanceContentManager.GetDirectorySize(worldDirectory),
                LastPlayed = data["LastPlayed"]?.AsLong() is { } played
                    ? DateTimeOffset.FromUnixTimeMilliseconds(played)
                    : null,
                VersionName = version?["Name"]?.AsString(),
                VersionId = version?["Id"]?.AsInt(),
                GameMode = gameType is { } type && type >= 0 && type < GameModeNames.Length
                    ? GameModeNames[type]
                    : "unknown",
                Hardcore = data["hardcore"]?.AsBool() ?? false,
                CheatsEnabled = data["allowCommands"]?.AsBool() ?? false,
                // The seed lives at Data.RandomSeed up to 1.15 and under Data.WorldGenSettings after.
                Seed = data["RandomSeed"]?.AsLong()
                    ?? data.Path("WorldGenSettings", "seed")?.AsLong(),
                IconPath = File.Exists(iconPath) ? iconPath : null,
            };
        }
        catch (Exception exception) when (exception is NbtException or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "World data at {Path} could not be read", worldDirectory);
            return null;
        }
    }
}
