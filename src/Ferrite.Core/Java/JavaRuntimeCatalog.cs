namespace Ferrite.Core.Java;

/// <summary>
/// Shape of Mojang's java-runtime catalog: OS name, then component name, then published builds.
/// </summary>
public sealed class JavaRuntimeCatalog : Dictionary<string, Dictionary<string, List<JavaRuntimeRelease>>>
{
}

public sealed class JavaRuntimeRelease
{
    public JavaRuntimeManifestReference? Manifest { get; set; }

    public JavaRuntimeVersion? Version { get; set; }

    public JavaRuntimeAvailability? Availability { get; set; }
}

public sealed class JavaRuntimeManifestReference
{
    public string? Sha1 { get; set; }

    public long? Size { get; set; }

    public string? Url { get; set; }
}

public sealed class JavaRuntimeVersion
{
    public string? Name { get; set; }

    public string? Released { get; set; }
}

public sealed class JavaRuntimeAvailability
{
    public int? Group { get; set; }

    public int? Progress { get; set; }
}

/// <summary>Per-OS runtime manifest listing every file of one JRE build.</summary>
public sealed class JavaRuntimeFileManifest
{
    public Dictionary<string, JavaRuntimeFileEntry> Files { get; set; } = new(StringComparer.Ordinal);
}

public sealed class JavaRuntimeFileEntry
{
    /// <summary>"file", "directory", or "link".</summary>
    public string? Type { get; set; }

    public bool? Executable { get; set; }

    public JavaRuntimeFileDownloads? Downloads { get; set; }

    /// <summary>Link target for entries of type "link".</summary>
    public string? Target { get; set; }
}

public sealed class JavaRuntimeFileDownloads
{
    public JavaRuntimeFileDownload? Raw { get; set; }

    public JavaRuntimeFileDownload? Lzma { get; set; }
}

public sealed class JavaRuntimeFileDownload
{
    public string? Url { get; set; }

    public string? Sha1 { get; set; }

    public long? Size { get; set; }
}

/// <summary>One installable runtime build shown to the user.</summary>
public sealed record JavaRuntimeOption(
    string Component,
    string VersionName,
    DateTimeOffset? Released,
    long? Size);
