using Ferrite.App.Localization;
using Ferrite.Core.Diagnostics;
using Ferrite.Core.Download;
using Ferrite.Core.Util;

namespace Ferrite.App.ViewModels;

/// <summary>
/// One row in the downloads view: either an operation that is running right now or one the launcher
/// has finished. Both come from the same operation log the diagnostics page reads, so a row here is a
/// record of something that really happened rather than a decoration.
/// </summary>
public sealed class DownloadItemViewModel
{
    private DownloadItemViewModel(
        string name,
        string? detail,
        DateTimeOffset startedAt,
        TimeSpan? duration,
        OperationOutcome? outcome,
        double fraction,
        bool isRunning,
        DownloadProgress? transfer)
    {
        Name = name;
        Detail = detail;
        StartedAt = startedAt;
        Duration = duration;
        Outcome = outcome;
        Fraction = fraction;
        IsRunning = isRunning;
        Transfer = transfer;
    }

    public string Name { get; }

    public string? Detail { get; }

    public DateTimeOffset StartedAt { get; }

    public TimeSpan? Duration { get; }

    public OperationOutcome? Outcome { get; }

    public double Fraction { get; }

    public bool HasMeasuredProgress => Fraction > 0;

    public bool IsRunning { get; }

    /// <summary>The live transfer behind a running operation, when the launcher reported one.</summary>
    public DownloadProgress? Transfer { get; }

    public bool HasTransfer => Transfer is not null;

    /// <summary>How many of the planned files are done.</summary>
    public string FilesText => Transfer is { } transfer
        ? Localizer.Format("L.Downloads.Files", transfer.CompletedFiles, transfer.TotalFiles)
        : string.Empty;

    /// <summary>Bytes transferred against the bytes this operation planned to move.</summary>
    public string BytesText => Transfer is { } transfer
        ? $"{ByteSize.Format(transfer.CompletedBytes)} / {ByteSize.Format(transfer.TotalBytes)}"
        : string.Empty;

    /// <summary>The current rate, or nothing while the engine has not measured one yet.</summary>
    public string SpeedText => Transfer is { BytesPerSecond: > 1 } transfer
        ? $"{ByteSize.Format((long)transfer.BytesPerSecond)}/s"
        : string.Empty;

    public string FailedText => Transfer is { FailedFiles: > 0 } transfer
        ? Localizer.Format("L.Downloads.FailedFiles", transfer.FailedFiles)
        : string.Empty;

    public bool HasFailures => Transfer is { FailedFiles: > 0 };

    public bool HasDetail => !string.IsNullOrWhiteSpace(Detail);

    public string StartedText => StartedAt.ToLocalTime().ToString("g");

    public string DurationText => Duration is { } span
        ? span.TotalSeconds >= 60
            ? $"{(int)span.TotalMinutes}m {span.Seconds}s"
            : $"{span.TotalSeconds:0.0}s"
        : string.Empty;

    /// <summary>The outcome as a word, or the running state while it is still going.</summary>
    public string StateText => IsRunning
        ? Localizer.Get("L.Downloads.Running")
        : Outcome switch
        {
            OperationOutcome.Succeeded => Localizer.Get("L.Downloads.Succeeded"),
            OperationOutcome.Failed => Localizer.Get("L.Downloads.Failed"),
            OperationOutcome.Cancelled => Localizer.Get("L.Downloads.Cancelled"),
            _ => string.Empty,
        };

    // Conditional style classes the state pill binds to, so tone comes from the design system.
    public bool IsRunningState => IsRunning;

    public bool IsSucceeded => Outcome == OperationOutcome.Succeeded;

    public bool IsFailed => Outcome == OperationOutcome.Failed;

    public static DownloadItemViewModel FromEntry(OperationEntry entry) => new(
        entry.Operation,
        entry.Detail,
        entry.StartedAt,
        entry.Duration,
        entry.Outcome,
        fraction: 0,
        isRunning: false,
        transfer: null);

    public static DownloadItemViewModel Running(
        string name,
        string? detail,
        double fraction,
        DownloadProgress? transfer = null) => new(
        name,
        detail,
        DateTimeOffset.UtcNow,
        duration: null,
        outcome: null,
        fraction,
        isRunning: true,
        transfer);
}
