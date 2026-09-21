using Ferrite.Core.Util;
using Microsoft.Extensions.Logging;

namespace Ferrite.Core.Game;

/// <summary>
/// Reads and edits an instance's <c>servers.dat</c>. The game stores it as an uncompressed NBT
/// compound with a <c>servers</c> list, so the reader and writer share the NBT model.
/// </summary>
public sealed class ServerListService
{
    private readonly ILogger<ServerListService> _logger;

    public ServerListService(ILogger<ServerListService> logger)
    {
        _logger = logger;
    }

    public static string ServerListPath(string gameDirectory) => Path.Combine(gameDirectory, "servers.dat");

    public IReadOnlyList<ServerEntry> Read(string gameDirectory)
    {
        var path = ServerListPath(gameDirectory);
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            var root = NbtReader.ReadFile(path);
            if (root["servers"]?.List is not { } list)
            {
                return [];
            }

            var servers = new List<ServerEntry>(list.Count);
            foreach (var entry in list)
            {
                var address = entry["ip"]?.AsString();
                if (string.IsNullOrWhiteSpace(address))
                {
                    continue;
                }

                servers.Add(new ServerEntry
                {
                    Name = entry["name"]?.AsString() ?? address,
                    Address = address,
                    IconBase64 = entry["icon"]?.AsString(),
                    AcceptTextures = entry["acceptTextures"]?.AsBool() ?? false,
                    Hidden = entry["hidden"]?.AsBool() ?? false,
                });
            }

            return servers;
        }
        catch (Exception exception) when (exception is NbtException or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "servers.dat at {Path} could not be read", path);
            return [];
        }
    }

    /// <summary>Replaces the server list. The file is written atomically.</summary>
    public void Write(string gameDirectory, IReadOnlyList<ServerEntry> servers)
    {
        ArgumentNullException.ThrowIfNull(servers);
        var entries = servers
            .Where(server => !string.IsNullOrWhiteSpace(server.Address))
            .Select(ToTag)
            .ToList();

        var root = new NbtTag
        {
            Type = NbtTagType.Compound,
            Name = string.Empty,
            Value = new Dictionary<string, NbtTag>(StringComparer.Ordinal)
            {
                ["servers"] = new NbtTag { Type = NbtTagType.List, Name = "servers", Value = entries },
            },
        };

        var bytes = NbtWriter.Write(root, compress: false);
        AtomicFile.WriteAllBytes(ServerListPath(gameDirectory), bytes);
        _logger.LogInformation("Wrote {Count} server(s) to servers.dat", entries.Count);
    }

    public IReadOnlyList<ServerEntry> Add(string gameDirectory, ServerEntry server)
    {
        var servers = Read(gameDirectory).ToList();
        servers.RemoveAll(existing =>
            string.Equals(existing.Address, server.Address, StringComparison.OrdinalIgnoreCase));
        servers.Add(server);
        Write(gameDirectory, servers);
        return servers;
    }

    public IReadOnlyList<ServerEntry> Remove(string gameDirectory, string address)
    {
        var servers = Read(gameDirectory)
            .Where(server => !string.Equals(server.Address, address, StringComparison.OrdinalIgnoreCase))
            .ToList();
        Write(gameDirectory, servers);
        return servers;
    }

    private static NbtTag ToTag(ServerEntry server) => new()
    {
        Type = NbtTagType.Compound,
        Value = new Dictionary<string, NbtTag>(StringComparer.Ordinal)
        {
            ["name"] = StringTag("name", server.Name),
            ["ip"] = StringTag("ip", server.Address),
            ["icon"] = StringTag("icon", server.IconBase64 ?? string.Empty),
            ["acceptTextures"] = ByteTag("acceptTextures", server.AcceptTextures),
            ["hidden"] = ByteTag("hidden", server.Hidden),
        },
    };

    private static NbtTag StringTag(string name, string value) =>
        new() { Type = NbtTagType.String, Name = name, Value = value };

    private static NbtTag ByteTag(string name, bool value) =>
        new() { Type = NbtTagType.Byte, Name = name, Value = value ? 1L : 0L };
}
