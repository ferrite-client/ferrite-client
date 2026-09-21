using Microsoft.Extensions.Logging;
using Ferrite.Core.Minecraft;

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
                "provision-java" => await Scenarios.ProvisionJavaAsync(
                    services,
                    GetOption(args, "--minecraft"),
                    GetOption(args, "--component"),
                    cancellation.Token),
                "instance-launch" => await Scenarios.InstanceLaunchWithPinnedJavaAsync(
                    services,
                    versionId,
                    seconds,
                    GetOption(args, "--java"),
                    args.Contains("--default", StringComparer.OrdinalIgnoreCase),
                    GetOption(args, "--world"),
                    GetOption(args, "--join"),
                    GetOption(args, "--loader-version"),
                    GetOption(args, "--instance"),
                    cancellation.Token),
                "manifest" => await Scenarios.ManifestAsync(services, cancellation.Token),
                "install" => await Scenarios.InstallAsync(services, versionId, cancellation.Token),
                "verify" => await Scenarios.VerifyAsync(services, versionId, cancellation.Token),
                "repair" => await Scenarios.RepairAsync(services, versionId, cancellation.Token),
                "launch" => await Scenarios.LaunchAsync(
                    services,
                    versionId,
                    seconds,
                    GetOption(args, "--instance"),
                    cancellation.Token),
                "modrinth" => await Scenarios.ModrinthSearchAsync(services, versionId, cancellation.Token),
                "browse" => await Scenarios.BrowseAsync(
                    services,
                    GetOption(args, "--query") ?? "sodium",
                    GetOption(args, "--category"),
                    GetOption(args, "--minecraft"),
                    GetOption(args, "--loader"),
                    cancellation.Token),
                "curseforge" => await Scenarios.CurseForgeAsync(services, versionId, cancellation.Token),
                "ftb" => await Scenarios.FtbAsync(
                    services,
                    GetOption(args, "--term") ?? "direwolf",
                    args.Contains("--install", StringComparer.OrdinalIgnoreCase),
                    GetOption(args, "--pack"),
                    GetOption(args, "--version"),
                    args.Contains("--force", StringComparer.OrdinalIgnoreCase),
                    cancellation.Token),
                "content" => await Scenarios.InstallContentAsync(
                    services,
                    versionId,
                    GetOption(args, "--slug") ?? "sodium",
                    GetOption(args, "--version"),
                    ParseLoader(GetOption(args, "--loader")),
                    cancellation.Token),
                "updates" => await Scenarios.CheckContentUpdatesAsync(
                    services,
                    GetOption(args, "--instance"),
                    args.Contains("--apply", StringComparer.OrdinalIgnoreCase),
                    cancellation.Token),
                "toggle" => await Scenarios.ToggleModAsync(services, versionId, cancellation.Token),
                "modpack" => await Scenarios.InstallModpackAsync(
                    services,
                    GetOption(args, "--source") ?? versionId,
                    GetOption(args, "--slug"),
                    GetOption(args, "--update"),
                    GetOption(args, "--update-id"),
                    cancellation.Token),
                "export" => await Scenarios.ExportModpackAsync(services, versionId, cancellation.Token),
                "worlds" => await Scenarios.ListWorldsAsync(
                    services,
                    versionId,
                    GetOption(args, "--game-dir"),
                    cancellation.Token),
                "backup-world" => await Scenarios.BackupWorldAsync(
                    services,
                    versionId,
                    GetOption(args, "--game-dir"),
                    cancellation.Token),
                "ping" => await Scenarios.PingAsync(
                    services,
                    args.Skip(1).Where(argument => !argument.StartsWith("--", StringComparison.Ordinal)).ToList(),
                    cancellation.Token),
                "crash" => await Scenarios.AnalyzeCrashAsync(
                    services,
                    args.Length > 1 && !args[1].StartsWith("--", StringComparison.Ordinal) ? args[1] : null,
                    GetOption(args, "--game-dir"),
                    cancellation.Token),
                "diagnostics" => await Scenarios.ExportDiagnosticsAsync(
                    services,
                    GetOption(args, "--instance"),
                    GetOption(args, "--out"),
                    cancellation.Token),
                "cache" => await Scenarios.ContentCacheAsync(services, versionId, cancellation.Token),
                "lan" => await Scenarios.LanWorldsAsync(
                    services,
                    seconds,
                    GetOption(args, "--motd"),
                    int.TryParse(GetOption(args, "--port"), out var lanPort) ? lanPort : null,
                    cancellation.Token),
                "local-server" => await Scenarios.LocalServerAsync(
                    services,
                    versionId,
                    seconds,
                    GetOption(args, "--instance"),
                    GetOption(args, "--out"),
                    cancellation.Token),
                "packs" => await Scenarios.PackMetadataAsync(
                    services,
                    GetOption(args, "--game-dir"),
                    cancellation.Token),
                "optifine" => await Scenarios.OptiFineAsync(
                    services,
                    GetOption(args, "--jar"),
                    GetOption(args, "--game-dir"),
                    GetOption(args, "--version"),
                    cancellation.Token),
                "sign-update" => await Scenarios.SignUpdateAsync(
                    services,
                    GetOption(args, "--out"),
                    GetOption(args, "--package"),
                    GetOption(args, "--version"),
                    GetOption(args, "--private-key"),
                    GetOption(args, "--runtime"),
                    GetOption(args, "--kind"),
                    GetOption(args, "--notes"),
                    cancellation.Token),
                "update-check" => await Scenarios.UpdateCheckAsync(
                    services,
                    GetOption(args, "--feed"),
                    GetOption(args, "--key"),
                    GetOption(args, "--current"),
                    GetOption(args, "--install"),
                    args.Contains("--stage", StringComparer.OrdinalIgnoreCase),
                    cancellation.Token),
                "fabric" => await Scenarios.InstallLoaderAsync(
                    services,
                    LoaderKind.Fabric,
                    versionId,
                    GetOption(args, "--loader"),
                    cancellation.Token),
                "quilt" => await Scenarios.InstallLoaderAsync(
                    services,
                    LoaderKind.Quilt,
                    versionId,
                    GetOption(args, "--loader"),
                    cancellation.Token),
                "neoforge" => await Scenarios.InstallLoaderAsync(
                    services,
                    LoaderKind.NeoForge,
                    versionId,
                    GetOption(args, "--loader"),
                    cancellation.Token),
                "forge" => await Scenarios.InstallLoaderAsync(
                    services,
                    LoaderKind.Forge,
                    versionId,
                    GetOption(args, "--loader"),
                    cancellation.Token),
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
            var inner = exception.InnerException;
            var depth = 0;
            while (inner is not null && depth < 4)
            {
                Console.WriteLine($"  caused by {inner.GetType().Name}: {inner.Message}");
                inner = inner.InnerException;
                depth++;
            }

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
            ("launch", () => Scenarios.LaunchAsync(services, versionId, seconds, null, cancellationToken)),
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

    /// <summary>Parses a loader name from the command line; anything unknown means vanilla.</summary>
    private static LoaderKind ParseLoader(string? name) => name?.ToLowerInvariant() switch
    {
        "fabric" => LoaderKind.Fabric,
        "quilt" => LoaderKind.Quilt,
        "forge" => LoaderKind.Forge,
        "neoforge" => LoaderKind.NeoForge,
        "optifine" => LoaderKind.OptiFine,
        _ => LoaderKind.Vanilla,
    };

    private static int Fail(string message)
    {
        Console.WriteLine(message);
        Console.WriteLine(
            "Usage: Ferrite.Verify <java|manifest|install|verify|repair|launch|modrinth|all> [version] [--root path] [--seconds n]");
        return 64;
    }
}
