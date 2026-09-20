using Ferrite.Core.Platform;

namespace Ferrite.Core.Java;

public enum JavaRuntimeSource
{
    Path,
    JavaHome,
    CommonLocation,
    Registry,
    LauncherRuntime,
    ManagedRuntime,
    UserSpecified,
}

/// <summary>One usable Java installation, as discovered or provisioned.</summary>
public sealed record JavaRuntime
{
    public required string ExecutablePath { get; init; }

    public string? HomePath { get; init; }

    public Version? Version { get; init; }

    public int? MajorVersion { get; init; }

    public string? Vendor { get; init; }

    public CpuArchitecture Architecture { get; init; } = CpuArchitecture.Unknown;

    public bool Is64Bit { get; init; }

    public JavaRuntimeSource Source { get; init; } = JavaRuntimeSource.CommonLocation;

    /// <summary>Set when the runtime came from the managed runtime store.</summary>
    public string? Component { get; init; }

    public string DisplayName
    {
        get
        {
            var vendor = string.IsNullOrEmpty(Vendor) ? "Java" : Vendor;
            var version = Version?.ToString() ?? (MajorVersion is { } major ? major.ToString() : "unknown");
            var arch = Architecture == CpuArchitecture.Unknown ? string.Empty : $" {Architecture}";
            return $"{vendor} {version}{arch}";
        }
    }

    public string ShortVersion => MajorVersion is { } major ? major.ToString() : "?";
}
