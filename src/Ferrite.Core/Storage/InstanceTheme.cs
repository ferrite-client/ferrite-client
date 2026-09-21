namespace Ferrite.Core.Storage;

/// <summary>
/// The per-instance theme: a background image and an accent colour. Both are optional, and both are
/// validated here rather than in the view, so a stored value is always something that can be rendered.
/// </summary>
public static class InstanceTheme
{
    /// <summary>
    /// Normalises a user-entered colour to <c>#RRGGBB</c>. A blank value means "no accent" and
    /// succeeds with a null result; a non-blank value that is not a colour fails, so a typo is an
    /// error rather than a silent reset.
    /// </summary>
    public static bool TryNormalizeAccent(string? value, out string? accent)
    {
        accent = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var text = value.Trim().TrimStart('#');
        if (text.Length == 3 && text.All(IsHex))
        {
            // "#abc" is the same colour as "#aabbcc".
            text = string.Concat(text.Select(character => new string(character, 2)));
        }

        if (text.Length != 6 || !text.All(IsHex))
        {
            return false;
        }

        accent = "#" + text.ToUpperInvariant();
        return true;
    }

    /// <summary>The file name a picked background is stored under, inside the instance's theme folder.</summary>
    public static string BackgroundRelativePath(string extension)
    {
        var safeExtension = extension.StartsWith('.') ? extension : "." + extension;
        return $"theme/background{safeExtension.ToLowerInvariant()}";
    }

    private static bool IsHex(char character) =>
        character is >= '0' and <= '9'
        or >= 'a' and <= 'f'
        or >= 'A' and <= 'F';
}
