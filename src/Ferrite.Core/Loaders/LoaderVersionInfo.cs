using Ferrite.Core.Minecraft;

namespace Ferrite.Core.Loaders;

/// <summary>One installable loader version for a specific Minecraft version.</summary>
public sealed record LoaderVersionInfo(
    LoaderKind Kind,
    string Version,
    string MinecraftVersion,
    bool Stable,
    string? Maven)
{
    public string DisplayName => $"{Kind.ToDisplayName()} {Version}";

    /// <summary>Version id the loader installs as, which is what the instance launches.</summary>
    public string VersionId => Kind switch
    {
        LoaderKind.Fabric => $"fabric-loader-{Version}-{MinecraftVersion}",
        LoaderKind.Quilt => $"quilt-loader-{Version}-{MinecraftVersion}",
        LoaderKind.NeoForge => $"neoforge-{Version}",
        LoaderKind.Forge => $"{MinecraftVersion}-forge-{Version}",
        _ => Version,
    };
}
