using System.Text.Json;
using Ferrite.Core.Download;
using Ferrite.Core.Json;
using Ferrite.Core.Minecraft;
using Ferrite.Core.Net;
using Ferrite.Core.Platform;
using Ferrite.Core.Util;
using Microsoft.Extensions.Logging;

namespace Ferrite.Core.Java;

/// <summary>
/// Downloads and manages Java runtimes published by Mojang for a specific game version, so a user
/// without a suitable JDK can still launch.
/// </summary>
public sealed class JavaProvisioner
{
    public const string DefaultCatalogUrl =
        "https://launchermeta.mojang.com/v1/products/java-runtime/2ec0cc96c44e5a76b9c8b7c39df7210883d12871/all.json";

    private const int MaxCatalogBytes = 8 * 1024 * 1024;
    private const int MaxManifestBytes = 64 * 1024 * 1024;

    private readonly HttpService _http;
    private readonly DownloadEngine _downloads;
    private readonly JavaDetector _detector;
    private readonly AppPaths _paths;
    private readonly ILogger<JavaProvisioner> _logger;
    private readonly string _catalogUrl;

    private JavaRuntimeCatalog? _catalog;

    public JavaProvisioner(
        HttpService http,
        DownloadEngine downloads,
        JavaDetector detector,
        AppPaths paths,
        ILogger<JavaProvisioner> logger,
        string catalogUrl = DefaultCatalogUrl)
    {
        _http = http;
        _downloads = downloads;
        _detector = detector;
        _paths = paths;
        _logger = logger;
        _catalogUrl = catalogUrl;
    }

    public async Task<JavaRuntimeCatalog> GetCatalogAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        if (!forceRefresh && _catalog is not null)
        {
            return _catalog;
        }

        var bytes = await _http.GetBytesAsync(_catalogUrl, MaxCatalogBytes, cancellationToken).ConfigureAwait(false);
        var catalog = JsonSerializer.Deserialize<JavaRuntimeCatalog>(bytes, JsonDefaults.Remote)
            ?? throw new JavaProvisioningException("Mojang's Java runtime catalog could not be parsed.");

        _catalog = catalog;
        return catalog;
    }

    /// <summary>Lists published builds for a component on this host's OS, newest first.</summary>
    public async Task<IReadOnlyList<JavaRuntimeOption>> ListAvailableAsync(
        string component,
        CancellationToken cancellationToken)
    {
        var catalog = await GetCatalogAsync(forceRefresh: false, cancellationToken).ConfigureAwait(false);
        var osKey = OsKey();
        if (!catalog.TryGetValue(osKey, out var components)
            || !components.TryGetValue(component, out var releases)
            || releases.Count == 0)
        {
            _logger.LogInformation("No published Java runtime for component {Component} on {Os}", component, osKey);
            return [];
        }

        return releases
            .Where(release => release.Manifest?.Url is not null)
            .Select(release => new JavaRuntimeOption(
                component,
                release.Version?.Name ?? "unknown",
                DateTimeOffset.TryParse(release.Version?.Released, out var released) ? released : null,
                release.Manifest?.Size))
            .OrderByDescending(option => option.Released ?? DateTimeOffset.MinValue)
            .ToList();
    }

    /// <summary>Returns an already provisioned runtime for the component, if it is usable.</summary>
    public async Task<JavaRuntime?> TryFindProvisionedAsync(string component, CancellationToken cancellationToken)
    {
        var root = _paths.RuntimeDirectory(component);
        if (!Directory.Exists(root))
        {
            return null;
        }

        var executableName = OperatingSystem.IsWindows() ? "java.exe" : "java";
        var executable = Directory
            .EnumerateFiles(root, executableName, SearchOption.AllDirectories)
            .FirstOrDefault(file => string.Equals(
                Path.GetFileName(Path.GetDirectoryName(file)),
                "bin",
                StringComparison.OrdinalIgnoreCase));

        if (executable is null)
        {
            return null;
        }

        var runtime = await _detector
            .ProbeAsync(executable, JavaRuntimeSource.ManagedRuntime, cancellationToken)
            .ConfigureAwait(false);
        return runtime is null ? null : runtime with { Component = component };
    }

    /// <summary>Downloads the published runtime for the component and verifies it by running it.</summary>
    public async Task<JavaRuntime> ProvisionAsync(
        string component,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        var existing = await TryFindProvisionedAsync(component, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            _logger.LogInformation("Reusing provisioned runtime {Component}", component);
            return existing;
        }

        progress?.Report(new InstallProgress
        {
            Stage = InstallStage.ResolvingMetadata,
            Message = $"Java runtime {component}",
        });

        var catalog = await GetCatalogAsync(forceRefresh: false, cancellationToken).ConfigureAwait(false);
        var osKey = OsKey();
        if (!catalog.TryGetValue(osKey, out var components)
            || !components.TryGetValue(component, out var releases)
            || releases.Count == 0)
        {
            throw new JavaProvisioningException($"Mojang publishes no '{component}' runtime for '{osKey}'.");
        }

        var release = releases
            .Where(candidate => candidate.Manifest?.Url is not null)
            .OrderByDescending(candidate => candidate.Version?.Released ?? string.Empty)
            .First();

        var manifestBytes = await _http
            .GetBytesAsync(release.Manifest!.Url!, MaxManifestBytes, cancellationToken)
            .ConfigureAwait(false);
        var manifest = JsonSerializer.Deserialize<JavaRuntimeFileManifest>(manifestBytes, JsonDefaults.Remote)
            ?? throw new JavaProvisioningException("The Java runtime file manifest could not be parsed.");

        var root = _paths.RuntimeDirectory(component);
        var requests = new List<DownloadRequest>(manifest.Files.Count);
        var directories = new List<string>();
        var executablePaths = new List<string>();

        foreach (var (entryPath, entry) in manifest.Files)
        {
            var relative = NormalizeEntryPath(entryPath, component);
            if (relative is null)
            {
                continue;
            }

            string target;
            try
            {
                target = PathSafety.ResolveContained(root, relative);
            }
            catch (PathSafetyException)
            {
                _logger.LogWarning("Skipping suspicious runtime entry {Entry}", entryPath);
                continue;
            }

            switch (entry.Type)
            {
                case "directory":
                    directories.Add(target);
                    break;

                case "link":
                    // Links are packaging detail; the raw files are always present, so links are
                    // skipped rather than materialised.
                    break;

                default:
                    if (entry.Downloads?.Raw?.Url is { } url)
                    {
                        requests.Add(new DownloadRequest
                        {
                            Url = url,
                            TargetPath = target,
                            ExpectedSha1 = entry.Downloads.Raw.Sha1,
                            ExpectedSize = entry.Downloads.Raw.Size,
                            Label = Path.GetFileName(target),
                        });
                        if (entry.Executable == true)
                        {
                            executablePaths.Add(target);
                        }
                    }

                    break;
            }
        }

        foreach (var directory in directories)
        {
            Directory.CreateDirectory(directory);
        }

        progress?.Report(new InstallProgress { Stage = InstallStage.Downloading, Message = component });
        var summary = await _downloads
            .DownloadAsync(requests, DownloadProgressAdapter.Create(progress), cancellationToken)
            .ConfigureAwait(false);
        if (!summary.Success)
        {
            throw new InstallFailedException(
                $"Java runtime {component} could not be downloaded ({summary.Failures.Count} failures).",
                summary.Failures);
        }

        MarkExecutables(executablePaths);

        progress?.Report(new InstallProgress { Stage = InstallStage.Finalising, Message = component });
        var runtime = await TryFindProvisionedAsync(component, cancellationToken).ConfigureAwait(false)
            ?? throw new JavaProvisioningException(
                $"Java runtime {component} was downloaded but contained no java executable.");

        _logger.LogInformation("Provisioned Java runtime {Component}: {Display}", component, runtime.DisplayName);
        return runtime;
    }

    public static string OsKey() => PlatformInfo.Os switch
    {
        OperatingSystemKind.Windows => "windows",
        OperatingSystemKind.Linux => "linux",
        _ => "macos",
    };

    private static string? NormalizeEntryPath(string entryPath, string component)
    {
        var normalized = entryPath.Replace('\\', '/').TrimStart('/');
        var prefix = component + "/";
        if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[prefix.Length..];
        }

        return normalized.Length == 0 ? null : normalized;
    }

    private static void MarkExecutables(IEnumerable<string> paths)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        foreach (var path in paths)
        {
            try
            {
                var mode = File.GetUnixFileMode(path);
                File.SetUnixFileMode(
                    path,
                    mode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
            }
            catch (IOException)
            {
            }
        }
    }
}
