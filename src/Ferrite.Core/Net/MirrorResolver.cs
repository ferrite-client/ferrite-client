namespace Ferrite.Core.Net;

/// <summary>
/// Rewrites an artifact URL onto a configured mirror host. Overrides are keyed by the host (or
/// "host:port") of the address they replace, so a user can point one service at a local or regional
/// copy without touching anything else.
/// </summary>
/// <remarks>
/// The path and query are preserved, which is what makes this work for Maven-style repositories and
/// CDNs whose mirrors keep the same layout. A host that is not configured is returned unchanged.
/// </remarks>
public sealed class MirrorResolver
{
    private volatile IReadOnlyDictionary<string, string> _overrides =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public MirrorResolver(IReadOnlyDictionary<string, string>? overrides = null)
    {
        Update(overrides);
    }

    /// <summary>Replaces the active overrides. Called when the user saves settings.</summary>
    public void Update(IReadOnlyDictionary<string, string>? overrides)
    {
        _overrides = overrides is { Count: > 0 }
            ? new Dictionary<string, string>(overrides, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public bool IsEmpty => _overrides.Count == 0;

    /// <summary>The configured mirrors, for diagnostics.</summary>
    public IReadOnlyDictionary<string, string> Overrides => _overrides;

    /// <summary>
    /// Returns the mirror for a URL, or the original URL when nothing matches or the override is not
    /// a usable absolute address. A bad override never breaks a download: it is ignored.
    /// </summary>
    public string Resolve(string url)
    {
        if (string.IsNullOrWhiteSpace(url) || _overrides.Count == 0)
        {
            return url;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var original))
        {
            return url;
        }

        if (!TryFindMirror(original, out var mirrorUrl)
            || !Uri.TryCreate(mirrorUrl, UriKind.Absolute, out var mirror))
        {
            return url;
        }

        var builder = new UriBuilder(original)
        {
            Scheme = mirror.Scheme,
            Host = mirror.Host,
            Port = mirror.IsDefaultPort ? -1 : mirror.Port,
        };

        // A mirror may also declare a path prefix, which is prepended to the original path.
        var prefix = mirror.AbsolutePath.TrimEnd('/');
        if (prefix.Length > 0)
        {
            builder.Path = prefix + original.AbsolutePath;
        }

        return builder.Uri.AbsoluteUri;
    }

    private bool TryFindMirror(Uri original, out string mirrorUrl)
    {
        var overrides = _overrides;
        var hostAndPort = original.IsDefaultPort
            ? original.Host
            : $"{original.Host}:{original.Port}";

        return overrides.TryGetValue(hostAndPort, out mirrorUrl!)
            || overrides.TryGetValue(original.Host, out mirrorUrl!);
    }
}
