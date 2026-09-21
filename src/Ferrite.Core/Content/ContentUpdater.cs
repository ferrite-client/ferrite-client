using Ferrite.Core.Download;
using Ferrite.Core.Minecraft;
using Ferrite.Core.Platform;
using Ferrite.Core.Storage;
using Ferrite.Core.Util;
using Microsoft.Extensions.Logging;

namespace Ferrite.Core.Content;

/// <summary>An installed file that has a newer version compatible with the instance.</summary>
public sealed record ContentUpdate
{
    public required ContentManifestEntry Entry { get; init; }

    /// <summary>The provider's own name for the available version, used for display.</summary>
    public string? Title { get; init; }

    public required ContentVersion Available { get; init; }

    public string AvailableVersionName => Available.VersionNumber;
}

/// <summary>What an update check found, including what it could not check.</summary>
public sealed record ContentUpdateReport
{
    public required IReadOnlyList<ContentUpdate> Updates { get; init; }

    public required IReadOnlyList<string> UpToDate { get; init; }

    public required IReadOnlyList<string> Warnings { get; init; }

    public required IReadOnlyList<string> Skipped { get; init; }
}

public sealed record ContentUpdateResult(
    int Updated,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Checks launcher-installed content for newer compatible versions and applies them. Only files the
/// launcher installed are considered: a jar the user dropped in has no provider identity, and
/// guessing one would risk replacing the wrong file.
/// </summary>
public sealed class ContentUpdater
{
    private readonly DownloadEngine _downloads;
    private readonly ContentManifestStore _manifests;
    private readonly AppPaths _paths;
    private readonly ILogger<ContentUpdater> _logger;

    public ContentUpdater(
        DownloadEngine downloads,
        ContentManifestStore manifests,
        AppPaths paths,
        ILogger<ContentUpdater> logger)
    {
        _downloads = downloads;
        _manifests = manifests;
        _paths = paths;
        _logger = logger;
    }

    /// <summary>
    /// Resolves the newest compatible version for every recorded file. A project whose file nno
    /// longer exists, or that has no compatible release, is reported rather than dropped silently.
    /// </summary>
    public async Task<ContentUpdateReport> CheckAsync(
        InstanceRecord instance,
        IContentProvider provider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(provider);

        var filePath = _paths.InstanceContentManifestFile(instance.Id);
        var manifest = _manifests.Load(filePath);
        var updates = new List<ContentUpdate>();
        var upToDate = new List<string>();
        var warnings = new List<string>();
        var skipped = new List<string>();
        var loader = instance.Loader.ToContentProviderToken();

        foreach (var entry in manifest.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!string.Equals(entry.Provider, provider.Name, StringComparison.OrdinalIgnoreCase))
            {
                skipped.Add($"{entry.RelativePath} came from {entry.Provider}");
                continue;
            }

            if (!File.Exists(Path.Combine(_paths.InstanceGameDirectory(instance.Id), entry.RelativePath)))
            {
                // The file is gone, so keep the record but do not offer an update for it.
                skipped.Add($"{entry.RelativePath} is no longer installed");
                continue;
            }

            IReadOnlyList<ContentVersion> versions;
            try
            {
                versions = await provider
                    .GetVersionsAsync(entry.ProjectId, instance.MinecraftVersion, loader, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (ContentProviderException exception)
            {
                warnings.Add($"{entry.RelativePath}: {exception.Message}");
                continue;
            }

            var best = provider.SelectBestVersion(versions, instance.MinecraftVersion, loader);
            if (best is null)
            {
                warnings.Add($"No compatible version of {entry.RelativePath} exists for this instance.");
                continue;
            }

            if (string.Equals(best.VersionId, entry.VersionId, StringComparison.Ordinal))
            {
                upToDate.Add(entry.RelativePath);
                continue;
            }

            updates.Add(new ContentUpdate
            {
                Entry = entry,
                Available = best,
                Title = best.Name,
            });
        }

        _logger.LogInformation(
            "Update check for {Instance}: {Updates} update(s), {Current} current, {Skipped} skipped",
            instance.Name,
            updates.Count,
            upToDate.Count,
            skipped.Count);

        return new ContentUpdateReport
        {
            Updates = updates,
            UpToDate = upToDate,
            Warnings = warnings,
            Skipped = skipped,
        };
    }

    /// <summary>
    /// Downloads each update and only then removes the file it replaces, so a failed download leaves
    /// the instance exactly as it was.
    /// </summary>
    public async Task<ContentUpdateResult> ApplyAsync(
        InstanceRecord instance,
        IReadOnlyList<ContentUpdate> updates,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(updates);

        if (updates.Count == 0)
        {
            return new ContentUpdateResult(0, []);
        }

        var gameDirectory = _paths.InstanceGameDirectory(instance.Id);
        var filePath = _paths.InstanceContentManifestFile(instance.Id);
        var manifest = _manifests.Load(filePath);
        var warnings = new List<string>();

        progress?.Report(new InstallProgress
        {
            Stage = InstallStage.Downloading,
            Message = $"Updating {updates.Count} file(s)",
        });

        var applied = 0;
        foreach (var update in updates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var file = update.Available.PrimaryFile;
            if (file is null || string.IsNullOrEmpty(file.Url))
            {
                warnings.Add($"{update.AvailableVersionName} cannot be downloaded automatically.");
                continue;
            }

            var oldPath = Path.Combine(gameDirectory, update.Entry.RelativePath);
            var folder = Path.GetDirectoryName(oldPath) ?? gameDirectory;
            var newPath = Path.Combine(folder, PathSafety.SanitizeFileName(file.FileName));
            if (!PathSafety.IsContained(gameDirectory, newPath))
            {
                warnings.Add($"{file.FileName}: the target path escaped the instance.");
                continue;
            }

            var summary = await _downloads
                .DownloadAsync(
                    [
                        new DownloadRequest
                        {
                            Url = file.Url,
                            TargetPath = newPath,
                            ExpectedSha1 = file.Sha1,
                            ExpectedSha512 = file.Sha512,
                            ExpectedSize = file.Size > 0 ? file.Size : null,
                            Label = Path.GetFileName(newPath),
                        },
                    ],
                    DownloadProgressAdapter.Create(progress),
                    cancellationToken)
                .ConfigureAwait(false);

            if (!summary.Success)
            {
                warnings.Add(
                    $"{update.AvailableVersionName}: {summary.Failures.FirstOrDefault()?.Message ?? "download failed"}");
                continue;
            }

            // The new file is verified; only now is the replaced file removed and the manifest moved.
            if (!string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase) && File.Exists(oldPath))
            {
                TryDelete(oldPath, warnings);
            }

            var relative = Path.GetRelativePath(gameDirectory, newPath).Replace('\\', '/');
            manifest.Remove(update.Entry.RelativePath);
            manifest.Upsert(update.Entry with
            {
                RelativePath = relative,
                VersionId = update.Available.VersionId,
                Sha1 = file.Sha1,
                InstalledAt = DateTimeOffset.UtcNow,
            });
            applied++;
        }

        if (applied > 0)
        {
            _manifests.Save(filePath, manifest);
        }

        return new ContentUpdateResult(applied, warnings);
    }

    /// <summary>Drops a record when the user removes the file through the launcher.</summary>
    public void Forget(InstanceRecord instance, string relativePath) =>
        _manifests.Save(
            _paths.InstanceContentManifestFile(instance.Id),
            Forget(_manifests.Load(_paths.InstanceContentManifestFile(instance.Id)), relativePath));

    private static ContentManifest Forget(ContentManifest manifest, string relativePath)
    {
        manifest.Remove(relativePath);
        return manifest;
    }

    private void TryDelete(string path, List<string> warnings)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The new file is installed and hash-verified, so this is untidy rather than broken.
            warnings.Add($"{Path.GetFileName(path)} could not be removed: {exception.Message}");
            _logger.LogWarning(exception, "Replaced content file {Path} could not be removed", path);
        }
    }
}
