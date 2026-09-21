using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ferrite.Core.Json;
using Ferrite.Core.Util;
using Microsoft.Extensions.Logging;

namespace Ferrite.Core.Content;

/// <summary>Where a cached value came from and how old it is.</summary>
public sealed record CacheHit(DateTimeOffset StoredAt)
{
    public TimeSpan Age => DateTimeOffset.UtcNow - StoredAt;

    /// <summary>Human-readable age, coarse on purpose: "12 days" is as precise as it needs to be.</summary>
    public string AgeText => Age switch
    {
        { TotalMinutes: < 1 } => "just now",
        { TotalHours: < 1 } => $"{(int)Age.TotalMinutes} min ago",
        { TotalDays: < 1 } => $"{(int)Age.TotalHours} h ago",
        { TotalDays: < 30 } => $"{(int)Age.TotalDays} d ago",
        _ => StoredAt.ToLocalTime().ToString("yyyy-MM-dd"),
    };
}

/// <summary>
/// Stores what content providers returned so a search or project page still works when the network
/// does not. Values are keyed by provider, operation, and arguments, and each entry records when it
/// was written so the UI can say how old it is.
/// </summary>
public sealed class ContentCache
{
    private const int MaxEntryBytes = 8 * 1024 * 1024;
    private const string Prefix = "content";

    private readonly string _directory;
    private readonly ILogger<ContentCache> _logger;

    public ContentCache(string cacheDirectory, ILogger<ContentCache> logger)
    {
        _directory = Path.Combine(cacheDirectory, Prefix);
        _logger = logger;
    }

    /// <summary>Reads a cached value, or returns false when nothing usable is stored.</summary>
    public bool TryRead<T>(string key, out T value, out CacheHit hit)
        where T : class
    {
        value = default!;
        hit = null!;
        var path = PathFor(key);
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            var json = File.ReadAllBytes(path);
            if (json.Length > MaxEntryBytes)
            {
                return false;
            }

            var envelope = JsonSerializer.Deserialize<CacheEnvelope<T>>(json, JsonDefaults.Document);
            if (envelope?.Value is null)
            {
                return false;
            }

            value = envelope.Value!;
            hit = new CacheHit(envelope.StoredAt);
            return true;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(exception, "Cached content entry {Key} could not be read", key);
            return false;
        }
    }

    /// <summary>Writes a value. A failed write is not an error: the cache is an optimisation.</summary>
    public void Write<T>(string key, T value)
        where T : class
    {
        try
        {
            Directory.CreateDirectory(_directory);
            var envelope = new CacheEnvelope<T>(DateTimeOffset.UtcNow, value);
            var json = JsonSerializer.Serialize(envelope, JsonDefaults.Document);
            AtomicFile.WriteAllText(PathFor(key), json);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _logger.LogDebug(exception, "Cached content entry {Key} could not be written", key);
        }
    }

    /// <summary>A stable file name for an operation and its arguments.</summary>
    public static string KeyFor(string provider, string operation, params object?[] arguments)
    {
        var text = new StringBuilder(provider).Append('\u001f').Append(operation);
        foreach (var argument in arguments)
        {
            text.Append('\u001f').Append(argument?.ToString() ?? "<null>");
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()));
        return $"{provider}-{operation}-{Convert.ToHexString(hash)[..16].ToLowerInvariant()}";
    }

    private string PathFor(string key)
    {
        // Keys are generated here, but a defensive sanitise keeps a future caller from escaping.
        return Path.Combine(_directory, PathSafety.SanitizeFileName(key) + ".json");
    }

    private sealed record CacheEnvelope<T>(DateTimeOffset StoredAt, T? Value)
        where T : class;
}
