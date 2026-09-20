namespace Ferrite.Core.Auth;

/// <summary>Endpoint set for the authentication chain, overridable for tests and Yggdrasil servers.</summary>
public sealed record AuthEndpoints(
    string Authority,
    string XboxUser,
    string Xsts,
    string MinecraftLogin,
    string Profile,
    string Entitlements)
{
    public static AuthEndpoints Default { get; } = new(
        "https://login.microsoftonline.com/consumers/oauth2/v2.0",
        "https://user.auth.xboxlive.com/user/authenticate",
        "https://xsts.auth.xboxlive.com/xsts/authorize",
        "https://api.minecraftservices.com/authentication/login_with_xbox",
        "https://api.minecraftservices.com/minecraft/profile",
        "https://api.minecraftservices.com/entitlements/mcstore");
}
