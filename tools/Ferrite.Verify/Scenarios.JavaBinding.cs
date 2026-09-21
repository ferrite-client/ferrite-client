using Ferrite.Core.Java;
using Ferrite.Core.Minecraft;

namespace Ferrite.Verify;

internal static partial class Scenarios
{
    /// <summary>
    /// Launches through the product's own <see cref="InstanceLauncher"/> - the code path the Play
    /// button uses - with a specific runtime chosen, then reads the image of the process that
    /// actually started. That is the difference between "a runtime was chosen" and "the runtime the
    /// user asked for is the one that ran".
    /// </summary>
    /// <param name="java">
    /// A substring of a detected runtime's path or name, or a path to a Java executable the
    /// environment scan would not find. The second form is probed the way a user-supplied path is.
    /// </param>
    /// <param name="viaDefault">
    /// True to pin the launcher-wide default, false to pin the instance, so both precedence rules
    /// can be exercised.
    /// </param>
    public static async Task<int> InstanceLaunchWithPinnedJavaAsync(
        VerifyServices services,
        string versionId,
        int seconds,
        string? java,
        bool viaDefault,
        string? world,
        string? joinServer,
        CancellationToken cancellationToken)
    {
        var instance = await GetOrCreateInstanceAsync(services, versionId, cancellationToken)
            .ConfigureAwait(false);
        Console.WriteLine($"Instance: {instance.Name} ({instance.Id})");

        // A user-supplied path is probed by running it before anything else happens, which is the
        // same gate the Java page applies.
        var userPath = java is { Length: > 0 } && File.Exists(java) ? Path.GetFullPath(java) : null;
        if (userPath is not null)
        {
            var probed = await services.Java
                .ProbeUserSuppliedAsync(userPath, cancellationToken)
                .ConfigureAwait(false);
            Console.WriteLine(probed is null
                ? $"User path {userPath} did not answer as a Java runtime."
                : $"User path accepted: {probed.DisplayName} (Java {probed.ShortVersion})");
            if (probed is null)
            {
                return 2;
            }
        }

        var required = JavaCompatibility.RequiredMajorFor(versionId);
        var runtimes = await services.Java
            .DetectAsync(cancellationToken, userPath is null ? null : [userPath])
            .ConfigureAwait(false);
        var automatic = JavaSelection.SelectBest(runtimes, required);
        Console.WriteLine($"Version {versionId} requires Java {required?.ToString() ?? "unknown"}");
        Console.WriteLine(
            $"Automatic choice: {automatic?.DisplayName ?? "none"} ({automatic?.ExecutablePath ?? "-"})");

        var compatible = runtimes
            .Where(runtime => JavaCompatibility.Evaluate(runtime, required).IsCompatible)
            .ToList();
        var pinned = userPath is not null
            ? runtimes.FirstOrDefault(runtime =>
                string.Equals(runtime.ExecutablePath, userPath, StringComparison.OrdinalIgnoreCase))
            : FindPin(compatible, java)
              ?? compatible
                  .OrderBy(runtime => runtime.MajorVersion)
                  .ThenBy(runtime => runtime.ExecutablePath, StringComparer.OrdinalIgnoreCase)
                  .FirstOrDefault();

        if (pinned is null)
        {
            Console.WriteLine("No compatible runtime could be pinned; nothing to launch.");
            return 2;
        }

        Console.WriteLine($"Pinned choice:    {pinned.DisplayName} ({pinned.ExecutablePath})");
        Console.WriteLine(
            automatic is not null
            && string.Equals(automatic.ExecutablePath, pinned.ExecutablePath, StringComparison.OrdinalIgnoreCase)
                ? "  NOTE: the pin matches the automatic choice, so this run does not distinguish them."
                : "  The pin differs from the automatic choice, so the run distinguishes them.");

        instance.JavaPath = viaDefault ? null : pinned.ExecutablePath;
        if (joinServer is { Length: > 0 })
        {
            instance.LastServerAddress = joinServer;
            instance.LastServerPort = null;
        }

        // A per-instance memory setting and custom JVM argument, so this run also shows that the
        // instance's own launch settings reach the command the game is started with.
        instance.MemoryMb = 3072;
        instance.JvmArguments = ["-Dferrite.verify.marker=1"];
        await services.Instances.SaveAsync(instance, cancellationToken).ConfigureAwait(false);

        var result = await services.InstanceLauncher
            .LaunchAsync(
                new InstanceLaunchRequest
                {
                    Instance = instance,
                    Account = new LaunchAccount
                    {
                        PlayerName = "FerriteVerify",
                        Uuid = "00000000000000000000000000000001",
                        AccessToken = "verify-pipeline-token",
                        Xuid = "0",
                        ClientId = "0",
                    },
                    DefaultJavaPath = viaDefault ? pinned.ExecutablePath : null,
                    // The launcher's own scan has to be told about the user's path, exactly as the
                    // Java page's saved list does.
                    CustomJavaPaths = userPath is null ? [] : [userPath],
                    // A named world turns the launch into a quick play into it.
                    QuickPlayWorld = world,
                    JoinLastServer = joinServer is { Length: > 0 },
                },
                progress: null,
                cancellationToken)
            .ConfigureAwait(false);

        Console.WriteLine($"Pinned via:       {(viaDefault ? "launcher default" : "instance")}");
        Console.WriteLine($"Product launch started: {result.Started}");
        if (result.Compatibility is { } compatibility)
        {
            Console.WriteLine($"Compatibility: {compatibility.Message}");
        }

        foreach (var issue in result.Issues)
        {
            Console.WriteLine($"  preflight {(issue.IsBlocking ? "BLOCK" : "warn")}: {issue.Message}");
        }

        if (!result.Started || result.Process is null)
        {
            Console.WriteLine($"Launch did not start: {result.Error ?? "unknown reason"}");
            return 3;
        }

        var process = result.Process;
        Console.WriteLine($"Started pid {process.ProcessId}; log: {process.LogFilePath}");
        Console.WriteLine($"Process image: {process.ExecutablePath ?? "unavailable"}");

        // The command the product built, with credentials redacted. The instance's memory setting and
        // its custom JVM argument have to be in it, and the launch token must not be.
        var settingsReachedTheCommand = true;
        if (result.CommandPreview is { } preview)
        {
            Console.WriteLine("Command (credentials redacted):");
            Console.WriteLine("  " + preview);
        var memoryOk = preview.Contains("-Xmx3072M", StringComparison.Ordinal);
            var argumentOk = preview.Contains("-Dferrite.verify.marker=1", StringComparison.Ordinal);
            var redactedOk = !preview.Contains("verify-pipeline-token", StringComparison.Ordinal);
            if (world is { Length: > 0 })
            {
                Console.WriteLine(
                    $"  quickPlaySingleplayer in command: "
                    + $"{preview.Contains("--quickPlaySingleplayer", StringComparison.Ordinal)}");
            }
            settingsReachedTheCommand = memoryOk && argumentOk && redactedOk;
            Console.WriteLine(
                $"  instance memory in command: {memoryOk}; custom JVM argument in command: {argumentOk}; "
                + $"launch token redacted: {redactedOk}");
        }

        await Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken).ConfigureAwait(false);

        var log = await ReadLogAsync(process.LogFilePath, cancellationToken).ConfigureAwait(false);
        var reachedRenderer = log.Contains("Setting user:", StringComparison.Ordinal)
            && log.Contains("LWJGL", StringComparison.Ordinal);
        Console.WriteLine(reachedRenderer
            ? "The game's own log shows it reached the renderer (Setting user / LWJGL)."
            : "The game's own log did not show a renderer start.");

        if (!process.HasExited)
        {
            await process.StopAsync(TimeSpan.FromSeconds(20), cancellationToken).ConfigureAwait(false);
        }

        // The process image is the runtime the JVM was actually started from. A path reached through
        // a directory junction resolves to the real directory, so both are compared resolved.
        var running = process.ExecutablePath;
        var matches = running is not null
            && string.Equals(
                Path.GetFileName(running),
                Path.GetFileName(pinned.ExecutablePath),
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                ResolveDirectory(running),
                ResolveDirectory(pinned.ExecutablePath),
                StringComparison.OrdinalIgnoreCase);
        Console.WriteLine(matches
            ? $"PASS: the game ran on the pinned runtime ({pinned.DisplayName})."
            : $"FAIL: the game ran on '{running ?? "unknown"}' rather than the pinned '{pinned.ExecutablePath}'.");

        if (!reachedRenderer)
        {
            return 4;
        }

        if (!settingsReachedTheCommand)
        {
            Console.WriteLine("FAIL: the instance's own launch settings did not reach the command.");
            return 5;
        }

        return matches ? 0 : 6;
    }

    /// <summary>
    /// The real directory behind a path. Windows reports the resolved image of a process, so a runtime
    /// reached through a junction only compares equal once every level of the path is resolved.
    /// </summary>
    private static string ResolveDirectory(string executablePath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(executablePath));
        if (directory is null)
        {
            return executablePath;
        }

        var root = Path.GetPathRoot(directory) ?? string.Empty;
        var current = root;
        foreach (var part in directory[root.Length..]
                     .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            try
            {
                if (Directory.ResolveLinkTarget(current, returnFinalTarget: true)?.FullName is { } target)
                {
                    current = target;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // An unreadable level leaves the path as it is.
            }
        }

        return current;
    }

    private static JavaRuntime? FindPin(IReadOnlyList<JavaRuntime> compatible, string? hint)
    {
        if (string.IsNullOrWhiteSpace(hint))
        {
            return null;
        }

        return compatible.FirstOrDefault(runtime =>
            runtime.ExecutablePath.Contains(hint, StringComparison.OrdinalIgnoreCase)
            || runtime.DisplayName.Contains(hint, StringComparison.OrdinalIgnoreCase)
            || string.Equals(runtime.ShortVersion, hint, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<string> ReadLogAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return string.Empty;
        }

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }
}
