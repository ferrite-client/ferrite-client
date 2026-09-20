namespace Ferrite.Core.Auth;

/// <summary>
/// A signed-in account. Token material lives in the protected secret store, never in this record,
/// so the account list can be read without touching credentials.
/// </summary>
public sealed class AccountRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>"microsoft" or "yggdrasil".</summary>
    public string Kind { get; set; } = "microsoft";

    public string? PlayerName { get; set; }

    public string? Uuid { get; set; }

    public string? Xuid { get; set; }

    public string? SkinUrl { get; set; }

    /// <summary>Base URL of a Yggdrasil-compatible authentication server.</summary>
    public string? AuthServerUrl { get; set; }

    public bool OwnsMinecraft { get; set; } = true;

    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastUsedAt { get; set; }

    public string DisplayName => PlayerName ?? "(signed out)";
}
