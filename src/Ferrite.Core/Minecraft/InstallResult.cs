using Ferrite.Core.Download;

namespace Ferrite.Core.Minecraft;

public enum InstallStage
{
    ResolvingMetadata,
    Planning,
    Downloading,
    ExtractingNatives,
    Finalising,
}

/// <summary>Coarse stage plus the underlying download progress, for one progress UI.</summary>
public sealed record InstallProgress
{
    public required InstallStage Stage { get; init; }

    public DownloadProgress? Download { get; init; }

    public string? Message { get; init; }
}

public sealed record InstallResult(
    InstallManifest Manifest,
    DownloadSummary Download,
    int FilesVerified);

/// <summary>One managed file that is missing or does not match its expected hash.</summary>
public sealed record VerificationIssue(string Path, string Reason);

public sealed record VerificationReport(
    string VersionId,
    int FilesChecked,
    long TotalBytes,
    IReadOnlyList<VerificationIssue> Issues)
{
    public bool IsHealthy => Issues.Count == 0;
}
