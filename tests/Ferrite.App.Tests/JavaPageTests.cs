using Ferrite.App.Localization;
using Ferrite.App.Services;
using Ferrite.App.ViewModels;
using Ferrite.Core.Java;
using Ferrite.Core.Platform;
using Ferrite.Core.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ferrite.App.Tests;

/// <summary>
/// Adding a Java runtime by hand. The path has to be probed before it is accepted, it has to survive
/// a restart, and a path that stops working has to be dropped rather than offered as a choice.
/// </summary>
public sealed class JavaPageTests : IDisposable
{
    private readonly string _root;
    private readonly AppServices _services;
    private readonly JavaViewModel _viewModel;

    public JavaPageTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ferrite-java-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
        _services = new AppServices(loggerFactory, AppPaths.ForRoot(_root));
        Localizer.Apply(Avalonia.Application.Current, Localizer.English);
        _viewModel = new JavaViewModel(_services, new MainWindowViewModel(_services));
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

    /// <summary>A real Java executable from this machine, or null when there is none to test with.</summary>
    private async Task<JavaRuntime?> RealRuntimeAsync()
    {
        var runtimes = await _services.Java.DetectAsync(TestContext.Current.CancellationToken);
        return runtimes.FirstOrDefault();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task A_path_that_is_not_java_is_refused_and_not_stored()
    {
        var bogus = Path.Combine(_root, "not-java.exe");
        await File.WriteAllTextAsync(bogus, "this is not a runtime", TestContext.Current.CancellationToken);

        await _viewModel.AddJavaPathAsync(bogus);

        Assert.Empty(_services.Settings.Current.CustomJavaPaths);
        Assert.Contains("did not answer", _viewModel.StatusNote, StringComparison.OrdinalIgnoreCase);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task An_added_path_is_probed_stored_and_offered()
    {
        var runtime = await RealRuntimeAsync();
        if (runtime is null)
        {
            Assert.Skip("No Java runtime is installed on this machine.");
        }

        await _viewModel.AddJavaPathAsync(runtime!.ExecutablePath);

        Assert.Contains(
            runtime.ExecutablePath,
            _services.Settings.Current.CustomJavaPaths,
            StringComparer.OrdinalIgnoreCase);
        var added = Assert.Single(
            _viewModel.Runtimes,
            item => string.Equals(
                item.ExecutablePath,
                runtime.ExecutablePath,
                StringComparison.OrdinalIgnoreCase));
        Assert.Equal(JavaRuntimeSource.UserSpecified, added.Source);

        // The path has to survive the settings document being read again, not just live in memory.
        var reloaded = new SettingsStore(_services.Paths, NullLogger<SettingsStore>.Instance);
        var settings = await reloaded.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Contains(
            runtime.ExecutablePath,
            settings.CustomJavaPaths,
            StringComparer.OrdinalIgnoreCase);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task A_custom_path_that_stopped_working_is_dropped()
    {
        var runtime = await RealRuntimeAsync();
        if (runtime is null)
        {
            Assert.Skip("No Java runtime is installed on this machine.");
        }

        var settings = _services.Settings.Current;
        settings.CustomJavaPaths.Add(runtime!.ExecutablePath);
        settings.CustomJavaPaths.Add(Path.Combine(_root, "uninstalled", "bin", "java.exe"));
        await _services.Settings.SaveAsync(TestContext.Current.CancellationToken);

        await _viewModel.ScanCommand.ExecuteAsync(null);

        // The working path stays; the one that is gone is removed from the stored list.
        Assert.Contains(
            runtime.ExecutablePath,
            settings.CustomJavaPaths,
            StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            settings.CustomJavaPaths,
            path => path.Contains("uninstalled", StringComparison.OrdinalIgnoreCase));

        var reloaded = new SettingsStore(_services.Paths, NullLogger<SettingsStore>.Instance);
        var persisted = await reloaded.LoadAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(
            persisted.CustomJavaPaths,
            path => path.Contains("uninstalled", StringComparison.OrdinalIgnoreCase));
    }
}
