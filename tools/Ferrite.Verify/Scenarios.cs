using System.Diagnostics;
using Ferrite.Core.Java;
using Ferrite.Core.Minecraft;
using Ferrite.Core.Platform;
using Ferrite.Core.Rules;
using Ferrite.Core.Storage;
using Ferrite.Core.Util;

namespace Ferrite.Verify;

/// <summary>
/// End-to-end scenarios run against the live Mojang services. Each returns a process exit code so
/// the harness can be scripted.
/// </summary>
internal static class Scenarios
{
    public static async Task<int> ListJavaAsync(VerifyServices services, CancellationToken cancellationToken)
    {
        var runtimes = await services.Java.DetectAsync(cancellationToken);
        Console.WriteLine($"Discovered {runtimes.Count} Java runtime(s):");
        foreach (var runtime in runtimes)
        {
            Console.WriteLine(
                $"  Java {runtime.ShortVersion,-4} {runtime.DisplayName,-34} {runtime.Source,-14} {runtime.ExecutablePath}");
        }

        return runtimes.Count > 0 ? 0 : 2;
    }

    public static async Task<int> ManifestAsync(VerifyServices services, CancellationToken cancellationToken)
    {
        var manifest = await services.Manifest.GetManifestAsync(forceRefresh: true, cancellationToken);
        Console.WriteLine($"Manifest: {manifest.Versions.Count} versions");
        Console.WriteLine($"Latest release: {manifest.Latest?.Release}, latest snapshot: {manifest.Latest?.Snapshot}");
        Console.WriteLine($"Cache used: {services.Manifest.LastFetchUsedCache}");
        Console.WriteLine($"Cache file: {services.Manifest.ManifestCachePath}");

        var counts = manifest.Versions
            .GroupBy(entry => entry.Type)
            .Select(group => $"{group.Key}={group.Count()}");
        Console.WriteLine("By type: " + string.Join(", ", counts));
        return 0;
    }

    public static async Task<int> InstallAsync(
        VerifyServices services,
        string versionId,
        CancellationToken cancellationToken)
    {
        var instance = await GetOrCreateInstanceAsync(services, versionId, cancellationToken);
        Console.WriteLine($"Instance: {instance.Name} ({instance.Id})");
        Console.WriteLine($"Game directory: {services.Paths.InstanceGameDirectory(instance.Id)}");

        var stopwatch = Stopwatch.StartNew();
        var result = await services.Installer.InstallAsync(
            instance.Id,
            versionId,
            RuleContext.ForHost(),
            new Progress<InstallProgress>(ReportProgress),
            cancellationToken);
        stopwatch.Stop();

        Console.WriteLine();
        Console.WriteLine($"Install complete in {stopwatch.Elapsed.TotalSeconds:F1}s");
        Console.WriteLine($"  files:     {result.Manifest.FileCount}");
        Console.WriteLine($"  bytes:     {ByteSize.Format(result.Manifest.TotalBytes)}");
        Console.WriteLine($"  natives:   {result.Manifest.NativesDirectory}");
        Console.WriteLine($"  log4j cfg: {result.Manifest.LoggingConfigPath}");
        return 0;
    }

    public static async Task<int> VerifyAsync(
        VerifyServices services,
        string versionId,
        CancellationToken cancellationToken)
    {
        var instance = await GetOrCreateInstanceAsync(services, versionId, cancellationToken);
        var stopwatch = Stopwatch.StartNew();
        var report = await services.Installer.VerifyAsync(
            instance.Id,
            versionId,
            RuleContext.ForHost(),
            progress: null,
            cancellationToken);
        stopwatch.Stop();

        Console.WriteLine(
            $"Verified {report.FilesChecked} files ({ByteSize.Format(report.TotalBytes)}) in {stopwatch.Elapsed.TotalSeconds:F1}s");
        Console.WriteLine($"Healthy: {report.IsHealthy}");
        foreach (var issue in report.Issues.Take(20))
        {
            Console.WriteLine($"  {issue.Reason}: {issue.Path}");
        }

        return report.IsHealthy ? 0 : 3;
    }

    public static async Task<int> RepairAsync(
        VerifyServices services,
        string versionId,
        CancellationToken cancellationToken)
    {
        var instance = await GetOrCreateInstanceAsync(services, versionId, cancellationToken);
        var plan = await BuildPlanAsync(services, versionId, cancellationToken);

        var victim = plan.Libraries.FirstOrDefault(library => !library.IsNative)?.TargetPath;
        if (victim is null || !File.Exists(victim))
        {
            Console.WriteLine("No installed library to damage. Run install first.");
            return 2;
        }

        var originalLength = new FileInfo(victim).Length;
        await File.WriteAllBytesAsync(victim, new byte[Math.Min(64, originalLength)], cancellationToken);
        Console.WriteLine($"Damaged {victim} (was {ByteSize.Format(originalLength)})");

        var before = await services.Installer.VerifyAsync(
            instance.Id,
            versionId,
            RuleContext.ForHost(),
            progress: null,
            cancellationToken);
        Console.WriteLine($"Detection: {before.Issues.Count} issue(s) found");
        if (before.IsHealthy)
        {
            Console.WriteLine("Detection failed: damage was not reported.");
            return 3;
        }

        var after = await services.Installer.RepairAsync(
            instance.Id,
            versionId,
            RuleContext.ForHost(),
            new Progress<InstallProgress>(ReportProgress),
            cancellationToken);
        Console.WriteLine();
        Console.WriteLine($"Repair result: healthy={after.IsHealthy}, remaining issues={after.Issues.Count}");
        return after.IsHealthy ? 0 : 3;
    }

    public static async Task<int> LaunchAsync(
        VerifyServices services,
        string versionId,
        int seconds,
        CancellationToken cancellationToken)
    {
        var instance = await GetOrCreateInstanceAsync(services, versionId, cancellationToken);
        var plan = await BuildPlanAsync(services, versionId, cancellationToken);
        var java = await SelectJavaAsync(services, plan, cancellationToken);
        if (java is null)
        {
            Console.WriteLine("No compatible Java runtime is available.");
            return 2;
        }

        Console.WriteLine($"Java: {java.DisplayName} ({java.ExecutablePath})");

        var request = new LaunchRequest
        {
            Document = plan.Document,
            Plan = plan,
            Instance = instance,
            Account = SyntheticAccount(),
            Java = java,
            GameDirectory = services.Paths.InstanceGameDirectory(instance.Id),
            NativesDirectory = services.Paths.InstanceNativesDirectory(instance.Id, versionId),
            AssetsRoot = services.Paths.AssetsDirectory,
            LibrariesDirectory = services.Paths.LibrariesDirectory,
            LegacyAssetsDirectory = services.Paths.LegacyVirtualAssetsDirectory,
            LauncherVersion = "0.1.0-verify",
            DefaultMemoryMb = LaunchPreflight.SuggestDefaultMemoryMb(),
        };

        var command = new LaunchCommandBuilder().Build(request);
        Console.WriteLine("Command (credentials redacted):");
        Console.WriteLine("  " + command.ToDisplayString());

        var issues = LaunchPreflight.Check(request, command);
        foreach (var issue in issues)
        {
            Console.WriteLine($"  preflight {(issue.IsBlocking ? "BLOCK" : "warn")}: {issue.Message}");
        }

        if (issues.Any(issue => issue.IsBlocking))
        {
            Console.WriteLine("Blocking preflight issues prevent launch.");
            return 3;
        }

        Console.WriteLine();
        Console.WriteLine($"Starting the game for up to {seconds}s ...");
        var process = await services.Launcher.StartAsync(instance.Id, command, cancellationToken);
        Console.WriteLine($"Started pid {process.ProcessId}; log: {process.LogFilePath}");

        var sawLwjgl = false;
        var sawGraphics = false;
        process.OutputReceived += line =>
        {
            if (line.Contains("LWJGL", StringComparison.OrdinalIgnoreCase)
                || line.Contains("Setting user", StringComparison.OrdinalIgnoreCase))
            {
                sawLwjgl = true;
            }

            if (line.Contains("OpenGL", StringComparison.OrdinalIgnoreCase)
                || line.Contains("Backend library", StringComparison.OrdinalIgnoreCase)
                || line.Contains("Created:", StringComparison.OrdinalIgnoreCase))
            {
                sawGraphics = true;
            }

            Console.WriteLine("  | " + line);
        };

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(seconds));
        var exitedEarly = false;
        try
        {
            var exitCode = await process.WaitForExitAsync(deadline.Token);
            Console.WriteLine($"Game exited early with code {exitCode}");
            exitedEarly = true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Console.WriteLine("Game is still running; stopping it now.");
            await services.Launcher.StopAsync(instance.Id, TimeSpan.FromSeconds(10), cancellationToken);
        }

        Console.WriteLine($"Signals: lwjgl={sawLwjgl}, graphics={sawGraphics}, exitedEarly={exitedEarly}");
        Console.WriteLine($"Captured {process.RecentOutput.Count} output line(s); log: {process.LogFilePath}");
        return exitedEarly ? 4 : 0;
    }

    public static async Task<int> ModrinthSearchAsync(
        VerifyServices services,
        string query,
        CancellationToken cancellationToken)
    {
        var url = $"https://api.modrinth.com/v2/search?limit=3&query={Uri.EscapeDataString(query)}";
        var json = await services.Http.GetStringAsync(url, 4 * 1024 * 1024, cancellationToken);
        Console.WriteLine(json.Length > 1200 ? json[..1200] + "..." : json);
        return 0;
    }

    private static void ReportProgress(InstallProgress progress)
    {
        var download = progress.Download;
        var percent = download is null ? 0 : download.Fraction * 100;
        var rate = download is null ? string.Empty : ByteSize.FormatRate(download.BytesPerSecond);
        Console.WriteLine(
            $"  [{progress.Stage}] {percent,5:F1}%  {rate,-12} {progress.Message ?? string.Empty}".TrimEnd());
    }

    private static async Task<InstallPlan> BuildPlanAsync(
        VerifyServices services,
        string versionId,
        CancellationToken cancellationToken)
    {
        var document = await services.Resolver.ResolveAsync(versionId, cancellationToken);
        return await services.Planner.CreateAsync(document, versionId, RuleContext.ForHost(), cancellationToken);
    }

    private static async Task<JavaRuntime?> SelectJavaAsync(
        VerifyServices services,
        InstallPlan plan,
        CancellationToken cancellationToken)
    {
        var required = JavaCompatibility.RequiredMajorFor(plan.Document, plan.VersionId);
        var runtimes = await services.Java.DetectAsync(cancellationToken);
        return runtimes.FirstOrDefault(runtime => JavaCompatibility.Evaluate(runtime, required).IsCompatible);
    }

    private static async Task<InstanceRecord> GetOrCreateInstanceAsync(
        VerifyServices services,
        string versionId,
        CancellationToken cancellationToken)
    {
        var name = $"verify-{versionId}";
        var existing = (await services.Instances.LoadAllAsync(cancellationToken))
            .FirstOrDefault(record => string.Equals(record.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return existing;
        }

        return await services.Instances.CreateAsync(
            new InstanceRecord
            {
                Id = Guid.NewGuid(),
                Name = name,
                MinecraftVersion = versionId,
                Loader = LoaderKind.Vanilla,
            },
            cancellationToken);
    }

    /// <summary>
    /// A placeholder identity used only to prove the launch pipeline starts a real JVM. It is not an
    /// account type in the product: Ferrite has no offline account mode.
    /// </summary>
    private static LaunchAccount SyntheticAccount() => new()
    {
        PlayerName = "FerriteVerify",
        Uuid = "00000000000000000000000000000001",
        AccessToken = "verify-pipeline-token",
        Xuid = "0",
        ClientId = "0",
    };
}
