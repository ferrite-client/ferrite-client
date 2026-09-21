using Ferrite.Core.Java;
using Ferrite.Core.Minecraft;

namespace Ferrite.Verify;

/// <summary>Preparing, running, stopping, and exporting a local server.</summary>
internal static partial class Scenarios
{
    /// <summary>
    /// Downloads the version's own server jar, starts it on a spare port with the EULA accepted, waits
    /// for it to report that it is ready, stops it, and exports it. The server's own readiness line is
    /// the evidence that this is a real server rather than a prepared folder.
    /// </summary>
    public static async Task<int> LocalServerAsync(
        VerifyServices services,
        string versionId,
        int seconds,
        string? instanceName,
        string? outPath,
        CancellationToken cancellationToken)
    {
        var instance = instanceName is { Length: > 0 }
            ? (await services.Instances.LoadAllAsync(cancellationToken).ConfigureAwait(false))
                .FirstOrDefault(record => string.Equals(record.Name, instanceName, StringComparison.OrdinalIgnoreCase))
            : await GetOrCreateInstanceAsync(services, versionId, cancellationToken).ConfigureAwait(false);

        if (instance is null)
        {
            Console.WriteLine($"No instance named '{instanceName}'.");
            return 2;
        }

        Console.WriteLine($"Instance: {instance.Name} (Minecraft {instance.MinecraftVersion})");
        var document = await services.Resolver
            .ResolveAsync(instance.MinecraftVersion, cancellationToken)
            .ConfigureAwait(false);

        var server = document.Downloads?.Server;
        Console.WriteLine($"Version {document.Id} publishes a server jar: {server?.Url is { Length: > 0 }}");

        var options = new LocalServerOptions
        {
            LevelName = "ferrite-verify",
            Port = 25599,
            MaxPlayers = 4,
            OnlineMode = false,
            Motd = "Ferrite verification server",
            MemoryMb = 2048,
            AcceptEula = true,
        };

        var layout = await services.LocalServers
            .PrepareAsync(
                instance.Id,
                document,
                options,
                new Progress<InstallProgress>(ReportProgress),
                cancellationToken)
            .ConfigureAwait(false);
        Console.WriteLine();
        Console.WriteLine($"Server directory: {layout.Directory}");
        Console.WriteLine($"Server jar: {layout.JarPath} ({new FileInfo(layout.JarPath).Length:N0} bytes)");

        var runtimes = await services.Java.DetectAsync(cancellationToken).ConfigureAwait(false);
        var required = JavaCompatibility.RequiredMajorFor(document, instance.MinecraftVersion);
        var java = JavaSelection.SelectBest(runtimes, required);
        if (java is null)
        {
            Console.WriteLine("No compatible Java runtime is available.");
            return 2;
        }

        var command = LocalServerService.BuildCommand(layout, java, options, instance.JvmArguments);
        Console.WriteLine($"Java: {java.DisplayName} ({java.ExecutablePath})");
        Console.WriteLine($"Command: {command.ToDisplayString()}");
        Console.WriteLine();

        var lines = new List<string>();
        var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnLine(string line)
        {
            lock (lines)
            {
                lines.Add(line);
            }

            if (line.Contains("Done (", StringComparison.Ordinal)
                || line.Contains("For help, type", StringComparison.Ordinal))
            {
                ready.TrySetResult(true);
            }

            if (line.Contains("You need to agree to the EULA", StringComparison.OrdinalIgnoreCase)
                || line.Contains("FAILED TO BIND", StringComparison.OrdinalIgnoreCase))
            {
                ready.TrySetResult(false);
            }
        }

        var process = await services.Launcher
            .StartAsync(instance.Id, command, cancellationToken)
            .ConfigureAwait(false);
        process.OutputReceived += OnLine;

        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
            try
            {
                if (!await ready.Task.WaitAsync(timeout.Token).ConfigureAwait(false))
                {
                    Console.WriteLine("The server reported that it could not start.");
                    await StopAsync(services, instance.Id).ConfigureAwait(false);
                    PrintTail(lines);
                    return 3;
                }

                Console.WriteLine("The server reported that it is ready.");
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine($"The server did not become ready within {seconds}s.");
                await StopAsync(services, instance.Id).ConfigureAwait(false);
                PrintTail(lines);
                return 3;
            }
        }

        var worldDirectory = Path.Combine(layout.Directory, options.LevelName);
        Console.WriteLine($"The server created its world folder: {Directory.Exists(worldDirectory)}");

        await StopAsync(services, instance.Id).ConfigureAwait(false);
        Console.WriteLine("The server was stopped.");
        PrintTail(lines);

        if (outPath is { Length: > 0 })
        {
            var produced = await services.LocalServers
                .ExportAsync(layout, outPath, cancellationToken)
                .ConfigureAwait(false);
            Console.WriteLine($"Exported to {produced} ({new FileInfo(produced).Length:N0} bytes)");
        }

        Console.WriteLine("PASS: a real Minecraft server started from this instance's own version.");
        return 0;
    }

    private static Task StopAsync(VerifyServices services, Guid instanceId) => services.Launcher
        .StopAsync(instanceId, TimeSpan.FromSeconds(25), CancellationToken.None);

    private static void PrintTail(List<string> lines, int count = 12)
    {
        string[] tail;
        lock (lines)
        {
            tail = lines.TakeLast(count).ToArray();
        }

        Console.WriteLine();
        Console.WriteLine("--- server output (tail) ---");
        foreach (var line in tail)
        {
            Console.WriteLine($"  {line}");
        }
    }
}
