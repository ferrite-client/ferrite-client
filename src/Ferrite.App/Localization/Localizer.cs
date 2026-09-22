namespace Ferrite.App.Localization;

/// <summary>
/// The launcher's interface text. One table per language, plus the switch that puts a table into the
/// application's resources so every <c>DynamicResource</c> binding in the views updates at once.
/// </summary>
/// <remarks>
/// Text a view composes itself is looked up here by key. Text produced by Core (provider errors,
/// verification results) stays in English: those messages come from the engines that produced them,
/// and inventing a translation for a message this layer never sees would be worse than the English
/// original. The parity matrix says so explicitly.
/// </remarks>
public static class Localizer
{
    public const string English = "en";
    public const string Polish = "pl";

    private static readonly IReadOnlyList<LanguageOption> LanguageList =
    [
        new(English, "English"),
        new(Polish, "Polski"),
    ];

    private static IReadOnlyDictionary<string, string> _current = StringsEn.Table;

    // Application.Resources is a plain dictionary, and the headless UI tests build several shells on
    // separate dispatcher threads. Serialising the write keeps a language switch from corrupting it.
    private static readonly object ResourceGate = new();

    /// <summary>The language in effect.</summary>
    public static string Language { get; private set; } = English;

    /// <summary>Raised after a language change so views can refresh text they compute themselves.</summary>
    public static event Action? LanguageChanged;

    public static IReadOnlyList<LanguageOption> Available => LanguageList;

    /// <summary>Resolves a key, falling back to the English table and then to the key itself.</summary>
    public static string Get(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return string.Empty;
        }

        if (_current.TryGetValue(key, out var value))
        {
            return value;
        }

        return StringsEn.Table.TryGetValue(key, out var fallback) ? fallback : key;
    }

    public static string Format(string key, params object?[] arguments) =>
        string.Format(System.Globalization.CultureInfo.CurrentCulture, Get(key), arguments);

    /// <summary>Puts a table into the application resources and remembers the choice.</summary>
    public static void Apply(Avalonia.Application? application, string? language)
    {
        var resolved = Normalize(language);
        var table = TableFor(resolved);
        _current = table;
        var changed = !string.Equals(Language, resolved, StringComparison.Ordinal);
        Language = resolved;

        if (application?.Resources is { } resources)
        {
            // Replacing the entries is what makes DynamicResource bindings pick up the new language.
            lock (ResourceGate)
            {
                foreach (var (key, value) in table)
                {
                    resources[key] = value;
                }
            }
        }

        if (changed)
        {
            LanguageChanged?.Invoke();
        }
    }

    public static string Normalize(string? language) =>
        language is not null && language.StartsWith("pl", StringComparison.OrdinalIgnoreCase)
            ? Polish
            : English;

    public static IReadOnlyDictionary<string, string> TableFor(string? language) =>
        Normalize(language) == Polish ? StringsPl.Table : StringsEn.Table;
}

/// <summary>A language the interface can be shown in.</summary>
public sealed record LanguageOption(string Code, string DisplayName);
