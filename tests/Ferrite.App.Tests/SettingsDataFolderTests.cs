using Ferrite.App.Localization;
using Ferrite.App.Services;
using Ferrite.App.ViewModels;
using Ferrite.Core.Platform;
using Microsoft.Extensions.Logging;

namespace Ferrite.App.Tests;

/// <summary>
/// The Settings page's data-folder section: a bad choice is refused with a reason, a good one moves
/// the data and records the new root for the next start.
/// </summary>
public sealed class SettingsDataFolderTests : IDisposable
{
    private readonly string _root;
    private readonly string _launcherBase;
    private readonly AppServices _services;
    private readonly SettingsViewModel _viewModel;

    public SettingsDataFolderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ferrite-dataroot-" + Guid.NewGuid().ToString("N"));
        _launcherBase = Path.Combine(_root, "launcher");
        Directory.CreateDirectory(_launcherBase);
        var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
        _services = new AppServices(loggerFactory, AppPaths.ForRoot(Path.Combine(_root, "data-root")));
        Localizer.Apply(Avalonia.Application.Current, Localizer.English);
        _viewModel = new SettingsViewModel(_services, new MainWindowViewModel(_services))
        {
            // Never write the marker next to the running test host.
            LauncherBaseDirectory = _launcherBase,
        };
    }

    public void Dispose()
    {
        _services.Dispose();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task A_folder_the_launcher_does_not_own_is_refused_with_a_reason()
    {
        var foreign = Path.Combine(_root, "documents");
        Directory.CreateDirectory(foreign);
        await File.WriteAllTextAsync(
            Path.Combine(foreign, "thesis.docx"),
            "not launcher data",
            TestContext.Current.CancellationToken);

        await _viewModel.MoveDataRootAsync(foreign);

        Assert.True(_viewModel.HasDataRootStatus);
        Assert.Contains("not empty", _viewModel.DataRootStatus, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(_launcherBase, AppPaths.RootMarkerFileName)));
        // Nothing was copied into the user's folder.
        Assert.False(Directory.Exists(Path.Combine(foreign, "data")));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task Moving_records_the_new_root_and_says_a_restart_is_needed()
    {
        var source = _services.Paths.Root;
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(
            Path.Combine(source, "config", "settings.json"),
            """{ "schemaVersion": 1 }""",
            TestContext.Current.CancellationToken);

        var target = Path.Combine(_root, "moved");
        Directory.CreateDirectory(target);

        await _viewModel.MoveDataRootAsync(target);

        Assert.Contains("Restart", _viewModel.DataRootStatus, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Combine(target, "config", "settings.json")));
        Assert.Equal(
            Path.GetFullPath(target),
            File.ReadAllText(Path.Combine(_launcherBase, AppPaths.RootMarkerFileName)).Trim());

        // The next start resolves to the new folder, and reverting removes the marker again.
        Assert.Equal(Path.GetFullPath(target), AppPaths.CreateDefault(_launcherBase).Root);

        _viewModel.UseDefaultDataFolderCommand.Execute(null);
        Assert.False(File.Exists(Path.Combine(_launcherBase, AppPaths.RootMarkerFileName)));
        Assert.Contains(
            "default",
            _viewModel.DataRootStatus,
            StringComparison.OrdinalIgnoreCase);
    }
}
