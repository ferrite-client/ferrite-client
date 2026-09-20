namespace Ferrite.Core.Minecraft;

public enum LoaderKind
{
    Vanilla = 0,
    Fabric = 1,
    Quilt = 2,
    NeoForge = 3,
    Forge = 4,
    OptiFine = 5,
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
        _ => null,
    };
}
