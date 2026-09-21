using System.Diagnostics;
using Ferrite.Core.Content;
using Ferrite.Core.Java;
using Ferrite.Core.Loaders;
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
internal static partial class Scenarios
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
        var launchVersionId = LaunchVersionId(instance);
        Console.WriteLine($"Launch version: {launchVersionId}");
        var plan = await BuildPlanAsync(services, launchVersionId, cancellationToken);

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
        string? instanceName,
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

        var launchVersionId = LaunchVersionId(instance);
        Console.WriteLine($"Launch version: {launchVersionId}");
        var plan = await BuildPlanAsync(services, launchVersionId, cancellationToken);
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
            NativesDirectory = services.Paths.InstanceNativesDirectory(instance.Id, launchVersionId),
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

    /// <summary>
    /// Installs a loader into a dedicated instance: loader metadata, the vanilla artefacts, and
    /// (for Forge/NeoForge) the official installer processor chain.
    /// </summary>
    public static async Task<int> InstallLoaderAsync(
        VerifyServices services,
        LoaderKind kind,
        string minecraftVersion,
        string? loaderVersion,
        CancellationToken cancellationToken)
    {
        var java = await SelectJavaForMinecraftAsync(services, minecraftVersion, cancellationToken);
        if (java is null)
        {
            Console.WriteLine("No compatible Java runtime is available for the loader installer.");
            return 2;
        }

        Console.WriteLine($"Java for installer: {java.DisplayName}");

        var resolvedVersion = loaderVersion;
        if (string.IsNullOrEmpty(resolvedVersion))
        {
            var available = kind switch
            {
                LoaderKind.Fabric => await services.Fabric.ListFabricLoadersAsync(minecraftVersion, cancellationToken),
                LoaderKind.Quilt => await services.Fabric.ListQuiltLoadersAsync(minecraftVersion, cancellationToken),
                LoaderKind.NeoForge => await services.Forge.ListNeoForgeAsync(minecraftVersion, cancellationToken),
                LoaderKind.Forge => await services.Forge.ListForgeAsync(minecraftVersion, cancellationToken),
                _ => [],
            };

            if (available.Count == 0)
            {
                Console.WriteLine($"No {kind.ToDisplayName()} versions are available for {minecraftVersion}.");
                return 2;
            }

            var stable = available.Where(version => version.Stable).ToList();
            resolvedVersion = (stable.Count > 0 ? stable[^1] : available[^1]).Version;
            Console.WriteLine($"{kind.ToDisplayName()}: {available.Count} version(s) available; using {resolvedVersion}");
        }

        var stopwatch = Stopwatch.StartNew();
        switch (kind)
        {
            case LoaderKind.Fabric:
            case LoaderKind.Quilt:
                await services.Fabric.InstallAsync(kind, minecraftVersion, resolvedVersion, cancellationToken);
                break;
            default:
                await services.Forge.InstallAsync(
                    kind,
                    minecraftVersion,
                    resolvedVersion,
                    java,
                    new Progress<InstallProgress>(ReportProgress),
                    cancellationToken);
                break;
        }

        stopwatch.Stop();
        Console.WriteLine($"Loader install finished in {stopwatch.Elapsed.TotalSeconds:F1}s");

        var instance = await GetOrCreateInstanceAsync(services, minecraftVersion, cancellationToken);
        instance.Loader = kind;
        instance.LoaderVersion = resolvedVersion;
        await services.Instances.SaveAsync(instance, cancellationToken);

        var versionId = new LoaderVersionInfo(kind, resolvedVersion, minecraftVersion, true, null).VersionId;
        Console.WriteLine($"Launchable version id: {versionId}");

        var install = await services.Installer.InstallAsync(
            instance.Id,
            versionId,
            RuleContext.ForHost(),
            new Progress<InstallProgress>(ReportProgress),
            cancellationToken);
        Console.WriteLine();
        Console.WriteLine(
            $"Instance install complete: {install.Manifest.FileCount} files, {ByteSize.Format(install.Manifest.TotalBytes)}");
        return 0;
    }

    public static async Task<int> ModrinthSearchAsync(
        VerifyServices services,
        string query,
        CancellationToken cancellationToken)
    {
        var result = await services.Modrinth.SearchAsync(
            new ContentSearchQuery(query, Limit: 5),
            cancellationToken);
        Console.WriteLine($"Total hits: {result.TotalHits}");
        foreach (var hit in result.Hits)
        {
            Console.WriteLine(
                $"  [{hit.ProjectType}] {hit.Title} ({hit.Slug}) - {hit.Downloads:N0} downloads");
            Console.WriteLine($"      {hit.Description}");
        }

        return 0;
    }

    /// <summary>
    /// Exercises the CurseForge client. Every call needs a key the user obtains from CurseForge, so
    /// without one this reports the blocked state rather than pretending to succeed.
    /// </summary>
    public static async Task<int> CurseForgeAsync(
        VerifyServices services,
        string query,
        CancellationToken cancellationToken)
    {
        if (!services.CurseForge.IsConfigured)
        {
            Console.WriteLine("CurseForge: no API key configured.");
            Console.WriteLine($"  {services.CurseForge.UnavailableReason}");
            Console.WriteLine($"  secret store: {services.Paths.SecretsFile}");
            Console.WriteLine("  live calls are BLOCKED EXTERNAL; see docs/HUMAN_ACTION_REQUIRED.md (H2).");
            return 0;
        }

        var search = await services.CurseForge
            .SearchAsync(new ContentSearchQuery(query, Limit: 5), cancellationToken)
            .ConfigureAwait(false);
        Console.WriteLine($"Total hits: {search.TotalHits}");
        foreach (var hit in search.Hits)
        {
            Console.WriteLine(
                $"  [{hit.ProjectType}] {hit.Title} ({hit.Slug}) - {hit.Downloads:N0} downloads");
        }

        var first = search.Hits.FirstOrDefault();
        if (first is null)
        {
            Console.WriteLine("Search returned no hits to inspect further.");
            return 3;
        }

        var project = await services.CurseForge
            .GetProjectAsync(first.ProjectId, cancellationToken)
            .ConfigureAwait(false);
        Console.WriteLine($"Project {first.ProjectId}: {project?.Title ?? "(not found)"}");

        var versions = await services.CurseForge
            .GetVersionsAsync(first.ProjectId, gameVersion: null, loader: null, cancellationToken)
            .ConfigureAwait(false);
        Console.WriteLine($"Files: {versions.Count}");
        foreach (var version in versions.Take(5))
        {
            var file = version.PrimaryFile;
            var distribution = file is null || string.IsNullOrEmpty(file.Url)
                ? "no download url (third-party distribution disabled)"
                : "download url present";
            Console.WriteLine($"  {version.VersionNumber} [{version.VersionType}] {distribution}");
        }

        return 0;
    }

    /// <summary>
    /// Searches Modrinth, resolves dependencies, installs into an instance, and re-reads the
    /// installed mod metadata from disk.
    /// </summary>
    public static async Task<int> InstallContentAsync(
        VerifyServices services,
        string minecraftVersion,
        string slug,
        string? versionId,
        LoaderKind loader,
        CancellationToken cancellationToken)
    {
        var instance = await GetOrCreateInstanceAsync(services, minecraftVersion, loader, cancellationToken);
        Console.WriteLine(
            $"Instance: {instance.Name} ({instance.Loader.ToDisplayName()} {instance.LoaderVersion ?? "vanilla"})");

        var plan = await services.Content.PlanAsync(
            instance,
            slug,
            versionId,
            includeOptionalDependencies: false,
            cancellationToken);

        Console.WriteLine($"Plan: {plan.Items.Count} file(s)");
        foreach (var item in plan.Items)
        {
            Console.WriteLine($"  {item.TargetFolder}/{item.FileName} ({ByteSize.Format(item.Size)})");
        }

        foreach (var warning in plan.Warnings)
        {
            Console.WriteLine($"  warn: {warning}");
        }

        if (plan.OptionalDependencies.Count > 0)
        {
            Console.WriteLine($"  optional dependencies offered: {plan.OptionalDependencies.Count}");
        }

        var gameDirectory = services.Paths.InstanceGameDirectory(instance.Id);
        var result = await services.Content.InstallAsync(
            instance,
            plan,
            gameDirectory,
            new Progress<InstallProgress>(ReportProgress),
            cancellationToken);

        Console.WriteLine();
        Console.WriteLine($"Installed {result.Installed} file(s)");
        foreach (var file in result.Files)
        {
            Console.WriteLine($"  {file}");
        }

        var mods = await services.Mods.ListModsAsync(gameDirectory, cancellationToken);
        Console.WriteLine($"Mod inventory now reports {mods.Count} mod(s):");
        foreach (var mod in mods)
        {
            Console.WriteLine(
                $"  {mod.DisplayName} [{mod.Loader}] id={mod.ModId} version={mod.Version} enabled={mod.Enabled}");
            if (mod.Dependencies.Count > 0)
            {
                Console.WriteLine($"      depends on: {string.Join(", ", mod.Dependencies)}");
            }
        }

        return result.Installed > 0 ? 0 : 3;
    }

    /// <summary>Disables and re-enables a mod to prove the round trip loses nothing.</summary>
    public static async Task<int> ToggleModAsync(
        VerifyServices services,
        string minecraftVersion,
        CancellationToken cancellationToken)
    {
        var instance = await GetOrCreateInstanceAsync(services, minecraftVersion, cancellationToken);
        var gameDirectory = services.Paths.InstanceGameDirectory(instance.Id);
        var mods = await services.Mods.ListModsAsync(gameDirectory, cancellationToken);
        var target = mods.FirstOrDefault(mod => mod.Enabled);
        if (target is null)
        {
            Console.WriteLine("No enabled mod to toggle. Run the content scenario first.");
            return 2;
        }

        Console.WriteLine($"Toggling {target.FileName}");
        var disabledPath = InstanceContentManager.SetEnabled(target.FilePath, enabled: false);
        Console.WriteLine($"  disabled -> {Path.GetFileName(disabledPath)}");

        var afterDisable = await services.Mods.ListModsAsync(gameDirectory, cancellationToken);
        var disabledEntry = afterDisable.First(mod => mod.FilePath == disabledPath);
        Console.WriteLine($"  metadata still readable while disabled: {disabledEntry.DisplayName}, enabled={disabledEntry.Enabled}");

        var enabledPath = InstanceContentManager.SetEnabled(disabledPath, enabled: true);
        Console.WriteLine($"  re-enabled -> {Path.GetFileName(enabledPath)}");
        var afterEnable = await services.Mods.ListModsAsync(gameDirectory, cancellationToken);
        var enabledEntry = afterEnable.First(mod => mod.FilePath == enabledPath);
        Console.WriteLine($"  metadata after re-enable: {enabledEntry.DisplayName}, enabled={enabledEntry.Enabled}");

        return enabledEntry.Enabled && enabledPath == target.FilePath ? 0 : 3;
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
        return JavaSelection.SelectBest(runtimes, required);
    }

    private static async Task<JavaRuntime?> SelectJavaForMinecraftAsync(
        VerifyServices services,
        string minecraftVersion,
        CancellationToken cancellationToken)
    {
        var required = JavaCompatibility.RequiredMajorFor(minecraftVersion);
        var runtimes = await services.Java.DetectAsync(cancellationToken);
        return JavaSelection.SelectBest(runtimes, required);
    }

    /// <summary>The version id an instance actually launches: the loader version, or vanilla.</summary>
    private static string LaunchVersionId(InstanceRecord instance) =>
        instance.Loader == LoaderKind.Vanilla || string.IsNullOrEmpty(instance.LoaderVersion)
            ? instance.MinecraftVersion
            : new LoaderVersionInfo(
                instance.Loader,
                instance.LoaderVersion,
                instance.MinecraftVersion,
                true,
                null).VersionId;

    /// <summary>Vanilla instance, used by the scenarios whose subject is not the loader.</summary>
    private static Task<InstanceRecord> GetOrCreateInstanceAsync(
        VerifyServices services,
        string versionId,
        CancellationToken cancellationToken) =>
        GetOrCreateInstanceAsync(services, versionId, LoaderKind.Vanilla, cancellationToken);

    private static async Task<InstanceRecord> GetOrCreateInstanceAsync(
        VerifyServices services,
        string versionId,
        LoaderKind loader,
        CancellationToken cancellationToken)
    {
        var name = loader == LoaderKind.Vanilla ? $"verify-{versionId}" : $"verify-{versionId}-{loader}";
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
                Loader = loader,
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
