using System.Globalization;
using System.Text.Json;
using Ferrite.Core.Diagnostics;
using Ferrite.Core.Net;
using Microsoft.Extensions.Logging;

namespace Ferrite.Core.Auth;

/// <summary>
/// The full Microsoft -> Xbox Live -> XSTS -> Minecraft services chain for a native desktop
/// application. No password is ever requested, received, or stored: sign-in happens in the browser
/// or on the Microsoft device-code page.
/// </summary>
public sealed class MicrosoftAuthClient
{
    private const string Scope = "XboxLive.signin offline_access";
    private const int MaxResponseBytes = 4 * 1024 * 1024;

    private readonly HttpService _http;
    private readonly ILogger<MicrosoftAuthClient> _logger;
    private readonly SecretRedactor _redactor;
    private readonly AuthEndpoints _endpoints;
    private readonly string? _clientId;

    public MicrosoftAuthClient(
        HttpService http,
        ILogger<MicrosoftAuthClient> logger,
        string? clientId,
        SecretRedactor? redactor = null,
        AuthEndpoints? endpoints = null)
    {
        _http = http;
        _logger = logger;
        _clientId = string.IsNullOrWhiteSpace(clientId) ? null : clientId.Trim();
        _redactor = redactor ?? SecretRedactor.Shared;
        _endpoints = endpoints ?? AuthEndpoints.Default;
    }

    public bool IsConfigured => _clientId is not null;

    public async Task<DeviceCodeChallenge> RequestDeviceCodeAsync(CancellationToken cancellationToken)
    {
        var clientId = RequireClientId();
        var json = await _http.PostFormJsonAsync(
                $"{_endpoints.Authority}/devicecode",
                [
                    new KeyValuePair<string, string>("client_id", clientId),
                    new KeyValuePair<string, string>("scope", Scope),
                ],
                MaxResponseBytes,
                cancellationToken)
            .ConfigureAwait(false);

        var deviceCode = GetString(json, "device_code")
            ?? throw new AuthenticationException("Microsoft did not return a device code.");
        var userCode = GetString(json, "user_code")
            ?? throw new AuthenticationException("Microsoft did not return a user code.");

        return new DeviceCodeChallenge(
            deviceCode,
            userCode,
            GetString(json, "verification_uri") ?? "https://microsoft.com/link",
            GetString(json, "verification_uri_complete"),
            GetInt(json, "interval") ?? 5,
            GetInt(json, "expires_in") ?? 900);
    }

    /// <summary>
    /// Polls until the user completes sign-in, honouring the interval the service asks for and the
    /// documented <c>authorization_pending</c> / <c>slow_down</c> responses.
    /// </summary>
    public async Task<MicrosoftTokenSet> WaitForTokenAsync(
        DeviceCodeChallenge challenge,
        IProgress<string>? status,
        CancellationToken cancellationToken)
    {
        var clientId = RequireClientId();
        var deadline = DateTimeOffset.UtcNow.AddSeconds(challenge.ExpiresInSeconds);
        var interval = Math.Max(1, challenge.IntervalSeconds);

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            status?.Report("Waiting for you to finish signing in...");

            try
            {
                var json = await _http.PostFormJsonAsync(
                        $"{_endpoints.Authority}/token",
                        [
                            new KeyValuePair<string, string>("client_id", clientId),
                            new KeyValuePair<string, string>("grant_type", "urn:ietf:params:oauth:grant-type:device_code"),
                            new KeyValuePair<string, string>("device_code", challenge.DeviceCode),
                        ],
                        MaxResponseBytes,
                        cancellationToken)
                    .ConfigureAwait(false);

                return ReadTokenSet(json);
            }
            catch (HttpException exception) when (exception.StatusCode is 400)
            {
                var error = ExtractErrorCode(exception.Message);
                switch (error)
                {
                    case "authorization_pending":
                        break;
                    case "slow_down":
                        interval += 5;
                        break;
                    case "authorization_declined":
                        throw new AuthenticationException("Sign-in was declined.", error, exception);
                    case "expired_token":
                        throw new AuthenticationException("The sign-in code expired. Start again.", error, exception);
                    case "bad_verification_code":
                        throw new AuthenticationException("The sign-in code was not accepted.", error, exception);
                    default:
                        throw new AuthenticationException(
                            $"Microsoft rejected the sign-in request ({error ?? "unknown error"}).",
                            error,
                            exception);
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(interval), cancellationToken).ConfigureAwait(false);
        }

        throw new AuthenticationException("The sign-in code expired before it was used.", "expired_token");
    }

    public async Task<MicrosoftTokenSet> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var clientId = RequireClientId();
        var json = await _http.PostFormJsonAsync(
                $"{_endpoints.Authority}/token",
                [
                    new KeyValuePair<string, string>("client_id", clientId),
                    new KeyValuePair<string, string>("grant_type", "refresh_token"),
                    new KeyValuePair<string, string>("refresh_token", refreshToken),
                    new KeyValuePair<string, string>("scope", Scope),
                ],
                MaxResponseBytes,
                cancellationToken)
            .ConfigureAwait(false);

        return ReadTokenSet(json);
    }

    /// <summary>Runs the Xbox Live, XSTS, and Minecraft services steps for a Microsoft token.</summary>
    public async Task<MinecraftSession> AuthenticateAsync(
        MicrosoftTokenSet microsoft,
        CancellationToken cancellationToken)
    {
        var xbox = await AuthenticateXboxLiveAsync(microsoft.AccessToken, cancellationToken).ConfigureAwait(false);
        var xsts = await AuthorizeXstsAsync(xbox.Token, cancellationToken).ConfigureAwait(false);
        return await LoginWithXboxAsync(xsts, cancellationToken).ConfigureAwait(false);
    }

    public async Task<XboxTokenSet> AuthenticateXboxLiveAsync(
        string microsoftAccessToken,
        CancellationToken cancellationToken)
    {
        _redactor.Register(microsoftAccessToken);

        var body = JsonSerializer.Serialize(new
        {
            Properties = new
            {
                AuthMethod = "RPS",
                SiteName = "user.auth.xboxlive.com",
                RpsTicket = "d=" + microsoftAccessToken,
            },
            RelyingParty = "http://auth.xboxlive.com",
            TokenType = "JWT",
        });

        var json = await _http
            .PostJsonAsync(_endpoints.XboxUser, body, MaxResponseBytes, cancellationToken)
            .ConfigureAwait(false);

        var token = GetString(json, "Token")
            ?? throw new AuthenticationException("Xbox Live did not return a token.");
        var userHash = ReadUserHash(json)
            ?? throw new AuthenticationException("Xbox Live did not return a user hash.");
        _redactor.Register(token);

        return new XboxTokenSet(token, userHash, null);
    }

    public async Task<XboxTokenSet> AuthorizeXstsAsync(string xboxToken, CancellationToken cancellationToken)
    {
        var body = JsonSerializer.Serialize(new
        {
            Properties = new
            {
                SandboxId = "RETAIL",
                UserTokens = new[] { xboxToken },
            },
            RelyingParty = "rp://api.minecraftservices.com/",
            TokenType = "JWT",
        });

        try
        {
            var json = await _http
                .PostJsonAsync(_endpoints.Xsts, body, MaxResponseBytes, cancellationToken)
                .ConfigureAwait(false);

            var token = GetString(json, "Token")
                ?? throw new AuthenticationException("XSTS did not return a token.");
            var userHash = ReadUserHash(json)
                ?? throw new AuthenticationException("XSTS did not return a user hash.");
            _redactor.Register(token);

            return new XboxTokenSet(token, userHash, ReadXuid(json));
        }
        catch (HttpException exception) when (exception.StatusCode is 401)
        {
            var xerr = ExtractXerr(exception.Message);
            var message = xerr switch
            {
                "2148916233" => "This Microsoft account has no Xbox profile. Sign in at xbox.com once, then retry.",
                "2148916235" => "Xbox Live is not available in this account's region.",
                "2148916236" or "2148916237" => "This account needs adult verification on the Xbox website.",
                "2148916238" => "This is a child account. A parent must add it to a Microsoft family first.",
                _ => "Xbox Live authorization failed for this account.",
            };
            throw new AuthenticationException(message, xerr, exception);
        }
    }

    public async Task<MinecraftSession> LoginWithXboxAsync(XboxTokenSet xsts, CancellationToken cancellationToken)
    {
        var identityToken = $"XBL3.0 x={xsts.UserHash};{xsts.Token}";
        var body = JsonSerializer.Serialize(new { identityToken });

        var json = await _http
            .PostJsonAsync(_endpoints.MinecraftLogin, body, MaxResponseBytes, cancellationToken)
            .ConfigureAwait(false);

        var accessToken = GetString(json, "access_token")
            ?? throw new AuthenticationException("Minecraft services did not return an access token.");
        _redactor.Register(accessToken);

        var expiresIn = GetInt(json, "expires_in") ?? 86_400;
        return new MinecraftSession(accessToken, DateTimeOffset.UtcNow.AddSeconds(expiresIn), xsts.Xuid, null, null);
    }

    public async Task<MinecraftProfile?> GetProfileAsync(string minecraftAccessToken, CancellationToken cancellationToken)
    {
        _redactor.Register(minecraftAccessToken);
        try
        {
            var json = await _http
                .GetJsonWithBearerAsync(_endpoints.Profile, minecraftAccessToken, MaxResponseBytes, cancellationToken)
                .ConfigureAwait(false);

            var uuid = GetString(json, "id");
            var name = GetString(json, "name");
            if (string.IsNullOrEmpty(uuid) || string.IsNullOrEmpty(name))
            {
                return null;
            }

            string? skinUrl = null;
            string? capeUrl = null;
            if (json.TryGetProperty("skins", out var skins) && skins.ValueKind == JsonValueKind.Array)
            {
                foreach (var skin in skins.EnumerateArray())
                {
                    if (string.Equals(GetString(skin, "state"), "ACTIVE", StringComparison.OrdinalIgnoreCase))
                    {
                        skinUrl = GetString(skin, "url");
                    }
                }
            }

            if (json.TryGetProperty("capes", out var capes) && capes.ValueKind == JsonValueKind.Array)
            {
                foreach (var cape in capes.EnumerateArray())
                {
                    capeUrl ??= GetString(cape, "url");
                }
            }

            return new MinecraftProfile(uuid, name, skinUrl, capeUrl);
        }
        catch (HttpException exception) when (exception.StatusCode is 404)
        {
            // No Minecraft profile means the account does not own the game.
            return null;
        }
    }

    public async Task<EntitlementResult> CheckEntitlementAsync(
        string minecraftAccessToken,
        CancellationToken cancellationToken)
    {
        _redactor.Register(minecraftAccessToken);
        var json = await _http
            .GetJsonWithBearerAsync(_endpoints.Entitlements, minecraftAccessToken, MaxResponseBytes, cancellationToken)
            .ConfigureAwait(false);

        var products = new List<string>();
        if (json.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                if (GetString(item, "name") is { } name)
                {
                    products.Add(name);
                }
            }
        }

        var owns = products.Any(product =>
            product.Contains("minecraft", StringComparison.OrdinalIgnoreCase)
            || product.Contains("game", StringComparison.OrdinalIgnoreCase));
        return new EntitlementResult(owns, products);
    }

    private MicrosoftTokenSet ReadTokenSet(JsonElement json)
    {
        var accessToken = GetString(json, "access_token")
            ?? throw new AuthenticationException("Microsoft did not return an access token.");
        var refreshToken = GetString(json, "refresh_token") ?? string.Empty;
        var expiresIn = GetInt(json, "expires_in") ?? 3600;

        _redactor.Register(accessToken);
        _redactor.Register(refreshToken);
        _logger.LogInformation("Microsoft token acquired, expiring in {Seconds}s", expiresIn);

        return new MicrosoftTokenSet(accessToken, refreshToken, DateTimeOffset.UtcNow.AddSeconds(expiresIn));
    }

    private string RequireClientId() =>
        _clientId ?? throw new AuthenticationException(
            "No Microsoft client id is configured. Add one in Settings; see docs/HUMAN_ACTION_REQUIRED.md.",
            "client_id_missing");

    private static string? ReadUserHash(JsonElement json)
    {
        if (!json.TryGetProperty("DisplayClaims", out var claims)
            || !claims.TryGetProperty("xui", out var xui)
            || xui.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var entry in xui.EnumerateArray())
        {
            if (GetString(entry, "uhs") is { } uhs)
            {
                return uhs;
            }
        }

        return null;
    }

    private static string? ReadXuid(JsonElement json)
    {
        if (!json.TryGetProperty("DisplayClaims", out var claims)
            || !claims.TryGetProperty("xui", out var xui)
            || xui.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var entry in xui.EnumerateArray())
        {
            if (GetString(entry, "xid") is { } xid)
            {
                return xid;
            }
        }

        return null;
    }

    private static string? ExtractErrorCode(string message)
    {
        var element = TryParseTrailingJson(message);
        return element is null ? null : GetString(element.Value, "error");
    }

    private static string? ExtractXerr(string message)
    {
        var element = TryParseTrailingJson(message);
        if (element is null)
        {
            return null;
        }

        // XErr values are 64-bit: for example 2148916238 exceeds Int32.MaxValue.
        var xerr = GetLong(element.Value, "XErr");
        return xerr?.ToString(CultureInfo.InvariantCulture);
    }

    private static long? GetLong(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.Number
            ? value.GetInt64()
            : null;

    private static JsonElement? TryParseTrailingJson(string message)
    {
        var start = message.IndexOf('{');
        if (start < 0)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(message[start..]);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? GetString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? GetInt(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : null;
}
