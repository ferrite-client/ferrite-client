using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Ferrite.Core.Minecraft;

/// <summary>
/// Starts Minecraft processes and tracks them. The command is always passed as an argument list;
/// no shell is involved, so no argument can be reinterpreted.
/// </summary>
public sealed class LaunchService
{
    private readonly ILogger<LaunchService> _logger;
    private readonly ConcurrentDictionary<Guid, GameProcess> _running = new();

    public LaunchService(ILogger<LaunchService> logger)
    {
        _logger = logger;
    }

    public IReadOnlyDictionary<Guid, GameProcess> RunningInstances => _running;

    public bool TryGetRunning(Guid instanceId, out GameProcess process) => _running.TryGetValue(instanceId, out process!);

    public async Task<GameProcess> StartAsync(
        Guid instanceId,
        LaunchCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (_running.TryGetValue(instanceId, out var existing) && !existing.HasExited)
        {
            throw new InvalidOperationException("This instance is already running.");
        }

        if (!File.Exists(command.ExecutablePath))
        {
            throw new FileNotFoundException("The Java executable was not found.", command.ExecutablePath);
        }

        Directory.CreateDirectory(command.WorkingDirectory);
        var logFilePath = BuildLogFilePath(command.WorkingDirectory);

        var startInfo = new ProcessStartInfo
        {
            FileName = command.ExecutablePath,
            WorkingDirectory = command.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in command.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var (key, value) in command.EnvironmentVariables)
        {
            startInfo.Environment[key] = value;
        }

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException("The Java process could not be started.");
        }

        var gameProcess = new GameProcess(process, instanceId.ToString("N"), logFilePath);
        var logWriter = new StreamWriter(new FileStream(logFilePath, FileMode.Append, FileAccess.Write, FileShare.Read))
        {
            AutoFlush = true,
        };

        _logger.LogInformation(
            "Launched instance {Instance} (pid {Pid}) with {Count} arguments",
            instanceId,
            process.Id,
            command.Arguments.Count);
        _logger.LogDebug("Launch command: {Command}", command.ToDisplayString());

        var pumpCancellation = new CancellationTokenSource();
        void Publish(string line)
        {
            try
            {
                logWriter.WriteLine(line);
            }
            catch (IOException)
            {
            }

            gameProcess.Publish(line);
        }

        var stdoutTask = GameProcess.PumpAsync(process.StandardOutput, Publish, pumpCancellation.Token);
        var stderrTask = GameProcess.PumpAsync(process.StandardError, Publish, pumpCancellation.Token);

        _running[instanceId] = gameProcess;

        _ = Task.Run(async () =>
        {
            try
            {
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is InvalidOperationException or IOException)
            {
                _logger.LogDebug(exception, "Waiting for instance {Instance} ended unexpectedly", instanceId);
            }
            finally
            {
                var exitCode = SafeExitCode(process);
                gameProcess.Complete(exitCode);
                _running.TryRemove(instanceId, out _);
                await pumpCancellation.CancelAsync().ConfigureAwait(false);
                pumpCancellation.Dispose();
                logWriter.Dispose();
                _logger.LogInformation(
                    "Instance {Instance} exited with code {ExitCode} after {Duration}",
                    instanceId,
                    exitCode,
                    gameProcess.Duration);
            }
        });

        await Task.CompletedTask.ConfigureAwait(false);
        return gameProcess;
    }

    public async Task StopAsync(Guid instanceId, TimeSpan gracePeriod, CancellationToken cancellationToken)
    {
        if (!_running.TryGetValue(instanceId, out var process))
        {
            return;
        }

        await process.StopAsync(gracePeriod, cancellationToken).ConfigureAwait(false);
    }

    private static string BuildLogFilePath(string gameDirectory)
    {
        var logsDirectory = Path.Combine(gameDirectory, "logs");
        Directory.CreateDirectory(logsDirectory);
        return Path.Combine(logsDirectory, $"ferrite-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.log");
    }

    private static int SafeExitCode(Process process)
    {
        try
        {
            return process.ExitCode;
        }
        catch (InvalidOperationException)
        {
            return -1;
        }
    }
}
