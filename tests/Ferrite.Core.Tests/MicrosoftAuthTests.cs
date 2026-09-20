using Ferrite.Core.Auth;
using Ferrite.Core.Diagnostics;
using Ferrite.Core.Net;
using Ferrite.Core.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ferrite.Core.Tests;

/// <summary>
/// Exercises the authentication chain against a scripted HTTP boundary. This proves request shapes,
/// error mapping, and redaction. It is not evidence that a real Microsoft account signs in, which
/// needs a client id and a person (see docs/HUMAN_ACTION_REQUIRED.md).
/// </summary>
public sealed class MicrosoftAuthTests : IAsyncLifetime
{
    private const string ClientId = "00000000-1111-2222-3333-444444444444";

    private TestHttpServer _server = null!;
    private HttpService _http = null!;
    private AuthEndpoints _endpoints = null!;
    private SecretRedactor _redactor = null!;

    public ValueTask InitializeAsync()
    {
        _server = new TestHttpServer();
        _http = new HttpService(new HttpServiceOptions(), NullLogger<HttpService>.Instance);
        _redactor = new SecretRedactor();
        _endpoints = new AuthEndpoints(
            _server.BaseUrl + "/oauth",
            _server.BaseUrl + "/xbox/user",
            _server.BaseUrl + "/xbox/xsts",
            _server.BaseUrl + "/mc/login",
            _server.BaseUrl + "/mc/profile",
            _server.BaseUrl + "/mc/entitlements");
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await _server.DisposeAsync();
    }

    [Fact]
    public async Task Device_code_request_uses_the_expected_shape()
    {
        _server.AddHandler("POST", "/oauth/devicecode", _ => new TestResponse(
            200,
            """
            {
              "device_code": "device-code-value",
              "user_code": "ABCD-EFGH",
              "verification_uri": "https://microsoft.com/link",
              "verification_uri_complete": "https://microsoft.com/link?code=ABCD-EFGH",
              "expires_in": 900,
              "interval": 5
            }
            """));

        var challenge = await CreateClient().RequestDeviceCodeAsync(TestContext.Current.CancellationToken);

        Assert.Equal("ABCD-EFGH", challenge.UserCode);
        Assert.Equal("device-code-value", challenge.DeviceCode);
        Assert.Equal(5, challenge.IntervalSeconds);
        Assert.Equal(900, challenge.ExpiresInSeconds);

        var request = _server.LastRequest("/oauth/devicecode");
        Assert.NotNull(request);
        Assert.Contains("client_id=" + ClientId, request!.Body, StringComparison.Ordinal);
        Assert.Contains("XboxLive.signin", request.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Polling_waits_through_authorization_pending_then_succeeds()
    {
        var attempts = 0;
        _server.AddHandler("POST", "/oauth/token", _ =>
        {
            attempts++;
            return attempts < 3
                ? new TestResponse(400, """{"error":"authorization_pending"}""")
                : new TestResponse(
                    200,
                    """{"access_token":"msa-token","refresh_token":"msa-refresh","expires_in":3600}""");
        });

        var challenge = new DeviceCodeChallenge("device", "CODE", "https://microsoft.com/link", null, 1, 30);
        var tokens = await CreateClient().WaitForTokenAsync(challenge, null, TestContext.Current.CancellationToken);

        Assert.Equal("msa-token", tokens.AccessToken);
        Assert.Equal("msa-refresh", tokens.RefreshToken);
        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task Declined_sign_in_surfaces_a_clear_error()
    {
        _server.AddHandler("POST", "/oauth/token", _ => new TestResponse(
            400,
            """{"error":"authorization_declined","error_description":"user said no"}"""));

        var challenge = new DeviceCodeChallenge("device", "CODE", "https://microsoft.com/link", null, 1, 30);
        var exception = await Assert.ThrowsAsync<AuthenticationException>(() =>
            CreateClient().WaitForTokenAsync(challenge, null, TestContext.Current.CancellationToken));

        Assert.Equal("authorization_declined", exception.Code);
        Assert.Contains("declined", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Full_chain_produces_a_minecraft_session_and_profile()
    {
        _server.AddHandler("POST", "/xbox/user", request =>
        {
            Assert.Contains("d=msa-token", request.Body, StringComparison.Ordinal);
            Assert.Contains("user.auth.xboxlive.com", request.Body, StringComparison.Ordinal);
            return new TestResponse(
                200,
                """{"Token":"xbl-token","DisplayClaims":{"xui":[{"uhs":"user-hash"}]}}""");
        });

        _server.AddHandler("POST", "/xbox/xsts", request =>
        {
            Assert.Contains("xbl-token", request.Body, StringComparison.Ordinal);
            Assert.Contains("rp://api.minecraftservices.com/", request.Body, StringComparison.Ordinal);
            return new TestResponse(
                200,
                """{"Token":"xsts-token","DisplayClaims":{"xui":[{"uhs":"user-hash","xid":"2535410000000000"}]}}""");
        });

        _server.AddHandler("POST", "/mc/login", request =>
        {
            Assert.Contains("XBL3.0 x=user-hash;xsts-token", request.Body, StringComparison.Ordinal);
            return new TestResponse(200, """{"access_token":"mc-token","expires_in":86400}""");
        });

        _server.AddHandler("GET", "/mc/profile", _ => new TestResponse(
            200,
            """
            {
              "id": "069a79f444e94726a5befca90e38aaf5",
              "name": "Steve",
              "skins": [{ "state": "ACTIVE", "url": "https://example.invalid/skin.png" }],
              "capes": [{ "url": "https://example.invalid/cape.png" }]
            }
            """));

        _server.AddHandler("GET", "/mc/entitlements", _ => new TestResponse(
            200,
            """{"items":[{"name":"product_minecraft"},{"name":"game_minecraft"}]}"""));

        var client = CreateClient();
        var session = await client.AuthenticateAsync(
            new MicrosoftTokenSet("msa-token", "msa-refresh", DateTimeOffset.UtcNow.AddHours(1)),
            TestContext.Current.CancellationToken);

        Assert.Equal("mc-token", session.AccessToken);
        Assert.Equal("2535410000000000", session.Xuid);

        var profile = await client.GetProfileAsync(session.AccessToken, TestContext.Current.CancellationToken);
        Assert.NotNull(profile);
        Assert.Equal("Steve", profile!.PlayerName);
        Assert.Equal("https://example.invalid/skin.png", profile.SkinUrl);
        Assert.Equal("https://example.invalid/cape.png", profile.CapeUrl);

        var entitlement = await client.CheckEntitlementAsync(session.AccessToken, TestContext.Current.CancellationToken);
        Assert.True(entitlement.OwnsMinecraft);
    }

    [Fact]
    public async Task Xsts_child_account_error_maps_to_actionable_text()
    {
        _server.AddHandler("POST", "/xbox/xsts", _ => new TestResponse(
            401,
            """{"Identity":"0","XErr":2148916238,"Message":"","Redirect":"https://start.ui.xboxlive.com/AddChildToFamily"}"""));

        var exception = await Assert.ThrowsAsync<AuthenticationException>(() =>
            CreateClient().AuthorizeXstsAsync("xbl-token", TestContext.Current.CancellationToken));

        Assert.Equal("2148916238", exception.Code);
        Assert.Contains("child account", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Missing_client_id_is_reported_as_configuration_not_failure()
    {
        var client = new MicrosoftAuthClient(
            _http,
            NullLogger<MicrosoftAuthClient>.Instance,
            clientId: null,
            _redactor,
            _endpoints);

        Assert.False(client.IsConfigured);
        var exception = await Assert.ThrowsAsync<AuthenticationException>(() =>
            client.RequestDeviceCodeAsync(TestContext.Current.CancellationToken));
        Assert.Equal("client_id_missing", exception.Code);
    }

    [Fact]
    public async Task Tokens_are_registered_for_redaction()
    {
        _server.AddHandler("POST", "/xbox/user", _ => new TestResponse(
            200,
            """{"Token":"xbl-secret-token","DisplayClaims":{"xui":[{"uhs":"hash"}]}}"""));

        await CreateClient().AuthenticateXboxLiveAsync("msa-secret-token", TestContext.Current.CancellationToken);

        var redacted = _redactor.Redact("calling with msa-secret-token and xbl-secret-token");
        Assert.DoesNotContain("msa-secret-token", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("xbl-secret-token", redacted, StringComparison.Ordinal);
        Assert.Contains("redacted", redacted, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Entitlement_check_reports_missing_ownership()
    {
        _server.AddHandler("GET", "/mc/entitlements", _ => new TestResponse(200, """{"items":[]}"""));

        var result = await CreateClient().CheckEntitlementAsync("mc-token", TestContext.Current.CancellationToken);
        Assert.False(result.OwnsMinecraft);
    }

    [Fact]
    public async Task Profile_404_means_no_minecraft_profile()
    {
        _server.AddHandler("GET", "/mc/profile", _ => new TestResponse(404, """{"error":"NOT_FOUND"}"""));

        var profile = await CreateClient().GetProfileAsync("mc-token", TestContext.Current.CancellationToken);
        Assert.Null(profile);
    }

    private MicrosoftAuthClient CreateClient() => new(
        _http,
        NullLogger<MicrosoftAuthClient>.Instance,
        ClientId,
        _redactor,
        _endpoints);
}
