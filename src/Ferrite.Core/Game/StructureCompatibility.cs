using System.IO.Compression;
using System.Text.Json;
using Ferrite.Core.Json;

namespace Ferrite.Core.Game;

/// <summary>How a structure's data version compares with a game version's.</summary>
public enum StructureCompatibility
{
    Unknown,
    SameVersion,
    StructureIsOlder,
    StructureIsNewer,
    NoStructureVersion,
    NoGameVersion,
}

/// <summary>
/// Compares a structure's <c>DataVersion</c> with the data version of the game that would load it.
/// The game's own number comes from the <c>version.json</c> inside the client jar, which is the only
/// place that mapping exists - so this compares the two real numbers rather than a table that would
/// drift with every release.
/// </summary>
public static class StructureCompatibilityCheck
{
    private const int MaxVersionJsonBytes = 256 * 1024;

    /// <summary>The data version a client jar declares, or null when it cannot be read.</summary>
    public static int? ReadClientDataVersion(string clientJarPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientJarPath);
        if (!File.Exists(clientJarPath))
        {
            return null;
        }

        try
        {
            using var archive = ZipFile.OpenRead(clientJarPath);
            var entry = archive.GetEntry("version.json");
            if (entry is null || entry.Length > MaxVersionJsonBytes)
            {
                return null;
            }

            using var stream = entry.Open();
            using var document = JsonDocument.Parse(stream);
            return document.RootElement.TryGetProperty("world_version", out var version)
                   && version.TryGetInt32(out var value)
                ? value
                : null;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether a structure will load in a game. A structure saved by a newer game can contain blocks
    /// and states an older one does not know, which is why a newer structure is called out separately
    /// from an older one.
    /// </summary>
    public static StructureCompatibility Evaluate(int? structureDataVersion, int? gameDataVersion)
    {
        if (structureDataVersion is null or <= 0)
        {
            return StructureCompatibility.NoStructureVersion;
        }

        if (gameDataVersion is null or <= 0)
        {
            return StructureCompatibility.NoGameVersion;
        }

        return structureDataVersion == gameDataVersion
            ? StructureCompatibility.SameVersion
            : structureDataVersion < gameDataVersion
                ? StructureCompatibility.StructureIsOlder
                : StructureCompatibility.StructureIsNewer;
    }
}
