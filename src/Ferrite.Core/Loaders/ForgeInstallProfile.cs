using System.Text.Json;
using Ferrite.Core.Minecraft;

namespace Ferrite.Core.Loaders;

/// <summary>
/// Shape of the <c>install_profile.json</c> inside a Forge or NeoForge installer jar. Spec 1
/// inlines the version document; older installers reference a file inside the jar.
/// </summary>
public sealed class ForgeInstallProfile
{
    public int Spec { get; set; }

    public string? Profile { get; set; }

    public string? Version { get; set; }

    public string? Minecraft { get; set; }

    /// <summary>Inline version document JSON (spec 1).</summary>
    public string? Json { get; set; }

    /// <summary>Jar-relative path to the version document (older installers).</summary>
    public string? Path { get; set; }

    public string? ServerJarPath { get; set; }

    public Dictionary<string, JsonElement>? Data { get; set; }

    public List<ForgeProcessor>? Processors { get; set; }

    public List<ForgeInstallLibrary>? Libraries { get; set; }
}

public sealed class ForgeProcessor
{
    /// <summary>When present, the processor only runs for the listed sides.</summary>
    public List<string>? Sides { get; set; }

    /// <summary>Maven coordinates of the processor jar, which supplies the main class.</summary>
    public string? Jar { get; set; }

    public List<string> Classpath { get; set; } = [];

    public List<string> Args { get; set; } = [];

    public Dictionary<string, string>? Outputs { get; set; }

    public bool AppliesTo(string side) =>
        Sides is null || Sides.Count == 0 || Sides.Contains(side, StringComparer.OrdinalIgnoreCase);
}

public sealed class ForgeInstallLibrary
{
    public string? Name { get; set; }

    public string? Url { get; set; }

    public LibraryDownloads? Downloads { get; set; }

    public string? Sha1 { get; set; }

    public long? Size { get; set; }
}
