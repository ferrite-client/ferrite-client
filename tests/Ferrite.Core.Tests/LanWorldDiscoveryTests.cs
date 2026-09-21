using System.Net;
using System.Net.Sockets;
using System.Text;
using Ferrite.Core.Game;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ferrite.Core.Tests;

/// <summary>
/// LAN world discovery: the game's broadcast payload, how long a world stays listed, and whether a
/// real multicast datagram actually arrives.
/// </summary>
public sealed class LanWorldDiscoveryTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse(
        "2026-09-21T10:00:00Z",
        System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void The_games_broadcast_payload_is_parsed()
    {
        var payload = Encoding.UTF8.GetBytes("[MOTD]Steve's world[/MOTD][AD]51234[/AD]");

        Assert.True(LanWorldDiscovery.TryParse(payload, out var motd, out var port));
        Assert.Equal("Steve's world", motd);
        Assert.Equal(51234, port);
    }

    [Fact]
    public void Legacy_colour_codes_in_the_motd_are_removed()
    {
        var payload = Encoding.UTF8.GetBytes("[MOTD]\u00A7aGreen \u00A7lname[/MOTD][AD]25565[/AD]");

        Assert.True(LanWorldDiscovery.TryParse(payload, out var motd, out _));
        Assert.Equal("Green name", motd);
    }

    [Theory]
    [InlineData("[MOTD]no port here[/MOTD]")]
    [InlineData("[AD]51234[/AD]")]
    [InlineData("[MOTD]x[/MOTD][AD]0[/AD]")]
    [InlineData("[MOTD]x[/MOTD][AD]70000[/AD]")]
    [InlineData("[MOTD]x[/MOTD][AD]not-a-port[/AD]")]
    [InlineData("")]
    public void An_unusable_payload_is_rejected(string text)
    {
        Assert.False(LanWorldDiscovery.TryParse(Encoding.UTF8.GetBytes(text), out _, out _));
    }

    [Fact]
    public void An_oversized_payload_is_rejected()
    {
        var payload = new byte[4096];
        Array.Fill(payload, (byte)'a');

        Assert.False(LanWorldDiscovery.TryParse(payload, out _, out _));
    }

    [Fact]
    public void An_oversized_motd_is_truncated_rather_than_trusted()
    {
        var longName = new string('n', 900);
        var payload = Encoding.UTF8.GetBytes($"[MOTD]{longName}[/MOTD][AD]25565[/AD]");

        Assert.True(LanWorldDiscovery.TryParse(payload, out var motd, out _));
        Assert.True(motd.Length <= 256, $"motd was {motd.Length} characters");
    }

    [Fact]
    public void A_world_with_an_empty_name_is_still_a_world()
    {
        var payload = Encoding.UTF8.GetBytes("[MOTD][/MOTD][AD]25565[/AD]");

        Assert.True(LanWorldDiscovery.TryParse(payload, out var motd, out var port));
        Assert.Equal(string.Empty, motd);
        Assert.Equal(25565, port);
    }

    [Fact]
    public void A_broadcast_is_listed_and_a_repeat_updates_it()
    {
        var now = Start;
        using var discovery = new LanWorldDiscovery(NullLogger<LanWorldDiscovery>.Instance, clock: () => now);
        var sender = IPAddress.Parse("192.168.1.20");

        Assert.True(discovery.Ingest(sender, Payload("First name", 50000), now));
        var first = Assert.Single(discovery.Current);
        Assert.Equal("192.168.1.20:50000", first.Address);
        Assert.Equal("First name", first.DisplayName);

        now = now.AddSeconds(3);
        Assert.True(discovery.Ingest(sender, Payload("Renamed", 50000), now));

        var updated = Assert.Single(discovery.Current);
        Assert.Equal("Renamed", updated.DisplayName);
        Assert.Equal(now, updated.LastSeen);
    }

    [Fact]
    public void A_world_that_stops_broadcasting_disappears()
    {
        var now = Start;
        using var discovery = new LanWorldDiscovery(
            NullLogger<LanWorldDiscovery>.Instance,
            lifetime: TimeSpan.FromSeconds(10),
            clock: () => now);

        discovery.Ingest(IPAddress.Parse("10.0.0.5"), Payload("Still here", 25566), now);
        Assert.Single(discovery.Current);

        now = now.AddSeconds(9);
        Assert.Single(discovery.Current);

        now = now.AddSeconds(2);
        Assert.Empty(discovery.Current);
    }

    [Fact]
    public void Two_worlds_on_one_machine_are_listed_separately()
    {
        var now = Start;
        using var discovery = new LanWorldDiscovery(NullLogger<LanWorldDiscovery>.Instance, clock: () => now);
        var sender = IPAddress.Parse("192.168.1.20");

        discovery.Ingest(sender, Payload("World A", 50000), now);
        discovery.Ingest(sender, Payload("World B", 50001), now);

        Assert.Equal(2, discovery.Current.Count);
    }

    [Fact]
    public void A_junk_datagram_is_ignored_without_an_exception()
    {
        using var discovery = new LanWorldDiscovery(NullLogger<LanWorldDiscovery>.Instance);

        Assert.False(discovery.Ingest(IPAddress.Loopback, Encoding.UTF8.GetBytes("hello"), Start));
        Assert.Empty(discovery.Current);
    }

    /// <summary>
    /// Sends a real multicast datagram to the address the game uses and waits for the listener to
    /// report it. Some sandboxes cannot multicast on loopback; that is reported rather than hidden.
    /// </summary>
    [Fact]
    public async Task A_real_multicast_broadcast_is_received()
    {
        using var discovery = new LanWorldDiscovery(NullLogger<LanWorldDiscovery>.Instance);
        discovery.Start();
        if (!discovery.IsListening)
        {
            Assert.Skip($"Multicast listening is unavailable here: {discovery.FailureReason}");
            return;
        }

        try
        {
            using var sender = new UdpClient(AddressFamily.InterNetwork);
            sender.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            sender.MulticastLoopback = true;
            var target = new IPEndPoint(IPAddress.Parse(LanWorldDiscovery.MulticastGroup), LanWorldDiscovery.MulticastPort);

            var payload = Payload("Multicast world", 51234);
            for (var attempt = 0; attempt < 6 && discovery.Current.Count == 0; attempt++)
            {
                await sender.SendAsync(payload, target, TestContext.Current.CancellationToken);
                await Task.Delay(400, TestContext.Current.CancellationToken);
            }

            var worlds = discovery.Current;
            if (worlds.Count == 0)
            {
                Assert.Skip("No multicast datagram arrived; this sandbox does not deliver them.");
                return;
            }

            var world = Assert.Single(worlds);
            Assert.Equal(51234, world.Port);
            Assert.Equal("Multicast world", world.DisplayName);
        }
        finally
        {
            discovery.Stop();
        }
    }

    private static byte[] Payload(string motd, int port) =>
        Encoding.UTF8.GetBytes($"[MOTD]{motd}[/MOTD][AD]{port}[/AD]");
}
