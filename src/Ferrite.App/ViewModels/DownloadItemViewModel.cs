using Ferrite.App.Localization;
using Ferrite.Core.Diagnostics;

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
        bool isRunning)
    {
        Name = name;
        Detail = detail;
        StartedAt = startedAt;
        Duration = duration;
        Outcome = outcome;
        Fraction = fraction;
        IsRunning = isRunning;
    }

    public string Name { get; }

    public string? Detail { get; }

    public DateTimeOffset StartedAt { get; }

    public TimeSpan? Duration { get; }

    public OperationOutcome? Outcome { get; }

    public double Fraction { get; }

    public bool HasMeasuredProgress => Fraction > 0;

    public bool IsRunning { get; }

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
        isRunning: false);

    public static DownloadItemViewModel Running(string name, string? detail, double fraction) => new(
        name,
        detail,
        DateTimeOffset.UtcNow,
        duration: null,
        outcome: null,
        fraction,
        isRunning: true);
}
