using System.Text.Json;
using Ferrite.Core.Download;
using Ferrite.Core.Json;
using Ferrite.Core.Platform;
using Ferrite.Core.Rules;
using Ferrite.Core.Util;
using Microsoft.Extensions.Logging;

namespace Ferrite.Core.Minecraft;

/// <summary>
/// Materialises a version into an instance: downloads every artefact, extracts natives, mirrors
/// legacy virtual assets, and records an install manifest that repair can work from.
/// </summary>
public sealed class MinecraftInstaller
{
    private static readonly string[] NativeSubdirectories = ["java", "jna", "lwjgl", "netty"];

    private readonly DownloadEngine _downloads;
    private readonly InstallPlanner _planner;
    private readonly VersionResolver _resolver;
    private readonly AppPaths _paths;
    private readonly ILogger<MinecraftInstaller> _logger;

    public MinecraftInstaller(
        DownloadEngine downloads,
        InstallPlanner planner,
        VersionResolver resolver,
        AppPaths paths,
        ILogger<MinecraftInstaller> logger)
    {
        _downloads = downloads;
        _planner = planner;
        _resolver = resolver;
        _paths = paths;
        _logger = logger;
    }

    public string ManifestPath(Guid instanceId) =>
        Path.Combine(_paths.InstanceDirectory(instanceId), "installation.json");

    public async Task<InstallResult> InstallAsync(
        Guid instanceId,
        string versionId,
        RuleContext context,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(versionId);

        progress?.Report(new InstallProgress { Stage = InstallStage.ResolvingMetadata });
        var document = await _resolver.ResolveAsync(versionId, cancellationToken).ConfigureAwait(false);

        progress?.Report(new InstallProgress { Stage = InstallStage.Planning, Message = versionId });
        var plan = await _planner.CreateAsync(document, versionId, context, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation(
            "Installing {Version}: {Files} files, {Bytes} bytes",
            versionId,
            plan.FileCount,
            plan.TotalBytes);

        var summary = await _downloads
            .DownloadAsync(plan.Downloads, DownloadProgressAdapter.Create(progress), cancellationToken)
            .ConfigureAwait(false);

        if (!summary.Success)
        {
            throw new InstallFailedException(
                $"Installation of {versionId} failed for {summary.Failures.Count} file(s).",
                summary.Failures);
        }

        progress?.Report(new InstallProgress { Stage = InstallStage.ExtractingNatives });
        var nativesDirectory = await ExtractNativesAsync(instanceId, versionId, plan, cancellationToken)
            .ConfigureAwait(false);

        if (plan.VirtualAssets)
        {
            await MaterializeVirtualAssetsAsync(plan, cancellationToken).ConfigureAwait(false);
        }

        progress?.Report(new InstallProgress { Stage = InstallStage.Finalising });
        var manifest = new InstallManifest
        {
            BaseVersionId = document.InheritsFrom ?? versionId,
            ResolvedVersionId = versionId,
            InstalledAt = DateTimeOffset.UtcNow,
            FileCount = plan.FileCount,
            TotalBytes = plan.TotalBytes,
            NativesDirectory = nativesDirectory,
            LoggingConfigPath = plan.LoggingConfigPath,
        };

        await WriteManifestAsync(instanceId, manifest, cancellationToken).ConfigureAwait(false);
        return new InstallResult(manifest, summary, plan.FileCount);
    }

    public async Task<InstallManifest?> TryReadManifestAsync(Guid instanceId, CancellationToken cancellationToken)
    {
        var path = ManifestPath(instanceId);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var bytes = await AtomicFile.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<InstallManifest>(bytes, JsonDefaults.Document);
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            _logger.LogWarning(exception, "Install manifest for {Instance} could not be read", instanceId);
            return null;
        }
    }

    public Task WriteManifestAsync(Guid instanceId, InstallManifest manifest, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(manifest, JsonDefaults.Document);
        return AtomicFile.WriteAllTextAsync(ManifestPath(instanceId), json, cancellationToken);
    }

    private async Task<string> ExtractNativesAsync(
        Guid instanceId,
        string versionId,
        InstallPlan plan,
        CancellationToken cancellationToken)
    {
        var nativesRoot = _paths.InstanceNativesDirectory(instanceId, versionId);
        if (Directory.Exists(nativesRoot))
        {
            Directory.Delete(nativesRoot, recursive: true);
        }

        Directory.CreateDirectory(nativesRoot);
        foreach (var subdirectory in NativeSubdirectories)
        {
            Directory.CreateDirectory(Path.Combine(nativesRoot, subdirectory));
        }

        foreach (var native in plan.Natives)
        {
            if (!File.Exists(native.ArchivePath))
            {
                _logger.LogWarning("Native archive missing, skipping: {Path}", native.ArchivePath);
                continue;
            }

            var exclusions = native.Exclusions;
            var result = await ArchiveExtractor
                .ExtractZipAsync(
                    native.ArchivePath,
                    nativesRoot,
                    new ArchiveExtractionOptions
                    {
                        Include = path => !IsExcluded(path, exclusions),
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            _logger.LogDebug("Extracted {Count} native files from {Archive}", result.FilesExtracted, native.ArchivePath);
        }

        CopyNativesIntoJavaDirectory(nativesRoot);
        return nativesRoot;
    }

    /// <summary>
    /// Modern versions point <c>java.library.path</c> at a <c>java</c> subdirectory of the natives
    /// directory, so the extracted libraries are mirrored there as well as left at the root.
    /// </summary>
    private static void CopyNativesIntoJavaDirectory(string nativesRoot)
    {
        var javaDirectory = Path.Combine(nativesRoot, "java");
        foreach (var file in Directory.EnumerateFiles(nativesRoot, "*", SearchOption.TopDirectoryOnly))
        {
            var destination = Path.Combine(javaDirectory, Path.GetFileName(file));
            try
            {
                File.Copy(file, destination, overwrite: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private async Task MaterializeVirtualAssetsAsync(InstallPlan plan, CancellationToken cancellationToken)
    {
        var virtualRoot = _paths.LegacyVirtualAssetsDirectory;
        foreach (var asset in plan.Assets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (asset.Hash.Length < 2)
            {
                continue;
            }

            var source = Path.Combine(_paths.AssetObjectsDirectory, asset.Hash[..2], asset.Hash);
            if (!File.Exists(source))
            {
                continue;
            }

            string destination;
            try
            {
                destination = PathSafety.ResolveContained(virtualRoot, asset.RelativePath);
            }
            catch (PathSafetyException)
            {
                continue;
            }

            var directory = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.Copy(source, destination, overwrite: true);
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <summary>
    /// Re-derives the artefact set and checks every managed file against its expected size and
    /// hash. Nothing is downloaded.
    /// </summary>
    public async Task<VerificationReport> VerifyAsync(
        Guid instanceId,
        string versionId,
        RuleContext context,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        var plan = await BuildPlanAsync(versionId, context, progress, cancellationToken).ConfigureAwait(false);
        return await VerifyPlanAsync(plan, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies the installation and re-acquires only what is missing or corrupt, then refreshes
    /// natives and the install manifest.
    /// </summary>
    public async Task<VerificationReport> RepairAsync(
        Guid instanceId,
        string versionId,
        RuleContext context,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        var plan = await BuildPlanAsync(versionId, context, progress, cancellationToken).ConfigureAwait(false);
        var report = await VerifyPlanAsync(plan, cancellationToken).ConfigureAwait(false);
        if (report.IsHealthy)
        {
            _logger.LogInformation("Instance {Instance} is already healthy", instanceId);
            return report;
        }

        var brokenPaths = new HashSet<string>(
            report.Issues.Select(issue => issue.Path),
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var toRepair = plan.Downloads.Where(request => brokenPaths.Contains(request.TargetPath)).ToList();
        _logger.LogInformation("Repairing {Count} file(s) for instance {Instance}", toRepair.Count, instanceId);

        var summary = await _downloads
            .DownloadAsync(toRepair, DownloadProgressAdapter.Create(progress), cancellationToken)
            .ConfigureAwait(false);
        if (!summary.Success)
        {
            throw new InstallFailedException(
                $"Repair of {versionId} failed for {summary.Failures.Count} file(s).",
                summary.Failures);
        }

        progress?.Report(new InstallProgress { Stage = InstallStage.ExtractingNatives });
        var nativesDirectory = await ExtractNativesAsync(instanceId, versionId, plan, cancellationToken)
            .ConfigureAwait(false);
        if (plan.VirtualAssets)
        {
            await MaterializeVirtualAssetsAsync(plan, cancellationToken).ConfigureAwait(false);
        }

        var manifest = await TryReadManifestAsync(instanceId, cancellationToken).ConfigureAwait(false)
            ?? new InstallManifest { BaseVersionId = versionId, ResolvedVersionId = versionId };
        manifest.ResolvedVersionId = versionId;
        manifest.FileCount = plan.FileCount;
        manifest.TotalBytes = plan.TotalBytes;
        manifest.NativesDirectory = nativesDirectory;
        manifest.LoggingConfigPath = plan.LoggingConfigPath;
        await WriteManifestAsync(instanceId, manifest, cancellationToken).ConfigureAwait(false);

        progress?.Report(new InstallProgress { Stage = InstallStage.Finalising });
        return await VerifyPlanAsync(plan, cancellationToken).ConfigureAwait(false);
    }

    private async Task<InstallPlan> BuildPlanAsync(
        string versionId,
        RuleContext context,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new InstallProgress { Stage = InstallStage.ResolvingMetadata });
        var document = await _resolver.ResolveAsync(versionId, cancellationToken).ConfigureAwait(false);
        progress?.Report(new InstallProgress { Stage = InstallStage.Planning, Message = versionId });
        return await _planner.CreateAsync(document, versionId, context, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<VerificationReport> VerifyPlanAsync(InstallPlan plan, CancellationToken cancellationToken)
    {
        var issues = new List<VerificationIssue>();
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var checkedPaths = new HashSet<string>(comparer);
        var filesChecked = 0;

        foreach (var request in plan.Downloads)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!checkedPaths.Add(request.TargetPath))
            {
                continue;
            }

            filesChecked++;
            if (!File.Exists(request.TargetPath))
            {
                issues.Add(new VerificationIssue(request.TargetPath, "missing"));
                continue;
            }

            var ok = await Hashing
                .VerifyAsync(request.TargetPath, request.ExpectedSize, request.ExpectedSha1, cancellationToken)
                .ConfigureAwait(false);
            if (!ok)
            {
                issues.Add(new VerificationIssue(request.TargetPath, "corrupt"));
            }
        }

        return new VerificationReport(plan.VersionId, filesChecked, plan.TotalBytes, issues);
    }

    private static bool IsExcluded(string relativePath, IReadOnlyList<string> exclusions)
    {
        if (exclusions.Count == 0)
        {
            return false;
        }

        foreach (var exclusion in exclusions)
        {
            if (exclusion.Length == 0)
            {
                continue;
            }

            var normalized = exclusion.Replace('\\', Path.DirectorySeparatorChar).TrimEnd(Path.DirectorySeparatorChar);
            if (relativePath.StartsWith(normalized, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(Path.GetFileName(relativePath), normalized, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
