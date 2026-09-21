using Ferrite.Core.Java;
using Ferrite.Core.Loaders;
using Ferrite.Core.Minecraft;
using Ferrite.Core.Platform;
using Ferrite.Core.Rules;
using Ferrite.Core.Storage;
using Ferrite.Core.Util;
using Microsoft.Extensions.Logging;

namespace Ferrite.Core.Content;

/// <summary>
/// Install steps shared by the Modrinth and CurseForge modpack installers: instance resolution,
/// loader installation, and the backup that protects existing content.
/// </summary>
internal sealed class ModpackInstallSupport
{
    private readonly InstanceStore _instances;
    private readonly FabricLoaderService _fabric;
    private readonly ForgeLoaderService _forge;
    private readonly MinecraftInstaller _installer;
    private readonly JavaDetector _java;
    private readonly AppPaths _paths;
    private readonly ILogger _logger;

    public ModpackInstallSupport(
        InstanceStore instances,
        FabricLoaderService fabric,
        ForgeLoaderService forge,
        MinecraftInstaller installer,
        JavaDetector java,
        AppPaths paths,
        ILogger logger)
    {
        _instances = instances;
        _fabric = fabric;
        _forge = forge;
        _installer = installer;
        _java = java;
        _paths = paths;
        _logger = logger;
    }

    public async Task<InstanceRecord> ResolveInstanceAsync(
        ModpackInstallRequest request,
        string fallbackName,
        string minecraftVersion,
        LoaderKind loader,
        string? loaderVersion,
        CancellationToken cancellationToken)
    {
        if (request.TargetInstanceId is { } id)
        {
            return await _instances.TryLoadAsync(id, cancellationToken).ConfigureAwait(false)
                ?? throw new InstanceNotFoundException(id);
        }

        var name = request.InstanceName
            ?? (string.IsNullOrWhiteSpace(fallbackName) ? "Modpack" : fallbackName);

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

    public async Task InstallLoaderAsync(
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

    public async Task InstallMinecraftAsync(
        Guid instanceId,
        string versionId,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken) =>
        await _installer
            .InstallAsync(instanceId, versionId, RuleContext.ForHost(), progress, cancellationToken)
            .ConfigureAwait(false);

    public string? BackupIfNeeded(ModpackInstallRequest request, InstanceRecord instance, List<string> warnings)
    {
        var gameDirectory = GameDirectory(instance);
        if (!request.BackupExisting || !HasUserContent(gameDirectory))
        {
            return null;
        }

        var backupPath = BackupGameDirectory(instance, gameDirectory);
        warnings.Add($"Existing instance content was backed up to {backupPath}");
        return backupPath;
    }

    public string GameDirectory(InstanceRecord instance) => _paths.InstanceGameDirectory(instance.Id);

    /// <summary>
    /// Applies a pack's override folders on top of the instance, stripping the folder prefix so the
    /// files land where Minecraft expects them. Extraction refuses entries that escape the target.
    /// </summary>
    public static async Task<int> ExtractOverridesAsync(
        string archivePath,
        string gameDirectory,
        IReadOnlyList<string> prefixes,
        CancellationToken cancellationToken)
    {
        var total = 0;
        foreach (var prefix in prefixes)
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

    public static bool HasUserContent(string gameDirectory)
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
