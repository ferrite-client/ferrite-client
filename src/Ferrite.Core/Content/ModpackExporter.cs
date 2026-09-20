using System.IO.Compression;
using System.Text.Json;
using Ferrite.Core.Loaders;
using Ferrite.Core.Minecraft;
using Ferrite.Core.Platform;
using Ferrite.Core.Storage;
using Ferrite.Core.Util;
using Microsoft.Extensions.Logging;

namespace Ferrite.Core.Content;

/// <summary>
/// Exports an instance as a Modrinth modpack.
/// <para>
/// The exported pack is self-contained: instance content is written under <c>overrides/</c> and the
/// declared <c>files[]</c> list is empty. That is deliberate. A locally installed mod has no reliable
/// provider download URL, and inventing one would produce a pack that fails to install elsewhere.
/// Shipping the bytes as overrides always installs correctly, and the loader dependency is still
/// declared so the receiving launcher installs the right loader.
/// </para>
/// </summary>
public sealed class ModpackExporter
{
    private static readonly string[] ExcludedDirectories =
    [
        "logs",
        "screenshots",
        "crash-reports",
        ".cache",
        "quickPlay",
        "natives",
    ];

    private readonly AppPaths _paths;
    private readonly ILogger<ModpackExporter> _logger;

    public ModpackExporter(AppPaths paths, ILogger<ModpackExporter> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public async Task<ModpackExportResult> ExportAsync(
        InstanceRecord instance,
        string outputPath,
        bool includeSaves,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        var gameDirectory = _paths.InstanceGameDirectory(instance.Id);
        if (!Directory.Exists(gameDirectory))
        {
            throw new ContentProviderException($"Instance {instance.Name} has no game directory to export.");
        }

        var versionId = instance.Modpack?.VersionId ?? $"ferrite-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        var index = new ModrinthIndex
        {
            FormatVersion = 1,
            Game = "minecraft",
            VersionId = versionId,
            Name = instance.Name,
            Summary = $"Exported from Ferrite on {DateTimeOffset.UtcNow:yyyy-MM-dd}",
            Dependencies = BuildDependencies(instance),
            Files = [],
        };

        var staging = Path.Combine(_paths.TemporaryDirectory, "export-" + Guid.NewGuid().ToString("N"));
        var overridesDirectory = Path.Combine(staging, "overrides");
        Directory.CreateDirectory(overridesDirectory);

        try
        {
            var overrideCount = CopyOverrides(gameDirectory, overridesDirectory, includeSaves, cancellationToken);
            var indexPath = Path.Combine(staging, "modrinth.index.json");
            await AtomicFile
                .WriteAllTextAsync(
                    indexPath,
                    JsonSerializer.Serialize(index, Ferrite.Core.Json.JsonDefaults.Document),
                    cancellationToken)
                .ConfigureAwait(false);

            await ZipDirectoryAsync(staging, outputPath, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Exported {Instance} as {Path} ({Overrides} override file(s))",
                instance.Name,
                outputPath,
                overrideCount);

            return new ModpackExportResult(outputPath, index.Files.Count, overrideCount, versionId);
        }
        finally
        {
            TryDeleteDirectory(staging);
        }
    }

    /// <summary>Loader dependencies for an instance, in the form a Modrinth pack expects.</summary>
    public static Dictionary<string, string> BuildDependencies(InstanceRecord instance)
    {
        var dependencies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["minecraft"] = instance.MinecraftVersion,
        };

        if (instance.Loader == LoaderKind.Vanilla || string.IsNullOrEmpty(instance.LoaderVersion))
        {
            return dependencies;
        }

        var key = instance.Loader switch
        {
            LoaderKind.Fabric => "fabric-loader",
            LoaderKind.Quilt => "quilt-loader",
            LoaderKind.NeoForge => "neoforge",
            LoaderKind.Forge => "forge",
            _ => null,
        };

        if (key is not null)
        {
            dependencies[key] = instance.LoaderVersion!;
        }

        return dependencies;
    }

    private static int CopyOverrides(
        string gameDirectory,
        string overridesDirectory,
        bool includeSaves,
        CancellationToken cancellationToken)
    {
        var count = 0;
        foreach (var file in Directory.EnumerateFiles(gameDirectory, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(gameDirectory, file);
            if (IsExcluded(relative, includeSaves))
            {
                continue;
            }

            var target = Path.Combine(overridesDirectory, relative);
            var directory = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.Copy(file, target, overwrite: true);
            count++;
        }

        return count;
    }

    private static bool IsExcluded(string relativePath, bool includeSaves)
    {
        var segments = relativePath.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]);
        if (segments.Length == 0)
        {
            return false;
        }

        var top = segments[0];
        if (ExcludedDirectories.Contains(top, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        return !includeSaves && string.Equals(top, "saves", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task ZipDirectoryAsync(
        string sourceDirectory,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporary = outputPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 128 * 1024,
                useAsync: true))
            {
                using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
                foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var relative = Path.GetRelativePath(sourceDirectory, file).Replace('\\', '/');
                    var entry = archive.CreateEntry(relative, CompressionLevel.Optimal);
                    await using var entryStream = entry.Open();
                    await using var source = new FileStream(
                        file,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        bufferSize: 128 * 1024,
                        useAsync: true);
                    await source.CopyToAsync(entryStream, 128 * 1024, cancellationToken).ConfigureAwait(false);
                }
            }

            File.Move(temporary, outputPath, overwrite: true);
        }
        catch
        {
            AtomicFile.TryDelete(temporary);
            throw;
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }
}
