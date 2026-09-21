using Ferrite.Core.Content;
using Ferrite.Core.Diagnostics;
using Ferrite.Core.Util;

namespace Ferrite.Verify;

/// <summary>Crash report analysis and the support bundle.</summary>
internal static partial class Scenarios
{
    /// <summary>
    /// Parses a real crash report and reports what it says and which installed mods its stack trace
    /// touches. Defaults to the newest report in the local Minecraft installation.
    /// </summary>
    public static async Task<int> AnalyzeCrashAsync(
        VerifyServices services,
        string? reportPath,
        string? gameDirectory,
        CancellationToken cancellationToken)
    {
        var directory = gameDirectory
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                ".minecraft",
                "crash-reports");

        var path = reportPath is { Length: > 0 } && File.Exists(reportPath)
            ? reportPath
            : Directory.Exists(directory)
                ? Directory.EnumerateFiles(directory, "*.txt")
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault()
                : null;

        if (path is null)
        {
            Console.WriteLine($"No crash report found in {directory}.");
            return 2;
        }

        var text = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        if (!CrashReportParser.LooksLikeCrashReport(text))
        {
            Console.WriteLine($"{path} is not a Minecraft crash report.");
            return 3;
        }

        var report = CrashReportParser.Parse(path, text);
        Console.WriteLine($"Report: {path}");
        Console.WriteLine($"  parsed: {report.Frames.Count} frame(s), {report.ListedMods.Count} listed mod(s)");

        var mods = gameDirectory is null
            ? await FindInstalledModsAsync(services, cancellationToken).ConfigureAwait(false)
            : await services.Mods.ListModsAsync(gameDirectory, cancellationToken).ConfigureAwait(false);

        Console.WriteLine($"Attributing against {mods.Count} installed mod(s)");
        Console.WriteLine();
        Console.Write(CrashAnalysisText.Render(CrashAnalyzer.Analyze(report, mods)));
        return 0;
    }

    /// <summary>Builds a support bundle and lists what went into it.</summary>
    public static async Task<int> ExportDiagnosticsAsync(
        VerifyServices services,
        string? instanceName,
        string? outputPath,
        CancellationToken cancellationToken)
    {
        var instances = await services.Instances.LoadAllAsync(cancellationToken).ConfigureAwait(false);
        var instance = instanceName is { Length: > 0 }
            ? instances.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, instanceName, StringComparison.OrdinalIgnoreCase))
            : instances.OrderByDescending(candidate => candidate.CreatedAt).FirstOrDefault();

        if (instanceName is { Length: > 0 } && instance is null)
        {
            Console.WriteLine($"No instance named '{instanceName}'.");
            return 2;
        }

        var target = outputPath ?? Path.Combine(
            services.Paths.TemporaryDirectory,
            $"ferrite-diagnostics-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.zip");

        var result = await services.Diagnostics
            .ExportAsync(
                new DiagnosticsBundleRequest
                {
                    OutputPath = target,
                    Instance = instance,
                    Notes = "Ferrite.Verify diagnostics scenario",
                },
                cancellationToken)
            .ConfigureAwait(false);

        Console.WriteLine($"Bundle: {result.Path}");
        Console.WriteLine($"  instance: {instance?.Name ?? "(launcher-wide)"}");
        Console.WriteLine($"  size:     {ByteSize.Format(result.Bytes)}");
        Console.WriteLine($"  entries:  {result.FileCount}");
        foreach (var entry in result.Entries)
        {
            Console.WriteLine($"    {entry}");
        }

        return result.FileCount > 0 ? 0 : 3;
    }

    /// <summary>Mods of the newest instance, used when no game directory is supplied.</summary>
    private static async Task<IReadOnlyList<ModMetadata>> FindInstalledModsAsync(
        VerifyServices services,
        CancellationToken cancellationToken)
    {
        var instances = await services.Instances.LoadAllAsync(cancellationToken).ConfigureAwait(false);
        var instance = instances.OrderByDescending(candidate => candidate.CreatedAt).FirstOrDefault();
        if (instance is null)
        {
            return [];
        }

        var gameDirectory = services.Paths.InstanceGameDirectory(instance.Id);
        Console.WriteLine($"Using mods from instance {instance.Name}");
        return await services.Mods.ListModsAsync(gameDirectory, cancellationToken).ConfigureAwait(false);
    }
}
