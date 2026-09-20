namespace Ferrite.Core.Net;

public sealed class HttpServiceOptions
{
    public string UserAgent { get; init; } = "Ferrite/0.1.0";

    public string? ProxyUrl { get; init; }

    public int MaxConnectionsPerServer { get; init; } = 16;

    /// <summary>Timeout applied to metadata requests. Downloads manage their own timeout.</summary>
    public TimeSpan MetadataTimeout { get; init; } = TimeSpan.FromSeconds(60);

    public int MaxAttempts { get; init; } = 4;

    public TimeSpan BaseRetryDelay { get; init; } = TimeSpan.FromMilliseconds(400);
}
