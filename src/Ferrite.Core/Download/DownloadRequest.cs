namespace Ferrite.Core.Download;

/// <summary>
/// One file to acquire. <see cref="TargetPath"/> is always caller-supplied and is expected to
/// already be inside the intended store; the engine never interprets remote file names.
/// </summary>
public sealed record DownloadRequest
{
    public required string Url { get; init; }

    public required string TargetPath { get; init; }

    public string? ExpectedSha1 { get; init; }

    /// <summary>Used by the updater, whose packages are published with SHA-256 digests.</summary>
    public string? ExpectedSha256 { get; init; }

    public string? ExpectedSha512 { get; init; }

    public long? ExpectedSize { get; init; }

    /// <summary>Human-readable name used in progress reporting.</summary>
    public string? Label { get; init; }

    /// <summary>Alternative hosts tried in order when the primary URL fails.</summary>
    public IReadOnlyList<string> FallbackUrls { get; init; } = [];

    public string DisplayName => Label ?? Path.GetFileName(TargetPath);
}
