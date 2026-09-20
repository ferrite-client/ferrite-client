namespace Ferrite.Core.Minecraft;

public sealed class AssetIndexDocument
{
    public AssetIndexInfo? Info { get; set; }

    public Dictionary<string, AssetObject> Objects { get; set; } = new(StringComparer.Ordinal);
}

public sealed class AssetIndexInfo
{
    /// <summary>Legacy asset indexes mirror objects into a virtual directory tree.</summary>
    public bool? Virtual { get; set; }

    /// <summary>Very old indexes copy resources directly into the game directory.</summary>
    public bool? MapToResources { get; set; }
}

public sealed class AssetObject
{
    public string Hash { get; set; } = string.Empty;

    public long Size { get; set; }
}
