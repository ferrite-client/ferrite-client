using Ferrite.Core.Storage;
using Ferrite.Core.Util;

namespace Ferrite.Core.Content;

/// <summary>Which modpack format an archive uses.</summary>
public enum ModpackArchiveKind
{
    Unknown = 0,
    Modrinth = 1,
    CurseForge = 2,
}

public static class ModpackArchives
{
    public const string ModrinthIndexEntry = "modrinth.index.json";
    public const string CurseForgeManifestEntry = "manifest.json";

    /// <summary>
    /// Identifies a pack archive by its root entry, so an import can pick the right installer
    /// without the user choosing a format.
    /// </summary>
    public static ModpackArchiveKind DetectKind(string archivePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        if (!File.Exists(archivePath))
        {
            return ModpackArchiveKind.Unknown;
        }

        try
        {
            if (ArchiveExtractor.ContainsEntry(archivePath, ModrinthIndexEntry))
            {
                return ModpackArchiveKind.Modrinth;
            }

            if (ArchiveExtractor.ContainsEntry(archivePath, CurseForgeManifestEntry))
            {
                return ModpackArchiveKind.CurseForge;
            }
        }
        catch (InvalidDataException)
        {
            return ModpackArchiveKind.Unknown;
        }

        return ModpackArchiveKind.Unknown;
    }
}

public sealed record ModpackInstallRequest
{
    /// <summary>Path to a `.mrpack` file on disk.</summary>
    public required string ArchivePath { get; init; }

    /// <summary>Install into this instance instead of creating one.</summary>
    public Guid? TargetInstanceId { get; init; }

    /// <summary>Overrides the pack's own name when creating an instance.</summary>
    public string? InstanceName { get; init; }

    /// <summary>
    /// When the target instance already has content, move the whole instance into backups before
    /// applying the pack, so a mistaken install is recoverable.
    /// </summary>
    public bool BackupExisting { get; init; } = true;

    public string? SourceUrl { get; init; }
}

public sealed record ModpackInstallResult
{
    public required InstanceRecord Instance { get; init; }

    public required string VersionId { get; init; }

    public required int FilesDownloaded { get; init; }

    public required int FilesSkipped { get; init; }

    public required int OverrideFiles { get; init; }

    public required IReadOnlyList<string> Warnings { get; init; }

    public string? BackupPath { get; init; }
}

/// <summary>Result of exporting an instance as a modpack archive.</summary>
public sealed record ModpackExportResult(
    string ArchivePath,
    int FileCount,
    int OverrideCount,
    string VersionId);
