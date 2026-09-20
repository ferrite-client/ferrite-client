namespace Ferrite.Core.Util;

public sealed record ArchiveExtractionResult(
    int FilesExtracted,
    long BytesExtracted,
    IReadOnlyList<string> SkippedEntries);
