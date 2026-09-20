namespace Ferrite.Core.Content;

/// <summary>Typed view of a Forge/NeoForge <c>mods.toml</c> descriptor.</summary>
public sealed class ForgeModToml
{
    public List<ForgeModEntry> Mods { get; set; } = [];

    public Dictionary<string, List<ForgeDependencyEntry>> Dependencies { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ForgeModEntry
{
    public string? ModId { get; set; }

    public string? Version { get; set; }

    public string? DisplayName { get; set; }

    public string? Description { get; set; }

    public string? Authors { get; set; }

    public string? DisplayUrl { get; set; }
}

public sealed class ForgeDependencyEntry
{
    public string? ModId { get; set; }

    public bool? Mandatory { get; set; }

    public string? VersionRange { get; set; }

    public string? Ordering { get; set; }

    public string? Side { get; set; }
}
