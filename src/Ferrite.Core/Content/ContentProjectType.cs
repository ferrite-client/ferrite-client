namespace Ferrite.Core.Content;

/// <summary>Project categories providers expose and Ferrite supports.</summary>
public enum ContentProjectType
{
    Mod,
    Modpack,
    ResourcePack,
    Shader,
    Datapack,
    Plugin,
    Unknown,
}

public static class ContentProjectTypes
{
    public static ContentProjectType Parse(string? value) => value switch
    {
        "mod" => ContentProjectType.Mod,
        "modpack" => ContentProjectType.Modpack,
        "resourcepack" => ContentProjectType.ResourcePack,
        "shader" => ContentProjectType.Shader,
        "datapack" => ContentProjectType.Datapack,
        "plugin" => ContentProjectType.Plugin,
        _ => ContentProjectType.Unknown,
    };

    public static string ToApiValue(this ContentProjectType type) => type switch
    {
        ContentProjectType.Mod => "mod",
        ContentProjectType.Modpack => "modpack",
        ContentProjectType.ResourcePack => "resourcepack",
        ContentProjectType.Shader => "shader",
        ContentProjectType.Datapack => "datapack",
        ContentProjectType.Plugin => "plugin",
        _ => "mod",
    };

    /// <summary>The instance subfolder content of this type is installed into.</summary>
    public static string TargetFolder(this ContentProjectType type) => type switch
    {
        ContentProjectType.Mod => "mods",
        ContentProjectType.ResourcePack => "resourcepacks",
        ContentProjectType.Shader => "shaderpacks",
        ContentProjectType.Datapack => "datapacks",
        ContentProjectType.Plugin => "plugins",
        _ => "mods",
    };
}
