namespace Ferrite.Core.Game;

/// <summary>Summary of one saved world, read from its <c>level.dat</c>.</summary>
public sealed record WorldInfo
{
    public required string DirectoryPath { get; init; }

    public required string FolderName { get; init; }

    public required string Name { get; init; }

    public long SizeBytes { get; init; }

    public DateTimeOffset? LastPlayed { get; init; }

    public string? VersionName { get; init; }

    public int? VersionId { get; init; }

    public string GameMode { get; init; } = "unknown";

    public bool Hardcore { get; init; }

    public bool CheatsEnabled { get; init; }

    public long? Seed { get; init; }

    public string? IconPath { get; init; }

    public bool HasIcon => IconPath is not null;

    public string SizeText => Ferrite.Core.Util.ByteSize.Format(SizeBytes);

    public string LastPlayedText => LastPlayed is { } played
        ? played.LocalDateTime.ToString("yyyy-MM-dd HH:mm")
        : "never";
}
