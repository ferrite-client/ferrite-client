using System.Text.Json;
using Ferrite.Core.Download;
using Ferrite.Core.Json;
using Ferrite.Core.Net;
using Ferrite.Core.Platform;
using Ferrite.Core.Rules;
using Ferrite.Core.Util;
using Microsoft.Extensions.Logging;

namespace Ferrite.Core.Minecraft;

/// <summary>
/// Turns a resolved version document into a concrete, host-specific artefact set. This is the
/// only place that decides what belongs on the classpath, what gets extracted, and what gets
/// downloaded.
/// </summary>
public sealed class InstallPlanner
{
    public const string DefaultLibraryBaseUrl = "https://libraries.minecraft.net/";
    public const string DefaultResourceBaseUrl = "https://resources.download.minecraft.net/";

    private const int MaxAssetIndexBytes = 64 * 1024 * 1024;

    private readonly HttpService _http;
    private readonly AppPaths _paths;
    private readonly ILogger<InstallPlanner> _logger;

    public InstallPlanner(HttpService http, AppPaths paths, ILogger<InstallPlanner> logger)
    {
        _http = http;
        _paths = paths;
        _logger = logger;
    }

    public async Task<InstallPlan> CreateAsync(
        VersionDocument document,
        string versionId,
        RuleContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(context);

        var downloads = new List<DownloadRequest>();
        var libraries = new List<ResolvedLibrary>();
        var natives = new List<NativeEntry>();
        long totalBytes = 0;

        foreach (var library in document.ResolvedLibraries.Count > 0 ? document.ResolvedLibraries : document.Libraries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!RuleEvaluator.IsAllowed(library.Rules, context))
            {
                continue;
            }

            if (!MavenCoordinates.TryParse(library.Name, out var coordinates))
            {
                _logger.LogWarning("Skipping library with unparsable coordinates: {Name}", library.Name);
                continue;
            }

            var target = Path.Combine(_paths.LibrariesDirectory, coordinates.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            var artifact = ResolveArtifact(library, coordinates);
            if (artifact is null)
            {
                continue;
            }

            downloads.Add(artifact);
            totalBytes += artifact.ExpectedSize ?? 0;
            libraries.Add(new ResolvedLibrary(coordinates, artifact.TargetPath, artifact, coordinates.IsNativeClassifier));

            var nativeRequest = ResolveNativeRequest(library, coordinates, context);
            if (nativeRequest is not null)
            {
                downloads.Add(nativeRequest);
                totalBytes += nativeRequest.ExpectedSize ?? 0;
                natives.Add(new NativeEntry(nativeRequest.TargetPath, library.Extract?.Exclude ?? []));
            }
            else if (coordinates.IsNativeClassifier)
            {
                natives.Add(new NativeEntry(artifact.TargetPath, library.Extract?.Exclude ?? []));
            }
        }

        string? clientJarPath = null;
        if (document.Downloads?.Client is { } client && !string.IsNullOrEmpty(client.Url))
        {
            clientJarPath = _paths.VersionClientJarFile(versionId);
            var request = new DownloadRequest
            {
                Url = client.Url,
                TargetPath = clientJarPath,
                ExpectedSha1 = client.Sha1,
                ExpectedSize = client.Size,
                Label = $"{versionId} client jar",
            };
            downloads.Add(request);
            totalBytes += client.Size ?? 0;
        }

        string? loggingConfigPath = null;
        if (document.Logging?.Client?.File is { } loggingFile && !string.IsNullOrEmpty(loggingFile.Url))
        {
            var fileName = PathSafety.SanitizeFileName(loggingFile.Id ?? "client-logging.xml");
            loggingConfigPath = Path.Combine(_paths.VersionDirectory(versionId), fileName);
            downloads.Add(new DownloadRequest
            {
                Url = loggingFile.Url,
                TargetPath = loggingConfigPath,
                ExpectedSha1 = loggingFile.Sha1,
                ExpectedSize = loggingFile.Size,
                Label = fileName,
            });
            totalBytes += loggingFile.Size ?? 0;
        }

        var assets = new List<AssetObjectEntry>();
        string? assetIndexPath = null;
        var virtualAssets = false;
        if (document.AssetIndex is { } index && !string.IsNullOrEmpty(index.Url))
        {
            assetIndexPath = Path.Combine(
                _paths.AssetIndexesDirectory,
                PathSafety.SanitizeFileName(index.Id ?? document.Assets ?? "index") + ".json");

            var indexDocument = await LoadAssetIndexAsync(index, assetIndexPath, cancellationToken).ConfigureAwait(false);
            if (indexDocument is not null)
            {
                virtualAssets = indexDocument.Info?.Virtual == true || indexDocument.Info?.MapToResources == true;
                var seenHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var (name, asset) in indexDocument.Objects)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (string.IsNullOrEmpty(asset.Hash) || asset.Hash.Length < 2)
                    {
                        continue;
                    }

                    assets.Add(new AssetObjectEntry(name, asset.Hash, asset.Size));
                    if (!seenHashes.Add(asset.Hash))
                    {
                        continue;
                    }

                    var relative = asset.Hash[..2] + "/" + asset.Hash;
                    var target = Path.Combine(
                        _paths.AssetObjectsDirectory,
                        asset.Hash[..2],
                        asset.Hash);
                    downloads.Add(new DownloadRequest
                    {
                        Url = DefaultResourceBaseUrl + relative,
                        TargetPath = target,
                        ExpectedSha1 = asset.Hash,
                        ExpectedSize = asset.Size,
                        Label = "asset " + asset.Hash[..Math.Min(8, asset.Hash.Length)],
                    });
                    totalBytes += asset.Size;
                }
            }
        }

        return new InstallPlan
        {
            VersionId = versionId,
            Document = document,
            Downloads = downloads,
            Libraries = libraries,
            Natives = natives,
            Assets = assets,
            AssetIndex = document.AssetIndex,
            AssetIndexPath = assetIndexPath,
            ClientJarPath = clientJarPath,
            LoggingConfig = document.Logging?.Client?.File,
            LoggingConfigPath = loggingConfigPath,
            TotalBytes = totalBytes,
            VirtualAssets = virtualAssets,
        };
    }

    private DownloadRequest? ResolveArtifact(Library library, MavenCoordinates coordinates)
    {
        var explicitArtifact = library.Downloads?.Artifact;
        var relative = explicitArtifact?.Path ?? coordinates.RelativePath;
        var target = Path.Combine(_paths.LibrariesDirectory, relative.Replace('/', Path.DirectorySeparatorChar));

        var url = explicitArtifact?.Url;
        if (string.IsNullOrEmpty(url))
        {
            var baseUrl = string.IsNullOrEmpty(library.Url) ? DefaultLibraryBaseUrl : library.Url!;
            url = baseUrl.TrimEnd('/') + "/" + coordinates.RelativePath;
        }

        return new DownloadRequest
        {
            Url = url,
            TargetPath = target,
            ExpectedSha1 = explicitArtifact?.Sha1 ?? library.Sha1,
            ExpectedSize = explicitArtifact?.Size ?? library.Size,
            Label = coordinates.FileName,
        };
    }

    /// <summary>
    /// Resolves the legacy natives format, where a classifier inside
    /// <c>downloads.classifiers</c> supplies the OS-specific archive.
    /// </summary>
    private DownloadRequest? ResolveNativeRequest(
        Library library,
        MavenCoordinates coordinates,
        RuleContext context)
    {
        if (library.Natives is not { Count: > 0 } natives
            || !natives.TryGetValue(context.MojangOsName, out var classifier))
        {
            return null;
        }

        if (library.Downloads?.Classifiers is not { } classifiers
            || !classifiers.TryGetValue(classifier, out var artifact)
            || string.IsNullOrEmpty(artifact.Url))
        {
            _logger.LogWarning(
                "Native classifier {Classifier} for {Name} has no downloadable artifact",
                classifier,
                library.Name);
            return null;
        }

        var relative = artifact.Path
            ?? new MavenCoordinates(coordinates.Group, coordinates.Artifact, coordinates.Version, classifier).RelativePath;
        var target = Path.Combine(_paths.LibrariesDirectory, relative.Replace('/', Path.DirectorySeparatorChar));

        return new DownloadRequest
        {
            Url = artifact.Url,
            TargetPath = target,
            ExpectedSha1 = artifact.Sha1,
            ExpectedSize = artifact.Size,
            Label = classifier,
        };
    }

    private async Task<AssetIndexDocument?> LoadAssetIndexAsync(
        AssetIndexReference reference,
        string targetPath,
        CancellationToken cancellationToken)
    {
        if (File.Exists(targetPath))
        {
            var valid = await Hashing
                .VerifyAsync(targetPath, reference.Size, reference.Sha1, cancellationToken)
                .ConfigureAwait(false);
            if (valid)
            {
                var parsed = TryParseIndex(targetPath);
                if (parsed is not null)
                {
                    return parsed;
                }
            }
        }

        var bytes = await _http
            .GetBytesAsync(reference.Url!, MaxAssetIndexBytes, cancellationToken)
            .ConfigureAwait(false);
        await AtomicFile.WriteAllBytesAsync(targetPath, bytes, cancellationToken).ConfigureAwait(false);

        try
        {
            return JsonSerializer.Deserialize<AssetIndexDocument>(bytes, JsonDefaults.Remote)
                ?? throw new VersionMetadataException("Asset index was empty.");
        }
        catch (JsonException exception)
        {
            throw new VersionMetadataException("Asset index is not valid JSON.", exception);
        }
    }

    private AssetIndexDocument? TryParseIndex(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            return JsonSerializer.Deserialize<AssetIndexDocument>(bytes, JsonDefaults.Remote);
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            _logger.LogWarning(exception, "Cached asset index at {Path} could not be read", path);
            return null;
        }
    }
}
