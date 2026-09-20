using System.Text.Json.Serialization;

namespace Ferrite.Core.Rules;

/// <summary>A single Mojang rule that decides whether a library or argument applies.</summary>
public sealed class Rule
{
    /// <summary>"allow" or "disallow".</summary>
    public string? Action { get; set; }

    public OsRule? Os { get; set; }

    public Dictionary<string, bool>? Features { get; set; }

    public bool IsAllow => string.Equals(Action, "allow", StringComparison.OrdinalIgnoreCase);
}

public sealed class OsRule
{
    /// <summary>windows, linux, osx, or a free-form name used by older versions.</summary>
    public string? Name { get; set; }

    public string? Arch { get; set; }

    /// <summary>Legacy regular expression matched against the OS version string.</summary>
    public string? Version { get; set; }

    public OsVersionRange? VersionRange { get; set; }
}

public sealed class OsVersionRange
{
    public string? Min { get; set; }

    public string? Max { get; set; }
}

/// <summary>Rules and value for one argument entry in the modern arguments format.</summary>
public sealed class ArgumentEntry
{
    [JsonPropertyName("rules")]
    public List<Rule>? Rules { get; set; }

    [JsonPropertyName("value")]
    public List<string> Value { get; set; } = [];
}
