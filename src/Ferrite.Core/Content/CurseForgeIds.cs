namespace Ferrite.Core.Content;

/// <summary>
/// CurseForge API identifiers. The service is keyed by numeric ids rather than slugs, so the mapping
/// lives in one place.
/// </summary>
public static class CurseForgeIds
{
    public const string ApiBase = "https://api.curseforge.com/v1";

    /// <summary>Base of the public site; project pages add a content-type segment and the slug.</summary>
    public const string WebBase = "https://www.curseforge.com/minecraft";

    /// <summary>Minecraft's game id on CurseForge.</summary>
    public const int MinecraftGameId = 432;

    /// <summary>URL segment CurseForge uses for each content type.</summary>
    public static string WebSegmentFor(ContentProjectType type) => type switch
    {
        ContentProjectType.Modpack => "modpacks",
        ContentProjectType.ResourcePack => "texture-packs",
        ContentProjectType.Shader => "shaders",
        ContentProjectType.Datapack => "data-packs",
        _ => "mc-mods",
    };

    public static int? ClassIdFor(ContentProjectType type) => type switch
    {
        ContentProjectType.Mod => 6,
        ContentProjectType.Modpack => 4471,
        ContentProjectType.ResourcePack => 12,
        ContentProjectType.Shader => 6552,
        ContentProjectType.Datapack => 6945,
        _ => null,
    };

    public static ContentProjectType ProjectTypeFor(int? classId) => classId switch
    {
        6 => ContentProjectType.Mod,
        4471 => ContentProjectType.Modpack,
        12 => ContentProjectType.ResourcePack,
        6552 => ContentProjectType.Shader,
        6945 => ContentProjectType.Datapack,
        _ => ContentProjectType.Unknown,
    };

    /// <summary>Mod loader ids accepted by the search and file endpoints.</summary>
    public static int? ModLoaderFor(string? loader) => loader?.ToLowerInvariant() switch
    {
        "forge" => 1,
        "fabric" => 4,
        "quilt" => 5,
        "neoforge" => 6,
        _ => null,
    };

    /// <summary>CurseForge release types: 1 release, 2 beta, 3 alpha.</summary>
    public static string ReleaseTypeName(int releaseType) => releaseType switch
    {
        1 => "release",
        2 => "beta",
        3 => "alpha",
        _ => "release",
    };

    /// <summary>CurseForge dependency relations: 3 required, 2 optional, 5 incompatible.</summary>
    public static string RelationName(int relationType) => relationType switch
    {
        1 => "embedded",
        2 => "optional",
        3 => "required",
        4 => "tool",
        5 => "incompatible",
        6 => "embedded",
        _ => "required",
    };
}
