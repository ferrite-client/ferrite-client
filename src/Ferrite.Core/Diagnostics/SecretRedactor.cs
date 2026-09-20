using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Ferrite.Core.Diagnostics;

/// <summary>
/// Removes credential material from anything Ferrite logs, shows, or exports.
/// Secrets obtained at runtime are registered explicitly; well-known token shapes are also
/// matched structurally so an unregistered leak still does not reach a log file.
/// </summary>
public sealed partial class SecretRedactor
{
    private const string Placeholder = "***redacted***";

    private readonly ConcurrentDictionary<string, byte> _literals = new(StringComparer.Ordinal);

    public static SecretRedactor Shared { get; } = new();

    /// <summary>Registers a known secret value so every later redaction removes it.</summary>
    public void Register(string? secret)
    {
        if (string.IsNullOrWhiteSpace(secret) || secret.Length < 8)
        {
            return;
        }

        _literals[secret] = 0;
    }

    public void RegisterRange(IEnumerable<string?> secrets)
    {
        foreach (var secret in secrets)
        {
            Register(secret);
        }
    }

    public string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        var result = text;

        foreach (var literal in _literals.Keys)
        {
            if (literal.Length >= 8 && result.Contains(literal, StringComparison.Ordinal))
            {
                result = result.Replace(literal, Placeholder, StringComparison.Ordinal);
            }
        }

        result = AuthorizationPattern().Replace(result, match => match.Groups[1].Value + " " + Placeholder);
        result = KeyedValuePattern().Replace(result, match => match.Groups[1].Value + match.Groups[2].Value + Placeholder);
        return result;
    }

    public bool ContainsSecret(string? text) =>
        !string.IsNullOrEmpty(text) && !string.Equals(Redact(text), text, StringComparison.Ordinal);

    [GeneratedRegex(@"(?i)\b(Bearer)\s+[A-Za-z0-9._~+/=-]{8,}")]
    private static partial Regex AuthorizationPattern();

    [GeneratedRegex(
        "(?i)\\b(access[_-]?token|refresh[_-]?token|id[_-]?token|xsts[_-]?token|session[_-]?token|client[_-]?secret|api[_-]?key|authorization)(\"?\\s*[:=]\\s*\"?)([A-Za-z0-9._~+/=-]{8,})")]
    private static partial Regex KeyedValuePattern();
}
