using System.Text.Json;
using Ferrite.Core.Download;
using Ferrite.Core.Json;
using Ferrite.Core.Minecraft;
using Ferrite.Core.Net;
using Ferrite.Core.Platform;
using Ferrite.Core.Rules;
using Ferrite.Core.Util;
using Microsoft.Extensions.Logging;

namespace Ferrite.Core.Loaders;

/// <summary>What a LabyMod install produced.</summary>
public sealed record LabyModInstallResult(
    string VersionId,
    string LabyModVersion,
    int LibraryCount,
    int AssetCount);

/// <summary>A written LabyMod version profile.</summary>
public sealed record LabyModProfile(string VersionId, int LibraryCount);

/// <summary>
/// Installs LabyMod 4, which behaves like a loader rather than like an installer: LabyMod publishes a
/// manifest, a library list, and one version document per Minecraft version, and the launcher is
/// meant to merge them into its own version store. This does exactly that, then hands the version to
/// the same installer every other version goes through, so the client, the libraries, the natives,
/// and the assets are resolved by the code that already does it.
/// </summary>
public sealed class LabyModInstaller
{
    public const string ManifestUrl =
        "https://laby-releases.s3.de.io.cloud.ovh.net/api/v1/manifest/production/latest.json";

    public const string LibrariesUrl =
        "https://laby-releases.s3.de.io.cloud.ovh.net/api/v1/libraries/production.json";

    public const string DownloadBase =
        "https://laby-releases.s3.de.io.cloud.ovh.net/api/v1/download";

    /// <summary>Where LabyMod's own assets live, relative to the game directory.</summary>
    public const string AssetFolder = "labymod-neo/assets";

    private const int MaxManifestBytes = 1024 * 1024;
    private const int MaxVersionBytes = 4 * 1024 * 1024;
    private const int MaxLibrariesBytes = 8 * 1024 * 1024;

    private readonly HttpService _http;
    private readonly DownloadEngine _downloads;
    private readonly MinecraftInstaller _installer;
    private readonly AppPaths _paths;
    private readonly ILogger<LabyModInstaller> _logger;
    private readonly string _manifestUrl;
    private readonly string _librariesUrl;
    private readonly string _downloadBase;

    /// <param name="manifestUrl">Overrides the manifest address; used by tests with a local server.</param>
    public LabyModInstaller(
        HttpService http,
        DownloadEngine downloads,
        MinecraftInstaller installer,
        AppPaths paths,
        ILogger<LabyModInstaller> logger,
        string? manifestUrl = null,
        string? librariesUrl = null,
        string? downloadBase = null)
    {
        _http = http;
        _downloads = downloads;
        _installer = installer;
        _paths = paths;
        _logger = logger;
        _manifestUrl = manifestUrl ?? ManifestUrl;
        _librariesUrl = librariesUrl ?? LibrariesUrl;
        _downloadBase = (downloadBase ?? DownloadBase).TrimEnd('/');
    }

    /// <summary>The version id a merged LabyMod build installs as.</summary>
    public static string VersionIdFor(LabyModManifest manifest, string minecraftVersion)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(minecraftVersion);
        return $"{minecraftVersion}-LabyMod-4-{manifest.CommitReference}";
    }

    public async Task<LabyModManifest> GetManifestAsync(CancellationToken cancellationToken)
    {
        var manifest = await _http
            .TryGetJsonAsync<LabyModManifest>(_manifestUrl, MaxManifestBytes, cancellationToken)
            .ConfigureAwait(false);
        if (manifest?.CommitReference is not { Length: > 0 } || manifest.LabyModVersion is not { Length: > 0 })
        {
            throw new LoaderException("LabyMod's manifest could not be read.");
        }

        return manifest;
    }

    /// <summary>
    /// Writes the merged version document into the store. The published document is already complete -
    /// it names its assets, its client, its arguments, and its main class - so the merge adds the
    /// LabyMod libraries and the client jar rather than rebuilding it.
    /// </summary>
    public async Task<LabyModProfile> WriteProfileAsync(
        LabyModManifest manifest,
        string minecraftVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(minecraftVersion);

        var entry = manifest.MinecraftVersions.FirstOrDefault(version =>
            string.Equals(version.Tag, minecraftVersion, StringComparison.OrdinalIgnoreCase));
        if (entry?.CustomManifestUrl is not { Length: > 0 } manifestUrl)
        {
            throw new LoaderException($"LabyMod does not offer a build for Minecraft {minecraftVersion}.");
        }

        var document = await _http
            .TryGetJsonAsync<VersionDocument>(manifestUrl, MaxVersionBytes, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new LoaderException($"LabyMod's version document could not be read from {manifestUrl}.");

        var libraries = await _http
            .TryGetJsonAsync<LabyModLibrariesDocument>(_librariesUrl, MaxLibrariesBytes, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new LoaderException("LabyMod's library list could not be read.");

        var versionId = VersionIdFor(manifest, minecraftVersion);
        foreach (var library in libraries.Libraries.Where(library =>
                     string.Equals(library.MinecraftVersion, "all", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(library.MinecraftVersion, minecraftVersion, StringComparison.OrdinalIgnoreCase)))
        {
            if (library.Name is not { Length: > 0 } name || library.Url is not { Length: > 0 } url)
            {
                continue;
            }

            document.Libraries.Add(WithExplicitDownload(
                name,
                url,
                library.Sha1,
                library.Size));
        }

        document.Libraries.Add(WithExplicitDownload(
            $"net.labymod:LabyMod:{manifest.LabyModVersion}",
            $"{_downloadBase}/labymod4/production/{manifest.CommitReference}.jar",
            manifest.Sha1,
            // The manifest's size is 14 bytes short of the file it publishes, so only the hash is
            // enforced. The hash was checked against the live file when this was written.
            size: 0));

        // The id is what the instance will launch, so it has to be this build's own.
        document.Id = versionId;
        var directory = _paths.VersionDirectory(versionId);
        Directory.CreateDirectory(directory);
        await AtomicFile.WriteAllTextAsync(
                _paths.VersionJsonFile(versionId),
                JsonSerializer.Serialize(document, JsonDefaults.Document),
                cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Wrote the LabyMod profile {VersionId}: {Libraries} library entry(ies)",
            versionId,
            document.Libraries.Count);
        return new LabyModProfile(versionId, document.Libraries.Count);
    }

    /// <summary>
    /// Adds a library with a complete download URL. LabyMod's URLs are the file's own address, not a
    /// repository base, so they go in an explicit artifact together with the maven path the file would
    /// have - without that path the entry cannot be placed in the store.
    /// </summary>
    private static Library WithExplicitDownload(string name, string url, string? sha1, long size)
    {
        if (!MavenCoordinates.TryParse(name, out var coordinates))
        {
            // A name that is not maven coordinates still has to be downloaded, so the URL is used as
            // the file name and the planner is left to place it by name.
            return new Library
            {
                Name = name,
                Downloads = new LibraryDownloads
                {
                    Artifact = new DownloadArtifact
                    {
                        Path = name,
                        Url = url,
                        Sha1 = sha1 is { Length: > 0 } value ? value : null,
                        Size = size > 0 ? size : null,
                    },
                },
            };
        }

        return new Library
        {
            Name = name,
            Downloads = new LibraryDownloads
            {
                    Artifact = new DownloadArtifact
                    {
                        Path = coordinates.RelativePath,
                        Url = url,
                        Sha1 = sha1 is { Length: > 0 } sha1Value ? sha1Value : null,
                        Size = size > 0 ? size : null,
                    },
            },
        };
    }

    /// <summary>
    /// Installs LabyMod for an instance: the merged profile, then everything the version needs, then
    /// LabyMod's own assets into the instance's game directory.
    /// </summary>
    public async Task<LabyModInstallResult> InstallAsync(
        Guid instanceId,
        string minecraftVersion,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        var manifest = await GetManifestAsync(cancellationToken).ConfigureAwait(false);
        var profile = await WriteProfileAsync(manifest, minecraftVersion, cancellationToken)
            .ConfigureAwait(false);

        await _installer
            .InstallAsync(instanceId, profile.VersionId, RuleContext.ForHost(), progress, cancellationToken)
            .ConfigureAwait(false);

        var assets = await DownloadAssetsAsync(instanceId, manifest, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation(
            "Installed LabyMod {Version} for instance {Instance}: {Assets} asset(s)",
            manifest.LabyModVersion,
            instanceId,
            assets);

        return new LabyModInstallResult(
            profile.VersionId,
            manifest.LabyModVersion!,
            profile.LibraryCount,
            assets);
    }

    /// <summary>
    /// Downloads LabyMod's shared assets next to the game, which is where LabyMod looks for them. The
    /// manifest names each asset and the hash its URL is built from, but publishes no checksum for
    /// the file itself, so these are fetched and stored rather than verified.
    /// </summary>
    public async Task<int> DownloadAssetsAsync(
        Guid instanceId,
        LabyModManifest manifest,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.Assets.Count == 0)
        {
            return 0;
        }

        var root = Path.Combine(_paths.InstanceGameDirectory(instanceId), AssetFolder);
        var requests = new List<DownloadRequest>();
        foreach (var (name, hash) in manifest.Assets)
        {
            if (name.Length == 0 || hash.Length == 0)
            {
                continue;
            }

            var target = PathSafety.ResolveContained(root, PathSafety.SanitizeFileName(name) + ".jar");
            requests.Add(new DownloadRequest
            {
                Url = $"{_downloadBase}/assets/labymod4/production/{manifest.CommitReference}"
                    + $"/{Uri.EscapeDataString(name)}/{Uri.EscapeDataString(hash)}.jar",
                TargetPath = target,
                Label = $"{name}.jar",
            });
        }

        if (requests.Count == 0)
        {
            return 0;
        }

        var summary = await _downloads
            .DownloadAsync(requests, DownloadProgressAdapter.Create(null), cancellationToken)
            .ConfigureAwait(false);
        if (!summary.Success)
        {
            throw new LoaderException(
                "LabyMod's assets could not be downloaded: "
                + (summary.Failures.FirstOrDefault()?.Message ?? "unknown failure"));
        }

        return requests.Count;
    }
}
