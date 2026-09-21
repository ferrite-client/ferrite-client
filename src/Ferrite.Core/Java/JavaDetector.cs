using Ferrite.Core.Platform;
using Microsoft.Extensions.Logging;

namespace Ferrite.Core.Java;

/// <summary>
/// Finds Java installations from the environment, common vendor directories, the Minecraft
/// launcher's own runtimes, the managed runtime store, and the Windows registry. Every candidate
/// is probed by running it, so a broken entry is dropped instead of trusted.
/// </summary>
public sealed class JavaDetector
{
    private readonly AppPaths _paths;
    private readonly ILogger<JavaDetector> _logger;
    private readonly JavaCandidateEnumerator _enumerator;

    public JavaDetector(AppPaths paths, ILogger<JavaDetector> logger)
    {
        _paths = paths;
        _logger = logger;
        _enumerator = new JavaCandidateEnumerator(paths, logger);
    }

    /// <summary>
    /// Finds every usable runtime. <paramref name="extraPaths"/> are probed first, so a path the user
    /// supplied is offered even when the environment scan would not have found it.
    /// </summary>
    public async Task<IReadOnlyList<JavaRuntime>> DetectAsync(
        CancellationToken cancellationToken,
        IReadOnlyList<string>? extraPaths = null)
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var seen = new HashSet<string>(comparer);
        var results = new List<JavaRuntime>();

        var candidates = (extraPaths ?? [])
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => (path, JavaRuntimeSource.UserSpecified))
            .Concat(_enumerator.EnumerateCandidates());

        foreach (var (path, source) in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string full;
            try
            {
                full = Path.GetFullPath(path);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
            {
                continue;
            }

            if (!seen.Add(full))
            {
                continue;
            }

            var runtime = await ProbeAsync(full, source, cancellationToken).ConfigureAwait(false);
            if (runtime is not null)
            {
                results.Add(runtime);
            }
        }

        _logger.LogInformation("Discovered {Count} usable Java runtime(s)", results.Count);
        return results
            .OrderByDescending(runtime => runtime.MajorVersion ?? 0)
            .ThenBy(runtime => runtime.ExecutablePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<JavaRuntime?> ProbeAsync(
        string executablePath,
        JavaRuntimeSource source,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            return null;
        }

        var probe = await JavaProcessProbe
            .ProbeAsync(executablePath, timeout: null, cancellationToken)
            .ConfigureAwait(false);
        if (probe is null)
        {
            _logger.LogDebug("Java candidate did not respond: {Path}", executablePath);
            return null;
        }

        return new JavaRuntime
        {
            ExecutablePath = executablePath,
            HomePath = probe.Home ?? DeriveHome(executablePath),
            Version = ParseVersionText(probe.VersionText),
            MajorVersion = probe.MajorVersion,
            Vendor = probe.Vendor,
            Architecture = MapArchitecture(probe.Architecture),
            Is64Bit = probe.Is64Bit,
            Source = source,
        };
    }

    /// <summary>Re-probes a user-supplied path, marking the source as user specified.</summary>
    public Task<JavaRuntime?> ProbeUserSuppliedAsync(string executablePath, CancellationToken cancellationToken) =>
        ProbeAsync(executablePath, JavaRuntimeSource.UserSpecified, cancellationToken);

    public static string? DeriveHome(string executablePath)
    {
        var binDirectory = Path.GetDirectoryName(Path.GetFullPath(executablePath));
        if (binDirectory is null)
        {
            return null;
        }

        return string.Equals(Path.GetFileName(binDirectory), "bin", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(binDirectory)
            : binDirectory;
    }

    public static Version? ParseVersionText(string? versionText)
    {
        if (string.IsNullOrWhiteSpace(versionText))
        {
            return null;
        }

        var cleaned = new string(versionText.TakeWhile(c => char.IsDigit(c) || c is '.' or '_' or '-').ToArray());
        cleaned = cleaned.Replace('_', '.').Replace('-', '.');
        return Version.TryParse(cleaned.TrimEnd('.'), out var version) ? version : null;
    }

    internal static CpuArchitecture MapArchitecture(string? architecture)
    {
        if (string.IsNullOrEmpty(architecture))
        {
            return CpuArchitecture.Unknown;
        }

        if (architecture.Contains("aarch64", StringComparison.OrdinalIgnoreCase)
            || architecture.Contains("arm64", StringComparison.OrdinalIgnoreCase))
        {
            return CpuArchitecture.Arm64;
        }

        if (architecture.Contains("64", StringComparison.OrdinalIgnoreCase))
        {
            return CpuArchitecture.X64;
        }

        if (architecture.Contains("arm", StringComparison.OrdinalIgnoreCase))
        {
            return CpuArchitecture.Arm32;
        }

        return CpuArchitecture.X86;
    }
}
