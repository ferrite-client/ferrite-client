using Ferrite.Core.Content;

namespace Ferrite.Core.Tests;

/// <summary>
/// Turning an FTB pack's file list into downloads. The paths come from a remote document, so the
/// refusal cases matter as much as the happy path.
/// </summary>
public sealed class FtbPackInstallerTests : IDisposable
{
    private readonly string _gameDirectory;

    public FtbPackInstallerTests()
    {
        _gameDirectory = Path.Combine(
            Path.GetTempPath(),
            "ferrite-ftb-" + Guid.NewGuid().ToString("N"),
            "minecraft");
        Directory.CreateDirectory(_gameDirectory);
    }

    public void Dispose()
    {
        var root = Directory.GetParent(_gameDirectory)!.FullName;
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static FtbFile File(
        string name,
        string path,
        string? url = "https://cdn.example/file.bin",
        bool optional = false,
        string? sha1 = "abc",
        long size = 10) => new()
    {
        Name = name,
        Path = path,
        Url = url,
        Optional = optional,
        Sha1 = sha1,
        Size = size,
    };

    [Fact]
    public void A_file_lands_in_the_directory_the_pack_names()
    {
        var (requests, skipped, warnings) = FtbPackInstaller.PlanFiles(
            [File("Bookshelf.jar", "./mods")],
            _gameDirectory);

        var request = Assert.Single(requests);
        Assert.Equal(0, skipped);
        Assert.Empty(warnings);
        Assert.Equal(Path.Combine(_gameDirectory, "mods", "Bookshelf.jar"), request.TargetPath);
        Assert.Equal("abc", request.ExpectedSha1);
        Assert.Equal(10, request.ExpectedSize);
    }

    [Theory]
    [InlineData("./")]
    [InlineData(".")]
    [InlineData("")]
    public void A_root_path_still_lands_inside_the_instance(string path)
    {
        var (requests, _, _) = FtbPackInstaller.PlanFiles([File("options.txt", path)], _gameDirectory);

        var request = Assert.Single(requests);
        Assert.Equal(Path.Combine(_gameDirectory, "options.txt"), request.TargetPath);
    }

    [Fact]
    public void An_optional_file_is_not_installed()
    {
        var (requests, skipped, _) = FtbPackInstaller.PlanFiles(
            [File("Extra.jar", "./mods", optional: true), File("Core.jar", "./mods")],
            _gameDirectory);

        Assert.Equal("Core.jar", Assert.Single(requests).Label);
        Assert.Equal(1, skipped);
    }

    [Fact]
    public void A_file_without_a_download_is_reported()
    {
        var (requests, skipped, warnings) = FtbPackInstaller.PlanFiles(
            [File("Missing.jar", "./mods", url: null, sha1: null)],
            _gameDirectory);

        Assert.Empty(requests);
        Assert.Equal(1, skipped);
        Assert.Contains("without a download", Assert.Single(warnings), StringComparison.Ordinal);
    }

    [Fact]
    public void A_path_that_climbs_out_of_the_instance_is_refused()
    {
        var (requests, skipped, warnings) = FtbPackInstaller.PlanFiles(
            [File("evil.jar", "../../../windows/system32")],
            _gameDirectory);

        Assert.Empty(requests);
        Assert.Equal(1, skipped);
        Assert.Contains("Refused", Assert.Single(warnings), StringComparison.Ordinal);
    }

    [Fact]
    public void An_absolute_path_is_refused()
    {
        var (requests, skipped, warnings) = FtbPackInstaller.PlanFiles(
            [File("evil.jar", @"C:\Windows\System32"), File("rooted.jar", "/")],
            _gameDirectory);

        Assert.Empty(requests);
        Assert.Equal(2, skipped);
        Assert.Equal(2, warnings.Count);
        Assert.All(warnings, warning => Assert.Contains("Refused", warning, StringComparison.Ordinal));
    }

    [Fact]
    public void Every_planned_target_stays_inside_the_instance()
    {
        var (requests, _, _) = FtbPackInstaller.PlanFiles(
            [
                File("a.jar", "./mods"),
                File("b.jar", "config"),
                File("c.json", "./config/nested"),
                File("d.txt", "."),
            ],
            _gameDirectory);

        Assert.Equal(4, requests.Count);
        Assert.All(
            requests,
            request => Assert.True(
                Path.GetFullPath(request.TargetPath).StartsWith(
                    Path.GetFullPath(_gameDirectory) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase),
                $"{request.TargetPath} escaped the instance"));
    }
}
