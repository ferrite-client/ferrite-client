using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Ferrite.App.Localization;

namespace Ferrite.App.ViewModels;

/// <summary>Severity a log line was written at, as far as the line itself says.</summary>
public enum LogSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>One rendered log line with the severity the line declares.</summary>
public sealed record LogLineViewModel(string Text, LogSeverity Severity)
{
    public bool IsWarning => Severity == LogSeverity.Warning;

    public bool IsError => Severity == LogSeverity.Error;
}

/// <summary>
/// The log view's own state: severity, search, and whether to follow the tail. Logs are a power-user
/// surface, so this filters in place rather than making the user read four hundred lines to find the
/// one that explains a crash.
/// </summary>
public sealed partial class InstanceDetailViewModel
{
    [ObservableProperty]
    private string _logQuery = string.Empty;

    public IReadOnlyList<ChoiceOption> LogSeverities { get; } =
    [
        new("all", Localizer.Get("L.Instance.LogAll")),
        new("warning", Localizer.Get("L.Instance.LogWarnings")),
        new("error", Localizer.Get("L.Instance.LogErrors")),
    ];

    [ObservableProperty]
    private ChoiceOption _selectedLogSeverity = new("all", Localizer.Get("L.Instance.LogAll"));

    /// <summary>When on, the list keeps the newest line in view as the log is re-read.</summary>
    [ObservableProperty]
    private bool _followTail = true;

    public ObservableCollection<LogLineViewModel> VisibleLogLines { get; } = [];

    public bool HasVisibleLogLines => VisibleLogLines.Count > 0;

    public bool HasLogFilter =>
        !string.IsNullOrWhiteSpace(LogQuery)
        || !string.Equals(SelectedLogSeverity.Value, "all", StringComparison.Ordinal);

    /// <summary>Nothing to show because the instance has no log file yet.</summary>
    public bool ShowsEmptyLog => !HasVisibleLogLines && LogLines.Count == 0;

    /// <summary>Nothing to show because the filter excluded everything the log did contain.</summary>
    public bool ShowsNoLogMatches => !HasVisibleLogLines && LogLines.Count > 0;

    public string LogSummaryText =>
        Localizer.Format("L.Instance.LogShown", VisibleLogLines.Count, LogLines.Count);

    private IReadOnlyList<LogLineViewModel> LogLines { get; set; } = [];

    partial void OnLogQueryChanged(string value) => RebuildVisibleLogLines();

    partial void OnSelectedLogSeverityChanged(ChoiceOption value) => RebuildVisibleLogLines();

    partial void OnLogTextChanged(string value)
    {
        LogLines = ParseLog(value);
        RebuildVisibleLogLines();
    }

    private void RebuildVisibleLogLines()
    {
        var query = LogQuery?.Trim() ?? string.Empty;
        var minimum = SelectedLogSeverity?.Value switch
        {
            "warning" => LogSeverity.Warning,
            "error" => LogSeverity.Error,
            _ => LogSeverity.Info,
        };

        VisibleLogLines.Clear();
        foreach (var line in LogLines)
        {
            if (line.Severity < minimum)
            {
                continue;
            }

            if (query.Length > 0
                && !line.Text.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            {
                continue;
            }

            VisibleLogLines.Add(line);
        }

        OnPropertyChanged(nameof(HasVisibleLogLines));
        OnPropertyChanged(nameof(ShowsEmptyLog));
        OnPropertyChanged(nameof(ShowsNoLogMatches));
        OnPropertyChanged(nameof(HasLogFilter));
        OnPropertyChanged(nameof(LogSummaryText));
    }

    /// <summary>
    /// Reads a level out of each line. Vanilla and modded logs both put the level in the thread banner
    /// ("[main/WARN]"), and a stack trace's own "Exception" or "Caused by" lines are the other place a
    /// reader looks first.
    /// </summary>
    private static IReadOnlyList<LogLineViewModel> ParseLog(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var lines = new List<LogLineViewModel>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            lines.Add(new LogLineViewModel(line, Classify(line)));
        }

        return lines;
    }

    private static LogSeverity Classify(string line) =>
        Has(line, "/ERROR]") || Has(line, "/FATAL]") || Has(line, "Exception")
            ? LogSeverity.Error
            : Has(line, "/WARN]") || Has(line, "Caused by:")
                ? LogSeverity.Warning
                : LogSeverity.Info;

    private static bool Has(string line, string token) =>
        line.Contains(token, StringComparison.OrdinalIgnoreCase);
}
