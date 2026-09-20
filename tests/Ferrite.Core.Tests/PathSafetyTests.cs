using Ferrite.Core.Util;

namespace Ferrite.Core.Tests;

public sealed class PathSafetyTests
{
    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("folder/../../outside.txt")]
    [InlineData("folder/..")]
    [InlineData("..")]
    [InlineData("/etc/passwd")]
    [InlineData("\\windows\\system32\\evil.dll")]
    [InlineData("C:/windows/evil.txt")]
    [InlineData("C:\\windows\\evil.txt")]
    [InlineData("~/secrets.txt")]
    [InlineData("folder/file.txt:stream")]
    public void NormalizeRelativePath_rejects_escaping_paths(string entry)
    {
        Assert.Throws<PathSafetyException>(() => PathSafety.NormalizeRelativePath(entry));
    }

    [Theory]
    [InlineData("mods/example.jar", "mods\\example.jar")]
    [InlineData("./mods/example.jar", "mods\\example.jar")]
    [InlineData("mods//example.jar", "mods\\example.jar")]
    [InlineData("config/foo/bar.toml", "config\\foo\\bar.toml")]
    public void NormalizeRelativePath_normalises_acceptable_paths(string input, string expectedWindows)
    {
        var normalized = PathSafety.NormalizeRelativePath(input);
        var expected = expectedWindows.Replace('\\', Path.DirectorySeparatorChar);
        Assert.Equal(expected, normalized);
    }

    [Fact]
    public void NormalizeRelativePath_rejects_null_character()
    {
        Assert.Throws<PathSafetyException>(() => PathSafety.NormalizeRelativePath("mods/bad\0name.jar"));
    }

    [Fact]
    public void NormalizeRelativePath_rejects_reserved_device_names()
    {
        Assert.Throws<PathSafetyException>(() => PathSafety.NormalizeRelativePath("mods/CON.jar"));
        Assert.Throws<PathSafetyException>(() => PathSafety.NormalizeRelativePath("LPT1"));
    }

    [Fact]
    public void ResolveContained_stays_inside_root()
    {
        var root = Path.Combine(Path.GetTempPath(), "ferrite-path-root");
        var resolved = PathSafety.ResolveContained(root, "mods/example.jar");
        Assert.StartsWith(Path.GetFullPath(root), resolved, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("example.jar", resolved, StringComparison.Ordinal);
    }

    [Fact]
    public void IsContained_distinguishes_prefix_lookalikes()
    {
        var root = Path.Combine(Path.GetTempPath(), "ferrite-root");
        var sibling = Path.Combine(Path.GetTempPath(), "ferrite-root-evil", "file.txt");
        Assert.True(PathSafety.IsContained(root, Path.Combine(root, "a", "b.txt")));
        Assert.True(PathSafety.IsContained(root, root));
        Assert.False(PathSafety.IsContained(root, sibling));
    }

    [Fact]
    public void SanitizeFileName_replaces_invalid_characters_and_reserved_names()
    {
        Assert.Equal("CON_", PathSafety.SanitizeFileName("CON"));
        Assert.Equal("unnamed", PathSafety.SanitizeFileName("   "));
        Assert.DoesNotContain('/', PathSafety.SanitizeFileName("a/b"));
        Assert.DoesNotContain('\\', PathSafety.SanitizeFileName("a\\b"));
    }
}
