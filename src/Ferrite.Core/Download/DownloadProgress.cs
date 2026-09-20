namespace Ferrite.Core.Download;

public sealed record DownloadProgress
{
    public required int TotalFiles { get; init; }

    public required int CompletedFiles { get; init; }

    public required int FailedFiles { get; init; }

    public required int SkippedFiles { get; init; }

    public required long TotalBytes { get; init; }

    public required long CompletedBytes { get; init; }

    public required double BytesPerSecond { get; init; }

    public string? CurrentItem { get; init; }

    public double Fraction
    {
        get
        {
            if (TotalBytes > 0)
            {
                return Math.Clamp((double)CompletedBytes / TotalBytes, 0d, 1d);
            }

            return TotalFiles > 0 ? Math.Clamp((double)CompletedFiles / TotalFiles, 0d, 1d) : 0d;
        }
    }

    public TimeSpan? EstimatedRemaining
    {
        get
        {
            if (BytesPerSecond <= 1 || TotalBytes <= 0 || CompletedBytes >= TotalBytes)
            {
                return null;
            }

            var remaining = (TotalBytes - CompletedBytes) / BytesPerSecond;
            return remaining > 86400 ? null : TimeSpan.FromSeconds(remaining);
        }
    }
}
