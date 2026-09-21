namespace Ferrite.Core.Content;

/// <summary>
/// Compatibility rules shared by every content provider. A version whose game version or loader
/// does not match the instance is never selected, so an incompatible file cannot be installed by
/// accident.
/// </summary>
public static class ContentCompatibility
{
    private static readonly string[] KnownLoaders =
    [
        "fabric", "quilt", "forge", "neoforge", "liteloader", "rift", "cauldron", "optifine",
    ];

    /// <summary>Never returns true for a version that does not match the instance's game/loader.</summary>
    public static bool IsCompatible(ContentVersion version, string? gameVersion, string? loader)
    {
        ArgumentNullException.ThrowIfNull(version);

        if (!string.IsNullOrEmpty(gameVersion)
            && version.GameVersions.Count > 0
            && !version.GameVersions.Any(candidate => Matches(candidate, gameVersion)))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(loader)
            && version.Loaders.Count > 0
            && !version.Loaders.Any(candidate => Matches(candidate, loader)))
        {
            return false;
        }

        return true;
    }

    /// <summary>Selects the newest compatible version, preferring full releases when available.</summary>
    public static ContentVersion? SelectBestVersion(
        IReadOnlyList<ContentVersion> versions,
        string? gameVersion,
        string? loader,
        bool preferRelease = true)
    {
        ArgumentNullException.ThrowIfNull(versions);

        var compatible = versions.Where(version => IsCompatible(version, gameVersion, loader)).ToList();
        if (compatible.Count == 0)
        {
            return null;
        }

        if (preferRelease)
        {
            var releases = compatible.Where(version => version.IsRelease).ToList();
            if (releases.Count > 0)
            {
                compatible = releases;
            }
        }

        return compatible
            .OrderByDescending(version => version.PublishedAt ?? DateTimeOffset.MinValue)
            .First();
    }

    /// <summary>
    /// Splits a provider's flat version token list into game versions and loader names. CurseForge
    /// mixes both into one array alongside tokens such as "Client" that carry no compatibility
    /// meaning.
    /// </summary>
    public static (IReadOnlyList<string> GameVersions, IReadOnlyList<string> Loaders) SplitVersionTokens(
        IReadOnlyList<string> tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);

        var gameVersions = new List<string>();
        var loaders = new List<string>();
        foreach (var token in tokens)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                continue;
            }

            if (IsKnownLoader(token))
            {
                loaders.Add(token);
            }
            else if (char.IsAsciiDigit(token[0]))
            {
                gameVersions.Add(token);
            }
        }

        return (gameVersions, loaders);
    }

    /// <summary>Maps a loader token from either provider onto the launcher's loader kind.</summary>
    public static Minecraft.LoaderKind? LoaderKindFor(string? token) => Normalize(token ?? string.Empty) switch
    {
        "fabric" => Minecraft.LoaderKind.Fabric,
        "quilt" => Minecraft.LoaderKind.Quilt,
        "forge" => Minecraft.LoaderKind.Forge,
        "neoforge" => Minecraft.LoaderKind.NeoForge,
        "optifine" => Minecraft.LoaderKind.OptiFine,
        _ => null,
    };

    private static bool IsKnownLoader(string token)
    {
        var normalized = Normalize(token);
        return KnownLoaders.Contains(normalized, StringComparer.Ordinal);
    }

    private static bool Matches(string candidate, string expected) =>
        string.Equals(Normalize(candidate), Normalize(expected), StringComparison.Ordinal);

    /// <summary>Providers differ in case and separators ("NeoForge" vs "neoforge").</summary>
    private static string Normalize(string value) =>
        value.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
}
