using Ferrite.Core.Content;
using Ferrite.Core.Minecraft;
using Ferrite.Core.Util;

namespace Ferrite.Verify;

/// <summary>Modpack install and export scenarios.</summary>
internal static partial class Scenarios
{
    /// <summary>
    /// Installs a Modrinth modpack from a URL or a local file, then reports what landed on disk.
    /// </summary>
    public static async Task<int> InstallModpackAsync(
        VerifyServices services,
        string source,
        string? slug,
        string? updateInstanceName,
        string? updateInstanceId,
        CancellationToken cancellationToken)
    {
        var archivePath = await ResolveArchiveAsync(services, source, slug, cancellationToken)
            .ConfigureAwait(false);
        if (archivePath is null)
        {
            return 2;
        }

        // Updating points the installer at the instance that already exists instead of letting it
        // create one, which is the difference between an update and a fresh install.
        var instances = await services.Instances.LoadAllAsync(cancellationToken).ConfigureAwait(false);
        var target = Guid.TryParse(updateInstanceId, out var targetId)
            ? instances.FirstOrDefault(record => record.Id == targetId)
            : updateInstanceName is { Length: > 0 }
                ? instances.FirstOrDefault(record => string.Equals(
                    record.Name,
                    updateInstanceName,
                    StringComparison.OrdinalIgnoreCase))
                : null;
        if (updateInstanceName is { Length: > 0 } && target is null)
        {
            Console.WriteLine($"No instance named '{updateInstanceName}'.");
            return 2;
        }

        if (updateInstanceId is { Length: > 0 } && target is null)
        {
            Console.WriteLine($"No instance with id '{updateInstanceId}'.");
            return 2;
        }

        if (target is not null)
        {
            Console.WriteLine($"Updating instance: {target.Name} ({target.Id})");
            Console.WriteLine(
                $"  before: minecraft {target.MinecraftVersion}, loader {target.Loader} {target.LoaderVersion}, "
                + $"pack '{target.Modpack?.Name ?? "-"}' {target.Modpack?.VersionName ?? "-"}");
        }

        var request = new ModpackInstallRequest
        {
            ArchivePath = archivePath,
            SourceUrl = source.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? source : null,
            BackupExisting = true,
            TargetInstanceId = target?.Id,
        };
        var progress = new Progress<InstallProgress>(ReportProgress);

        var kind = ModpackArchives.DetectKind(archivePath);
        ModpackInstallResult result;
        if (kind == ModpackArchiveKind.CurseForge)
        {
            var manifest = CurseForgePackInstaller.ReadManifest(archivePath);
            var (loader, loaderVersion) = CurseForgePackInstaller.ResolveLoader(manifest);
            Console.WriteLine($"Pack: {manifest.Name} {manifest.Version} (CurseForge manifest)");
            Console.WriteLine($"Minecraft: {manifest.Minecraft?.Version}, loader: {loader} {loaderVersion ?? "-"}");
            Console.WriteLine($"Declared files: {manifest.Files.Count}");

            result = await services.CurseForgePacks
                .InstallAsync(request, progress, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            var index = MrpackInstaller.ReadIndex(archivePath);
            Console.WriteLine($"Pack: {index.Name} {index.VersionId} (format {index.FormatVersion})");
            Console.WriteLine($"Dependencies: {string.Join(", ", index.Dependencies.Select(pair => pair.Key + "=" + pair.Value))}");
            Console.WriteLine($"Declared files: {index.Files.Count}");

            result = await services.Modpacks
                .InstallAsync(request, progress, cancellationToken)
                .ConfigureAwait(false);
        }

        Console.WriteLine();
        Console.WriteLine($"Instance: {result.Instance.Name} ({result.Instance.Id})");
        Console.WriteLine($"  launch version: {result.VersionId}");
        Console.WriteLine($"  files:          {result.FilesDownloaded} downloaded, {result.FilesSkipped} skipped");
        Console.WriteLine($"  overrides:      {result.OverrideFiles}");
        Console.WriteLine(
            $"  pack identity:  {result.Instance.Modpack?.Provider} "
            + $"'{result.Instance.Modpack?.Name}' {result.Instance.Modpack?.VersionName}");
        Console.WriteLine(
            $"  record now:     minecraft {result.Instance.MinecraftVersion}, "
            + $"loader {result.Instance.Loader} {result.Instance.LoaderVersion}");
        foreach (var warning in result.Warnings)
        {
            Console.WriteLine($"  warn: {warning}");
        }

        var gameDirectory = services.Paths.InstanceGameDirectory(result.Instance.Id);
        var mods = await services.Mods.ListModsAsync(gameDirectory, cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"  mod inventory:  {mods.Count} mod(s)");
        foreach (var mod in mods.Take(8))
        {
            Console.WriteLine($"      {mod.DisplayName} [{mod.Loader}] {mod.Version}");
        }

        // An update has to have left the content it replaced somewhere the user can find it.
        if (target is not null)
        {
            var backups = Directory.Exists(services.Paths.BackupsDirectory)
                ? Directory.EnumerateDirectories(services.Paths.BackupsDirectory)
                    .Where(directory => Path.GetFileName(directory)
                        .StartsWith("modpack-", StringComparison.OrdinalIgnoreCase))
                    .ToList()
                : [];
            Console.WriteLine($"  replaced content backed up: {backups.Count} folder(s)");
            foreach (var backup in backups)
            {
                Console.WriteLine($"      {Path.GetFileName(backup)}");
            }

            if (backups.Count == 0)
            {
                Console.WriteLine("FAIL: the content the update replaced was not backed up.");
                return 4;
            }
        }

        return result.FilesDownloaded > 0 || mods.Count > 0 ? 0 : 3;
    }

    /// <summary>Exports an instance as a modpack and re-imports it into a fresh instance.</summary>
    public static async Task<int> ExportModpackAsync(
        VerifyServices services,
        string minecraftVersion,
        CancellationToken cancellationToken)
    {
        var instances = await services.Instances.LoadAllAsync(cancellationToken).ConfigureAwait(false);
        var instance = instances
            .Where(candidate => candidate.Modpack is not null)
            .OrderByDescending(candidate => candidate.Modpack!.InstalledAt ?? candidate.CreatedAt)
            .FirstOrDefault()
            ?? instances.OrderByDescending(candidate => candidate.CreatedAt).FirstOrDefault();

        if (instance is null)
        {
            Console.WriteLine("No instance to export.");
            return 2;
        }

        var outputPath = Path.Combine(services.Paths.TemporaryDirectory, "export-" + instance.Id.ToString("N") + ".mrpack");
        var export = await services.ModpackExporter
            .ExportAsync(instance, outputPath, includeSaves: false, cancellationToken)
            .ConfigureAwait(false);

        Console.WriteLine($"Exported {instance.Name} to {export.ArchivePath}");
        Console.WriteLine($"  version id: {export.VersionId}, overrides: {export.OverrideCount}");
        Console.WriteLine($"  size: {ByteSize.Format(new FileInfo(export.ArchivePath).Length)}");

        var roundTrip = MrpackInstaller.ReadIndex(export.ArchivePath);
        Console.WriteLine(
            $"  re-read index: {roundTrip.Name} {roundTrip.VersionId}, "
            + $"{roundTrip.Dependencies.Count} dependency(ies)");

        // Re-import into a separate instance to prove the archive is self-contained.
        var import = await services.Modpacks
            .InstallAsync(
                new ModpackInstallRequest
                {
                    ArchivePath = export.ArchivePath,
                    InstanceName = instance.Name + " (imported)",
                    BackupExisting = false,
                },
                new Progress<InstallProgress>(ReportProgress),
                cancellationToken)
            .ConfigureAwait(false);

        var gameDirectory = services.Paths.InstanceGameDirectory(import.Instance.Id);
        var mods = await services.Mods.ListModsAsync(gameDirectory, cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"Re-imported as {import.Instance.Name}: {import.OverrideFiles} override(s), {mods.Count} mod(s)");

        return mods.Count > 0 || import.OverrideFiles > 0 ? 0 : 3;
    }

    private static async Task<string?> ResolveArchiveAsync(
        VerifyServices services,
        string source,
        string? slug,
        CancellationToken cancellationToken)
    {
        // A slug resolves the project's own file the way the browser does, so a pack can be fetched by
        // name rather than by a URL the caller had to find first.
        if (slug is { Length: > 0 })
        {
            var project = await services.Modrinth
                .GetProjectAsync(slug, cancellationToken)
                .ConfigureAwait(false);
            if (project is null)
            {
                Console.WriteLine($"No Modrinth project named '{slug}'.");
                return null;
            }

            Console.WriteLine($"Project: {project.Title} ({project.Slug})");
            var versions = await services.Modrinth
                .GetVersionsAsync(project.ProjectId, null, null, cancellationToken)
                .ConfigureAwait(false);
            var version = versions.FirstOrDefault(candidate => candidate.IsRelease)
                ?? versions.FirstOrDefault();
            if (version?.PrimaryFile?.Url is not { Length: > 0 } url)
            {
                Console.WriteLine($"'{slug}' publishes no downloadable file.");
                return null;
            }

            Console.WriteLine($"Version: {version.VersionNumber} ({version.VersionType})");
            source = url;
        }

        if (!source.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            if (File.Exists(source))
            {
                return source;
            }

            Console.WriteLine($"Archive not found: {source}");
            return null;
        }

        var fileName = PathSafety.SanitizeFileName(Path.GetFileName(new Uri(source).LocalPath));
        var target = Path.Combine(services.Paths.TemporaryDirectory, fileName);
        Console.WriteLine($"Downloading {fileName}...");
        var summary = await services.Downloads
            .DownloadAsync(
                [
                    new Ferrite.Core.Download.DownloadRequest
                    {
                        Url = source,
                        TargetPath = target,
                        Label = fileName,
                    },
                ],
                progress: null,
                cancellationToken)
            .ConfigureAwait(false);

        if (!summary.Success)
        {
            Console.WriteLine($"Download failed: {summary.Failures[0].Message}");
            return null;
        }

        return target;
    }
}
