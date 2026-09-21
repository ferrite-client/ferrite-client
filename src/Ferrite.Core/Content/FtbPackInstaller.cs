using Ferrite.Core.Download;
using Ferrite.Core.Java;
using Ferrite.Core.Loaders;
using Ferrite.Core.Minecraft;
using Ferrite.Core.Platform;
using Ferrite.Core.Storage;
using Ferrite.Core.Util;
using Microsoft.Extensions.Logging;

namespace Ferrite.Core.Content;

/// <summary>
/// Installs a Feed The Beast pack. FTB publishes a file list rather than an archive, so the installer
/// resolves the pack's own version, loader, and game version, installs those, then downloads each
/// declared file to the directory the pack names - with the same containment check every other
/// installer uses, because the paths come from a remote document.
/// </summary>
public sealed class FtbPackInstaller
{
    private const int MaxConcurrentFiles = 8;

    private readonly FtbClient _client;
    private readonly DownloadEngine _downloads;
    private readonly InstanceStore _instances;
    private readonly ModpackInstallSupport _support;
    private readonly ILogger<FtbPackInstaller> _logger;

    public FtbPackInstaller(
        FtbClient client,
        DownloadEngine downloads,
        InstanceStore instances,
        FabricLoaderService fabric,
        ForgeLoaderService forge,
        MinecraftInstaller installer,
        JavaDetector java,
        AppPaths paths,
        ILogger<FtbPackInstaller> logger)
    {
        _client = client;
        _downloads = downloads;
        _instances = instances;
        _support = new ModpackInstallSupport(instances, fabric, forge, installer, java, paths, logger);
        _logger = logger;
    }

    /// <summary>
    /// Installs one FTB pack version as a new instance. The pack's own targets decide the Minecraft
    /// version and the loader, so the instance is built for the pack rather than for a guess.
    /// </summary>
    public async Task<ModpackInstallResult> InstallAsync(
        string packId,
        string versionId,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!int.TryParse(packId, out var numericPack) || !int.TryParse(versionId, out var numericVersion))
        {
            throw new ContentProviderException("That is not a Feed The Beast pack id.");
        }

        var project = await _client.GetProjectAsync(packId, cancellationToken).ConfigureAwait(false)
            ?? throw new ContentProviderException($"FTB pack {packId} could not be read.");
        var files = await _client.TryGetVersionAsync(numericPack, numericVersion, cancellationToken)
                .ConfigureAwait(false)
            ?? throw new ContentProviderException(
                $"FTB pack {packId} version {versionId} could not be read.");

        var minecraftVersion = TargetVersion(files.Targets, "game")
            ?? throw new ContentProviderException("The pack does not declare a Minecraft version.");
        var loader = LoaderFrom(TargetName(files.Targets, "modloader"));
        var loaderVersion = loader == LoaderKind.Vanilla ? null : TargetVersion(files.Targets, "modloader");

        var versionName = files.Name ?? versionId;
        var instanceName = $"{project.Title} {versionName}".Trim();
        var versionFullId = loader == LoaderKind.Vanilla || loaderVersion is null
            ? minecraftVersion
            : new LoaderVersionInfo(loader, loaderVersion, minecraftVersion, true, null).VersionId;

        // The support helper reads only the name and the optional target instance from the request; an
        // FTB install has no archive, so the path is left empty deliberately.
        var request = new ModpackInstallRequest { ArchivePath = string.Empty, InstanceName = instanceName };
        var instance = await _support
            .ResolveInstanceAsync(request, instanceName, minecraftVersion, loader, loaderVersion, cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Installing FTB pack {Pack} {Version} ({Loader} {LoaderVersion}, Minecraft {Minecraft})",
            project.Title,
            versionName,
            loader.ToDisplayName(),
            loaderVersion,
            minecraftVersion);

        await _support
            .InstallLoaderAsync(loader, minecraftVersion, loaderVersion, progress, cancellationToken)
            .ConfigureAwait(false);
        await _support
            .InstallMinecraftAsync(instance.Id, versionFullId, progress, cancellationToken)
            .ConfigureAwait(false);

        var gameDirectory = _support.GameDirectory(instance);
        var (downloaded, skipped, warnings) = await DownloadPackFilesAsync(
                files, gameDirectory, progress, cancellationToken)
            .ConfigureAwait(false);

        instance.Modpack = new ModpackIdentity
        {
            Provider = ModpackProvider.Ftb,
            ProjectId = packId,
            VersionId = versionId,
            Name = project.Title,
            VersionName = versionName,
            SourceUrl = $"{FtbClient.WebBase}/{packId}",
            InstalledAt = DateTimeOffset.UtcNow,
        };
        instance.MinecraftVersion = minecraftVersion;
        instance.Loader = loader;
        instance.LoaderVersion = loaderVersion;
        await _instances.SaveAsync(instance, cancellationToken).ConfigureAwait(false);

        return new ModpackInstallResult
        {
            Instance = instance,
            VersionId = versionFullId,
            FilesDownloaded = downloaded,
            FilesSkipped = skipped,
            OverrideFiles = 0,
            Warnings = warnings,
            BackupPath = null,
        };
    }

    /// <summary>
    /// Downloads the pack's files into the instance. A file with no download URL, or one the pack
    /// marks optional, is reported rather than silently dropped; a path that would escape the
    /// instance is refused outright.
    /// </summary>
    private async Task<(int Downloaded, int Skipped, IReadOnlyList<string> Warnings)> DownloadPackFilesAsync(
        FtbVersionFiles files,
        string gameDirectory,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        var (requests, plannedSkipped, plannedWarnings) = PlanFiles(files.Files, gameDirectory);
        var skipped = plannedSkipped;
        var warnings = new List<string>(plannedWarnings);

        var downloaded = 0;
        foreach (var batch in Chunk(requests, MaxConcurrentFiles))
        {
            var summary = await _downloads
                .DownloadAsync(batch, DownloadProgressAdapter.Create(progress), cancellationToken)
                .ConfigureAwait(false);
            downloaded += summary.DownloadedFiles + summary.SkippedFiles;
            skipped += summary.Failures.Count;
            foreach (var failure in summary.Failures)
            {
                warnings.Add($"{Path.GetFileName(failure.TargetPath)}: {failure.Message}");
            }
        }

        Directory.CreateDirectory(gameDirectory);
        _logger.LogInformation(
            "FTB pack files: {Downloaded} downloaded, {Skipped} skipped",
            downloaded,
            skipped);
        return (downloaded, skipped, warnings);
    }

    /// <summary>
    /// Turns a pack's file list into download requests. Optional files and files with no download are
    /// skipped with a reason, and a path that would leave the instance is refused rather than
    /// normalised into something that writes elsewhere.
    /// </summary>
    internal static (IReadOnlyList<DownloadRequest> Requests, int Skipped, IReadOnlyList<string> Warnings)
        PlanFiles(IReadOnlyList<FtbFile> files, string gameDirectory)
    {
        var warnings = new List<string>();
        var requests = new List<DownloadRequest>();
        var skipped = 0;

        foreach (var file in files)
        {
            if (file.Optional)
            {
                skipped++;
                continue;
            }

            if (file.Url is not { Length: > 0 } url || file.Name is not { Length: > 0 } name)
            {
                warnings.Add($"The pack lists \"{file.Name ?? "(unnamed)"}\" without a download.");
                skipped++;
                continue;
            }

            string target;
            try
            {
                var directory = NormalizeDirectory(file.Path);
                var relative = PathSafety.NormalizeRelativePath(
                    directory.Length == 0 ? name : $"{directory}/{name}");
                target = PathSafety.ResolveContained(gameDirectory, relative);
            }
            catch (PathSafetyException exception)
            {
                warnings.Add($"Refused \"{name}\": {exception.Message}");
                skipped++;
                continue;
            }

            requests.Add(new DownloadRequest
            {
                Url = url,
                TargetPath = target,
                ExpectedSha1 = file.Sha1 is { Length: > 0 } sha1 ? sha1 : null,
                ExpectedSize = file.Size > 0 ? file.Size : null,
                Label = name,
            });
        }

        return (requests, skipped, warnings);
    }

    /// <summary>
    /// A pack writes a directory as <c>./mods</c>, <c>config</c>, or <c>.</c>. Only a leading
    /// <c>./</c> is removed: stripping it everywhere would turn a traversal path into a harmless one
    /// instead of letting the containment check refuse it.
    /// </summary>
    private static string NormalizeDirectory(string? path)
    {
        var directory = (path ?? string.Empty).Trim();
        while (directory.StartsWith("./", StringComparison.Ordinal)
               || directory.StartsWith(".\\", StringComparison.Ordinal))
        {
            directory = directory[2..];
        }

        return directory;
    }

    private static IEnumerable<IReadOnlyList<DownloadRequest>> Chunk(
        IReadOnlyList<DownloadRequest> requests,
        int size)
    {
        for (var index = 0; index < requests.Count; index += size)
        {
            yield return requests.Skip(index).Take(size).ToList();
        }
    }

    private static string? TargetVersion(IReadOnlyList<FtbTarget> targets, string type) =>
        targets.FirstOrDefault(target => string.Equals(target.Type, type, StringComparison.OrdinalIgnoreCase))
            ?.Version;

    private static string? TargetName(IReadOnlyList<FtbTarget> targets, string type) =>
        targets.FirstOrDefault(target => string.Equals(target.Type, type, StringComparison.OrdinalIgnoreCase))
            ?.Name;

    private static LoaderKind LoaderFrom(string? name) => name?.ToLowerInvariant() switch
    {
        "forge" => LoaderKind.Forge,
        "neoforge" => LoaderKind.NeoForge,
        "fabric" => LoaderKind.Fabric,
        "quilt" => LoaderKind.Quilt,
        _ => LoaderKind.Vanilla,
    };
}
