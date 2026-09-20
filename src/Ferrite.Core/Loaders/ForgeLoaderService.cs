using System.Text.Json;
using System.Xml.Linq;
using Ferrite.Core.Download;
using Ferrite.Core.Java;
using Ferrite.Core.Minecraft;
using Ferrite.Core.Net;
using Ferrite.Core.Platform;
using Ferrite.Core.Util;
using Microsoft.Extensions.Logging;

namespace Ferrite.Core.Loaders;

/// <summary>
/// Forge and NeoForge. Both ship an installer jar whose <c>install_profile.json</c> declares the
/// libraries, data files, and processor chain that produce a launchable client.
/// </summary>
public sealed class ForgeLoaderService
{
    public const string NeoForgeMavenBase = "https://maven.neoforged.net/releases/";
    public const string ForgeMavenBase = "https://maven.minecraftforge.net/";

    private const int MaxMetadataBytes = 32 * 1024 * 1024;
    private const long MaxInstallerBytes = 256L * 1024 * 1024;

    private readonly HttpService _http;
    private readonly DownloadEngine _downloads;
    private readonly VersionManifestService _manifest;
    private readonly AppPaths _paths;
    private readonly ForgeProcessorRunner _runner;
    private readonly ILogger<ForgeLoaderService> _logger;

    public ForgeLoaderService(
        HttpService http,
        DownloadEngine downloads,
        VersionManifestService manifest,
        AppPaths paths,
        ILogger<ForgeLoaderService> logger,
        ILoggerFactory loggerFactory)
    {
        _http = http;
        _downloads = downloads;
        _manifest = manifest;
        _paths = paths;
        _logger = logger;
        _runner = new ForgeProcessorRunner(downloads, paths, loggerFactory.CreateLogger<ForgeProcessorRunner>());
    }

    public Task<IReadOnlyList<LoaderVersionInfo>> ListNeoForgeAsync(
        string minecraftVersion,
        CancellationToken cancellationToken) =>
        ListFromMavenAsync(
            LoaderKind.NeoForge,
            $"{NeoForgeMavenBase}net/neoforged/neoforge/maven-metadata.xml",
            NeoForgePrefix(minecraftVersion),
            minecraftVersion,
            cancellationToken);

    public Task<IReadOnlyList<LoaderVersionInfo>> ListForgeAsync(
        string minecraftVersion,
        CancellationToken cancellationToken) =>
        ListFromMavenAsync(
            LoaderKind.Forge,
            $"{ForgeMavenBase}net/minecraftforge/forge/maven-metadata.xml",
            minecraftVersion + "-",
            minecraftVersion,
            cancellationToken);

    public async Task<VersionDocument> InstallAsync(
        LoaderKind kind,
        string minecraftVersion,
        string loaderVersion,
        JavaRuntime java,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(java);

        var installerUrl = InstallerUrl(kind, minecraftVersion, loaderVersion);
        var installerName = PathSafety.SanitizeFileName(Path.GetFileName(new Uri(installerUrl).LocalPath));
        var installerPath = Path.Combine(_paths.StoreDirectory, "installers", installerName);

        progress?.Report(new InstallProgress
        {
            Stage = InstallStage.Downloading,
            Message = $"{kind.ToDisplayName()} {loaderVersion} installer",
        });

        var summary = await _downloads
            .DownloadAsync(
                [
                    new DownloadRequest
                    {
                        Url = installerUrl,
                        TargetPath = installerPath,
                        Label = installerName,
                    },
                ],
                DownloadProgressAdapter.Create(progress),
                cancellationToken)
            .ConfigureAwait(false);
        if (!summary.Success)
        {
            throw new LoaderException($"The {kind.ToDisplayName()} installer could not be downloaded.");
        }

        if (new FileInfo(installerPath).Length > MaxInstallerBytes)
        {
            throw new LoaderException("The installer jar is larger than the accepted limit.");
        }

        var profile = ReadInstallProfile(installerPath);
        var versionDocumentJson = ResolveVersionDocumentJson(profile, installerPath);
        var document = VersionManifestService.ParseVersionDocument(
            System.Text.Encoding.UTF8.GetBytes(versionDocumentJson),
            installerUrl);

        var versionId = profile.Version ?? document.Id
            ?? new LoaderVersionInfo(kind, loaderVersion, minecraftVersion, true, null).VersionId;
        document.Id = versionId;
        document.InheritsFrom ??= profile.Minecraft ?? minecraftVersion;
        await _manifest.WriteLocalVersionAsync(versionId, document, cancellationToken).ConfigureAwait(false);

        var clientJar = await EnsureClientJarAsync(document.InheritsFrom, cancellationToken).ConfigureAwait(false);
        await DownloadInstallerLibrariesAsync(profile, progress, cancellationToken).ConfigureAwait(false);
        await _runner
            .RunAsync(profile, installerPath, clientJar, java, progress, cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Installed {Kind} {Version} for {Minecraft} as {Id}",
            kind,
            loaderVersion,
            minecraftVersion,
            versionId);
        return document;
    }

    public static string InstallerUrl(LoaderKind kind, string minecraftVersion, string loaderVersion) => kind switch
    {
        LoaderKind.NeoForge =>
            $"{NeoForgeMavenBase}net/neoforged/neoforge/{loaderVersion}/neoforge-{loaderVersion}-installer.jar",
        LoaderKind.Forge =>
            $"{ForgeMavenBase}net/minecraftforge/forge/{minecraftVersion}-{loaderVersion}/forge-{minecraftVersion}-{loaderVersion}-installer.jar",
        _ => throw new LoaderException($"{kind} is not installed from a Forge-style installer."),
    };

    /// <summary>
    /// NeoForge numbers builds after the Minecraft minor: 1.21.1 becomes 21.1, 26.3 stays 26.3.
    /// </summary>
    public static string NeoForgePrefix(string minecraftVersion)
    {
        var parts = minecraftVersion.Split('.');
        if (parts.Length >= 3 && parts[0] == "1" && int.TryParse(parts[1], out _))
        {
            return parts[1] + "." + parts[2];
        }

        return parts.Length >= 2 ? parts[0] + "." + parts[1] : minecraftVersion;
    }

    private async Task<IReadOnlyList<LoaderVersionInfo>> ListFromMavenAsync(
        LoaderKind kind,
        string metadataUrl,
        string versionPrefix,
        string minecraftVersion,
        CancellationToken cancellationToken)
    {
        var xml = await _http.GetStringAsync(metadataUrl, MaxMetadataBytes, cancellationToken).ConfigureAwait(false);
        var results = new List<LoaderVersionInfo>();

        try
        {
            var document = XDocument.Parse(xml);
            foreach (var element in document.Descendants("version"))
            {
                var raw = element.Value.Trim();
                if (raw.Length == 0 || !raw.StartsWith(versionPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var version = kind == LoaderKind.Forge ? raw[(minecraftVersion.Length + 1)..] : raw;
                results.Add(new LoaderVersionInfo(
                    kind,
                    version,
                    minecraftVersion,
                    Stable: !raw.Contains("beta", StringComparison.OrdinalIgnoreCase),
                    Maven: null));
            }
        }
        catch (System.Xml.XmlException exception)
        {
            throw new LoaderException($"{kind} maven metadata could not be parsed.", exception);
        }

        _logger.LogInformation(
            "{Kind}: {Count} version(s) found for {Minecraft}",
            kind,
            results.Count,
            minecraftVersion);
        return results;
    }

    private ForgeInstallProfile ReadInstallProfile(string installerPath)
    {
        var json = ArchiveExtractor.ReadEntryText(installerPath, "install_profile.json", MaxMetadataBytes)
            ?? throw new LoaderException("The installer contains no install_profile.json.");

        try
        {
            return JsonSerializer.Deserialize<ForgeInstallProfile>(json, Ferrite.Core.Json.JsonDefaults.Remote)
                ?? throw new LoaderException("install_profile.json was empty.");
        }
        catch (JsonException exception)
        {
            throw new LoaderException("install_profile.json is not valid JSON.", exception);
        }
    }

    private static string ResolveVersionDocumentJson(ForgeInstallProfile profile, string installerPath)
    {
        // NeoForge spec 1 stores a jar-relative path in "json" (for example "/version.json") while
        // older Forge stores one in "path"; some installers inline the document instead. Detect the
        // shape rather than assuming a field name means one thing.
        var candidate = !string.IsNullOrWhiteSpace(profile.Json) ? profile.Json : profile.Path;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            throw new LoaderException("The installer declares neither an inline nor a referenced version document.");
        }

        if (candidate.TrimStart().StartsWith('{'))
        {
            return candidate;
        }

        var entryName = candidate.TrimStart('/');
        return ArchiveExtractor.ReadEntryText(installerPath, entryName, MaxMetadataBytes)
            ?? throw new LoaderException($"The installer is missing the version document '{entryName}'.");
    }

    private async Task DownloadInstallerLibrariesAsync(
        ForgeInstallProfile profile,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (profile.Libraries is not { Count: > 0 } libraries)
        {
            return;
        }

        var requests = new List<DownloadRequest>(libraries.Count);
        foreach (var library in libraries)
        {
            if (!MavenCoordinates.TryParse(library.Name, out var coordinates))
            {
                continue;
            }

            var artifact = library.Downloads?.Artifact;
            var relative = artifact?.Path ?? coordinates.RelativePath;
            var url = artifact?.Url;
            if (string.IsNullOrEmpty(url))
            {
                var baseUrl = string.IsNullOrEmpty(library.Url) ? NeoForgeMavenBase : library.Url!;
                url = baseUrl.TrimEnd('/') + "/" + coordinates.RelativePath;
            }

            requests.Add(new DownloadRequest
            {
                Url = url,
                TargetPath = Path.Combine(
                    _paths.LibrariesDirectory,
                    relative.Replace('/', Path.DirectorySeparatorChar)),
                ExpectedSha1 = artifact?.Sha1 ?? library.Sha1,
                ExpectedSize = artifact?.Size ?? library.Size,
                Label = coordinates.FileName,
            });
        }

        progress?.Report(new InstallProgress
        {
            Stage = InstallStage.Downloading,
            Message = "Installer libraries",
        });

        var summary = await _downloads
            .DownloadAsync(requests, DownloadProgressAdapter.Create(progress), cancellationToken)
            .ConfigureAwait(false);
        if (!summary.Success)
        {
            throw new LoaderException(
                $"Installer libraries could not be downloaded ({summary.Failures.Count} failures).");
        }
    }

    /// <summary>Processor chains need the vanilla client jar; download it if it is absent.</summary>
    private async Task<string> EnsureClientJarAsync(string minecraftVersion, CancellationToken cancellationToken)
    {
        var target = _paths.VersionClientJarFile(minecraftVersion);
        var manifest = await _manifest.GetManifestAsync(forceRefresh: false, cancellationToken).ConfigureAwait(false);
        var entry = manifest.Find(minecraftVersion)
            ?? throw new LoaderException($"Minecraft {minecraftVersion} is not in the version manifest.");
        var document = await _manifest.GetMojangVersionAsync(entry, cancellationToken).ConfigureAwait(false);

        var client = document.Downloads?.Client
            ?? throw new LoaderException($"Minecraft {minecraftVersion} declares no client download.");

        if (await Hashing.VerifyAsync(target, client.Size, client.Sha1, cancellationToken).ConfigureAwait(false))
        {
            return target;
        }

        var summary = await _downloads
            .DownloadAsync(
                [
                    new DownloadRequest
                    {
                        Url = client.Url!,
                        TargetPath = target,
                        ExpectedSha1 = client.Sha1,
                        ExpectedSize = client.Size,
                        Label = $"{minecraftVersion} client jar",
                    },
                ],
                progress: null,
                cancellationToken)
            .ConfigureAwait(false);
        if (!summary.Success)
        {
            throw new LoaderException($"The Minecraft {minecraftVersion} client jar could not be downloaded.");
        }

        return target;
    }
}
