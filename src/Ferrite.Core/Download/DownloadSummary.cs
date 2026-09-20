namespace Ferrite.Core.Download;

public sealed record DownloadFailure(string Url, string TargetPath, string Message);

public sealed record DownloadSummary(
    int TotalFiles,
    int DownloadedFiles,
    int SkippedFiles,
    IReadOnlyList<DownloadFailure> Failures)
{
    public bool Success => Failures.Count == 0;
}
