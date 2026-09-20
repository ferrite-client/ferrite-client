using Ferrite.Core.Platform;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System.Runtime.Versioning;

namespace Ferrite.Core.Java;

/// <summary>
/// Enumerates candidate Java executables. Kept separate from probing so search policy can change
/// without touching version detection.
/// </summary>
internal sealed class JavaCandidateEnumerator
{
    private const int MaxSearchDepth = 5;

    private static readonly string[] CommonVendorRoots =
    [
        "Java",
        "Eclipse Adoptium",
        "Eclipse Foundation",
        "AdoptOpenJDK",
        "Microsoft",
        "Zulu",
        "BellSoft",
        "Amazon Corretto",
        "Semeru",
        "SapMachine",
        "GraalVM",
        "Liberica",
        "RedHat",
    ];

    private readonly AppPaths _paths;
    private readonly ILogger _logger;

    public JavaCandidateEnumerator(AppPaths paths, ILogger logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public IEnumerable<(string Path, JavaRuntimeSource Source)> EnumerateCandidates()
    {
        var executableName = OperatingSystem.IsWindows() ? "java.exe" : "java";

        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(javaHome))
        {
            yield return (Path.Combine(javaHome, "bin", executableName), JavaRuntimeSource.JavaHome);
        }

        foreach (var pathEntry in EnumeratePathDirectories())
        {
            yield return (Path.Combine(pathEntry, executableName), JavaRuntimeSource.Path);
        }

        if (OperatingSystem.IsWindows())
        {
            foreach (var candidate in EnumerateWindowsCandidates(executableName))
            {
                yield return candidate;
            }
        }
        else
        {
            foreach (var root in new[] { "/usr/lib/jvm", "/Library/Java/JavaVirtualMachines", "/opt/java" })
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                foreach (var found in EnumerateExecutables(root, executableName, MaxSearchDepth))
                {
                    yield return (found, JavaRuntimeSource.CommonLocation);
                }
            }
        }

        foreach (var runtimeRoot in LauncherRuntimeRoots())
        {
            if (!Directory.Exists(runtimeRoot))
            {
                continue;
            }

            foreach (var found in EnumerateExecutables(runtimeRoot, executableName, MaxSearchDepth))
            {
                yield return (found, JavaRuntimeSource.LauncherRuntime);
            }
        }

        if (Directory.Exists(_paths.RuntimesDirectory))
        {
            foreach (var found in EnumerateExecutables(_paths.RuntimesDirectory, executableName, MaxSearchDepth))
            {
                yield return (found, JavaRuntimeSource.ManagedRuntime);
            }
        }
    }

    private static IEnumerable<string> EnumeratePathDirectories()
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            yield break;
        }

        foreach (var entry in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = entry.Trim().Trim('"');
            if (trimmed.Length > 0)
            {
                yield return trimmed;
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private IEnumerable<(string Path, JavaRuntimeSource Source)> EnumerateWindowsCandidates(string executableName)
    {
        var roots = new List<string>();
        foreach (var specialFolder in new[]
                 {
                     Environment.SpecialFolder.ProgramFiles,
                     Environment.SpecialFolder.ProgramFilesX86,
                     Environment.SpecialFolder.LocalApplicationData,
                 })
        {
            var folder = Environment.GetFolderPath(specialFolder);
            if (!string.IsNullOrEmpty(folder))
            {
                roots.Add(folder);
            }
        }

        foreach (var root in roots)
        {
            foreach (var vendorRoot in new[] { root, Path.Combine(root, "Programs") })
            {
                if (!Directory.Exists(vendorRoot))
                {
                    continue;
                }

                foreach (var vendor in CommonVendorRoots)
                {
                    var vendorDirectory = Path.Combine(vendorRoot, vendor);
                    if (!Directory.Exists(vendorDirectory))
                    {
                        continue;
                    }

                    foreach (var found in EnumerateExecutables(vendorDirectory, executableName, 3))
                    {
                        yield return (found, JavaRuntimeSource.CommonLocation);
                    }
                }
            }
        }

        foreach (var home in EnumerateRegistryJavaHomes())
        {
            yield return (Path.Combine(home, "bin", executableName), JavaRuntimeSource.Registry);
        }
    }

    [SupportedOSPlatform("windows")]
    private IEnumerable<string> EnumerateRegistryJavaHomes()
    {
        string[] keys =
        [
            @"SOFTWARE\JavaSoft\Java Development Kit",
            @"SOFTWARE\JavaSoft\Java Runtime Environment",
            @"SOFTWARE\WOW6432Node\JavaSoft\Java Development Kit",
            @"SOFTWARE\WOW6432Node\JavaSoft\Java Runtime Environment",
        ];

        foreach (var keyPath in keys)
        {
            List<string> homes = [];
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(keyPath);
                if (key is not null)
                {
                    foreach (var subKeyName in key.GetSubKeyNames())
                    {
                        using var subKey = key.OpenSubKey(subKeyName);
                        if (subKey?.GetValue("JavaHome") is string home && !string.IsNullOrWhiteSpace(home))
                        {
                            homes.Add(home);
                        }
                    }
                }
            }
            catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                _logger.LogDebug(exception, "Registry key {Key} could not be read", keyPath);
            }

            foreach (var home in homes)
            {
                yield return home;
            }
        }
    }

    private static IEnumerable<string> LauncherRuntimeRoots()
    {
        if (!OperatingSystem.IsWindows())
        {
            yield break;
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrEmpty(appData))
        {
            yield return Path.Combine(appData, ".minecraft", "runtime");
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrEmpty(localAppData))
        {
            yield return Path.Combine(
                localAppData,
                "Packages",
                "Microsoft.4297127D64EC6_8wekyb3d8bbwe",
                "LocalCache",
                "Local",
                "runtime");
        }

        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrEmpty(programFilesX86))
        {
            yield return Path.Combine(programFilesX86, "Minecraft Launcher", "runtime");
        }
    }

    private static IEnumerable<string> EnumerateExecutables(string root, string executableName, int maxDepth)
    {
        var queue = new Queue<(string Directory, int Depth)>();
        queue.Enqueue((root, 0));

        while (queue.Count > 0)
        {
            var (directory, depth) = queue.Dequeue();
            string[] children;
            try
            {
                children = Directory.GetDirectories(directory);
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
            {
                continue;
            }

            var candidate = Path.Combine(directory, "bin", executableName);
            if (File.Exists(candidate))
            {
                yield return candidate;
            }

            if (depth >= maxDepth)
            {
                continue;
            }

            foreach (var child in children)
            {
                queue.Enqueue((child, depth + 1));
            }
        }
    }
}
