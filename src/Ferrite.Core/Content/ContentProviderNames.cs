namespace Ferrite.Core.Content;

/// <summary>Display labels for provider tokens, kept in one place so the UI never hardcodes them.</summary>
public static class ContentProviderNames
{
    public static string DisplayNameFor(string provider) => provider switch
    {
        CurseForgeClient.ProviderName => "CurseForge",
        ModrinthClient.ProviderName => "Modrinth",
        FtbClient.ProviderName => "FTB",
        _ => provider,
    };
}
