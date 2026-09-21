using Ferrite.Core.Download;
using Ferrite.Core.Minecraft;
using Ferrite.Core.Util;

namespace Ferrite.Core.Content;

/// <summary>File downloads for a Modrinth modpack.</summary>
public sealed partial class MrpackInstaller
{
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
}
