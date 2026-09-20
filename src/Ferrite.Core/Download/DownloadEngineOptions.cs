namespace Ferrite.Core.Download;

public sealed class DownloadEngineOptions
{
    public int MaxConcurrency { get; init; } = 8;

    public bool AllowResume { get; init; } = true;

    public TimeSpan PerAttemptTimeout { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>Whole-file attempts, covering transport failure and hash mismatch.</summary>
    public int MaxAttempts { get; init; } = 3;
}
