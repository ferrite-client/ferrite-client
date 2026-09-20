using Ferrite.Core.Download;
using Ferrite.Core.Java;
using Ferrite.Core.Loaders;
using Ferrite.Core.Minecraft;
using Ferrite.Core.Rules;
using Ferrite.Core.Storage;
using Ferrite.Core.Util;
using Microsoft.Extensions.Logging;

namespace Ferrite.Core.Content;

/// <summary>Install steps for a Modrinth modpack.</summary>
public sealed partial class MrpackInstaller
{
    private async Task<InstanceRecord> ResolveInstanceAsync(
        ModpackInstallRequest request,
        ModrinthIndex index,
        string minecraftVersion,
        LoaderKind loader,
        string? loaderVersion,
        CancellationToken cancellationToken)
    {
        if (request.TargetInstanceId is { } id)
        {
            var existing = await _instances.TryLoadAsync(id, cancellationToken).ConfigureAwait(false)
                ?? throw new InstanceNotFoundException(id);
            return existing;
        }

        var name = request.InstanceName
            ?? (string.IsNullOrWhiteSpace(index.Name) ? "Modpack" : index.Name!);

        return await _instances.CreateAsync(
            new InstanceRecord
            {
                Id = Guid.NewGuid(),
                Name = name,
                MinecraftVersion = minecraftVersion,
                Loader = loader,
                LoaderVersion = loaderVersion,
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task InstallLoaderAsync(
        LoaderKind loader,
        string minecraftVersion,
        string? loaderVersion,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (loader == LoaderKind.Vanilla || loaderVersion is null)
        {
            return;
        }

        progress?.Report(new InstallProgress
        {
            Stage = InstallStage.ResolvingMetadata,
            Message = $"{loader.ToDisplayName()} {loaderVersion}",
        });

        if (loader is LoaderKind.Fabric or LoaderKind.Quilt)
        {
            await _fabric
                .InstallAsync(loader, minecraftVersion, loaderVersion, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        var required = JavaCompatibility.RequiredMajorFor(minecraftVersion);
        var runtimes = await _java.DetectAsync(cancellationToken).ConfigureAwait(false);
        var runtime = JavaSelection.SelectBest(runtimes, required)
            ?? throw new ContentProviderException(
                $"Installing {loader.ToDisplayName()} needs Java {required ?? 8}, which is not installed.");

        await _forge
            .InstallAsync(loader, minecraftVersion, loaderVersion, runtime, progress, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<(int Downloaded, int Skipped)> DownloadPackFilesAsync(
        ModrinthIndex index,
        string gameDirectory,
        List<string> warnings,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        var requests = new List<DownloadRequest>(index.Files.Count);
        var skipped = 0;

        foreach (var file in index.Files)
        {
            if (file.Env.IsClientUnsupported)
            {
                skipped++;
                continue;
            }

            var url = file.Downloads.FirstOrDefault();
            if (string.IsNullOrEmpty(url))
            {
                warnings.Add($"{file.Path} declares no download URL.");
                skipped++;
                continue;
            }

            string target;
            try
            {
                target = PathSafety.ResolveContained(gameDirectory, file.Path);
            }
            catch (PathSafetyException)
            {
                warnings.Add($"Skipped {file.Path}: the path escapes the instance.");
                skipped++;
                continue;
            }

            requests.Add(new DownloadRequest
            {
                Url = url,
                TargetPath = target,
                ExpectedSha1 = file.Hashes.Sha1,
                ExpectedSha512 = file.Hashes.Sha512,
                ExpectedSize = file.FileSize > 0 ? file.FileSize : null,
                Label = Path.GetFileName(target),
            });
        }

        if (requests.Count == 0)
        {
            return (0, skipped);
        }

        progress?.Report(new InstallProgress
        {
            Stage = InstallStage.Downloading,
            Message = $"Modpack files ({requests.Count})",
        });

        var summary = await _downloads
            .DownloadAsync(requests, DownloadProgressAdapter.Create(progress), cancellationToken)
            .ConfigureAwait(false);
        foreach (var failure in summary.Failures)
        {
            warnings.Add($"{Path.GetFileName(failure.TargetPath)}: {failure.Message}");
        }

        return (summary.DownloadedFiles + summary.SkippedFiles, skipped);
    }

    private async Task<int> ExtractOverridesAsync(
        string archivePath,
        string gameDirectory,
        CancellationToken cancellationToken)
    {
        var total = 0;
        foreach (var prefix in new[] { "overrides", "client-overrides" })
        {
            var result = await ArchiveExtractor
                .ExtractZipAsync(
                    archivePath,
                    gameDirectory,
                    new ArchiveExtractionOptions { StripPrefix = prefix },
                    cancellationToken)
                .ConfigureAwait(false);
            total += result.FilesExtracted;
        }

        return total;
    }

    private static bool HasUserContent(string gameDirectory)
    {
        if (!Directory.Exists(gameDirectory))
        {
            return false;
        }

        return Directory.EnumerateFiles(gameDirectory, "*", SearchOption.AllDirectories).Any();
    }

    private string BackupGameDirectory(InstanceRecord instance, string gameDirectory)
    {
        Directory.CreateDirectory(_paths.BackupsDirectory);
        var stem = $"modpack-{PathSafety.SanitizeFileName(instance.Name)}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}";
        var destination = Path.Combine(_paths.BackupsDirectory, stem);
        var suffix = 1;
        while (Directory.Exists(destination))
        {
            destination = Path.Combine(_paths.BackupsDirectory, $"{stem}-{suffix++}");
        }

        InstanceStore.MoveDirectory(gameDirectory, destination);
        Directory.CreateDirectory(gameDirectory);
        _logger.LogInformation("Backed up {Instance} game directory to {Path}", instance.Name, destination);
        return destination;
    }
}
