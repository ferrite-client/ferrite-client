using Ferrite.Core.Content;
using Ferrite.Core.Minecraft;

namespace Ferrite.Verify;

/// <summary>Feed The Beast browsing, and installing a pack.</summary>
internal static partial class Scenarios
{
    /// <summary>
    /// Searches FTB, then reads the pack behind the first hit and lists its versions. With
    /// <paramref name="install"/> it also reports the chosen version's file list and, when the total
    /// is under the cap, installs it as a new instance.
    /// </summary>
    public static async Task<int> FtbAsync(
        VerifyServices services,
        string term,
        bool install,
        string? packId,
        string? versionId,
        bool force,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"Searching FTB for \"{term}\"");
        var result = await services.Ftb
            .SearchAsync(
                new ContentSearchQuery(term, ContentProjectType.Modpack, Limit: 12),
                cancellationToken)
            .ConfigureAwait(false);
        Console.WriteLine($"Total: {result.TotalHits}, returned {result.Hits.Count}");
        foreach (var hit in result.Hits)
        {
            Console.WriteLine($"  [{hit.ProjectId}] {hit.Title} - {hit.Downloads:N0} installs");
        }

        ContentSummary? chosen;
        if (packId is { Length: > 0 })
        {
            chosen = result.Hits.FirstOrDefault(hit => hit.ProjectId == packId);
            if (chosen is null
                && await services.Ftb.GetProjectAsync(packId, cancellationToken).ConfigureAwait(false)
                    is { } project)
            {
                chosen = new ContentSummary(
                    project.Provider,
                    project.ProjectId,
                    project.Slug,
                    project.Title,
                    project.Description,
                    project.ProjectType,
                    project.Downloads,
                    project.IconUrl,
                    project.Authors?.FirstOrDefault(),
                    [],
                    null);
            }
        }
        else
        {
            chosen = result.Hits.FirstOrDefault();
        }

        if (chosen is null)
        {
            Console.WriteLine("No pack to inspect.");
            return 2;
        }

        var details = await services.Ftb
            .GetProjectAsync(chosen.ProjectId, cancellationToken)
            .ConfigureAwait(false);
        Console.WriteLine();
        Console.WriteLine($"Pack {chosen.ProjectId}: {details?.Title}");
        Console.WriteLine($"  synopsis: {details?.Description}");
        Console.WriteLine($"  game versions: {string.Join(", ", details?.GameVersions ?? [])}");
        Console.WriteLine($"  loaders: {string.Join(", ", details?.Loaders ?? [])}");
        Console.WriteLine($"  authors: {string.Join(", ", details?.Authors ?? [])}");

        var versions = await services.Ftb
            .GetVersionsAsync(chosen.ProjectId, null, null, cancellationToken)
            .ConfigureAwait(false);
        Console.WriteLine($"  versions: {versions.Count}");
        foreach (var version in versions.TakeLast(5))
        {
            Console.WriteLine(
                $"    {version.VersionId} {version.VersionNumber} "
                + $"({string.Join("/", version.GameVersions)}, {string.Join("/", version.Loaders)})");
        }

        if (!install)
        {
            return 0;
        }

        var target = versionId is { Length: > 0 }
            ? versions.FirstOrDefault(version => version.VersionId == versionId)
                ?? await services.Ftb.GetVersionAsync(chosen.ProjectId, versionId, cancellationToken)
                    .ConfigureAwait(false)
            : versions.LastOrDefault();
        if (target is null)
        {
            Console.WriteLine("No version to install.");
            return 2;
        }

        // The chosen version from the pack document carries no files; the file list is one request.
        var withFiles = target.Files.Count > 0
            ? target
            : await services.Ftb.GetVersionAsync(chosen.ProjectId, target.VersionId, cancellationToken)
                .ConfigureAwait(false);
        if (withFiles is null)
        {
            Console.WriteLine("The version's file list could not be read.");
            return 3;
        }

        var installable = withFiles.Files.Where(file => file.Size > 0).ToList();
        var totalBytes = installable.Sum(file => file.Size);
        Console.WriteLine();
        Console.WriteLine(
            $"Version {withFiles.VersionNumber}: {withFiles.Files.Count} file(s), "
            + $"{totalBytes / (1024 * 1024)} MiB");

        const long cap = 700L * 1024 * 1024;
        if (totalBytes > cap && !force)
        {
            Console.WriteLine($"(over the {cap / (1024 * 1024)} MiB cap; pass --force to install anyway)");
            return 0;
        }

        Console.WriteLine("Installing as a new instance...");
        var result2 = await services.FtbPacks
            .InstallAsync(
                chosen.ProjectId,
                withFiles.VersionId,
                new Progress<InstallProgress>(ReportProgress),
                cancellationToken)
            .ConfigureAwait(false);
        Console.WriteLine();
        Console.WriteLine($"Instance: {result2.Instance.Name} ({result2.VersionId})");
        Console.WriteLine(
            $"  {result2.FilesDownloaded} file(s) downloaded, {result2.FilesSkipped} skipped, "
            + $"{result2.Warnings.Count} warning(s)");
        foreach (var warning in result2.Warnings.Take(5))
        {
            Console.WriteLine($"    {warning}");
        }

        var modsDirectory = Path.Combine(services.Paths.InstanceGameDirectory(result2.Instance.Id), "mods");
        var modCount = Directory.Exists(modsDirectory) ? Directory.EnumerateFiles(modsDirectory, "*.jar").Count() : 0;
        Console.WriteLine($"  mods on disk: {modCount}");

        Console.WriteLine("PASS: an FTB pack was installed as an instance.");
        return 0;
    }
}
