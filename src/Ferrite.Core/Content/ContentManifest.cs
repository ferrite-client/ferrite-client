using System.Text.Json;
using Ferrite.Core.Json;
using Ferrite.Core.Util;
using Microsoft.Extensions.Logging;

namespace Ferrite.Core.Content;

/// <summary>One file the launcher installed into an instance, and where it came from.</summary>
public sealed record ContentManifestEntry
{
    /// <summary>Path relative to the instance's game directory, for example <c>mods/sodium.jar</c>.</summary>
    public required string RelativePath { get; init; }

    public required string Provider { get; init; }

    public required string ProjectId { get; init; }

    public required string VersionId { get; init; }

    public ContentProjectType ProjectType { get; init; } = ContentProjectType.Mod;

    public string? Sha1 { get; init; }

    public DateTimeOffset InstalledAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// What the launcher put into an instance. A mod file on disk cannot be matched back to a provider
/// project by name, so the launcher records the link when it installs. Files a user drops in are
/// deliberately absent: they are not the launcher's to update.
/// </summary>
public sealed class ContentManifest
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public List<ContentManifestEntry> Entries { get; set; } = [];

    /// <summary>Matching is by relative path, case-insensitively, as Windows paths are.</summary>
    public ContentManifestEntry? Find(string relativePath) => Entries.FirstOrDefault(entry =>
        string.Equals(Normalize(entry.RelativePath), Normalize(relativePath), StringComparison.OrdinalIgnoreCase));

    public void Upsert(ContentManifestEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Entries.RemoveAll(existing =>
            string.Equals(Normalize(existing.RelativePath), Normalize(entry.RelativePath), StringComparison.OrdinalIgnoreCase));
        Entries.Add(entry);
    }

    public bool Remove(string relativePath) => Entries.RemoveAll(entry =>
        string.Equals(Normalize(entry.RelativePath), Normalize(relativePath), StringComparison.OrdinalIgnoreCase)) > 0;

    private static string Normalize(string path) => PathSafety
        .NormalizeRelativePath(path)
        .Replace('\\', '/');
}

/// <summary>
/// Reads and writes the manifest next to the instance metadata. A damaged manifest is reported and
/// replaced rather than blocking the instance: losing the update history is annoying, not fatal.
/// </summary>
public sealed class ContentManifestStore
{
    private readonly ILogger<ContentManifestStore> _logger;

    public ContentManifestStore(ILogger<ContentManifestStore> logger)
    {
        _logger = logger;
    }

    public ContentManifest Load(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!File.Exists(filePath))
        {
            return new ContentManifest();
        }

        try
        {
            var json = File.ReadAllBytes(filePath);
            var manifest = JsonSerializer.Deserialize<ContentManifest>(json, JsonDefaults.Document)
                ?? new ContentManifest();
            if (manifest.SchemaVersion > ContentManifest.CurrentSchemaVersion)
            {
                _logger.LogWarning(
                    "Content manifest schema {Version} is newer than this build understands; ignoring it",
                    manifest.SchemaVersion);
                return new ContentManifest();
            }

            return manifest;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Content manifest at {Path} could not be read", filePath);
            return new ContentManifest();
        }
    }

    public void Save(string filePath, ContentManifest manifest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(manifest);
        try
        {
            manifest.SchemaVersion = ContentManifest.CurrentSchemaVersion;
            AtomicFile.WriteAllText(filePath, JsonSerializer.Serialize(manifest, JsonDefaults.Document));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Content manifest at {Path} could not be written", filePath);
        }
    }
}
