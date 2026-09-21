using Ferrite.Core.Storage;

namespace Ferrite.Core.Tests;

/// <summary>
/// The per-instance theme's validation. It lives in Core so the stored value is always something the
/// view can render, and so a typo is an error rather than a silent reset.
/// </summary>
public sealed class InstanceThemeTests
{
    [Theory]
    [InlineData("#D08A3E", "#D08A3E")]
    [InlineData("d08a3e", "#D08A3E")]
    [InlineData("  #d08a3e  ", "#D08A3E")]
    [InlineData("#abc", "#AABBCC")]
    [InlineData("f00", "#FF0000")]
    public void A_hex_colour_is_normalised(string input, string expected)
    {
        Assert.True(InstanceTheme.TryNormalizeAccent(input, out var accent));
        Assert.Equal(expected, accent);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_value_means_no_accent(string? input)
    {
        Assert.True(InstanceTheme.TryNormalizeAccent(input, out var accent));
        Assert.Null(accent);
    }

    [Theory]
    [InlineData("copper")]
    [InlineData("#12345")]
    [InlineData("#1234567")]
    [InlineData("#gggggg")]
    [InlineData("rgb(1,2,3)")]
    public void A_value_that_is_not_a_colour_fails(string input)
    {
        Assert.False(InstanceTheme.TryNormalizeAccent(input, out var accent));
        Assert.Null(accent);
    }

    [Theory]
    [InlineData(".png", "theme/background.png")]
    [InlineData("png", "theme/background.png")]
    [InlineData(".PNG", "theme/background.png")]
    public void A_background_is_stored_under_the_instance_theme_folder(string extension, string expected)
    {
        Assert.Equal(expected, InstanceTheme.BackgroundRelativePath(extension));
    }
}
