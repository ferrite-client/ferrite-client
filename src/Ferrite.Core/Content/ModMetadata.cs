namespace Ferrite.Core.Content;

/// <summary>Metadata extracted from an installed mod file.</summary>
public sealed record ModMetadata
{
    public required string FilePath { get; init; }

    public required string FileName { get; init; }

    public long Size { get; init; }

    public string? ModId { get; init; }

    public string? Name { get; init; }

    public string? Version { get; init; }

    public string? Description { get; init; }

    public string? Authors { get; init; }

    /// <summary>fabric, quilt, forge, neoforge, legacy, or unknown.</summary>
    public string Loader { get; init; } = "unknown";

    public IReadOnlyList<string> Dependencies { get; init; } = [];

    public string? Homepage { get; init; }

    /// <summary>False when the file is a disabled mod (renamed with a <c>.disabled</c> suffix).</summary>
    public bool Enabled { get; init; } = true;

    public string DisplayName => Name ?? ModId ?? Path.GetFileNameWithoutExtension(FileName);
}

/// <summary>One file in an instance's content folder.</summary>
public sealed record ContentFileEntry(
    string FilePath,
    string FileName,
    long Size,
    bool Enabled,
    DateTimeOffset ModifiedAt,
    /// <summary>Formats the pack declares, when the entry is a pack that declares any.</summary>
    string? PackFormatText = null,
    /// <summary>How the pack's formats relate to this instance, when that could be determined.</summary>
    string? CompatibilityText = null,
    /// <summary>True when the pack declares formats that exclude the instance's format.</summary>
    bool IsPackMismatch = false);
