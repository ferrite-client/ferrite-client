using System.Text.Json;
using Ferrite.Core.Game;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ferrite.Core.Tests;

public sealed class ServerPingerTests
{
    [Theory]
    [InlineData("play.example.net", 25565)]
    [InlineData("play.example.net:25566", 25566)]
    [InlineData("[::1]:25567", 25567)]
    [InlineData("[2001:db8::1]", 25565)]
    public void Parses_server_addresses(string address, int expectedPort)
    {
        var (_, port) = ServerPinger.ParseAddress(address, null);
        Assert.Equal(expectedPort, port);
    }

    [Fact]
    public void An_explicit_port_wins_over_the_address()
    {
        var (host, port) = ServerPinger.ParseAddress("play.example.net:25566", 25599);
        Assert.Equal("play.example.net:25566", host);
        Assert.Equal(25599, port);
    }

    [Fact]
    public void Parses_a_status_response_with_a_string_description()
    {
        var status = ServerPinger.ParseStatus(
            "play.example.net",
            25565,
            """
            {
              "version": { "name": "1.21.1", "protocol": 767 },
              "players": { "max": 100, "online": 42 },
              "description": "A Minecraft Server"
            }
            """,
            TimeSpan.FromMilliseconds(37));

        Assert.True(status.Online);
        Assert.Equal("A Minecraft Server", status.Motd);
        Assert.Equal(42, status.PlayersOnline);
        Assert.Equal(100, status.PlayersMax);
        Assert.Equal("1.21.1", status.VersionName);
        Assert.Equal(767, status.ProtocolVersion);
        Assert.Equal("42/100", status.PlayerCountText);
        Assert.Equal("37 ms", status.LatencyText);
    }

    [Fact]
    public void Flattens_a_component_description_with_extras_and_colour_codes()
    {
        var status = ServerPinger.ParseStatus(
            "play.example.net",
            25565,
            """
            {
              "description": {
                "text": "\u00a7aWelcome",
                "extra": [ { "text": " to " }, { "text": "Ferrite", "extra": [ { "text": "!" } ] } ]
              }
            }
            """,
            TimeSpan.FromMilliseconds(5));

        Assert.Equal("Welcome to Ferrite!", status.Motd);
    }

    [Fact]
    public void Flattens_an_array_description()
    {
        var element = JsonDocument.Parse("""[{"text":"A"},{"text":"B"}]""").RootElement;
        Assert.Equal("AB", ServerPinger.FlattenChatComponent(element));
    }

    [Fact]
    public async Task An_unreachable_server_reports_offline_rather_than_throwing()
    {
        var pinger = new ServerPinger(NullLogger<ServerPinger>.Instance);
        var status = await pinger.PingAsync(
            "127.0.0.1",
            1,
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        Assert.False(status.Online);
        Assert.NotNull(status.Error);
        Assert.Equal("offline", status.LatencyText);
    }
}
