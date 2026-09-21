using System.Diagnostics;
using System.Text;

namespace Ferrite.Core.Minecraft;

/// <summary>
/// A running Minecraft process. Output is streamed line by line to listeners and mirrored into a
/// per-launch log file so a crash can be investigated afterwards.
/// </summary>
public sealed class GameProcess : IDisposable
{
    private readonly Process _process;
    private readonly List<string> _tail = [];
    private readonly object _gate = new();
    private readonly TaskCompletionSource<int> _exitSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _disposed;

    internal GameProcess(Process process, string instanceName, string logFilePath)
    {
        _process = process;
        InstanceName = instanceName;
        LogFilePath = logFilePath;
        ExecutablePath = TryReadExecutablePath(process);
    }

    public int ProcessId => _process.Id;

    /// <summary>
    /// The executable the game is actually running on, read from the started process. Diagnostics can
    /// then name the runtime in use instead of repeating the one that was requested.
    /// </summary>
    public string? ExecutablePath { get; }

    private static string? TryReadExecutablePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return null;
        }
    }

    public string InstanceName { get; }

    public string LogFilePath { get; }

    public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? ExitedAt { get; private set; }

    public bool HasExited => _process.HasExited;

    public int? ExitCode => _process.HasExited ? _process.ExitCode : null;

    public TimeSpan Duration => (ExitedAt ?? DateTimeOffset.UtcNow) - StartedAt;

    /// <summary>Raised for every line of game output, on a background thread.</summary>
    public event Action<string>? OutputReceived;

    public event Action<int>? Exited;

    /// <summary>The most recent output lines, bounded so a chatty log cannot grow without limit.</summary>
    public IReadOnlyList<string> RecentOutput
    {
        get
        {
            lock (_gate)
            {
                return _tail.ToArray();
            }
        }
    }

    public Task<int> WaitForExitAsync(CancellationToken cancellationToken = default) =>
        _exitSource.Task.WaitAsync(cancellationToken);

    /// <summary>Asks the game to close, then forces it if it does not.</summary>
    public async Task StopAsync(TimeSpan gracePeriod, CancellationToken cancellationToken)
    {
        if (_process.HasExited)
        {
            return;
        }

        try
        {
            _process.CloseMainWindow();
        }
        catch (InvalidOperationException)
        {
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(gracePeriod);
        try
        {
            await _process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            ForceKill();
        }
    }

    public void ForceKill()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _process.Dispose();
    }

    internal void Complete(int exitCode)
    {
        ExitedAt = DateTimeOffset.UtcNow;
        _exitSource.TrySetResult(exitCode);
        Exited?.Invoke(exitCode);
    }

    internal void Publish(string line)
    {
        lock (_gate)
        {
            _tail.Add(line);
            if (_tail.Count > 500)
            {
                _tail.RemoveRange(0, _tail.Count - 500);
            }
        }

        OutputReceived?.Invoke(line);
    }

    internal static async Task PumpAsync(
        StreamReader reader,
        Action<string> publish,
        CancellationToken cancellationToken)
    {
        var buffer = new StringBuilder();
        var chunk = new char[4096];
        while (true)
        {
            int read;
            try
            {
                read = await reader.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (IOException)
            {
                return;
            }

            if (read == 0)
            {
                break;
            }

            for (var index = 0; index < read; index++)
            {
                var character = chunk[index];
                if (character == '\n')
                {
                    publish(buffer.ToString().TrimEnd('\r'));
                    buffer.Clear();
                }
                else if (buffer.Length < 8192)
                {
                    buffer.Append(character);
                }
            }
        }

        if (buffer.Length > 0)
        {
            publish(buffer.ToString());
        }
    }
}
