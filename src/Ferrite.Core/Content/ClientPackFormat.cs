using System.IO.Compression;
using System.Text.Json;
using Ferrite.Core.Platform;
using Microsoft.Extensions.Logging;

namespace Ferrite.Core.Content;

/// <summary>
/// The resource and data pack formats a specific Minecraft version uses.
/// </summary>
/// <remarks>
/// The mapping from game version to pack format is not published as a table Ferrite could embed
/// without it drifting: it lives in the client jar's own <c>version.json</c>, under
/// <c>pack_version</c>. Reading it from the installed client file is exact and needs no maintenance,
/// and when the client file is absent the answer is "unknown" rather than a guess.
/// </remarks>
public sealed class ClientPackFormat
{
    private const int MaxVersionJsonBytes = 256 * 1024;

    private readonly AppPaths _paths;
    private readonly ILogger<ClientPackFormat> _logger;

    public ClientPackFormat(AppPaths paths, ILogger<ClientPackFormat> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    /// <summary>Reads the formats for a version, or null when the client jar is not installed.</summary>
    public PackFormats? Read(string minecraftVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(minecraftVersion);
        var jarPath = _paths.VersionClientJarFile(minecraftVersion);
        if (!File.Exists(jarPath))
        {
            return null;
        }

        try
        {
            using var archive = ZipFile.OpenRead(jarPath);
            var entry = archive.Entries.FirstOrDefault(candidate =>
                string.Equals(candidate.FullName, "version.json", StringComparison.OrdinalIgnoreCase));
            if (entry is null || entry.Length > MaxVersionJsonBytes)
            {
                return null;
            }

            using var stream = entry.Open();
            using var reader = new StreamReader(stream);
            return Parse(reader.ReadToEnd());
        }
        catch (Exception exception) when (
            exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(exception, "pack_version could not be read from {Path}", jarPath);
            return null;
        }
    }

    /// <summary>Parses the <c>pack_version</c> block of a client jar's <c>version.json</c>.</summary>
    public static PackFormats? Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("pack_version", out var packVersion))
            {
                return null;
            }

            if (packVersion.ValueKind == JsonValueKind.Number && packVersion.TryGetInt32(out var single))
            {
                return new PackFormats(single, single, null, null);
            }

            if (packVersion.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            // Older clients name a single format per kind; current ones split it into a major and a
            // minor (for example resource_major 97, resource_minor 1). A pack's pack_format is the
            // major, so the minor is carried for display rather than comparison.
            var resource = ReadInt(packVersion, "resource") ?? ReadInt(packVersion, "resource_major");
            var data = ReadInt(packVersion, "data") ?? ReadInt(packVersion, "data_major");
            var resourceMinor = ReadInt(packVersion, "resource_minor");
            var dataMinor = ReadInt(packVersion, "data_minor");

            return resource is null && data is null
                ? null
                : new PackFormats(resource, data, resourceMinor, dataMinor);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int? ReadInt(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var parsed)
            ? parsed
            : null;
}

/// <summary>Formats one Minecraft version uses for resource packs and datapacks.</summary>
public sealed record PackFormats(int? Resource, int? Data, int? ResourceMinor = null, int? DataMinor = null)
{
    public string ResourceText => Describe(Resource, ResourceMinor);

    public string DataText => Describe(Data, DataMinor);

    private static string Describe(int? major, int? minor) => major switch
    {
        null => "unknown",
        _ when minor is { } value && value > 0 => $"{major}.{value}",
        _ => major.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };
}
