using System.Text;
using Ferrite.Core.Download;
using Ferrite.Core.Minecraft;
using Ferrite.Core.Net;
using Ferrite.Core.Platform;
using Ferrite.Core.Util;
using Microsoft.Extensions.Logging;

namespace Ferrite.Core.Update;

/// <summary>What an update check found.</summary>
public sealed record UpdateCheckResult
{
    public required UpdateManifest Manifest { get; init; }

    public required bool IsNewer { get; init; }

    /// <summary>The package matching this machine, if the feed publishes one.</summary>
    public UpdatePackage? Package { get; init; }

    public required string CurrentVersion { get; init; }

    public string Notes => Manifest.Notes ?? string.Empty;
}

/// <summary>A staged update ready to be applied by its hand-off script.</summary>
public sealed record UpdateStageResult(
    string Version,
    string StageDirectory,
    string PayloadDirectory,
    string ScriptPath,
    long Bytes);

/// <summary>
/// Checks a signed update feed and stages a verified package for hand-off. The launcher never
/// overwrites itself: staging unpacks the new build next to the data root and writes a script that
/// waits for this process to exit, replaces the install directory, and relaunches.
/// </summary>
public sealed class UpdateService
{
    private const int MaxManifestBytes = 1024 * 1024;
    private const int MaxSignatureBytes = 16 * 1024;
    private const string PayloadMarkerName = ".ferrite-update-stage";

    private readonly HttpService _http;
    private readonly DownloadEngine _downloads;
    private readonly AppPaths _paths;
    private readonly ILogger<UpdateService> _logger;
    private readonly string? _publicKeyPem;

    public UpdateService(
        HttpService http,
        DownloadEngine downloads,
        AppPaths paths,
        ILogger<UpdateService> logger,
        string? publicKeyPem)
    {
        _http = http;
        _downloads = downloads;
        _paths = paths;
        _logger = logger;
        _publicKeyPem = publicKeyPem;
    }

    /// <summary>False when this build carries no feed key, in which case updates stay off.</summary>
    public bool IsVerificationConfigured => !string.IsNullOrWhiteSpace(_publicKeyPem);

    /// <summary>Fetches, verifies, and interprets the feed.</summary>
    public async Task<UpdateCheckResult> CheckAsync(
        string feedUrl,
        string currentVersion,
        string? runtime,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(feedUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentVersion);

        if (!IsVerificationConfigured)
        {
            throw new UpdateException(
                "This build carries no update signing key, so an update feed cannot be trusted. "
                + "See docs/HUMAN_ACTION_REQUIRED.md (H3).");
        }

        var baseUri = Normalize(feedUrl);
        var manifestUri = new Uri(baseUri, UpdateSignature.ManifestEntryName);
        var signatureUri = new Uri(baseUri, UpdateSignature.SignatureEntryName);

        _logger.LogInformation("Checking {Uri} for updates (current {Version})", manifestUri, currentVersion);

        var manifestBytes = await _http
            .GetBytesAsync(manifestUri.AbsoluteUri, MaxManifestBytes, cancellationToken)
            .ConfigureAwait(false);
        var signatureBytes = await _http
            .GetBytesAsync(signatureUri.AbsoluteUri, MaxSignatureBytes, cancellationToken)
            .ConfigureAwait(false);

        var signature = Encoding.UTF8.GetString(signatureBytes);
        if (!UpdateSignature.Verify(manifestBytes, signature, _publicKeyPem!))
        {
            throw new UpdateException(
                $"The update manifest from {manifestUri} did not match its signature. Refusing to use it.");
        }

        var manifest = UpdateManifest.Parse(Encoding.UTF8.GetString(manifestBytes));
        var isNewer = CompareVersions(manifest.Version, currentVersion) > 0;
        var package = ResolvePackage(SelectPackage(manifest, runtime, DetectLocalKind()), baseUri);

        if (isNewer && package is null)
        {
            _logger.LogInformation(
                "Update {Version} is newer but publishes no package for {Runtime}",
                manifest.Version,
                runtime);
        }

        return new UpdateCheckResult
        {
            Manifest = manifest,
            IsNewer = isNewer,
            Package = package,
            CurrentVersion = currentVersion,
        };
    }

    /// <summary>
    /// Downloads and verifies the package, unpacks it into the staging directory, and writes the
    /// hand-off script. Nothing outside the staging directory is touched.
    /// </summary>
    public async Task<UpdateStageResult> StageAsync(
        UpdateCheckResult check,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(check);
        var package = check.Package
            ?? throw new UpdateException("There is no package for this machine in the update feed.");

        var versionDirectory = Path.Combine(
            _paths.UpdateStagingDirectory,
            PathSafety.SanitizeFileName(check.Manifest.Version));
        var payloadDirectory = Path.Combine(versionDirectory, "payload");
        var archivePath = Path.Combine(versionDirectory, "package.zip");

        if (Directory.Exists(versionDirectory))
        {
            Directory.Delete(versionDirectory, recursive: true);
        }

        Directory.CreateDirectory(versionDirectory);
        await File.WriteAllTextAsync(
                Path.Combine(versionDirectory, PayloadMarkerName),
                check.Manifest.Version,
                cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Staging update {Version} from {Url} into {Directory}",
            check.Manifest.Version,
            package.Url,
            versionDirectory);

        var summary = await _downloads
            .DownloadAsync(
                [
                    new DownloadRequest
                    {
                        Url = package.Url,
                        TargetPath = archivePath,
                        ExpectedSha256 = package.Sha256,
                        ExpectedSize = package.Size > 0 ? package.Size : null,
                        Label = $"Ferrite {check.Manifest.Version}",
                    },
                ],
                DownloadProgressAdapter.Create(progress),
                cancellationToken)
            .ConfigureAwait(false);

        if (!summary.Success)
        {
            throw new UpdateException(
                "The update package could not be downloaded or did not match its SHA-256: "
                + $"{summary.Failures.FirstOrDefault()?.Message ?? "unknown failure"}");
        }

        var extraction = await ArchiveExtractor
            .ExtractZipAsync(
                archivePath,
                payloadDirectory,
                new ArchiveExtractionOptions(),
                cancellationToken)
            .ConfigureAwait(false);

        if (!File.Exists(Path.Combine(payloadDirectory, UpdateHandoff.ExecutableName)))
        {
            throw new UpdateException(
                $"{UpdateHandoff.ExecutableName} is not present in the update package, "
                + "so it cannot be applied.");
        }

        var scriptPath = await UpdateHandoff
            .WriteScriptAsync(versionDirectory, cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Update {Version} staged: {Files} file(s) extracted to {Directory}",
            check.Manifest.Version,
            extraction.FilesExtracted,
            payloadDirectory);

        return new UpdateStageResult(
            check.Manifest.Version,
            versionDirectory,
            payloadDirectory,
            scriptPath,
            new FileInfo(archivePath).Length);
    }

    /// <summary>
    /// Turns a package's URL into an absolute one resolved against the feed, so a feed that names its
    /// files relatively can be moved to another host without re-signing.
    /// </summary>
    public static UpdatePackage? ResolvePackage(UpdatePackage? package, Uri feedBase)
    {
        ArgumentNullException.ThrowIfNull(feedBase);
        if (package is null)
        {
            return null;
        }

        if (!Uri.TryCreate(package.Url, UriKind.Relative, out var relative))
        {
            return package;
        }

        if (!UpdateManifest.IsUsablePackageUrl(package.Url))
        {
            throw new UpdateException(
                $"Update package path '{package.Url}' is not a plain file name relative to the feed.");
        }

        var resolved = new Uri(feedBase, relative);
        if (!UpdateManifest.IsUsablePackageUrl(resolved.AbsoluteUri))
        {
            throw new UpdateException(
                $"Update package '{package.Url}' resolves to {resolved}, which is not an HTTPS address.");
        }

        return package with { Url = resolved.AbsoluteUri };
    }

    /// <summary>Newest package for this runtime, preferring the requested build shape.</summary>
    public static UpdatePackage? SelectPackage(
        UpdateManifest manifest,
        string? runtime,
        string? preferredKind = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var candidates = manifest.Packages
            .Where(package => string.Equals(package.Runtime, runtime, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        if (preferredKind is { Length: > 0 })
        {
            var preferred = candidates.FirstOrDefault(package =>
                string.Equals(package.Kind, preferredKind, StringComparison.OrdinalIgnoreCase));
            if (preferred is not null)
            {
                return preferred;
            }
        }

        // Without a stated preference, take the build that runs anywhere rather than depending on
        // the order a feed happens to list its packages in.
        return candidates.FirstOrDefault(package =>
                   string.Equals(package.Kind, SelfContainedKind, StringComparison.OrdinalIgnoreCase))
               ?? candidates[0];
    }

    public const string SelfContainedKind = "self-contained";
    public const string FrameworkDependentKind = "framework-dependent";

    /// <summary>
    /// Which shape this copy of Ferrite is: a self-contained publish carries the .NET runtime beside
    /// the executable. Used to prefer the matching update, so a framework-dependent install does not
    /// silently grow by a hundred megabytes on its first update.
    /// </summary>
    public static string DetectLocalKind(string? baseDirectory = null)
    {
        var directory = baseDirectory ?? AppContext.BaseDirectory;
        return File.Exists(Path.Combine(directory, "System.Private.CoreLib.dll"))
            ? SelfContainedKind
            : FrameworkDependentKind;
    }

    /// <summary>Compares dotted versions, treating a suffix as older than the bare release.</summary>
    public static int CompareVersions(string left, string right)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(left);
        ArgumentException.ThrowIfNullOrWhiteSpace(right);

        var leftParts = SplitVersion(left);
        var rightParts = SplitVersion(right);
        var length = Math.Max(leftParts.Numbers.Count, rightParts.Numbers.Count);

        for (var index = 0; index < length; index++)
        {
            var a = index < leftParts.Numbers.Count ? leftParts.Numbers[index] : 0;
            var b = index < rightParts.Numbers.Count ? rightParts.Numbers[index] : 0;
            if (a != b)
            {
                return a.CompareTo(b);
            }
        }

        // 1.2.0-beta sorts before 1.2.0.
        var leftSuffix = leftParts.Suffix.Length == 0 ? 1 : 0;
        var rightSuffix = rightParts.Suffix.Length == 0 ? 1 : 0;
        if (leftSuffix != rightSuffix)
        {
            return leftSuffix.CompareTo(rightSuffix);
        }

        return string.Compare(leftParts.Suffix, rightParts.Suffix, StringComparison.OrdinalIgnoreCase);
    }

    private static (List<int> Numbers, string Suffix) SplitVersion(string value)
    {
        var text = value.Trim().TrimStart('v', 'V');
        var suffixIndex = text.IndexOfAny(['-', '+']);
        var numeric = suffixIndex >= 0 ? text[..suffixIndex] : text;
        var suffix = suffixIndex >= 0 ? text[suffixIndex..] : string.Empty;

        var numbers = new List<int>();
        foreach (var part in numeric.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            numbers.Add(int.TryParse(part, out var parsed) ? parsed : 0);
        }

        return (numbers, suffix);
    }

    /// <summary>
    /// Normalises a feed address to a directory URI. HTTPS is required; plain HTTP is accepted only
    /// for a loopback host so a self-hosted or test feed does not need a certificate.
    /// </summary>
    private static Uri Normalize(string feedUrl)
    {
        if (!Uri.TryCreate(feedUrl, UriKind.Absolute, out var uri))
        {
            throw new UpdateException($"'{feedUrl}' is not a valid update feed address.");
        }

        if (uri.Scheme == Uri.UriSchemeHttps)
        {
            return EnsureTrailingSlash(uri);
        }

        if (uri.Scheme == Uri.UriSchemeHttp && UpdateManifest.IsLoopback(uri))
        {
            return EnsureTrailingSlash(uri);
        }

        throw new UpdateException("An update feed must use HTTPS (plain HTTP is allowed only for loopback).");
    }

    private static Uri EnsureTrailingSlash(Uri uri) =>
        uri.AbsoluteUri.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/");
}
