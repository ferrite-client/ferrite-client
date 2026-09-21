using Ferrite.Core.Platform;

namespace Ferrite.Core.Tests;

/// <summary>
/// Moving the launcher's data folder. Every refusal has to happen before anything is copied, the copy
/// has to carry the data intact, and a restart has to pick the new location up.
/// </summary>
public sealed class DataRootRelocationTests : IDisposable
{
    private readonly string _root;
    private readonly string _current;
    private readonly string _launcherBase;

    public DataRootRelocationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ferrite-rootmove-" + Guid.NewGuid().ToString("N"));
        _current = Path.Combine(_root, "current");
        _launcherBase = Path.Combine(_root, "launcher");
        Directory.CreateDirectory(_current);
        Directory.CreateDirectory(_launcherBase);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private void Seed()
    {
        var paths = AppPaths.ForRoot(_current);
        paths.EnsureCreated();
        File.WriteAllText(Path.Combine(paths.ConfigDirectory, "settings.json"), """{ "schemaVersion": 1 }""");
        var gameDirectory = paths.InstanceGameDirectory(Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(gameDirectory, "mods"));
        File.WriteAllText(Path.Combine(gameDirectory, "mods", "example.jar"), "mod bytes");
        File.WriteAllText(Path.Combine(_current, "logs", "launcher.log"), "a log line");
    }

    [Fact]
    public void The_same_folder_inside_it_or_containing_it_are_all_refused()
    {
        Seed();

        var same = DataRootRelocator.Validate(_current, _current);
        Assert.False(same.IsValid);
        Assert.Contains(same.Issues, issue => issue.Contains("already", StringComparison.OrdinalIgnoreCase));

        var nested = DataRootRelocator.Validate(_current, Path.Combine(_current, "inner"));
        Assert.False(nested.IsValid);
        Assert.Contains(nested.Issues, issue => issue.Contains("inside", StringComparison.OrdinalIgnoreCase));

        var parent = DataRootRelocator.Validate(_current, _root);
        Assert.False(parent.IsValid);
        Assert.Contains(parent.Issues, issue => issue.Contains("contain", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_folder_that_is_not_empty_and_not_ferrite_is_refused()
    {
        Seed();
        var foreign = Path.Combine(_root, "someone-elses");
        Directory.CreateDirectory(foreign);
        File.WriteAllText(Path.Combine(foreign, "taxes.xlsx"), "not ours");

        var validation = DataRootRelocator.Validate(_current, foreign);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Issues, issue => issue.Contains("not empty", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_relative_path_is_refused()
    {
        var validation = DataRootRelocator.Validate(_current, "somewhere/relative");

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Issues, issue => issue.Contains("absolute", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void An_empty_folder_is_accepted_and_reports_what_would_be_copied()
    {
        Seed();
        var target = Path.Combine(_root, "target");
        Directory.CreateDirectory(target);

        var validation = DataRootRelocator.Validate(_current, target);

        Assert.True(validation.IsValid, validation.Summary);
        Assert.True(validation.TotalBytes > 0, "the size of the data to move should be reported");
    }

    [Fact]
    public async Task Moving_copies_the_data_and_the_next_start_uses_the_new_folder()
    {
        Seed();
        var target = Path.Combine(_root, "moved");
        Directory.CreateDirectory(target);
        var reports = new List<string>();

        var result = await DataRootRelocator.MoveAsync(
            _current,
            target,
            new Progress<string>(reports.Add),
            TestContext.Current.CancellationToken);

        Assert.Equal(Path.GetFullPath(target), result.Target);
        Assert.True(result.FilesCopied >= 3, $"copied {result.FilesCopied} file(s)");

        // The data is there, including the instance folders and the logs.
        Assert.True(File.Exists(Path.Combine(target, "config", "settings.json")));
        Assert.True(File.Exists(Path.Combine(target, "logs", "launcher.log")));
        Assert.True(Directory.Exists(Path.Combine(target, "data", "instances")));
        Assert.Empty(Directory.GetFiles(target, "*.part-*", SearchOption.AllDirectories));

        // Nothing was deleted from the old folder: a failed restart must not lose the user's data.
        Assert.True(File.Exists(Path.Combine(_current, "config", "settings.json")));

        // The marker next to the executable is what a restart reads.
        DataRootRelocator.WriteRootMarker(target, _launcherBase);
        var resolved = AppPaths.CreateDefault(_launcherBase);
        Assert.Equal(Path.GetFullPath(target), resolved.Root);

        // And clearing it returns the launcher to its normal location.
        Assert.True(DataRootRelocator.ClearRootMarker(_launcherBase));
        Assert.False(File.Exists(Path.Combine(_launcherBase, AppPaths.RootMarkerFileName)));
        Assert.False(DataRootRelocator.ClearRootMarker(_launcherBase));
    }

    [Fact]
    public void The_environment_variable_still_wins_over_the_marker()
    {
        var marker = Path.Combine(_launcherBase, AppPaths.RootMarkerFileName);
        File.WriteAllText(marker, Path.Combine(_root, "from-marker"));
        var fromEnvironment = Path.Combine(_root, "from-environment");

        try
        {
            Environment.SetEnvironmentVariable(AppPaths.EnvironmentVariableName, fromEnvironment);
            var resolved = AppPaths.CreateDefault(_launcherBase);

            Assert.Equal(Path.GetFullPath(fromEnvironment), resolved.Root);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AppPaths.EnvironmentVariableName, null);
        }
    }
}
