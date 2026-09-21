namespace Ferrite.Core.Content;

/// <summary>
/// A CurseForge modpack's <c>manifest.json</c>. Field names follow the published format; see
/// <c>docs/RESEARCH.md</c> section 5.
/// </summary>
public sealed record CurseForgeManifest
{
    public int ManifestVersion { get; init; }

    public string? ManifestType { get; init; }

    public string? Name { get; init; }

    public string? Version { get; init; }

    public string? Author { get; init; }

    public CurseForgeManifestMinecraft? Minecraft { get; init; }

    public IReadOnlyList<CurseForgeManifestFile> Files { get; init; } = [];

    /// <summary>Folder whose contents are copied over the instance, usually "overrides".</summary>
    public string? Overrides { get; init; }
}

public sealed record CurseForgeManifestMinecraft
{
    public string? Version { get; init; }

    public IReadOnlyList<CurseForgeManifestLoader> ModLoaders { get; init; } = [];
}

/// <summary>A loader entry such as <c>{"id":"neoforge-21.1.72","primary":true}</c>.</summary>
public sealed record CurseForgeManifestLoader
{
    public string? Id { get; init; }

    public bool Primary { get; init; }
}

public sealed record CurseForgeManifestFile
{
    public int ProjectID { get; init; }

    public int FileID { get; init; }

    public bool Required { get; init; } = true;
}
