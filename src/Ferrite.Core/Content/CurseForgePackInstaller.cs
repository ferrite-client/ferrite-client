using System.Globalization;
using System.Text.Json;
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
/// Installs a CurseForge modpack: read <c>manifest.json</c>, install the declared loader and
/// Minecraft version, resolve every declared file through the CurseForge API, then apply the
/// overrides folder.
/// </summary>
/// <remarks>
/// File metadata comes from the CurseForge API, which requires a user-issued key. A pack whose
/// files were uploaded with third-party distribution disabled cannot be installed without the
/// official launcher; those files are reported rather than silently skipped.
/// </remarks>
public sealed class CurseForgePackInstaller
{
    private const int MaxManifestBytes = 4 * 1024 * 1024;
    private const string ManifestEntryName = "manifest.json";
    private const string DefaultOverridesFolder = "overrides";
    private const string ModsFolder = "mods";

    private readonly CurseForgeClient _client;
    private readonly DownloadEngine _downloads;
    private readonly InstanceStore _instances;
    private readonly ModpackInstallSupport _support;
    private readonly ILogger<CurseForgePackInstaller> _logger;

    public CurseForgePackInstaller(
        CurseForgeClient client,
        DownloadEngine downloads,
        InstanceStore instances,
        FabricLoaderService fabric,
        ForgeLoaderService forge,
        MinecraftInstaller installer,
        JavaDetector java,
        AppPaths paths,
        ILogger<CurseForgePackInstaller> logger)
    {
        _client = client;
        _downloads = downloads;
        _instances = instances;
        _support = new ModpackInstallSupport(instances, fabric, forge, installer, java, paths, logger);
        _logger = logger;
    }

    public async Task<ModpackInstallResult> InstallAsync(
        ModpackInstallRequest request,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!File.Exists(request.ArchivePath))
        {
            throw new ContentProviderException($"Modpack archive not found: {request.ArchivePath}");
        }

        var manifest = ReadManifest(request.ArchivePath);
        var minecraftVersion = manifest.Minecraft?.Version;
        if (string.IsNullOrWhiteSpace(minecraftVersion))
        {
            throw new ContentProviderException("The modpack does not declare a Minecraft version.");
        }

        if (!_client.IsConfigured)
        {
            throw new ContentProviderException(_client.UnavailableReason
                ?? "CurseForge needs an API key before its modpacks can be installed.");
        }

        var (loader, loaderVersion) = ResolveLoader(manifest);
        var versionId = loader == LoaderKind.Vanilla || loaderVersion is null
            ? minecraftVersion
            : new LoaderVersionInfo(loader, loaderVersion, minecraftVersion, true, null).VersionId;

        var warnings = new List<string>();
        var instance = await _support
            .ResolveInstanceAsync(
                request,
                manifest.Name ?? "Modpack",
                minecraftVersion,
                loader,
                loaderVersion,
                cancellationToken)
            .ConfigureAwait(false);
        var gameDirectory = _support.GameDirectory(instance);

        var backupPath = _support.BackupIfNeeded(request, instance, warnings);

        _logger.LogInformation(
            "Installing CurseForge modpack {Name} {Version} ({Loader} {LoaderVersion}, Minecraft {Minecraft})",
            manifest.Name,
            manifest.Version,
            loader.ToDisplayName(),
            loaderVersion,
            minecraftVersion);

        await _support
            .InstallLoaderAsync(loader, minecraftVersion, loaderVersion, progress, cancellationToken)
            .ConfigureAwait(false);

        await _support
            .InstallMinecraftAsync(instance.Id, versionId, progress, cancellationToken)
            .ConfigureAwait(false);

        var (downloaded, skipped) = await DownloadPackFilesAsync(
                manifest,
                gameDirectory,
                warnings,
                progress,
                cancellationToken)
            .ConfigureAwait(false);

        var overrideFolder = string.IsNullOrWhiteSpace(manifest.Overrides)
            ? DefaultOverridesFolder
            : manifest.Overrides!;
        var overrideFiles = await ModpackInstallSupport
            .ExtractOverridesAsync(
                request.ArchivePath,
                gameDirectory,
                [overrideFolder],
                cancellationToken)
            .ConfigureAwait(false);

        instance.Modpack = new ModpackIdentity
        {
            Provider = ModpackProvider.CurseForge,
            VersionId = manifest.Version,
            Name = manifest.Name,
            VersionName = manifest.Version,
            SourceUrl = request.SourceUrl,
            InstalledAt = DateTimeOffset.UtcNow,
        };
        instance.MinecraftVersion = minecraftVersion;
        instance.Loader = loader;
        instance.LoaderVersion = loaderVersion;
        await _instances.SaveAsync(instance, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "CurseForge modpack installed into {Instance}: {Downloaded} file(s), {Overrides} override(s)",
            instance.Name,
            downloaded,
            overrideFiles);

        return new ModpackInstallResult
        {
            Instance = instance,
            VersionId = versionId,
            FilesDownloaded = downloaded,
            FilesSkipped = skipped,
            OverrideFiles = overrideFiles,
            Warnings = warnings,
            BackupPath = backupPath,
        };
    }

    /// <summary>Reads and validates a pack manifest without installing anything.</summary>
    public static CurseForgeManifest ReadManifest(string archivePath)
    {
        var json = ArchiveExtractor.ReadEntryText(archivePath, ManifestEntryName, MaxManifestBytes)
            ?? throw new ContentProviderException(
                "The archive is not a CurseForge modpack: no manifest.json at its root.");

        CurseForgeManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<CurseForgeManifest>(json, Json.JsonDefaults.Remote)
                ?? throw new ContentProviderException("manifest.json was empty.");
        }
        catch (JsonException exception)
        {
            throw new ContentProviderException("manifest.json is not valid JSON.", exception);
        }

        if (string.IsNullOrWhiteSpace(manifest.Minecraft?.Version))
        {
            throw new ContentProviderException("manifest.json does not declare a Minecraft version.");
        }

        return manifest;
    }

    /// <summary>Maps the manifest's loader entries onto a loader kind and version.</summary>
    public static (LoaderKind Loader, string? Version) ResolveLoader(CurseForgeManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var loaders = manifest.Minecraft?.ModLoaders ?? [];
        var primary = loaders.FirstOrDefault(loader => loader.Primary) ?? loaders.FirstOrDefault();
        if (primary?.Id is not { Length: > 0 } id)
        {
            return (LoaderKind.Vanilla, null);
        }

        var separator = id.IndexOf('-', StringComparison.Ordinal);
        if (separator <= 0 || separator == id.Length - 1)
        {
            return (LoaderKind.Vanilla, null);
        }

        var kind = ContentCompatibility.LoaderKindFor(id[..separator]);
        return kind is null ? (LoaderKind.Vanilla, null) : (kind.Value, id[(separator + 1)..]);
    }

    private async Task<(int Downloaded, int Skipped)> DownloadPackFilesAsync(
        CurseForgeManifest manifest,
        string gameDirectory,
        List<string> warnings,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        var declared = manifest.Files.Where(file => file.FileID > 0).ToList();
        if (declared.Count == 0)
        {
            return (0, 0);
        }

        progress?.Report(new InstallProgress
        {
            Stage = InstallStage.ResolvingMetadata,
            Message = $"CurseForge files ({declared.Count})",
        });

        var resolved = await _client
            .GetFilesAsync(declared.Select(file => file.FileID).ToList(), cancellationToken)
            .ConfigureAwait(false);
        var plan = PlanFiles(declared, resolved, gameDirectory);
        warnings.AddRange(plan.Warnings);
        var requests = plan.Requests;
        var skipped = plan.Skipped;

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

    /// <summary>
    /// Turns the manifest's declared files plus the metadata the API returned into download
    /// requests. Files without a URL are reported rather than silently dropped: CurseForge withholds
    /// the URL when an author disabled third-party distribution.
    /// </summary>
    public static CurseForgeFilePlan PlanFiles(
        IReadOnlyList<CurseForgeManifestFile> declared,
        IReadOnlyList<ContentVersion> resolved,
        string gameDirectory)
    {
        ArgumentNullException.ThrowIfNull(declared);
        ArgumentNullException.ThrowIfNull(resolved);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDirectory);

        var byFileId = new Dictionary<int, ContentVersion>();
        foreach (var version in resolved)
        {
            if (int.TryParse(version.VersionId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            {
                byFileId[id] = version;
            }
        }

        var modsDirectory = Path.Combine(gameDirectory, ModsFolder);
        var requests = new List<DownloadRequest>();
        var warnings = new List<string>();
        var skipped = 0;

        foreach (var file in declared)
        {
            if (!byFileId.TryGetValue(file.FileID, out var version))
            {
                warnings.Add(
                    $"CurseForge file {file.FileID} of project {file.ProjectID} is no longer available.");
                skipped++;
                continue;
            }

            var download = version.PrimaryFile;
            if (download is null || string.IsNullOrEmpty(download.Url))
            {
                warnings.Add(
                    $"{version.VersionNumber} cannot be downloaded: the author has not allowed "
                    + "third-party distribution.");
                skipped++;
                continue;
            }

            var fileName = PathSafety.SanitizeFileName(download.FileName);
            var target = Path.Combine(modsDirectory, fileName);
            if (!PathSafety.IsContained(gameDirectory, target))
            {
                warnings.Add($"Skipped {download.FileName}: the target path escaped the instance.");
                skipped++;
                continue;
            }

            requests.Add(new DownloadRequest
            {
                Url = download.Url,
                TargetPath = target,
                ExpectedSha1 = download.Sha1,
                ExpectedSha512 = download.Sha512,
                ExpectedSize = download.Size > 0 ? download.Size : null,
                Label = fileName,
            });
        }

        return new CurseForgeFilePlan(requests, warnings, skipped);
    }
}

/// <summary>Download requests a CurseForge pack resolves to, plus what could not be resolved.</summary>
public sealed record CurseForgeFilePlan(
    IReadOnlyList<DownloadRequest> Requests,
    IReadOnlyList<string> Warnings,
    int Skipped);
