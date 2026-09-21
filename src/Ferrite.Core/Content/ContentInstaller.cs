using Ferrite.Core.Download;
using Ferrite.Core.Minecraft;
using Ferrite.Core.Storage;
using Ferrite.Core.Util;
using Microsoft.Extensions.Logging;

namespace Ferrite.Core.Content;

/// <summary>
/// Resolves a project and its required dependencies into an install plan, then places the files in
/// the instance. A version whose game version or loader does not match the instance is never
/// selected.
/// </summary>
public sealed class ContentInstaller
{
    private const int MaxDependencyDepth = 6;
    private const int MaxDependencyCount = 64;

    private readonly IContentProvider _provider;
    private readonly DownloadEngine _downloads;
    private readonly Platform.AppPaths _paths;
    private readonly ContentManifestStore _manifests;
    private readonly ILogger<ContentInstaller> _logger;

    public ContentInstaller(
        IContentProvider provider,
        DownloadEngine downloads,
        Platform.AppPaths paths,
        ContentManifestStore manifests,
        ILogger<ContentInstaller> logger)
    {
        _provider = provider;
        _downloads = downloads;
        _paths = paths;
        _manifests = manifests;
        _logger = logger;
    }

    public async Task<ContentInstallPlan> PlanAsync(
        InstanceRecord instance,
        string projectIdOrSlug,
        string? versionId,
        bool includeOptionalDependencies,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instance);
        var loader = instance.Loader.ToContentProviderToken();
        var warnings = new List<string>();
        var items = new List<ContentInstallItem>();
        var optional = new List<ContentVersion>();
        var visited = new HashSet<string>(StringComparer.Ordinal);

        var rootProject = await _provider
            .GetProjectAsync(projectIdOrSlug, cancellationToken)
            .ConfigureAwait(false);
        if (rootProject is null)
        {
            throw new ContentProviderException(
                $"Project '{projectIdOrSlug}' was not found on {_provider.Name}.");
        }

        ContentVersion? rootVersion;
        if (versionId is { Length: > 0 })
        {
            rootVersion = await _provider
                .GetVersionAsync(rootProject.ProjectId, versionId, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            var candidates = await _provider
                .GetVersionsAsync(rootProject.ProjectId, instance.MinecraftVersion, loader, cancellationToken)
                .ConfigureAwait(false);
            rootVersion = _provider.SelectBestVersion(candidates, instance.MinecraftVersion, loader);
        }

        if (rootVersion is null)
        {
            throw new ContentProviderException(
                $"No {rootProject.Title} version is compatible with Minecraft {instance.MinecraftVersion}"
                + (loader is null ? "." : $" and {loader}."));
        }

        var queue = new Queue<(ContentVersion Version, ContentProjectType Type, int Depth)>();
        queue.Enqueue((rootVersion, rootProject.ProjectType, 0));

        while (queue.Count > 0 && items.Count < MaxDependencyCount)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (version, type, depth) = queue.Dequeue();
            if (!visited.Add(version.VersionId))
            {
                continue;
            }

            var file = version.PrimaryFile;
            if (file is null)
            {
                warnings.Add($"{version.VersionNumber} has no downloadable file.");
                continue;
            }

            if (string.IsNullOrEmpty(file.Url))
            {
                // CurseForge only returns a URL when the author allowed third-party distribution.
                warnings.Add(
                    $"{version.VersionNumber} cannot be downloaded automatically: "
                    + "the author has not allowed third-party distribution.");
                continue;
            }

            items.Add(new ContentInstallItem(
                version.ProjectId,
                version.VersionId,
                file.FileName,
                file.Url,
                file.Size,
                file.Sha1,
                file.Sha512,
                type,
                type.TargetFolder()));

            if (depth >= MaxDependencyDepth)
            {
                continue;
            }

            foreach (var dependency in version.Dependencies)
            {
                var kind = dependency.Kind;
                if (string.Equals(kind, "incompatible", StringComparison.OrdinalIgnoreCase))
                {
                    warnings.Add(
                        $"{version.VersionNumber} declares {dependency.ProjectId ?? dependency.FileName} as incompatible.");
                    continue;
                }

                var isOptional = string.Equals(kind, "optional", StringComparison.OrdinalIgnoreCase);
                if (isOptional && !includeOptionalDependencies)
                {
                    continue;
                }

                var isRequired = string.Equals(kind, "required", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(kind, "embedded", StringComparison.OrdinalIgnoreCase);
                if (!isRequired && !isOptional)
                {
                    continue;
                }

                var resolved = await ResolveDependencyAsync(dependency, instance, loader, cancellationToken)
                    .ConfigureAwait(false);
                if (resolved is null)
                {
                    var name = dependency.ProjectId ?? dependency.VersionId ?? dependency.FileName ?? "unknown";
                    warnings.Add($"Dependency {name} has no compatible version for this instance.");
                    continue;
                }

                if (isOptional)
                {
                    optional.Add(resolved);
                }

                queue.Enqueue((resolved, ContentProjectType.Mod, depth + 1));
            }
        }

        if (items.Count >= MaxDependencyCount)
        {
            warnings.Add($"Dependency resolution stopped after {MaxDependencyCount} files.");
        }

        _logger.LogInformation(
            "Planned {Count} file(s) for {Project} on {Instance}",
            items.Count,
            rootProject.Title,
            instance.Name);

        return new ContentInstallPlan(rootProject.ProjectId, items, warnings, optional);
    }

    public async Task<ContentInstallResult> InstallAsync(
        InstanceRecord instance,
        ContentInstallPlan plan,
        string gameDirectory,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var requests = new List<DownloadRequest>(plan.Items.Count);
        var targets = new List<string>(plan.Items.Count);
        var recorded = new List<ContentInstallItem>(plan.Items.Count);
        var warnings = new List<string>(plan.Warnings);

        foreach (var item in plan.Items)
        {
            var folder = Path.Combine(gameDirectory, item.TargetFolder);
            Directory.CreateDirectory(folder);
            var fileName = PathSafety.SanitizeFileName(item.FileName);
            var target = Path.Combine(folder, fileName);
            if (!PathSafety.IsContained(gameDirectory, target))
            {
                warnings.Add($"Skipped {item.FileName}: the target path escaped the instance.");
                continue;
            }

            targets.Add(target);
            recorded.Add(item);
            requests.Add(new DownloadRequest
            {
                Url = item.Url,
                TargetPath = target,
                ExpectedSha1 = item.Sha1,
                ExpectedSha512 = item.Sha512,
                ExpectedSize = item.Size > 0 ? item.Size : null,
                Label = fileName,
            });
        }

        progress?.Report(new InstallProgress
        {
            Stage = InstallStage.Downloading,
            Message = $"Installing {requests.Count} file(s)",
        });

        var summary = await _downloads
            .DownloadAsync(requests, DownloadProgressAdapter.Create(progress), cancellationToken)
            .ConfigureAwait(false);

        foreach (var failure in summary.Failures)
        {
            warnings.Add($"{Path.GetFileName(failure.TargetPath)}: {failure.Message}");
        }

        RecordManifest(instance, gameDirectory, summary, recorded, targets);

        return new ContentInstallResult(summary.DownloadedFiles + summary.SkippedFiles, targets, warnings);
    }

    /// <summary>
    /// Records what landed on disk so the content can be checked for updates later. Only files that
    /// actually succeeded are recorded, otherwise an update check would chase a file that is absent.
    /// </summary>
    private void RecordManifest(
        InstanceRecord instance,
        string gameDirectory,
        DownloadSummary summary,
        IReadOnlyList<ContentInstallItem> items,
        IReadOnlyList<string> targets)
    {
        var failed = summary.Failures
            .Select(failure => failure.TargetPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var filePath = _paths.InstanceContentManifestFile(instance.Id);
        var manifest = _manifests.Load(filePath);
        var recorded = 0;

        for (var index = 0; index < items.Count; index++)
        {
            if (failed.Contains(targets[index]))
            {
                continue;
            }

            var relative = Path.GetRelativePath(gameDirectory, targets[index]).Replace('\\', '/');
            if (relative.StartsWith("..", StringComparison.Ordinal))
            {
                continue;
            }

            manifest.Upsert(new ContentManifestEntry
            {
                RelativePath = relative,
                Provider = _provider.Name,
                ProjectId = items[index].ProjectId,
                VersionId = items[index].VersionId,
                ProjectType = items[index].ProjectType,
                Sha1 = items[index].Sha1,
                InstalledAt = DateTimeOffset.UtcNow,
            });
            recorded++;
        }

        if (recorded > 0)
        {
            _manifests.Save(filePath, manifest);
        }
    }

    private async Task<ContentVersion?> ResolveDependencyAsync(
        ContentDependency dependency,
        InstanceRecord instance,
        string? loader,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(dependency.VersionId))
        {
            var pinned = await _provider
                .GetVersionAsync(dependency.ProjectId ?? string.Empty, dependency.VersionId, cancellationToken)
                .ConfigureAwait(false);
            if (pinned is not null)
            {
                return ContentCompatibility.IsCompatible(pinned, instance.MinecraftVersion, loader)
                    ? pinned
                    : null;
            }
        }

        if (string.IsNullOrEmpty(dependency.ProjectId))
        {
            return null;
        }

        var versions = await _provider
            .GetVersionsAsync(dependency.ProjectId, instance.MinecraftVersion, loader, cancellationToken)
            .ConfigureAwait(false);
        return _provider.SelectBestVersion(versions, instance.MinecraftVersion, loader);
    }
}
