using Ferrite.Core.Minecraft;
using Microsoft.Extensions.Logging;

namespace Ferrite.Core.Auth;

/// <summary>
/// Ties authentication to persistence: signs an account in, keeps its tokens fresh, and produces the
/// credential set the launch pipeline needs.
/// </summary>
public sealed class AccountService
{
    private static readonly TimeSpan RefreshThreshold = TimeSpan.FromMinutes(10);

    private readonly AccountStore _store;
    private readonly MicrosoftAuthClient _microsoft;
    private readonly ILogger<AccountService> _logger;

    public AccountService(AccountStore store, MicrosoftAuthClient microsoft, ILogger<AccountService> logger)
    {
        _store = store;
        _microsoft = microsoft;
        _logger = logger;
    }

    public IReadOnlyList<AccountRecord> Accounts => _store.Accounts;

    public bool CanSignIn => _microsoft.IsConfigured;

    public bool IsSecretStorageDegraded => _store.IsSecretStorageDegraded;

    public void Load() => _store.Load();

    public Task<DeviceCodeChallenge> StartSignInAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting Microsoft device-code sign-in");
        return _microsoft.RequestDeviceCodeAsync(cancellationToken);
    }

    /// <summary>Completes sign-in for a device code and persists the account.</summary>
    public async Task<AccountRecord> CompleteSignInAsync(
        DeviceCodeChallenge challenge,
        IProgress<string>? status,
        CancellationToken cancellationToken)
    {
        var microsoft = await _microsoft
            .WaitForTokenAsync(challenge, status, cancellationToken)
            .ConfigureAwait(false);

        status?.Report("Signing in to Xbox Live...");
        var session = await _microsoft.AuthenticateAsync(microsoft, cancellationToken).ConfigureAwait(false);

        status?.Report("Checking Minecraft ownership...");
        var entitlement = await _microsoft
            .CheckEntitlementAsync(session.AccessToken, cancellationToken)
            .ConfigureAwait(false);

        status?.Report("Reading your Minecraft profile...");
        var profile = await _microsoft.GetProfileAsync(session.AccessToken, cancellationToken).ConfigureAwait(false);

        var account = new AccountRecord
        {
            Id = Guid.NewGuid(),
            Kind = "microsoft",
            PlayerName = profile?.PlayerName ?? "Unknown",
            Uuid = profile?.Uuid,
            Xuid = session.Xuid,
            SkinUrl = profile?.SkinUrl,
            OwnsMinecraft = entitlement.OwnsMinecraft,
            AddedAt = DateTimeOffset.UtcNow,
            LastUsedAt = DateTimeOffset.UtcNow,
        };

        _store.Add(account);
        _store.SetSecret(account.Id, "msa-access", microsoft.AccessToken);
        _store.SetSecret(account.Id, "msa-refresh", microsoft.RefreshToken);
        _store.SetSecret(account.Id, "mc-access", session.AccessToken);
        _store.SetSecret(account.Id, "mc-expires", session.ExpiresAt.ToString("O"));
        _store.Save();

        _logger.LogInformation("Signed in as {Name}", account.PlayerName);
        return account;
    }

    public void SignOut(Guid accountId)
    {
        _store.Remove(accountId);
        _logger.LogInformation("Signed out account {Id}", accountId);
    }

    /// <summary>
    /// Produces launch credentials, refreshing tokens close to expiry. Returns null when the account
    /// cannot be used without signing in again.
    /// </summary>
    public async Task<LaunchAccount?> GetLaunchAccountAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var account = _store.Find(accountId);
        if (account is null)
        {
            return null;
        }

        var accessToken = _store.GetSecret(accountId, "mc-access");
        var expires = ReadExpiry(_store.GetSecret(accountId, "mc-expires"));

        if (string.IsNullOrEmpty(accessToken) || expires is null || expires - DateTimeOffset.UtcNow < RefreshThreshold)
        {
            var refreshed = await RefreshAsync(accountId, cancellationToken).ConfigureAwait(false);
            if (refreshed is null)
            {
                return null;
            }

            accessToken = refreshed.AccessToken;
        }

        return new LaunchAccount
        {
            PlayerName = account.PlayerName ?? "Player",
            Uuid = account.Uuid ?? "00000000000000000000000000000000",
            AccessToken = accessToken!,
            Xuid = account.Xuid,
            UserType = account.Kind == "yggdrasil" ? "mojang" : "msa",
        };
    }

    /// <summary>Refreshes the Minecraft session, returning null when it cannot be refreshed.</summary>
    public async Task<MinecraftSession?> RefreshAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var refreshToken = _store.GetSecret(accountId, "msa-refresh");
        if (string.IsNullOrEmpty(refreshToken))
        {
            return null;
        }

        try
        {
            var microsoft = await _microsoft.RefreshAsync(refreshToken, cancellationToken).ConfigureAwait(false);
            var session = await _microsoft.AuthenticateAsync(microsoft, cancellationToken).ConfigureAwait(false);

            _store.SetSecret(accountId, "msa-access", microsoft.AccessToken);
            if (!string.IsNullOrEmpty(microsoft.RefreshToken))
            {
                _store.SetSecret(accountId, "msa-refresh", microsoft.RefreshToken);
            }

            _store.SetSecret(accountId, "mc-access", session.AccessToken);
            _store.SetSecret(accountId, "mc-expires", session.ExpiresAt.ToString("O"));
            _store.Save();

            _logger.LogInformation("Refreshed the Minecraft session for account {Id}", accountId);
            return session;
        }
        catch (AuthenticationException exception)
        {
            _logger.LogWarning(exception, "The session for account {Id} could not be refreshed", accountId);
            return null;
        }
    }

    private static DateTimeOffset? ReadExpiry(string? value) =>
        DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
}
