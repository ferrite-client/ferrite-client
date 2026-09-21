using System.Diagnostics;
using Ferrite.Core.Update;

namespace Ferrite.Core.Tests;

/// <summary>
/// The hand-off script is the part of self-update that touches an installed copy, so it is both
/// inspected and executed here: it must refuse anything that is not a marked staging directory, and
/// when it does run it must replace the install, start the new build, and clean up after itself.
/// </summary>
public sealed class UpdateHandoffTests : IDisposable
{
    private readonly string _workspace;

    public UpdateHandoffTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), "ferrite-handoff-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspace);
    }

    public void Dispose()
    {
        // The stand-in build exits on its own, but Windows can hold a released image briefly.
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                Directory.Delete(_workspace, recursive: true);
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(200);
            }
        }
    }

    [Fact]
    public async Task A_directory_without_the_stage_marker_is_not_treated_as_staging()
    {
        var directory = Path.Combine(_workspace, "not-staged");
        Directory.CreateDirectory(directory);
        Assert.False(UpdateHandoff.IsStagingDirectory(directory));

        await UpdateHandoff.WriteScriptAsync(directory, TestContext.Current.CancellationToken);
        // Writing the script alone is not enough; only Ferrite's staging marker counts.
        Assert.False(UpdateHandoff.IsStagingDirectory(directory));
    }

    [Fact]
    public async Task Staging_writes_the_marker_the_script_requires()
    {
        var stage = Path.Combine(_workspace, "2.0.0");
        Directory.CreateDirectory(stage);
        await File.WriteAllTextAsync(
            Path.Combine(stage, UpdateHandoff.StageMarkerName),
            "2.0.0",
            TestContext.Current.CancellationToken);

        Assert.True(UpdateHandoff.IsStagingDirectory(stage));
    }

    [Fact]
    public void The_command_passes_every_value_as_an_argument()
    {
        var stage = new UpdateStageResult(
            "2.0.0",
            @"C:\staging\2.0.0",
            @"C:\staging\2.0.0\payload",
            @"C:\staging\2.0.0\apply-update.ps1",
            1024);

        var command = UpdateHandoff.BuildCommand(stage, processId: 4321, installDirectory: @"C:\Program Files\Ferrite");

        Assert.Contains("-File", command);
        Assert.Contains(stage.ScriptPath, command);
        Assert.Contains("4321", command);
        Assert.Contains(@"C:\Program Files\Ferrite", command);
        Assert.DoesNotContain(command, argument => argument.Contains('\n'));
    }

    /// <summary>
    /// Runs the generated script for real. The payload's executable is a copy of a harmless Windows
    /// program named Ferrite.exe, so the hand-off genuinely copies files and starts a process without
    /// leaving a launcher window running.
    /// </summary>
    [Fact]
    public async Task The_script_replaces_the_install_starts_the_build_and_cleans_up()
    {
        var shell = FindPowerShell();
        if (shell is null)
        {
            Assert.Skip("PowerShell is not available on this machine.");
            return;
        }

        // A program that starts and exits without waiting for input.
        var standIn = FindStandInExecutable();
        if (standIn is null)
        {
            Assert.Skip("No stand-in executable available for the payload.");
            return;
        }

        var stage = Path.Combine(_workspace, "stage");
        var payload = Path.Combine(stage, "payload");
        var install = Path.Combine(_workspace, "install");
        Directory.CreateDirectory(payload);
        Directory.CreateDirectory(install);
        await File.WriteAllTextAsync(
            Path.Combine(stage, UpdateHandoff.StageMarkerName),
            "2.0.0",
            TestContext.Current.CancellationToken);

        File.Copy(standIn, Path.Combine(payload, UpdateHandoff.ExecutableName));
        await File.WriteAllTextAsync(
            Path.Combine(payload, "Ferrite.Core.dll"),
            "new core",
            TestContext.Current.CancellationToken);

        // The install that the update replaces.
        await File.WriteAllTextAsync(
            Path.Combine(install, "Ferrite.Core.dll"),
            "old core",
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(install, "keep-me.txt"),
            "user file",
            TestContext.Current.CancellationToken);

        var script = await UpdateHandoff.WriteScriptAsync(stage, TestContext.Current.CancellationToken);

        // A process id that has already exited, so the script does not wait.
        using var exited = Process.Start(new ProcessStartInfo(standIn) { UseShellExecute = false });
        Assert.NotNull(exited);
        await exited!.WaitForExitAsync(TestContext.Current.CancellationToken);

        var start = new ProcessStartInfo(shell)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in UpdateHandoff.BuildCommand(
                     new UpdateStageResult("2.0.0", stage, payload, script, 0),
                     exited.Id,
                     install))
        {
            start.ArgumentList.Add(argument);
        }

        using var runner = Process.Start(start);
        Assert.NotNull(runner);
        var output = await runner!.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var error = await runner.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await runner.WaitForExitAsync(TestContext.Current.CancellationToken);

        Assert.True(runner.ExitCode == 0, $"hand-off failed ({runner.ExitCode}): {output}{error}");

        // The replaced file now holds the new content, and the payload was copied in full.
        Assert.Equal(
            "new core",
            await File.ReadAllTextAsync(
                Path.Combine(install, "Ferrite.Core.dll"),
                TestContext.Current.CancellationToken));
        Assert.True(File.Exists(Path.Combine(install, UpdateHandoff.ExecutableName)));

        // A user file that the package does not mention is left alone.
        Assert.True(File.Exists(Path.Combine(install, "keep-me.txt")));

        // The staging directory and the script clean themselves up.
        Assert.False(Directory.Exists(stage));
        Assert.False(File.Exists(script));
        Assert.Contains("update complete", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_script_refuses_an_unmarked_directory()
    {
        var shell = FindPowerShell();
        if (shell is null)
        {
            Assert.Skip("PowerShell is not available on this machine.");
            return;
        }

        var stage = Path.Combine(_workspace, "unmarked");
        var payload = Path.Combine(stage, "payload");
        var install = Path.Combine(_workspace, "install-unmarked");
        Directory.CreateDirectory(payload);
        Directory.CreateDirectory(install);
        File.WriteAllText(Path.Combine(payload, UpdateHandoff.ExecutableName), "not really a build");

        var script = await UpdateHandoff.WriteScriptAsync(stage, TestContext.Current.CancellationToken);

        var start = new ProcessStartInfo(shell)
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        foreach (var argument in UpdateHandoff.BuildCommand(
                     new UpdateStageResult("2.0.0", stage, payload, script, 0),
                     Environment.ProcessId,
                     install))
        {
            start.ArgumentList.Add(argument);
        }

        using var runner = Process.Start(start);
        Assert.NotNull(runner);
        await runner!.WaitForExitAsync(TestContext.Current.CancellationToken);

        Assert.NotEqual(0, runner.ExitCode);
        Assert.Empty(Directory.GetFiles(install));
        Assert.True(Directory.Exists(stage));
    }

    private static string? FindPowerShell()
    {
        var candidates = Environment.OSVersion.Platform == PlatformID.Win32NT
            ? new[] { "pwsh.exe", "powershell.exe" }
            : new[] { "pwsh", "powershell" };

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var candidate in candidates)
        {
            foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var full = Path.Combine(directory, candidate);
                if (File.Exists(full))
                {
                    return full;
                }
            }
        }

        return null;
    }

    private static string? FindStandInExecutable()
    {
        foreach (var name in new[] { "whoami.exe", "hostname.exe" })
        {
            var path = Path.Combine(Environment.SystemDirectory, name);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }
}
