using System.Text.Json;
using System.Text.Json.Serialization;
using Ferrite.Core.Rules;

namespace Ferrite.Core.Minecraft;

/// <summary>
/// A Mojang version document. Loader profiles (Fabric, Quilt, Forge) use the same shape with
/// <see cref="InheritsFrom"/> pointing at the base game version.
/// </summary>
public sealed class VersionDocument
{
    public string? Id { get; set; }

    public string? InheritsFrom { get; set; }

    public string? Type { get; set; }

    public string? MainClass { get; set; }

    public string? Assets { get; set; }

    public AssetIndexReference? AssetIndex { get; set; }

    public VersionDownloads? Downloads { get; set; }

    public List<Library> Libraries { get; set; } = [];

    public ArgumentsDocument? Arguments { get; set; }

    /// <summary>Legacy (pre-1.13) single-string game arguments.</summary>
    public string? MinecraftArguments { get; set; }

    public JavaVersionRequirement? JavaVersion { get; set; }

    public LoggingDocument? Logging { get; set; }

    public int? ComplianceLevel { get; set; }

    public int? MinimumLauncherVersion { get; set; }

    public DateTimeOffset? ReleaseTime { get; set; }

    public DateTimeOffset? Time { get; set; }

    /// <summary>Ordered, flattened library list after inheritance has been resolved.</summary>
    [JsonIgnore]
    public IReadOnlyList<Library> ResolvedLibraries { get; set; } = [];
}

public sealed class ArgumentsDocument
{
    public List<JsonElement> Game { get; set; } = [];

    public List<JsonElement> Jvm { get; set; } = [];

    [JsonPropertyName("default-user-jvm")]
    public List<JsonElement> DefaultUserJvm { get; set; } = [];
}

public sealed class AssetIndexReference
{
    public string? Id { get; set; }

    public string? Sha1 { get; set; }

    public long? Size { get; set; }

    public long? TotalSize { get; set; }

    public string? Url { get; set; }
}

public sealed class VersionDownloads
{
    public DownloadArtifact? Client { get; set; }

    public DownloadArtifact? Server { get; set; }
}

public sealed class DownloadArtifact
{
    public string? Path { get; set; }

    public string? Sha1 { get; set; }

    public string? Sha256 { get; set; }

    public long? Size { get; set; }

    public string? Url { get; set; }
}

public sealed class JavaVersionRequirement
{
    public string? Component { get; set; }

    public int? MajorVersion { get; set; }
}

public sealed class LoggingDocument
{
    public LoggingClient? Client { get; set; }
}

public sealed class LoggingClient
{
    public string? Argument { get; set; }

    public string? Type { get; set; }

    public LoggingFile? File { get; set; }
}

public sealed class LoggingFile
{
    public string? Id { get; set; }

    public string? Sha1 { get; set; }

    public long? Size { get; set; }

    public string? Url { get; set; }
}
