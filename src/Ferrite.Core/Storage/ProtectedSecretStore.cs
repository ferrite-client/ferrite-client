using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ferrite.Core.Json;
using Ferrite.Core.Util;
using Microsoft.Extensions.Logging;

namespace Ferrite.Core.Storage;

/// <summary>
/// Stores small secrets (tokens, API keys) protected by the operating system. On Windows the
/// payload is encrypted with DPAPI in the current-user scope. Elsewhere the payload is stored
/// with user-only file permissions and the store reports itself as degraded so the UI can say so.
/// </summary>
public sealed class ProtectedSecretStore
{
    private readonly string _filePath;
    private readonly ILogger _logger;
    private Dictionary<string, string> _entries = new(StringComparer.Ordinal);
    private bool _loaded;

    public ProtectedSecretStore(string filePath, ILogger logger)
    {
        _filePath = filePath;
        _logger = logger;
    }

    /// <summary>True when the platform has no OS-backed protection and secrets are weaker.</summary>
    public bool IsDegraded => !OperatingSystem.IsWindows();

    /// <summary>
    /// Reads the store once per process. Callers share one instance, so a second load would discard
    /// entries written since startup.
    /// </summary>
    public void Load()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        if (!File.Exists(_filePath))
        {
            _entries = new Dictionary<string, string>(StringComparer.Ordinal);
            return;
        }

        try
        {
            var json = File.ReadAllBytes(_filePath);
            var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonDefaults.Document)
                ?? new Dictionary<string, string>(StringComparer.Ordinal);

            var decoded = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (key, value) in raw)
            {
                var protectedBytes = Convert.FromBase64String(value);
                decoded[key] = Unprotect(protectedBytes);
            }

            _entries = decoded;
        }
        catch (Exception exception) when (exception is JsonException or FormatException or CryptographicException or IOException)
        {
            _logger.LogWarning(exception, "Stored secrets could not be read; starting with an empty secret store");
            _entries = new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    public void Save()
    {
        var encoded = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in _entries)
        {
            encoded[key] = Convert.ToBase64String(Protect(value));
        }

        var json = JsonSerializer.Serialize(encoded, JsonDefaults.Document);
        AtomicFile.WriteAllText(_filePath, json);
        RestrictPermissions(_filePath);
    }

    public string? Get(string key) => _entries.TryGetValue(key, out var value) ? value : null;

    public void Set(string key, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            _entries.Remove(key);
        }
        else
        {
            _entries[key] = value;
        }
    }

    public bool Contains(string key) => _entries.ContainsKey(key);

    public void Remove(string key) => _entries.Remove(key);

    private static byte[] Protect(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        if (OperatingSystem.IsWindows())
        {
            return ProtectedData.Protect(bytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        }

        return bytes;
    }

    private static string Unprotect(byte[] value)
    {
        if (OperatingSystem.IsWindows())
        {
            var bytes = ProtectedData.Unprotect(value, optionalEntropy: null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }

        return Encoding.UTF8.GetString(value);
    }

    private static void RestrictPermissions(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (IOException)
        {
        }
    }
}
