using System.Net;
using System.Net.Sockets;
using System.Text;
using Ferrite.Core.Game;

namespace Ferrite.Verify;

/// <summary>LAN world discovery.</summary>
internal static partial class Scenarios
{
    /// <summary>
    /// Listens on the address Minecraft broadcasts to, emulates one broadcast so the path is proven
    /// without a second machine, and then keeps listening so a real LAN world on this network would
    /// also be reported.
    /// </summary>
    public static async Task<int> LanWorldsAsync(
        VerifyServices services,
        int seconds,
        string? motd,
        int? port,
        CancellationToken cancellationToken)
    {
        using var discovery = services.LanWorlds;
        discovery.Start();

        if (!discovery.IsListening)
        {
            Console.WriteLine($"Cannot listen for LAN worlds here: {discovery.FailureReason}");
            return 2;
        }

        Console.WriteLine(
            $"Listening on {LanWorldDiscovery.MulticastGroup}:{LanWorldDiscovery.MulticastPort} "
            + $"for {seconds}s");

        var emulated = motd ?? "Ferrite verification world";
        var emulatedPort = port ?? 51234;
        using (var sender = new UdpClient(AddressFamily.InterNetwork))
        {
            sender.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            sender.MulticastLoopback = true;
            var payload = Encoding.UTF8.GetBytes($"[MOTD]{emulated}[/MOTD][AD]{emulatedPort}[/AD]");
            var target = new IPEndPoint(
                IPAddress.Parse(LanWorldDiscovery.MulticastGroup),
                LanWorldDiscovery.MulticastPort);

            for (var attempt = 0; attempt < 3; attempt++)
            {
                await sender.SendAsync(payload, target, cancellationToken).ConfigureAwait(false);
                await Task.Delay(300, cancellationToken).ConfigureAwait(false);
            }

            Console.WriteLine($"Emulated a Minecraft broadcast: \"{emulated}\" on port {emulatedPort}");
        }

        var deadline = DateTimeOffset.UtcNow.AddSeconds(seconds);
        var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var found = 0;

        while (DateTimeOffset.UtcNow < deadline)
        {
            foreach (var world in discovery.Current)
            {
                if (!reported.Add(world.Address))
                {
                    continue;
                }

                found++;
                Console.WriteLine($"  LAN world: {world.DisplayName} at {world.Address}");
            }

            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }

        Console.WriteLine();
        if (found == 0)
        {
            Console.WriteLine(
                "No LAN broadcasts were received. Multicast may be blocked on this network; "
                + "the parser and aggregation are covered by tests.");
            return 3;
        }

        Console.WriteLine($"{found} LAN world(s) observed.");
        return 0;
    }
}
