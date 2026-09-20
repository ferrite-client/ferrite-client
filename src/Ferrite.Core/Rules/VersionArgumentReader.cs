using System.Text.Json;
using Ferrite.Core.Json;

namespace Ferrite.Core.Rules;

/// <summary>
/// Reads the modern arguments format, which mixes plain strings with rule objects, into a
/// uniform list.
/// </summary>
public static class VersionArgumentReader
{
    public static List<ArgumentEntry> Read(IReadOnlyList<JsonElement>? items)
    {
        var entries = new List<ArgumentEntry>();
        if (items is null)
        {
            return entries;
        }

        foreach (var item in items)
        {
            switch (item.ValueKind)
            {
                case JsonValueKind.String:
                    entries.Add(new ArgumentEntry { Value = [item.GetString() ?? string.Empty] });
                    break;

                case JsonValueKind.Object:
                    entries.Add(ReadObject(item));
                    break;
            }
        }

        return entries;
    }

    /// <summary>Splits a legacy <c>minecraftArguments</c> string into tokens.</summary>
    public static List<ArgumentEntry> ReadLegacy(string? minecraftArguments)
    {
        var entries = new List<ArgumentEntry>();
        if (string.IsNullOrWhiteSpace(minecraftArguments))
        {
            return entries;
        }

        foreach (var token in minecraftArguments.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            entries.Add(new ArgumentEntry { Value = [token] });
        }

        return entries;
    }

    /// <summary>Flattens entries to the values that apply in the given context.</summary>
    public static List<string> Resolve(
        IReadOnlyList<ArgumentEntry> entries,
        RuleContext context,
        Func<string, string>? expand = null)
    {
        var result = new List<string>();
        foreach (var entry in entries)
        {
            if (!RuleEvaluator.IsAllowed(entry.Rules, context))
            {
                continue;
            }

            foreach (var value in entry.Value)
            {
                var text = expand is null ? value : expand(value);
                foreach (var token in SplitValue(text))
                {
                    if (token.Length > 0)
                    {
                        result.Add(token);
                    }
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Metadata sometimes packs several arguments into one value (for example
    /// <c>--width ${resolution_width} --height ${resolution_height}</c>).
    /// A <c>-D</c> value is never split: loader profiles use it to pass a single JVM property
    /// whose value legitimately contains spaces, such as Fabric's
    /// <c>-DFabricMcEmu= net.minecraft.client.main.Main </c>.
    /// </summary>
    public static IEnumerable<string> SplitValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        if (value.StartsWith("-D", StringComparison.Ordinal))
        {
            return [value];
        }

        return value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static ArgumentEntry ReadObject(JsonElement item)
    {
        var entry = new ArgumentEntry();

        if (item.TryGetProperty("rules", out var rules) && rules.ValueKind == JsonValueKind.Array)
        {
            try
            {
                entry.Rules = JsonSerializer.Deserialize<List<Rule>>(rules.GetRawText(), JsonDefaults.Remote);
            }
            catch (JsonException)
            {
                entry.Rules = null;
            }
        }

        if (item.TryGetProperty("value", out var value))
        {
            entry.Value = value.ValueKind switch
            {
                JsonValueKind.String => [value.GetString() ?? string.Empty],
                JsonValueKind.Array => value
                    .EnumerateArray()
                    .Where(static v => v.ValueKind == JsonValueKind.String)
                    .Select(static v => v.GetString() ?? string.Empty)
                    .ToList(),
                _ => [],
            };
        }

        return entry;
    }
}
