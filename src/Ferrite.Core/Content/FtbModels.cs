using System.Text.Json.Serialization;

namespace Ferrite.Core.Content;

/// <summary>One artwork entry on an FTB pack.</summary>
internal sealed class FtbArt
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}

/// <summary>One author entry on an FTB pack.</summary>
internal sealed class FtbAuthor
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

/// <summary>A target an FTB version declares: the game version, the loader, or the runtime.</summary>
internal sealed class FtbTarget
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    /// <summary>"game", "modloader", or "runtime".</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }
}

/// <summary>One version of an FTB pack, as listed on the pack document.</summary>
internal sealed class FtbVersion
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("targets")]
    public List<FtbTarget> Targets { get; set; } = [];
}

/// <summary>One file an FTB version installs.</summary>
internal sealed class FtbFile
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>The directory the file goes in, relative to the game directory, such as "./mods".</summary>
    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("sha1")]
    public string? Sha1 { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>True when the pack marks the file optional, so it is not installed by default.</summary>
    [JsonPropertyName("optional")]
    public bool Optional { get; set; }

    [JsonPropertyName("serveronly")]
    public bool ServerOnly { get; set; }

    [JsonPropertyName("clientonly")]
    public bool ClientOnly { get; set; }
}

/// <summary>An FTB pack document.</summary>
internal sealed class FtbPack
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("synopsis")]
    public string? Synopsis { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("art")]
    public List<FtbArt> Art { get; set; } = [];

    [JsonPropertyName("authors")]
    public List<FtbAuthor> Authors { get; set; } = [];

    [JsonPropertyName("installs")]
    public long Installs { get; set; }

    [JsonPropertyName("versions")]
    public List<FtbVersion> Versions { get; set; } = [];
}

/// <summary>An FTB version's full file list.</summary>
internal sealed class FtbVersionFiles
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("changelog")]
    public string? Changelog { get; set; }

    [JsonPropertyName("targets")]
    public List<FtbTarget> Targets { get; set; } = [];

    [JsonPropertyName("files")]
    public List<FtbFile> Files { get; set; } = [];
}

/// <summary>The id list the search endpoint answers with.</summary>
internal sealed class FtbSearchResult
{
    [JsonPropertyName("packs")]
    public List<int> Packs { get; set; } = [];

    [JsonPropertyName("total")]
    public long Total { get; set; }
}
