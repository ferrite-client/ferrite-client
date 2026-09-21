using Ferrite.App.Localization;
using Ferrite.App.Services;
using Ferrite.App.ViewModels;
using Ferrite.Core.Minecraft;
using Ferrite.Core.Platform;
using Ferrite.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Ferrite.App.Tests;

/// <summary>
/// The instance assistant end to end: it reads the instance's own crash report and mod list and names
/// the mod whose classes appear in the failing frame.
/// </summary>
public sealed class InstanceAssistantTests : IDisposable
{
    private readonly string _root;
    private readonly AppServices _services;

    public InstanceAssistantTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ferrite-assistant-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
        _services = new AppServices(loggerFactory, AppPaths.ForRoot(_root));
        Localizer.Apply(Avalonia.Application.Current, Localizer.English);
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
    public async Task The_assistant_reads_the_crash_report_and_names_the_mod()
    {
        var record = await _services.Instances.CreateAsync(
            new InstanceRecord
            {
                Id = Guid.NewGuid(),
                Name = "Broken",
                MinecraftVersion = "1.21.1",
                Loader = LoaderKind.Fabric,
                LoaderVersion = "0.19.5",
            },
            CancellationToken.None);

        var game = _services.Paths.InstanceGameDirectory(record.Id);
        var modsDirectory = Path.Combine(game, "mods");
        Directory.CreateDirectory(modsDirectory);
        ModArchiveFixture.WriteFabric(
            Path.Combine(modsDirectory, "sodium.jar"), "sodium", "Sodium", "0.6.0", 64, "fabric-api");

        var crashDirectory = Path.Combine(game, "crash-reports");
        Directory.CreateDirectory(crashDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(crashDirectory, "crash-2026-09-21_12.00.00-client.txt"),
            "---- Minecraft Crash Report ----\n"
            + "// Everything is fine\n"
            + "\n"
            + "Time: 2026-09-21 12:00:00\n"
            + "Description: Rendering overlay\n"
            + "\n"
            + "java.lang.NullPointerException: Cannot invoke \"x\" because \"y\" is null\n"
            + "\tat net.caffeinemc.mods.sodium.SodiumClientMod.run(SodiumClientMod.java:12)\n",
            CancellationToken.None);

        var shell = new MainWindowViewModel(_services);
        var viewModel = new InstanceDetailViewModel(record, _services, shell);
        await viewModel.InitializeAsync();

        Assert.Equal(string.Empty, viewModel.AdvisorText);
        await viewModel.AskAssistantCommand.ExecuteAsync(null);

        Assert.Contains("Instance assistant", viewModel.AdvisorText, StringComparison.Ordinal);
        Assert.Contains("Sodium", viewModel.AdvisorText, StringComparison.Ordinal);
        Assert.Contains("stack trace", viewModel.AdvisorText, StringComparison.Ordinal);
        Assert.False(viewModel.IsAdvising);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task The_assistant_reports_an_instance_whose_files_are_not_installed()
    {
        var record = await _services.Instances.CreateAsync(
            new InstanceRecord
            {
                Id = Guid.NewGuid(),
                Name = "Empty",
                MinecraftVersion = "1.21.1",
                Loader = LoaderKind.Vanilla,
            },
            CancellationToken.None);

        var shell = new MainWindowViewModel(_services);
        var viewModel = new InstanceDetailViewModel(record, _services, shell);
        await viewModel.InitializeAsync();

        await viewModel.AskAssistantCommand.ExecuteAsync(null);

        // Nothing is installed yet, so the honest answer is that the managed files are missing and
        // repair is the action - not a bare "no problems found".
        Assert.Contains("Instance assistant", viewModel.AdvisorText, StringComparison.Ordinal);
        Assert.Contains("Repair", viewModel.AdvisorText, StringComparison.Ordinal);
        Assert.False(viewModel.IsAdvising);
    }
}
