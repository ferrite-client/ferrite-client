using Ferrite.Core.Platform;

namespace Ferrite.Core.Rules;

/// <summary>Host facts a rule is evaluated against.</summary>
public sealed class RuleContext
{
    public OperatingSystemKind Os { get; init; } = PlatformInfo.Os;

    public CpuArchitecture Architecture { get; init; } = PlatformInfo.Arch;

    public string MojangOsName { get; init; } = PlatformInfo.MojangOsName;

    public string MojangArchName { get; init; } = PlatformInfo.MojangArchRuleValue;

    public Version OsVersion { get; init; } = PlatformInfo.OsVersion;

    /// <summary>Feature flags such as is_demo_user, has_custom_resolution, has_quick_plays_support.</summary>
    public IReadOnlyDictionary<string, bool> Features { get; init; } = new Dictionary<string, bool>();

    public static RuleContext ForHost(IReadOnlyDictionary<string, bool>? features = null) => new()
    {
        Features = features ?? new Dictionary<string, bool>(),
    };
}
