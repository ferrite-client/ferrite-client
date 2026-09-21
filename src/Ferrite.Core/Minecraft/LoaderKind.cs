namespace Ferrite.Core.Minecraft;

public enum LoaderKind
{
    Vanilla = 0,
    Fabric = 1,
    Quilt = 2,
    NeoForge = 3,
    Forge = 4,
    OptiFine = 5,

    /// <summary>
    /// LabyMod 4. Its loader version stores the whole installed version id, like OptiFine, because the
    /// published profile is a standalone version document rather than a patch of a vanilla one.
    /// </summary>
    LabyMod = 6,
}

public static class LoaderKindExtensions
{
    public static string ToDisplayName(this LoaderKind kind) => kind switch
    {
        LoaderKind.Vanilla => "Vanilla",
        LoaderKind.Fabric => "Fabric",
        LoaderKind.Quilt => "Quilt",
        LoaderKind.NeoForge => "NeoForge",
        LoaderKind.Forge => "Forge",
        LoaderKind.OptiFine => "OptiFine",
        LoaderKind.LabyMod => "LabyMod",
        _ => kind.ToString(),
    };

    /// <summary>The loader token used by content providers when filtering versions.</summary>
    public static string? ToContentProviderToken(this LoaderKind kind) => kind switch
    {
        LoaderKind.Fabric => "fabric",
        LoaderKind.Quilt => "quilt",
        LoaderKind.NeoForge => "neoforge",
        LoaderKind.Forge => "forge",
        LoaderKind.Vanilla => null,
        LoaderKind.OptiFine => "optifine",
        // No content provider publishes mods tagged for LabyMod, so there is no token to filter on.
        LoaderKind.LabyMod => null,
        _ => null,
    };
}
