namespace Ferrite.Core.Loaders;

/// <summary>
/// LabyMod's published manifest. It names the current build, the commit its files live under, the
/// client jar's hash, the shared assets, and one version document per supported Minecraft version.
/// </summary>
public sealed class LabyModManifest
{
    public string? LabyModVersion { get; set; }

    public string? CommitReference { get; set; }

    public string? Sha1 { get; set; }

    public long Size { get; set; }

    /// <summary>Asset name to content hash, for the files LabyMod looks up next to the game.</summary>
    public Dictionary<string, string> Assets { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public List<LabyModMinecraftVersion> MinecraftVersions { get; set; } = [];
}

/// <summary>One Minecraft version LabyMod supports, and where its version document lives.</summary>
public sealed class LabyModMinecraftVersion
{
    public string? Tag { get; set; }

    public string? CustomManifestUrl { get; set; }
}

/// <summary>The library list LabyMod publishes for its builds.</summary>
internal sealed class LabyModLibrariesDocument
{
    public List<LabyModLibrary> Libraries { get; set; } = [];
}

/// <summary>One library LabyMod ships, with an explicit download.</summary>
internal sealed class LabyModLibrary
{
    public string? Name { get; set; }

    public string? Url { get; set; }

    /// <summary><c>all</c>, or the Minecraft version the library is for.</summary>
    public string? MinecraftVersion { get; set; }

    public string? Sha1 { get; set; }

    public long Size { get; set; }
}
