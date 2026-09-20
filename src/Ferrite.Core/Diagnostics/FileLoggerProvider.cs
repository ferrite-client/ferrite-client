using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Ferrite.Core.Diagnostics;

/// <summary>
/// Structured JSON-lines logger with size-based rotation and redaction.
/// Every write is best effort: a logging failure must never take the launcher down.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private static readonly AsyncLocal<ImmutableStack<object>?> ScopeStack = new();

    private readonly FileLoggerOptions _options;
    private readonly object _gate = new();
    private readonly string _filePath;

    private StreamWriter? _writer;
    private bool _disposed;

    public FileLoggerProvider(FileLoggerOptions options)
    {
        _options = options;
        Directory.CreateDirectory(options.Directory);
        _filePath = Path.Combine(options.Directory, options.FileName);
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                _writer?.Flush();
                _writer?.Dispose();
            }
            catch (IOException)
            {
            }

            _writer = null;
        }
    }

    private bool IsEnabled(LogLevel level) => level != LogLevel.None && level >= _options.MinimumLevel;

    private void Write(LogLevel level, string category, EventId eventId, string message, Exception? exception)
    {
        if (_disposed || level < _options.MinimumLevel)
        {
            return;
        }

        try
        {
            var line = Render(level, category, eventId, message, exception);
            lock (_gate)
            {
                var writer = EnsureWriter();
                writer.WriteLine(line);
                writer.Flush();
                RotateIfNeeded();
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private string Render(LogLevel level, string category, EventId eventId, string message, Exception? exception)
    {
        using var buffer = new MemoryStream(512);
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("ts", DateTimeOffset.UtcNow.ToString("O"));
            json.WriteString("level", LevelText(level));
            json.WriteString("category", category);
            if (eventId.Id != 0 || eventId.Name is not null)
            {
                json.WriteNumber("eventId", eventId.Id);
                if (eventId.Name is not null)
                {
                    json.WriteString("eventName", eventId.Name);
                }
            }

            json.WriteString("message", _options.Redactor.Redact(message));

            WriteScopes(json);

            if (exception is not null)
            {
                json.WriteString("exception", _options.Redactor.Redact(exception.ToString()));
            }

            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private void WriteScopes(Utf8JsonWriter json)
    {
        var stack = ScopeStack.Value;
        if (stack is null || stack.IsEmpty)
        {
            return;
        }

        var merged = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var scope in stack.Reverse())
        {
            if (scope is IEnumerable<KeyValuePair<string, object?>> pairs)
            {
                foreach (var pair in pairs)
                {
                    merged[pair.Key] = pair.Value?.ToString();
                }
            }
        }

        if (merged.Count == 0)
        {
            return;
        }

        json.WriteStartObject("data");
        foreach (var pair in merged)
        {
            json.WriteString(pair.Key, _options.Redactor.Redact(pair.Value));
        }

        json.WriteEndObject();
    }

    private StreamWriter EnsureWriter()
    {
        if (_writer is not null)
        {
            return _writer;
        }

        _writer = new StreamWriter(
            new FileStream(_filePath, FileMode.Append, FileAccess.Write, FileShare.Read),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return _writer;
    }

    private void RotateIfNeeded()
    {
        if (_writer is null)
        {
            return;
        }

        if (_writer.BaseStream.Length < _options.MaxFileBytes)
        {
            return;
        }

        _writer.Dispose();
        _writer = null;

        for (var index = _options.RetainedFiles - 1; index >= 1; index--)
        {
            var source = RotatedPath(index);
            var destination = RotatedPath(index + 1);
            if (File.Exists(source))
            {
                if (File.Exists(destination))
                {
                    File.Delete(destination);
                }

                File.Move(source, destination);
            }
        }

        if (File.Exists(RotatedPath(1)))
        {
            File.Delete(RotatedPath(1));
        }

        File.Move(_filePath, RotatedPath(1));
    }

    private string RotatedPath(int index)
    {
        var stem = Path.GetFileNameWithoutExtension(_filePath);
        var extension = Path.GetExtension(_filePath);
        return Path.Combine(_options.Directory, $"{stem}.{index}{extension}");
    }

    private static string LevelText(LogLevel level) => level switch
    {
        LogLevel.Trace => "trace",
        LogLevel.Debug => "debug",
        LogLevel.Information => "info",
        LogLevel.Warning => "warn",
        LogLevel.Error => "error",
        LogLevel.Critical => "critical",
        _ => "none",
    };

    private sealed class FileLogger : ILogger
    {
        private readonly FileLoggerProvider _provider;
        private readonly string _category;

        public FileLogger(FileLoggerProvider provider, string category)
        {
            _provider = provider;
            _category = category;
        }

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            var previous = ScopeStack.Value ?? ImmutableStack<object>.Empty;
            ScopeStack.Value = previous.Push(state);
            return new ScopeDisposer(previous);
        }

        public bool IsEnabled(LogLevel logLevel) => _provider.IsEnabled(logLevel);

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            var message = formatter(state, exception);
            _provider.Write(logLevel, _category, eventId, message, exception);
        }

        private sealed class ScopeDisposer : IDisposable
        {
            private readonly ImmutableStack<object>? _previous;
            private bool _disposed;

            public ScopeDisposer(ImmutableStack<object>? previous)
            {
                _previous = previous;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                ScopeStack.Value = _previous;
            }
        }
    }
}
