namespace Ferrite.Core.Minecraft;

/// <summary>
/// Record of what was installed for an instance. Repair re-derives the artefact set from the
/// version metadata, so this stays a small summary rather than a full file list.
/// </summary>
public sealed class InstallManifest
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>Minecraft version this instance was installed from.</summary>
    public string BaseVersionId { get; set; } = string.Empty;

    /// <summary>Resolved version id, which differs from the base for loader installs.</summary>
    public string ResolvedVersionId { get; set; } = string.Empty;

    public string? Loader { get; set; }

    public string? LoaderVersion { get; set; }

    public DateTimeOffset InstalledAt { get; set; } = DateTimeOffset.UtcNow;

    public int FileCount { get; set; }

    public long TotalBytes { get; set; }

    public string NativesDirectory { get; set; } = string.Empty;

    public string? LoggingConfigPath { get; set; }
}
