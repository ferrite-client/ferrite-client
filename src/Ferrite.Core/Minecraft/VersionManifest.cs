namespace Ferrite.Core.Minecraft;

public sealed class VersionManifest
{
    public LatestVersions? Latest { get; set; }

    public List<VersionManifestEntry> Versions { get; set; } = [];

    public VersionManifestEntry? Find(string versionId) =>
        Versions.FirstOrDefault(entry => string.Equals(entry.Id, versionId, StringComparison.OrdinalIgnoreCase));
}

public sealed class LatestVersions
{
    public string? Release { get; set; }

    public string? Snapshot { get; set; }
}

public sealed class VersionManifestEntry
{
    public string Id { get; set; } = string.Empty;

    /// <summary>release, snapshot, old_beta, or old_alpha.</summary>
    public string Type { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public string? Sha1 { get; set; }

    public int? ComplianceLevel { get; set; }

    public DateTimeOffset? ReleaseTime { get; set; }

    public DateTimeOffset? Time { get; set; }
}
