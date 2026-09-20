using Microsoft.Extensions.Logging;

namespace Ferrite.Verify;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var command = args.Length > 0 ? args[0].ToLowerInvariant() : "all";
        var versionId = args.Length > 1 && !args[1].StartsWith("--", StringComparison.Ordinal) ? args[1] : "26.3";
        var dataRoot = GetOption(args, "--root");
        var seconds = int.TryParse(GetOption(args, "--seconds"), out var parsedSeconds) ? parsedSeconds : 45;

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        using var services = new VerifyServices(dataRoot, LogLevel.Information);
        Console.WriteLine("Ferrite verification harness");
        Console.WriteLine($"  data root: {services.Paths.Root}");
        Console.WriteLine($"  command:   {command} {versionId}");
        Console.WriteLine();

        try
        {
            return command switch
            {
                "java" => await Scenarios.ListJavaAsync(services, cancellation.Token),
                "manifest" => await Scenarios.ManifestAsync(services, cancellation.Token),
                "install" => await Scenarios.InstallAsync(services, versionId, cancellation.Token),
                "verify" => await Scenarios.VerifyAsync(services, versionId, cancellation.Token),
                "repair" => await Scenarios.RepairAsync(services, versionId, cancellation.Token),
                "launch" => await Scenarios.LaunchAsync(services, versionId, seconds, cancellation.Token),
                "modrinth" => await Scenarios.ModrinthSearchAsync(services, versionId, cancellation.Token),
                "all" => await RunAllAsync(services, versionId, seconds, cancellation.Token),
                _ => Fail($"Unknown command '{command}'."),
            };
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Cancelled.");
            return 130;
        }
        catch (Exception exception)
        {
            Console.WriteLine($"FAILED: {exception.GetType().Name}: {exception.Message}");
            return 1;
        }
    }

    private static async Task<int> RunAllAsync(
        VerifyServices services,
        string versionId,
        int seconds,
        CancellationToken cancellationToken)
    {
        var steps = new (string Name, Func<Task<int>> Run)[]
        {
            ("java discovery", () => Scenarios.ListJavaAsync(services, cancellationToken)),
            ("version manifest", () => Scenarios.ManifestAsync(services, cancellationToken)),
            ("install", () => Scenarios.InstallAsync(services, versionId, cancellationToken)),
            ("verify", () => Scenarios.VerifyAsync(services, versionId, cancellationToken)),
            ("repair", () => Scenarios.RepairAsync(services, versionId, cancellationToken)),
            ("launch", () => Scenarios.LaunchAsync(services, versionId, seconds, cancellationToken)),
        };

        var failures = 0;
        foreach (var (name, run) in steps)
        {
            Console.WriteLine($"=== {name} ===");
            var code = await run();
            Console.WriteLine($"=== {name}: exit {code} ===");
            Console.WriteLine();
            if (code != 0)
            {
                failures++;
            }
        }

        Console.WriteLine(failures == 0 ? "All scenarios passed." : $"{failures} scenario(s) failed.");
        return failures == 0 ? 0 : 1;
    }

    private static string? GetOption(string[] args, string name)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }

    private static int Fail(string message)
    {
        Console.WriteLine(message);
        Console.WriteLine(
            "Usage: Ferrite.Verify <java|manifest|install|verify|repair|launch|modrinth|all> [version] [--root path] [--seconds n]");
        return 64;
    }
}
