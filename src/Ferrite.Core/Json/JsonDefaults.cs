using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ferrite.Core.Json;

public static class JsonDefaults
{
    /// <summary>Options for launcher-owned documents: pretty, camelCase, tolerant of comments.</summary>
    public static JsonSerializerOptions Document { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    /// <summary>Options for remote payloads: tolerant, never pretty, case-insensitive.</summary>
    public static JsonSerializerOptions Remote { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };
}
