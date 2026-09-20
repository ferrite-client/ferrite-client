namespace Ferrite.Core.Auth;

/// <summary>Device-code challenge returned by the Microsoft identity platform.</summary>
public sealed record DeviceCodeChallenge(
    string DeviceCode,
    string UserCode,
    string VerificationUri,
    string? VerificationUriComplete,
    int IntervalSeconds,
    int ExpiresInSeconds);

public sealed record MicrosoftTokenSet(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);

public sealed record XboxTokenSet(string Token, string UserHash, string? Xuid);

public sealed record MinecraftSession(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    string? Xuid,
    string? PlayerName,
    string? Uuid);

public sealed record MinecraftProfile(string Uuid, string PlayerName, string? SkinUrl, string? CapeUrl);

/// <summary>Outcome of an entitlement check.</summary>
public sealed record EntitlementResult(bool OwnsMinecraft, IReadOnlyList<string> Products);

public sealed class AuthenticationException : Exception
{
    public AuthenticationException(string message, string? code = null, Exception? inner = null)
        : base(message, inner)
    {
        Code = code;
    }

    /// <summary>Provider error code, for example <c>authorization_pending</c> or an XSTS XErr.</summary>
    public string? Code { get; }
}
