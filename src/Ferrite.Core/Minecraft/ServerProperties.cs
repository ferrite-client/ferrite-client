using System.Text;

namespace Ferrite.Core.Minecraft;

/// <summary>
/// Minecraft's <c>server.properties</c>: a flat <c>key=value</c> file with comments and blank lines.
/// This document edits the keys the launcher owns and leaves every other line - including a line the
/// user added by hand - exactly as it was.
/// </summary>
public sealed class ServerPropertiesDocument
{
    private readonly List<string> _lines;
    private readonly Dictionary<string, int> _index;

    private ServerPropertiesDocument(List<string> lines, Dictionary<string, int> index)
    {
        _lines = lines;
        _index = index;
    }

    /// <summary>An empty document, used when the file does not exist yet.</summary>
    public static ServerPropertiesDocument Empty() =>
        new([], new Dictionary<string, int>(StringComparer.Ordinal));

    public static ServerPropertiesDocument Parse(string? text)
    {
        var lines = new List<string>();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        if (text is null)
        {
            return new ServerPropertiesDocument(lines, index);
        }

        foreach (var line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var trimmed = line.TrimStart();
            if (trimmed.Length > 0 && trimmed[0] is not ('#' or '!'))
            {
                var separator = line.IndexOf('=');
                if (separator > 0)
                {
                    var key = line[..separator].Trim();
                    // The first occurrence wins, which is how the game itself reads the file.
                    index.TryAdd(key, lines.Count);
                }
            }

            lines.Add(line);
        }

        // A trailing newline produces one empty final element, which round-trips as nothing.
        if (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return new ServerPropertiesDocument(lines, index);
    }

    public string? Get(string key) =>
        _index.TryGetValue(key, out var position)
            ? Unescape(_lines[position][(_lines[position].IndexOf('=') + 1)..])
            : null;

    public bool Contains(string key) => _index.ContainsKey(key);

    /// <summary>Sets a key, replacing its value in place or appending it at the end.</summary>
    public void Set(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var line = key + "=" + Escape(value);
        if (_index.TryGetValue(key, out var position))
        {
            _lines[position] = line;
            return;
        }

        _index[key] = _lines.Count;
        _lines.Add(line);
    }

    public string ToText() => _lines.Count == 0
        ? string.Empty
        : string.Join(Environment.NewLine, _lines) + Environment.NewLine;

    /// <summary>
    /// A value may contain a newline (an MOTD can), which would otherwise split the file. The game's
    /// reader understands the same escapes.
    /// </summary>
    private static string Escape(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\r", "\\r", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal);

    private static string Unescape(string value)
    {
        var builder = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '\\' && index + 1 < value.Length)
            {
                index++;
                builder.Append(value[index] switch
                {
                    'n' => '\n',
                    'r' => '\r',
                    _ => value[index],
                });
                continue;
            }

            builder.Append(value[index]);
        }

        return builder.ToString();
    }
}
