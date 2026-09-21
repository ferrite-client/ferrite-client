using System.Runtime.InteropServices;

namespace Ferrite.Core.Platform;

/// <summary>
/// Host facts the launch pipeline needs: operating system, architecture, and the
/// strings Mojang rules and native classifiers use to describe them.
/// </summary>
public static class PlatformInfo
{
    public static OperatingSystemKind Os { get; } = DetectOs();

    public static CpuArchitecture Arch { get; } = DetectArch();

    /// <summary>Operating system name as used by Mojang rule objects: windows, linux, osx.</summary>
    public static string MojangOsName { get; } = Os switch
    {
        OperatingSystemKind.Windows => "windows",
        OperatingSystemKind.Linux => "linux",
        _ => "osx",
    };

    /// <summary>Architecture token as used in native library classifiers.</summary>
    public static string NativeArchitecture { get; } = Arch switch
    {
        CpuArchitecture.X64 => "x86_64",
        CpuArchitecture.Arm64 => "arm64",
        CpuArchitecture.X86 => "x86",
        CpuArchitecture.Arm32 => "arm32",
        _ => "x86_64",
    };

    /// <summary>Operating system version, used to evaluate rule version ranges.</summary>
    public static Version OsVersion { get; } = Environment.OSVersion.Version;

    /// <summary>
    /// The architecture token Mojang uses inside rule <c>os.arch</c> values, for example
    /// "x86", "x86_64", "arm64" or "arm64-v8a".
    /// </summary>
    public static string MojangArchRuleValue { get; } = Arch switch
    {
        CpuArchitecture.X86 => "x86",
        CpuArchitecture.Arm32 => "arm32",
        CpuArchitecture.Arm64 => "arm64",
        _ => "x86_64",
    };

    public static bool IsWindows => Os == OperatingSystemKind.Windows;

    /// <summary>
    /// The .NET runtime identifier for this process, for example <c>win-x64</c>. Used to pick the
    /// matching update package from a feed.
    /// </summary>
    public static string CurrentRuntimeIdentifier() =>
        (Os switch
        {
            OperatingSystemKind.Windows => "win",
            OperatingSystemKind.Linux => "linux",
            _ => "osx",
        })
        + "-"
        + (Arch switch
        {
            CpuArchitecture.X64 => "x64",
            CpuArchitecture.Arm64 => "arm64",
            CpuArchitecture.X86 => "x86",
            CpuArchitecture.Arm32 => "arm",
            _ => "x64",
        });

    private static OperatingSystemKind DetectOs()
    {
        if (OperatingSystem.IsWindows())
        {
            return OperatingSystemKind.Windows;
        }

        if (OperatingSystem.IsMacOS())
        {
            return OperatingSystemKind.MacOs;
        }

        return OperatingSystemKind.Linux;
    }

    private static CpuArchitecture DetectArch()
    {
        return RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => CpuArchitecture.X64,
            Architecture.Arm64 => CpuArchitecture.Arm64,
            Architecture.X86 => CpuArchitecture.X86,
            Architecture.Arm => CpuArchitecture.Arm32,
            _ => CpuArchitecture.Unknown,
        };
    }
}
