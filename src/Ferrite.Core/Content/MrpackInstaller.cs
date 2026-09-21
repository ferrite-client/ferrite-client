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
/// Installs a Modrinth modpack: resolve the pack's loader and game version, install them, download
/// the declared files with their hashes, then apply the overrides.
/// </summary>
public sealed partial class MrpackInstaller
{
    private const int MaxIndexBytes = 16 * 1024 * 1024;
    private const string IndexEntryName = "modrinth.index.json";

    private readonly DownloadEngine _downloads;
    private readonly InstanceStore _instances;
    private readonly ModpackInstallSupport _support;
    private readonly ILogger<MrpackInstaller> _logger;

    public MrpackInstaller(
        DownloadEngine downloads,
        InstanceStore instances,
        FabricLoaderService fabric,
        ForgeLoaderService forge,
        MinecraftInstaller installer,
        JavaDetector java,
        AppPaths paths,
        ILogger<MrpackInstaller> logger)
    {
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

        var index = ReadIndex(request.ArchivePath);
        if (index.FormatVersion != 1)
        {
            throw new ContentProviderException($"Modpack format version {index.FormatVersion} is not supported.");
        }

        if (!index.Dependencies.TryGetValue("minecraft", out var minecraftVersion)
            || string.IsNullOrWhiteSpace(minecraftVersion))
        {
            throw new ContentProviderException("The modpack does not declare a Minecraft version.");
        }

        var (loader, loaderVersion) = ResolveLoader(index.Dependencies);
        var versionId = loader == LoaderKind.Vanilla || loaderVersion is null
            ? minecraftVersion
            : new LoaderVersionInfo(loader, loaderVersion, minecraftVersion, true, null).VersionId;

        var warnings = new List<string>();
        var instance = await _support
            .ResolveInstanceAsync(
                request,
                index.Name ?? "Modpack",
                minecraftVersion,
                loader,
                loaderVersion,
                cancellationToken)
            .ConfigureAwait(false);
        var gameDirectory = _support.GameDirectory(instance);

        var backupPath = _support.BackupIfNeeded(request, instance, warnings);

        _logger.LogInformation(
            "Installing modpack {Name} {Version} ({Loader} {LoaderVersion}, Minecraft {Minecraft})",
            index.Name,
            index.VersionId,
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
                index, gameDirectory, warnings, progress, cancellationToken)
            .ConfigureAwait(false);

        var overrideFiles = await ModpackInstallSupport
            .ExtractOverridesAsync(
                request.ArchivePath,
                gameDirectory,
                ["overrides", "client-overrides"],
                cancellationToken)
            .ConfigureAwait(false);

        instance.Modpack = new ModpackIdentity
        {
            Provider = ModpackProvider.Modrinth,
            VersionId = index.VersionId,
            Name = index.Name,
            VersionName = index.VersionId,
            SourceUrl = request.SourceUrl,
            InstalledAt = DateTimeOffset.UtcNow,
        };
        instance.MinecraftVersion = minecraftVersion;
        instance.Loader = loader;
        instance.LoaderVersion = loaderVersion;
        await _instances.SaveAsync(instance, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Modpack installed into {Instance}: {Downloaded} file(s), {Overrides} override(s)",
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

    /// <summary>Reads and validates a pack index without installing anything.</summary>
    public ModrinthIndex ReadIndex(string archivePath)
    {
        var json = ArchiveExtractor.ReadEntryText(archivePath, IndexEntryName, MaxIndexBytes)
            ?? throw new ContentProviderException(
                "The archive is not a Modrinth modpack: no modrinth.index.json.");

        try
        {
            var index = JsonSerializer.Deserialize<ModrinthIndex>(json, Ferrite.Core.Json.JsonDefaults.Remote)
                ?? throw new ContentProviderException("modrinth.index.json was empty.");
            if (index.Files.Count == 0 && index.Dependencies.Count == 0)
            {
                throw new ContentProviderException("modrinth.index.json declares no files or dependencies.");
            }

            return index;
        }
        catch (JsonException exception)
        {
            throw new ContentProviderException("modrinth.index.json is not valid JSON.", exception);
        }
    }

    /// <summary>Maps the pack's dependency map onto a loader kind and version.</summary>
    public static (LoaderKind Loader, string? Version) ResolveLoader(
        IReadOnlyDictionary<string, string> dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        foreach (var (key, value) in dependencies)
        {
            LoaderKind? kind = key.ToLowerInvariant() switch
            {
                "fabric-loader" => LoaderKind.Fabric,
                "quilt-loader" => LoaderKind.Quilt,
                "neoforge" => LoaderKind.NeoForge,
                "forge" => LoaderKind.Forge,
                _ => null,
            };

            if (kind is { } resolved && !string.IsNullOrWhiteSpace(value))
            {
                return (resolved, value);
            }
        }

        return (LoaderKind.Vanilla, null);
    }
}
