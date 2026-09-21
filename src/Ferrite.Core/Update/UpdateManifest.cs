using System.Text.Json;
using Ferrite.Core.Json;

namespace Ferrite.Core.Update;

/// <summary>One downloadable build in an update feed.</summary>
public sealed record UpdatePackage
{
    /// <summary>Runtime identifier, for example <c>win-x64</c>.</summary>
    public required string Runtime { get; init; }

    /// <summary><c>self-contained</c> or <c>framework-dependent</c>.</summary>
    public required string Kind { get; init; }

    public required string Url { get; init; }

    public required string Sha256 { get; init; }

    public long Size { get; init; }
}

/// <summary>
/// The signed description of the newest release. It is fetched as bytes and its detached signature
/// is verified before anything in it is parsed, so a hostile or corrupted feed cannot influence the
/// launcher.
/// </summary>
public sealed record UpdateManifest
{
    public const int SupportedSchemaVersion = 1;

    public int SchemaVersion { get; init; } = SupportedSchemaVersion;

    public string Version { get; init; } = string.Empty;

    public string? Channel { get; init; }

    public DateTimeOffset? PublishedAt { get; init; }

    public string? Notes { get; init; }

    public IReadOnlyList<UpdatePackage> Packages { get; init; } = [];

    /// <summary>Reads a verified manifest. Throws when the shape is not usable.</summary>
    public static UpdateManifest Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        UpdateManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<UpdateManifest>(json, JsonDefaults.Remote)
                ?? throw new UpdateException("The update manifest was empty.");
        }
        catch (JsonException exception)
        {
            throw new UpdateException("The update manifest is not valid JSON.", exception);
        }

        if (manifest.SchemaVersion > SupportedSchemaVersion)
        {
            throw new UpdateException(
                $"The update manifest declares schema {manifest.SchemaVersion}, which this build "
                + $"does not understand (it supports {SupportedSchemaVersion}).");
        }

        if (string.IsNullOrWhiteSpace(manifest.Version))
        {
            throw new UpdateException("The update manifest does not declare a version.");
        }

        foreach (var package in manifest.Packages)
        {
            if (string.IsNullOrWhiteSpace(package.Runtime)
                || string.IsNullOrWhiteSpace(package.Url)
                || string.IsNullOrWhiteSpace(package.Sha256))
            {
                throw new UpdateException("The update manifest contains a package without a runtime, url, or hash.");
            }

            if (!IsUsablePackageUrl(package.Url))
            {
                throw new UpdateException(
                    $"Update package '{package.Url}' must be an HTTPS address or a path relative to "
                    + "the feed (plain HTTP is allowed only for a loopback host).");
            }
        }

        return manifest;
    }

    /// <summary>
    /// Accepts an absolute HTTPS address, an absolute loopback HTTP address, or a relative file name.
    /// A relative name keeps a feed portable: the same signed manifest works on any host.
    /// </summary>
    public static bool IsUsablePackageUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute))
        {
            return absolute.Scheme switch
            {
                "https" => true,
                "http" => IsLoopback(absolute),
                _ => false,
            };
        }

        if (!Uri.TryCreate(url, UriKind.Relative, out var relative))
        {
            return false;
        }

        var path = relative.OriginalString.Replace('\\', '/');
        return path.Length > 0
            && !path.StartsWith('/')
            && !path.StartsWith("..", StringComparison.Ordinal)
            && !path.Contains("../", StringComparison.Ordinal);
    }

    /// <summary>True for addresses that never leave the machine, where HTTPS is not meaningful.</summary>
    internal static bool IsLoopback(Uri uri) =>
        uri.IsLoopback
        || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
        || string.Equals(uri.Host, "127.0.0.1", StringComparison.Ordinal);

    /// <summary>Serialises the manifest for a feed publisher or a test fixture.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonDefaults.Document);
}

public sealed class UpdateException : Exception
{
    public UpdateException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
