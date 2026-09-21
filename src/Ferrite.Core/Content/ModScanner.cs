using System.IO.Compression;
using System.Text.Json;
using Ferrite.Core.Util;
using Microsoft.Extensions.Logging;
using Tomlyn;

namespace Ferrite.Core.Content;

/// <summary>
/// Reads mod metadata from installed JARs. Every loader family is handled, because a launcher that
/// shows only file names cannot tell a user what is actually installed.
/// </summary>
public sealed class ModScanner
{
    private const int MaxMetadataBytes = 4 * 1024 * 1024;

    /// <summary>
    /// TOML keys in mod descriptors are lower camel case (<c>modId</c>) while C# properties are
    /// upper camel case, and Tomlyn matches property names case-sensitively by default.
    /// </summary>
    private static readonly TomlSerializerOptions TomlOptions = new()
    {
        TypeInfoResolver = TomlSerializerOptions.Default.TypeInfoResolver,
        PropertyNameCaseInsensitive = true,
    };

    private readonly ILogger<ModScanner> _logger;

    public ModScanner(ILogger<ModScanner> logger, ModMetadataCache? cache = null)
    {
        _logger = logger;
        Cache = cache ?? new ModMetadataCache();
    }

    /// <summary>
    /// Metadata already read out of unchanged files. Shared by every scan this scanner performs, so
    /// reopening the mod tab does not open every jar again.
    /// </summary>
    public ModMetadataCache Cache { get; }

    /// <summary>Scans a folder for mods off the calling thread. Never throws for one bad file.</summary>
    public Task<IReadOnlyList<ModMetadata>> ScanAsync(string modsDirectory, CancellationToken cancellationToken) =>
        Task.Run(() => Scan(modsDirectory, cancellationToken), cancellationToken);

    public IReadOnlyList<ModMetadata> Scan(string modsDirectory, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(modsDirectory))
        {
            return [];
        }

        var results = new List<ModMetadata>();
        foreach (var path in Directory.EnumerateFiles(modsDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileName = Path.GetFileName(path);
            if (!IsModCandidate(fileName))
            {
                continue;
            }

            results.Add(Read(path, cancellationToken));
        }

        return results
            .OrderBy(mod => mod.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public ModMetadata Read(string path, CancellationToken cancellationToken)
    {
        var fileName = Path.GetFileName(path);
        var enabled = !fileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
        var info = new FileInfo(path);
        var size = info.Length;

        // The descriptor can only have changed if the file's own fingerprint did.
        if (Cache.TryGet(path, size, info.LastWriteTimeUtc.Ticks, out var cached))
        {
            return cached;
        }

        var metadata = ReadUncached(path, fileName, size, enabled, cancellationToken);
        Cache.Set(path, size, info.LastWriteTimeUtc.Ticks, metadata);
        return metadata;
    }

    private ModMetadata ReadUncached(
        string path,
        string fileName,
        long size,
        bool enabled,
        CancellationToken cancellationToken)
    {
        try
        {
            using var archive = ZipFile.OpenRead(path);
            if (ReadJsonEntry(archive, "fabric.mod.json") is { } fabricJson)
            {
                return FromFabric(path, fileName, size, enabled, fabricJson);
            }

            if (ReadJsonEntry(archive, "quilt.mod.json") is { } quiltJson)
            {
                return FromQuilt(path, fileName, size, enabled, quiltJson);
            }

            var toml = ReadTextEntry(archive, "META-INF/neoforge.mods.toml")
                ?? ReadTextEntry(archive, "META-INF/mods.toml");
            if (toml is not null)
            {
                return FromForgeToml(path, fileName, size, enabled, toml);
            }

            if (ReadJsonEntry(archive, "mcmod.info") is { } legacyJson)
            {
                return FromLegacy(path, fileName, size, enabled, legacyJson);
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or JsonException or TomlException)
        {
            _logger.LogDebug(exception, "Mod metadata could not be read from {Path}", path);
        }

        return new ModMetadata
        {
            FilePath = path,
            FileName = fileName,
            Size = size,
            Enabled = enabled,
            Loader = "unknown",
        };
    }

    public static bool IsModCandidate(string fileName) =>
        fileName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith(".zip.disabled", StringComparison.OrdinalIgnoreCase);

    private static ModMetadata FromFabric(string path, string fileName, long size, bool enabled, JsonElement root) =>
        new()
        {
            FilePath = path,
            FileName = fileName,
            Size = size,
            Enabled = enabled,
            Loader = "fabric",
            ModId = GetString(root, "id"),
            Name = GetString(root, "name"),
            Version = GetString(root, "version"),
            Description = GetString(root, "description"),
            Authors = ReadAuthors(root, "authors"),
            Homepage = GetNestedString(root, "contact", "homepage"),
            Dependencies = ReadKeys(root, "depends"),
        };

    private static ModMetadata FromQuilt(string path, string fileName, long size, bool enabled, JsonElement root)
    {
        var loader = root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("quilt_loader", out var quiltLoader)
                ? quiltLoader
                : default;
        var metadata = loader.ValueKind == JsonValueKind.Object
            && loader.TryGetProperty("metadata", out var metadataElement)
                ? metadataElement
                : default;

        return new ModMetadata
        {
            FilePath = path,
            FileName = fileName,
            Size = size,
            Enabled = enabled,
            Loader = "quilt",
            ModId = GetString(loader, "id"),
            Name = GetString(metadata, "name"),
            Version = GetString(loader, "version"),
            Description = GetString(metadata, "description"),
            Authors = ReadAuthors(metadata, "contributors"),
            Homepage = GetNestedString(metadata, "contact", "homepage"),
            Dependencies = ReadKeys(loader, "depends"),
        };
    }

    private ModMetadata FromForgeToml(string path, string fileName, long size, bool enabled, string toml)
    {
        var loader = fileName.Contains("neoforge", StringComparison.OrdinalIgnoreCase) ? "neoforge" : "forge";
        try
        {
            var model = TomlSerializer.Deserialize<ForgeModToml>(toml, TomlOptions) ?? new ForgeModToml();
            var first = model.Mods.Count > 0 ? model.Mods[0] : null;

            var dependencies = new List<string>();
            foreach (var (_, entries) in model.Dependencies)
            {
                foreach (var entry in entries)
                {
                    if (string.IsNullOrEmpty(entry.ModId))
                    {
                        continue;
                    }

                    var mandatory = entry.Mandatory ?? true;
                    dependencies.Add(mandatory ? entry.ModId : entry.ModId + " (optional)");
                }
            }

            return new ModMetadata
            {
                FilePath = path,
                FileName = fileName,
                Size = size,
                Enabled = enabled,
                Loader = loader,
                ModId = first?.ModId,
                Name = first?.DisplayName,
                Version = first?.Version,
                Description = first?.Description,
                Authors = first?.Authors,
                Homepage = first?.DisplayUrl,
                Dependencies = dependencies,
            };
        }
        catch (Exception exception) when (exception is TomlException or InvalidOperationException)
        {
            _logger.LogDebug(exception, "Mod TOML could not be parsed in {Path}", path);
            return new ModMetadata
            {
                FilePath = path,
                FileName = fileName,
                Size = size,
                Enabled = enabled,
                Loader = loader,
            };
        }
    }

    private static ModMetadata FromLegacy(string path, string fileName, long size, bool enabled, JsonElement root)
    {
        var entry = root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0 ? root[0] : root;
        return new ModMetadata
        {
            FilePath = path,
            FileName = fileName,
            Size = size,
            Enabled = enabled,
            Loader = "legacy",
            ModId = GetString(entry, "modid"),
            Name = GetString(entry, "name"),
            Version = GetString(entry, "version"),
            Description = GetString(entry, "description"),
            Authors = GetString(entry, "authorList") ?? GetString(entry, "author"),
            Homepage = GetString(entry, "url"),
        };
    }

    private static JsonElement? ReadJsonEntry(ZipArchive archive, string entryName)
    {
        var text = ReadTextEntry(archive, entryName);
        if (text is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    private static string? ReadTextEntry(ZipArchive archive, string entryName)
    {
        var entry = archive.Entries.FirstOrDefault(candidate =>
            string.Equals(candidate.FullName, entryName, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            return null;
        }

        try
        {
            return ArchiveExtractor.ReadBounded(entry, MaxMetadataBytes, entryName);
        }
        catch (PathSafetyException)
        {
            // A mod that declares a small metadata file but streams a huge one is not readable.
            return null;
        }
    }

    private static string? GetString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? GetNestedString(JsonElement element, string property, string nested) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value)
            ? GetString(value, nested)
            : null;

    private static string? ReadAuthors(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var authors = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { } text)
            {
                authors.Add(text);
            }
            else if (item.ValueKind == JsonValueKind.Object && GetString(item, "name") is { } name)
            {
                authors.Add(name);
            }
        }

        return authors.Count > 0 ? string.Join(", ", authors) : null;
    }

    private static IReadOnlyList<string> ReadKeys(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(property, out var value)
            || value.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var keys = new List<string>();
        foreach (var nested in value.EnumerateObject())
        {
            keys.Add(nested.Name);
        }

        return keys;
    }

}
