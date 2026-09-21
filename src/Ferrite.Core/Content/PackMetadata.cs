using System.IO.Compression;
using System.Text.Json;
using Ferrite.Core.Json;

namespace Ferrite.Core.Content;

/// <summary>What a resource pack or datapack declares in its <c>pack.mcmeta</c>.</summary>
public sealed record PackMetadata
{
    public const string MetaFileName = "pack.mcmeta";

    /// <summary>The format the pack was written for.</summary>
    public int? PackFormat { get; init; }

    /// <summary>Explicit range or list of formats the pack accepts, when it declares one.</summary>
    public int? MinSupportedFormat { get; init; }

    public int? MaxSupportedFormat { get; init; }

    public string? Description { get; init; }

    /// <summary>Namespaces the pack filters out of blocks and items (1.21+ pack filters).</summary>
    public IReadOnlyList<string> FilteredNamespaces { get; init; } = [];

    /// <summary>
    /// True when the pack states a format range. A pack that only names one format is claiming
    /// exactly that format, which is not the same statement.
    /// </summary>
    public bool DeclaresRange => MinSupportedFormat is not null || MaxSupportedFormat is not null;

    /// <summary>Lowest format the pack accepts (its single format when it declares no range).</summary>
    public int? EffectiveMin => MinSupportedFormat ?? PackFormat;

    public int? EffectiveMax => MaxSupportedFormat ?? PackFormat;

    public string FormatText
    {
        get
        {
            if (PackFormat is null && !DeclaresRange)
            {
                return "no format declared";
            }

            if (!DeclaresRange)
            {
                return $"format {PackFormat}";
            }

            return EffectiveMin == EffectiveMax
                ? $"format {EffectiveMin}"
                : $"formats {EffectiveMin}–{EffectiveMax}";
        }
    }
}

/// <summary>How a pack's declared formats relate to the format a game version uses.</summary>
public enum PackCompatibility
{
    /// <summary>The pack declares nothing this launcher can compare.</summary>
    Unknown,

    /// <summary>The declared formats include the instance's format.</summary>
    Compatible,

    /// <summary>The declared formats do not include the instance's format.</summary>
    Mismatch,
}

public static class PackMetadataReader
{
    private const int MaxMetaBytes = 512 * 1024;

    /// <summary>Reads <c>pack.mcmeta</c> from a ZIP, the shape resource and data packs ship in.</summary>
    public static PackMetadata? ReadFromZip(string archivePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        if (!File.Exists(archivePath))
        {
            return null;
        }

        try
        {
            var json = Ferrite.Core.Util.ArchiveExtractor.ReadEntryText(
                archivePath,
                PackMetadata.MetaFileName,
                MaxMetaBytes);
            return json is null ? null : Parse(json);
        }
        catch (Exception exception) when (
            exception is InvalidDataException
                or IOException
                or UnauthorizedAccessException
                or Util.PathSafetyException)
        {
            return null;
        }
    }

    /// <summary>Reads <c>pack.mcmeta</c> from an unpacked pack directory.</summary>
    public static PackMetadata? ReadFromDirectory(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var path = Path.Combine(directory, PackMetadata.MetaFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var info = new FileInfo(path);
            if (info.Length > MaxMetaBytes)
            {
                return null;
            }

            return Parse(File.ReadAllText(path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Parses <c>pack.mcmeta</c>. Malformed input yields null rather than an exception.</summary>
    public static PackMetadata? Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });

            if (!document.RootElement.TryGetProperty("pack", out var pack)
                || pack.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var (min, max) = ReadSupportedFormats(pack);
            var formats = ReadFilteredNamespaces(document.RootElement);

            return new PackMetadata
            {
                PackFormat = ReadInt(pack, "pack_format"),
                MinSupportedFormat = min,
                MaxSupportedFormat = max,
                Description = ReadDescription(pack),
                FilteredNamespaces = formats,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Compares a pack's declared formats with the format the running version uses. A version whose
    /// format could not be determined yields Unknown rather than a guessed answer.
    /// </summary>
    public static PackCompatibility Evaluate(PackMetadata? metadata, int? instanceFormat)
    {
        if (metadata is null || instanceFormat is not { } format)
        {
            return PackCompatibility.Unknown;
        }

        if (metadata.EffectiveMin is not { } min || metadata.EffectiveMax is not { } max)
        {
            return PackCompatibility.Unknown;
        }

        return format >= min && format <= max ? PackCompatibility.Compatible : PackCompatibility.Mismatch;
    }

    public static string DescribeCompatibility(PackCompatibility compatibility, int? instanceFormat) =>
        compatibility switch
        {
            PackCompatibility.Compatible => $"matches this version (format {instanceFormat})",
            PackCompatibility.Mismatch => $"does not list format {instanceFormat}, which this version uses",
            _ => "compatibility unknown: the version's client file is not installed yet",
        };

    private static (int? Min, int? Max) ReadSupportedFormats(JsonElement pack)
    {
        if (!pack.TryGetProperty("supported_formats", out var supported))
        {
            return (null, null);
        }

        switch (supported.ValueKind)
        {
            case JsonValueKind.Number when supported.TryGetInt32(out var single):
                return (single, single);

            case JsonValueKind.Array:
            {
                var values = new List<int>();
                foreach (var item in supported.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var value))
                    {
                        values.Add(value);
                    }
                }

                return values.Count == 0 ? (null, null) : (values.Min(), values.Max());
            }

            case JsonValueKind.Object:
            {
                var min = ReadInt(supported, "min_inclusive");
                var max = ReadInt(supported, "max_inclusive");
                return (min, max);
            }

            default:
                return (null, null);
        }
    }

    private static IReadOnlyList<string> ReadFilteredNamespaces(JsonElement root)
    {
        var namespaces = new List<string>();
        if (!root.TryGetProperty("filter", out var filter) || filter.ValueKind != JsonValueKind.Object)
        {
            return namespaces;
        }

        foreach (var section in new[] { "block", "item" })
        {
            if (!filter.TryGetProperty(section, out var entries) || entries.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var entry in entries.EnumerateArray())
            {
                if (entry.ValueKind == JsonValueKind.Object
                    && entry.TryGetProperty("namespace", out var value)
                    && value.ValueKind == JsonValueKind.String
                    && value.GetString() is { Length: > 0 } text)
                {
                    namespaces.Add(text);
                }
            }
        }

        return namespaces.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>A description may be a plain string or a chat component; both become plain text.</summary>
    private static string? ReadDescription(JsonElement pack)
    {
        if (!pack.TryGetProperty("description", out var description))
        {
            return null;
        }

        return description.ValueKind switch
        {
            JsonValueKind.String => description.GetString(),
            JsonValueKind.Object when description.TryGetProperty("text", out var text)
                && text.ValueKind == JsonValueKind.String => text.GetString(),
            JsonValueKind.Object when description.TryGetProperty("translate", out var key)
                && key.ValueKind == JsonValueKind.String => key.GetString(),
            _ => null,
        };
    }

    private static int? ReadInt(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var parsed)
            ? parsed
            : null;
}
