namespace Ferrite.Core.Game;

/// <summary>One entry from an instance's <c>servers.dat</c>.</summary>
public sealed record ServerEntry
{
    public required string Name { get; init; }

    public required string Address { get; init; }

    /// <summary>Base64 PNG data URL stored by the game, if the user pinned an icon.</summary>
    public string? IconBase64 { get; init; }

    public bool AcceptTextures { get; init; }

    public bool Hidden { get; init; }

    public string DisplayText => string.IsNullOrWhiteSpace(Address) ? Name : $"{Name} ({Address})";
}

/// <summary>Live status reported by a server's status protocol.</summary>
public sealed record ServerStatus
{
    public required string Address { get; init; }

    public required bool Online { get; init; }

    public string? Motd { get; init; }

    public int? PlayersOnline { get; init; }

    public int? PlayersMax { get; init; }

    public string? VersionName { get; init; }

    public int? ProtocolVersion { get; init; }

    public TimeSpan Latency { get; init; }

    public string? Error { get; init; }

    public string PlayerCountText => PlayersOnline is { } online && PlayersMax is { } max
        ? $"{online}/{max}"
        : "?";

    public string LatencyText => Online ? $"{Latency.TotalMilliseconds:F0} ms" : "offline";
}
