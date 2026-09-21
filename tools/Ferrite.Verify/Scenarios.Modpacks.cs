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
        CancellationToken cancellationToken)
    {
        var archivePath = await ResolveArchiveAsync(services, source, cancellationToken).ConfigureAwait(false);
        if (archivePath is null)
        {
            return 2;
        }

        var request = new ModpackInstallRequest
        {
            ArchivePath = archivePath,
            SourceUrl = source.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? source : null,
            BackupExisting = true,
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
        CancellationToken cancellationToken)
    {
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
