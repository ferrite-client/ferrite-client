namespace Ferrite.Core.Util;

public sealed class ArchiveExtractionOptions
{
    public int MaxEntries { get; init; } = 100_000;

    public long MaxEntryBytes { get; init; } = 2L * 1024 * 1024 * 1024;

    public long MaxTotalBytes { get; init; } = 16L * 1024 * 1024 * 1024;

    public bool Overwrite { get; init; } = true;

    /// <summary>Case-insensitive prefix removed from entry names before writing.</summary>
    public string? StripPrefix { get; init; }

    /// <summary>
    /// Optional filter applied to the normalised, prefix-stripped relative path. Returning false
    /// skips the entry.
    /// </summary>
    public Func<string, bool>? Include { get; init; }
}
