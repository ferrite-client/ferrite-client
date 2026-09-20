namespace Ferrite.Core.Storage;

public enum ModpackProvider
{
    Unknown = 0,
    Modrinth = 1,
    CurseForge = 2,
    LocalFile = 3,
}

/// <summary>Set when an instance was created from a modpack, so it can be updated later.</summary>
public sealed class ModpackIdentity
{
    public ModpackProvider Provider { get; set; } = ModpackProvider.Unknown;

    public string? ProjectId { get; set; }

    public string? VersionId { get; set; }

    public string? Name { get; set; }

    public string? VersionName { get; set; }

    public string? SourceUrl { get; set; }

    public DateTimeOffset? InstalledAt { get; set; }
}
