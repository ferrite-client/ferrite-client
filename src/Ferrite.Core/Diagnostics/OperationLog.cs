using System.Text.Json;
using System.Text.Json.Serialization;
using Ferrite.Core.Json;
using Microsoft.Extensions.Logging;

namespace Ferrite.Core.Diagnostics;

/// <summary>Outcome of one user-visible operation.</summary>
public enum OperationOutcome
{
    Succeeded,
    Failed,
    Cancelled,
}

public sealed record OperationEntry(
    string Operation,
    OperationOutcome Outcome,
    DateTimeOffset StartedAt,
    TimeSpan Duration,
    string? Detail)
{
    [JsonIgnore]
    public string OutcomeText => Outcome switch
    {
        OperationOutcome.Succeeded => "succeeded",
        OperationOutcome.Failed => "failed",
        _ => "cancelled",
    };
}

/// <summary>
/// A bounded, persisted record of the operations the launcher ran: what it was doing, whether it
/// finished, and how long it took. It is the answer to "what did the launcher just do?" without
/// making a user read the structured log.
/// </summary>
public sealed class OperationLog
{
    private const int MaxEntriesRead = 512;

    /// <summary>One entry per line, so the file stays line-delimited rather than pretty-printed.</summary>
    private static readonly JsonSerializerOptions LineOptions = new(JsonDefaults.Document)
    {
        WriteIndented = false,
    };

    private readonly string _filePath;
    private readonly int _capacity;
    private readonly ILogger _logger;
    private readonly object _gate = new();
    private readonly LinkedList<OperationEntry> _entries = [];

    public OperationLog(string filePath, ILogger logger, int capacity = 200)
    {
        _filePath = filePath;
        _capacity = Math.Max(1, capacity);
        _logger = logger;
    }

    /// <summary>Reads recent entries from disk. A damaged line is skipped, never fatal.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return;
        }

        List<string> lines;
        try
        {
            lines = await ReadAllLinesAsync(_filePath, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException exception)
        {
            _logger.LogWarning(exception, "Operation history could not be read");
            return;
        }

        lock (_gate)
        {
            _entries.Clear();
            foreach (var line in lines.TakeLast(_capacity))
            {
                if (TryParse(line, out var entry))
                {
                    _entries.AddLast(entry);
                }
            }
        }
    }

    /// <summary>Starts an operation. The returned scope records the outcome when it is disposed.</summary>
    public OperationScope Begin(string operation) => new(this, operation);

    public void Record(string operation, OperationOutcome outcome, string? detail, TimeSpan duration)
    {
        var entry = new OperationEntry(operation, outcome, DateTimeOffset.UtcNow - duration, duration, detail);
        lock (_gate)
        {
            _entries.AddLast(entry);
            while (_entries.Count > _capacity)
            {
                _entries.RemoveFirst();
            }
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            File.AppendAllText(_filePath, JsonSerializer.Serialize(entry, LineOptions) + Environment.NewLine);
            TrimIfNeeded();
        }
        catch (IOException exception)
        {
            _logger.LogWarning(exception, "Operation history could not be written");
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.LogWarning(exception, "Operation history could not be written");
        }
    }

    /// <summary>Most recent operations first.</summary>
    public IReadOnlyList<OperationEntry> Recent(int count)
    {
        lock (_gate)
        {
            return _entries.Reverse().Take(Math.Max(0, count)).ToList();
        }
    }

    private void TrimIfNeeded()
    {
        // Keep the file from growing without bound while leaving room for a session's history.
        var info = new FileInfo(_filePath);
        if (!info.Exists || info.Length < 512 * 1024)
        {
            return;
        }

        var lines = File.ReadAllLines(_filePath);
        if (lines.Length <= _capacity * 2)
        {
            return;
        }

        File.WriteAllLines(_filePath, lines.TakeLast(_capacity * 2));
    }

    private static async Task<List<string>> ReadAllLinesAsync(string path, CancellationToken cancellationToken)
    {
        var lines = new List<string>();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            lines.Add(line);
            if (lines.Count > MaxEntriesRead * 2)
            {
                lines.RemoveRange(0, MaxEntriesRead);
            }
        }

        return lines;
    }

    private static bool TryParse(string line, out OperationEntry entry)
    {
        entry = null!;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        try
        {
            entry = JsonSerializer.Deserialize<OperationEntry>(line, LineOptions)!;
            return entry is not null && !string.IsNullOrWhiteSpace(entry.Operation);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Records one operation's outcome exactly once, however the caller exits.</summary>
    public sealed class OperationScope : IDisposable
    {
        private readonly OperationLog _owner;
        private readonly string _operation;
        private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;
        private OperationOutcome _outcome = OperationOutcome.Succeeded;
        private string? _detail;
        private bool _disposed;

        internal OperationScope(OperationLog owner, string operation)
        {
            _owner = owner;
            _operation = operation;
        }

        public void Fail(string? detail = null)
        {
            _outcome = OperationOutcome.Failed;
            _detail = detail ?? _detail;
        }

        public void Cancel(string? detail = null)
        {
            _outcome = OperationOutcome.Cancelled;
            _detail = detail ?? _detail;
        }

        public void Note(string? detail) => _detail = detail;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _owner.Record(_operation, _outcome, _detail, DateTimeOffset.UtcNow - _startedAt);
        }
    }
}
