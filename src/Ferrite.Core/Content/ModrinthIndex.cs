using System.Text.Json.Serialization;

namespace Ferrite.Core.Content;

/// <summary>
/// A Modrinth modpack index (<c>modrinth.index.json</c>). Shape verified against a real pack; see
/// <c>docs/RESEARCH.md</c> section 5.
/// </summary>
public sealed class ModrinthIndex
{
    public int FormatVersion { get; set; } = 1;

    public string Game { get; set; } = "minecraft";

    public string? VersionId { get; set; }

    public string? Name { get; set; }

    public string? Summary { get; set; }

    /// <summary>Loader id to version, for example <c>fabric-loader</c> or <c>minecraft</c>.</summary>
    public Dictionary<string, string> Dependencies { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public List<ModrinthIndexFile> Files { get; set; } = [];
}

public sealed class ModrinthIndexFile
{
    /// <summary>Instance-relative target path, for example <c>mods/example.jar</c>.</summary>
    public string Path { get; set; } = string.Empty;

    public ModrinthHashes Hashes { get; set; } = new();

    public ModrinthEnvironment Env { get; set; } = new();

    public List<string> Downloads { get; set; } = [];

    public long FileSize { get; set; }
}

public sealed class ModrinthHashes
{
    public string? Sha1 { get; set; }

    public string? Sha512 { get; set; }
}

public sealed class ModrinthEnvironment
{
    /// <summary>required, optional, or unsupported.</summary>
    public string Client { get; set; } = "required";

    public string Server { get; set; } = "required";

    [JsonIgnore]
    public bool IsClientUnsupported => string.Equals(Client, "unsupported", StringComparison.OrdinalIgnoreCase);
}
