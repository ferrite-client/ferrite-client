using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Ferrite.Core.Game;

/// <summary>A world another player has opened to the local network.</summary>
public sealed record LanWorld(
    string Host,
    int Port,
    string Motd,
    DateTimeOffset LastSeen)
{
    /// <summary>Ready to paste into the server list, and to ping.</summary>
    public string Address => $"{Host}:{Port}";

    public string DisplayName => string.IsNullOrWhiteSpace(Motd) ? Address : Motd;
}

/// <summary>
/// Listens for Minecraft's "Open to LAN" broadcasts and keeps the worlds that are still announcing
/// themselves. The game publishes a small UDP datagram to a multicast address in the local network
/// control block (224.0.0.0/24), which routers do not forward, so this only ever sees the local
/// network.
/// </summary>
public sealed class LanWorldDiscovery : IDisposable
{
    /// <summary>Where Minecraft broadcasts LAN worlds. Fixed by the game, not configurable.</summary>
    public const string MulticastGroup = "224.0.2.60";

    public const int MulticastPort = 4445;

    /// <summary>A world stops being listed when it has not broadcast for this long.</summary>
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromSeconds(15);

    private const int MaxPayloadBytes = 1024;
    private const int MaxMotdLength = 256;
    private const string MotdOpen = "[MOTD]";
    private const string MotdClose = "[/MOTD]";
    private const string AdOpen = "[AD]";
    private const string AdClose = "[/AD]";

    private readonly ILogger<LanWorldDiscovery> _logger;
    private readonly Func<DateTimeOffset> _clock;
    private readonly TimeSpan _lifetime;
    private readonly object _gate = new();
    private readonly List<LanWorld> _worlds = [];

    private UdpClient? _listener;
    private CancellationTokenSource? _cancellation;
    private Task? _loop;

    public LanWorldDiscovery(
        ILogger<LanWorldDiscovery> logger,
        TimeSpan? lifetime = null,
        Func<DateTimeOffset>? clock = null)
    {
        _logger = logger;
        _lifetime = lifetime ?? DefaultLifetime;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>Raised after a broadcast is accepted, so a view can refresh.</summary>
    public event Action? Changed;

    public bool IsListening => _listener is not null;

    /// <summary>Set when listening could not start, with the reason.</summary>
    public string? FailureReason { get; private set; }

    /// <summary>Worlds that have announced themselves recently, newest first.</summary>
    public IReadOnlyList<LanWorld> Current
    {
        get
        {
            var now = _clock();
            lock (_gate)
            {
                _worlds.RemoveAll(world => now - world.LastSeen > _lifetime);
                return _worlds
                    .OrderByDescending(world => world.LastSeen)
                    .ToList();
            }
        }
    }

    /// <summary>Starts listening. A failure is reported, not thrown: discovery is an extra.</summary>
    public void Start()
    {
        if (_listener is not null)
        {
            return;
        }

        FailureReason = null;
        try
        {
            var client = new UdpClient(AddressFamily.InterNetwork);
            client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            client.Client.Bind(new IPEndPoint(IPAddress.Any, MulticastPort));
            client.JoinMulticastGroup(IPAddress.Parse(MulticastGroup));
            client.MulticastLoopback = true;
            _listener = client;

            _cancellation = new CancellationTokenSource();
            _loop = Task.Run(() => ReceiveLoopAsync(client, _cancellation.Token));
            _logger.LogInformation(
                "Listening for LAN worlds on {Group}:{Port}",
                MulticastGroup,
                MulticastPort);
        }
        catch (Exception exception) when (exception is SocketException or ObjectDisposedException)
        {
            FailureReason = exception.Message;
            _logger.LogWarning(exception, "LAN world discovery could not start");
            Stop();
        }
    }

    public void Stop()
    {
        var listener = _listener;
        _listener = null;
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
        listener?.Dispose();
        _loop = null;

        lock (_gate)
        {
            _worlds.Clear();
        }
    }

    /// <summary>
    /// Records one observed broadcast. Kept separate from the socket so the parsing and the
    /// aggregation can be tested without a network.
    /// </summary>
    public bool Ingest(IPAddress sender, byte[] payload, DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(payload);

        if (!TryParse(payload, out var motd, out var port))
        {
            return false;
        }

        var address = sender.MapToIPv4().ToString();
        var world = new LanWorld(address, port, motd, observedAt);

        lock (_gate)
        {
            _worlds.RemoveAll(existing =>
                string.Equals(existing.Host, world.Host, StringComparison.Ordinal)
                && existing.Port == world.Port);
            _worlds.Add(world);
            _worlds.RemoveAll(existing => observedAt - existing.LastSeen > _lifetime);
        }

        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// Parses the game's broadcast payload, <c>[MOTD]name[/MOTD][AD]port[/AD]</c>. The port is
    /// required: without it there is nothing to connect to.
    /// </summary>
    public static bool TryParse(byte[] payload, out string motd, out int port)
    {
        motd = string.Empty;
        port = 0;

        if (payload is not { Length: > 0 } || payload.Length > MaxPayloadBytes)
        {
            return false;
        }

        var text = Encoding.UTF8.GetString(payload);
        var motdStart = text.IndexOf(MotdOpen, StringComparison.Ordinal);
        var motdEnd = text.IndexOf(MotdClose, StringComparison.Ordinal);
        var adStart = text.IndexOf(AdOpen, StringComparison.Ordinal);
        var adEnd = text.IndexOf(AdClose, StringComparison.Ordinal);

        // Both blocks are part of the game's format. A datagram carrying only a port is not something
        // Minecraft produces, so it is not treated as a world announcement.
        if (motdStart < 0 || motdEnd <= motdStart)
        {
            return false;
        }

        motd = Limit(StripFormatting(text[(motdStart + MotdOpen.Length)..motdEnd]));

        if (adStart < 0 || adEnd <= adStart)
        {
            return false;
        }

        var portText = text[(adStart + AdOpen.Length)..adEnd].Trim();
        return int.TryParse(portText, out port) && port is > 0 and <= 65535;
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }

    private async Task ReceiveLoopAsync(UdpClient client, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var result = await client.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                Ingest(result.RemoteEndPoint.Address, result.Buffer, _clock());
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception) when (exception is SocketException or ObjectDisposedException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                _logger.LogDebug(exception, "A LAN world broadcast could not be read");
            }
        }
    }

    /// <summary>Removes legacy section-sign colour codes a world name may carry.</summary>
    private static string StripFormatting(string value)
    {
        if (!value.Contains('\u00A7'))
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '\u00A7' && index + 1 < value.Length)
            {
                index++;
                continue;
            }

            builder.Append(value[index]);
        }

        return builder.ToString();
    }

    private static string Limit(string value) =>
        value.Length <= MaxMotdLength ? value.Trim() : value[..MaxMotdLength].Trim();
}
