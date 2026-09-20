using Ferrite.Core.Rules;

namespace Ferrite.Core.Minecraft;

public sealed class Library
{
    /// <summary>Maven coordinates: group:artifact:version or group:artifact:version:classifier.</summary>
    public string? Name { get; set; }

    public LibraryDownloads? Downloads { get; set; }

    /// <summary>Legacy mapping of OS name to classifier key for native extraction.</summary>
    public Dictionary<string, string>? Natives { get; set; }

    public List<Rule>? Rules { get; set; }

    /// <summary>Maven repository base URL used by loader profiles (Fabric/Quilt).</summary>
    public string? Url { get; set; }

    /// <summary>Inline hash used by loader profiles that omit a downloads block.</summary>
    public string? Sha1 { get; set; }

    public string? Sha256 { get; set; }

    public long? Size { get; set; }

    public ExtractRules? Extract { get; set; }
}

public sealed class LibraryDownloads
{
    public DownloadArtifact? Artifact { get; set; }

    public Dictionary<string, DownloadArtifact>? Classifiers { get; set; }
}

public sealed class ExtractRules
{
    public List<string>? Exclude { get; set; }
}
