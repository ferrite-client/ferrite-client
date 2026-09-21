using System.Buffers.Binary;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Ferrite.Core.Game;

/// <summary>
/// Queries a Minecraft server's status. The modern handshake/status exchange is tried first, with the
/// pre-1.7 legacy ping as a fallback for old servers. Responses are size-capped and every read is
/// bounded by the caller's timeout.
/// </summary>
public sealed class ServerPinger
{
    private const int DefaultProtocolVersion = 767;
    private const int MaxResponseBytes = 1024 * 1024;
    private const int MaxPacketBytes = 4 * 1024 * 1024;

    private readonly ILogger<ServerPinger> _logger;

    public ServerPinger(ILogger<ServerPinger> logger)
    {
        _logger = logger;
    }

    /// <summary>Pings a server. Returns an offline status instead of throwing when unreachable.</summary>
    public async Task<ServerStatus> PingAsync(
        string address,
        int? port,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var (host, resolvedPort) = ParseAddress(address, port);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);

            using var client = new TcpClient();
            await client.ConnectAsync(host, resolvedPort, timeoutSource.Token).ConfigureAwait(false);
            await using var stream = client.GetStream();

            try
            {
                return await PingModernAsync(host, resolvedPort, stream, stopwatch, timeoutSource.Token)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogDebug(exception, "Modern status ping failed for {Host}:{Port}", host, resolvedPort);
                return await PingLegacyAsync(host, resolvedPort, stream, stopwatch, timeoutSource.Token)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Offline(address, "The server did not respond in time.", stopwatch.Elapsed);
        }
        catch (Exception exception) when (exception is SocketException or IOException)
        {
            return Offline(address, exception.Message, stopwatch.Elapsed);
        }
    }

    /// <summary>Splits "host", "host:port" and "[v6]:port" into a host and port.</summary>
    public static (string Host, int Port) ParseAddress(string address, int? port)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        var value = address.Trim();
        if (port is { } explicitPort and > 0)
        {
            return (value, explicitPort);
        }

        if (value.StartsWith('['))
        {
            var close = value.IndexOf(']');
            if (close > 0)
            {
                var host = value[1..close];
                var remainder = value[(close + 1)..];
                return remainder.StartsWith(':') && int.TryParse(remainder[1..], out var v6Port)
                    ? (host, v6Port)
                    : (host, 25565);
            }
        }

        var separator = value.LastIndexOf(':');
        if (separator > 0 && int.TryParse(value[(separator + 1)..], out var parsed))
        {
            return (value[..separator], parsed);
        }

        return (value, 25565);
    }

    private async Task<ServerStatus> PingModernAsync(
        string host,
        int port,
        NetworkStream stream,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        var handshake = new List<byte>();
        WriteVarInt(handshake, 0x00);
        WriteVarInt(handshake, DefaultProtocolVersion);
        WriteString(handshake, host);
        handshake.Add((byte)(port >> 8));
        handshake.Add((byte)(port & 0xFF));
        WriteVarInt(handshake, 1);

        await WritePacketAsync(stream, handshake, cancellationToken).ConfigureAwait(false);
        await WritePacketAsync(stream, [0x00], cancellationToken).ConfigureAwait(false);

        var payload = await ReadPacketAsync(stream, cancellationToken).ConfigureAwait(false);
        var reader = new PacketReader(payload);
        var packetId = reader.ReadVarInt();
        if (packetId != 0x00)
        {
            throw new NbtException($"Unexpected status packet id {packetId}.");
        }

        var json = reader.ReadString();
        stopwatch.Stop();
        return ParseStatus(host, port, json, stopwatch.Elapsed);
    }

    private static async Task<ServerStatus> PingLegacyAsync(
        string host,
        int port,
        NetworkStream stream,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        await stream.WriteAsync(new byte[] { 0xFE, 0x01 }, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);

        var buffer = new byte[1024];
        var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        if (read < 3 || buffer[0] != 0xFF)
        {
            throw new NbtException("The server did not answer the legacy ping.");
        }

        var length = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(1, 2));
        if (length * 2 + 3 > read)
        {
            throw new NbtException("The legacy ping response was truncated.");
        }

        var text = Encoding.BigEndianUnicode.GetString(buffer, 3, length * 2);
        stopwatch.Stop();
        var fields = text.Split('\0');
        if (fields.Length < 6)
        {
            throw new NbtException("The legacy ping response had an unexpected shape.");
        }

        return new ServerStatus
        {
            Address = $"{host}:{port}",
            Online = true,
            Motd = fields[3],
            PlayersOnline = int.TryParse(fields[4], out var online) ? online : null,
            PlayersMax = int.TryParse(fields[5], out var max) ? max : null,
            VersionName = fields[2],
            ProtocolVersion = int.TryParse(fields[1], out var protocol) ? protocol : null,
            Latency = stopwatch.Elapsed,
        };
    }

    public static ServerStatus ParseStatus(string host, int port, string json, TimeSpan latency)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        int? online = null;
        int? max = null;
        if (root.TryGetProperty("players", out var players))
        {
            if (players.TryGetProperty("online", out var onlineValue) && onlineValue.ValueKind == JsonValueKind.Number)
            {
                online = onlineValue.GetInt32();
            }

            if (players.TryGetProperty("max", out var maxValue) && maxValue.ValueKind == JsonValueKind.Number)
            {
                max = maxValue.GetInt32();
            }
        }

        string? versionName = null;
        int? protocol = null;
        if (root.TryGetProperty("version", out var version))
        {
            if (version.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
            {
                versionName = name.GetString();
            }

            if (version.TryGetProperty("protocol", out var protocolValue)
                && protocolValue.ValueKind == JsonValueKind.Number)
            {
                protocol = protocolValue.GetInt32();
            }
        }

        var motd = root.TryGetProperty("description", out var description)
            ? FlattenChatComponent(description)
            : null;

        return new ServerStatus
        {
            Address = $"{host}:{port}",
            Online = true,
            Motd = motd,
            PlayersOnline = online,
            PlayersMax = max,
            VersionName = versionName,
            ProtocolVersion = protocol,
            Latency = latency,
        };
    }

    /// <summary>
    /// Flattens a chat component into plain text: nested <c>extra</c> entries are concatenated,
    /// <c>translate</c> falls back to its key, and legacy section-sign colour codes are removed.
    /// </summary>
    public static string FlattenChatComponent(JsonElement element)
    {
        var builder = new StringBuilder();
        AppendComponent(element, builder);
        return StripLegacyFormatting(builder.ToString()).Trim();
    }

    private static void AppendComponent(JsonElement element, StringBuilder builder)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                builder.Append(element.GetString());
                break;

            case JsonValueKind.Array:
                foreach (var child in element.EnumerateArray())
                {
                    AppendComponent(child, builder);
                }

                break;

            case JsonValueKind.Object:
                if (element.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                {
                    builder.Append(text.GetString());
                }
                else if (element.TryGetProperty("translate", out var translate)
                    && translate.ValueKind == JsonValueKind.String)
                {
                    builder.Append(translate.GetString());
                }

                if (element.TryGetProperty("extra", out var extra))
                {
                    AppendComponent(extra, builder);
                }

                break;
        }
    }

    private static string StripLegacyFormatting(string value)
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

    private static ServerStatus Offline(string address, string error, TimeSpan latency) => new()
    {
        Address = address,
        Online = false,
        Latency = latency,
        Error = error,
    };

    private static async Task WritePacketAsync(
        NetworkStream stream,
        IReadOnlyList<byte> payload,
        CancellationToken cancellationToken)
    {
        var frame = new List<byte>(payload.Count + 5);
        WriteVarInt(frame, payload.Count);
        frame.AddRange(payload);
        await stream.WriteAsync(frame.ToArray(), cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<byte[]> ReadPacketAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var length = await ReadVarIntAsync(stream, cancellationToken).ConfigureAwait(false);
        if (length <= 0 || length > MaxPacketBytes)
        {
            throw new NbtException($"The server announced an invalid packet length ({length}).");
        }

        var buffer = new byte[length];
        var offset = 0;
        while (offset < length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new NbtException("The server closed the connection mid-packet.");
            }

            offset += read;
        }

        return buffer;
    }

    private static async Task<int> ReadVarIntAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var result = 0;
        var shift = 0;
        var single = new byte[1];
        while (true)
        {
            var read = await stream.ReadAsync(single, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new NbtException("The server closed the connection while sending a VarInt.");
            }

            result |= (single[0] & 0x7F) << shift;
            if ((single[0] & 0x80) == 0)
            {
                return result;
            }

            shift += 7;
            if (shift > 35)
            {
                throw new NbtException("The server sent an oversized VarInt.");
            }
        }
    }

    private static void WriteVarInt(List<byte> target, int value)
    {
        var remaining = (uint)value;
        do
        {
            var current = (byte)(remaining & 0x7F);
            remaining >>= 7;
            if (remaining != 0)
            {
                current |= 0x80;
            }

            target.Add(current);
        }
        while (remaining != 0);
    }

    private static void WriteString(List<byte> target, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteVarInt(target, bytes.Length);
        target.AddRange(bytes);
    }

    private ref struct PacketReader
    {
        private readonly ReadOnlySpan<byte> _data;
        private int _position;

        public PacketReader(ReadOnlySpan<byte> data)
        {
            _data = data;
            _position = 0;
        }

        public int ReadVarInt()
        {
            var result = 0;
            var shift = 0;
            while (true)
            {
                if (_position >= _data.Length)
                {
                    throw new NbtException("The status packet ended early.");
                }

                var current = _data[_position++];
                result |= (current & 0x7F) << shift;
                if ((current & 0x80) == 0)
                {
                    return result;
                }

                shift += 7;
                if (shift > 35)
                {
                    throw new NbtException("The status packet contained an oversized VarInt.");
                }
            }
        }

        public string ReadString()
        {
            var length = ReadVarInt();
            if (length < 0 || length > MaxResponseBytes)
            {
                throw new NbtException($"The status string length {length} is out of range.");
            }

            if (_position + length > _data.Length)
            {
                throw new NbtException("The status string ran past the end of the packet.");
            }

            var value = Encoding.UTF8.GetString(_data.Slice(_position, length));
            _position += length;
            return value;
        }
    }
}
