using Ferrite.App.Localization;
using Ferrite.App.Services;
using Ferrite.App.ViewModels;
using Ferrite.Core.Minecraft;
using Ferrite.Core.Platform;
using Ferrite.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Ferrite.App.Tests;

/// <summary>
/// The quick-action palette: reaching a page or an instance from anywhere. The tests drive the real
/// view model, so the results are the ones the shell would show.
/// </summary>
public sealed class QuickActionTests : IDisposable
{
    private readonly string _root;
    private readonly AppServices _services;

    public QuickActionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ferrite-quick-" + Guid.NewGuid().ToString("N"));
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

    private async Task<MainWindowViewModel> SeedAsync()
    {
        foreach (var (name, loader) in new[] { ("Survival", LoaderKind.Vanilla), ("Tech pack", LoaderKind.Fabric) })
        {
            var record = await _services.Instances.CreateAsync(
                new InstanceRecord
                {
                    Id = Guid.NewGuid(),
                    Name = name,
                    MinecraftVersion = "1.21.1",
                    Loader = loader,
                    LoaderVersion = loader == LoaderKind.Vanilla ? null : "0.19.5",
                },
                CancellationToken.None);
            Directory.CreateDirectory(_services.Paths.InstanceGameDirectory(record.Id));
        }

        var shell = new MainWindowViewModel(_services);
        await shell.InitializeAsync();
        return shell;
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task The_palette_lists_pages_and_instances_and_filters_them()
    {
        var shell = await SeedAsync();
        Assert.False(shell.IsQuickActionsOpen);

        shell.OpenQuickActionsCommand.Execute(null);
        Assert.True(shell.IsQuickActionsOpen);
        Assert.True(shell.HasQuickResults);

        var titles = shell.QuickResults.Select(item => item.Title).ToList();
        Assert.Contains("Go to Library", titles);
        Assert.Contains("Go to Settings", titles);
        Assert.Contains("Open Survival", titles);
        Assert.Contains("Launch Tech pack", titles);
        Assert.Contains("New instance", titles);

        // The query narrows the results rather than paging them.
        shell.QuickQuery = "tech";
        Assert.Equal(
            new[] { "Open Tech pack", "Launch Tech pack" },
            shell.QuickResults.Select(item => item.Title).ToList());

        shell.QuickQuery = "zzzz";
        Assert.True(shell.HasNoQuickResults);
        Assert.False(shell.HasQuickResults);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task Choosing_a_result_runs_it_and_closes_the_palette()
    {
        var shell = await SeedAsync();

        shell.OpenQuickActionsCommand.Execute(null);
        var settings = shell.QuickResults.Single(item => item.Title == "Go to Settings");
        settings.Command.Execute(null);

        Assert.False(shell.IsQuickActionsOpen);
        Assert.Equal(AppPage.Settings, shell.CurrentPage);

        shell.OpenQuickActionsCommand.Execute(null);
        var open = shell.QuickResults.Single(item => item.Title == "Open Tech pack");
        open.Command.Execute(null);

        Assert.False(shell.IsQuickActionsOpen);
        Assert.Equal(AppPage.Library, shell.CurrentPage);
        var detail = Assert.IsType<InstanceDetailViewModel>(shell.DetailPage);
        Assert.Equal("Tech pack", detail.Name);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task Closing_the_palette_clears_the_query()
    {
        var shell = await SeedAsync();
        shell.OpenQuickActionsCommand.Execute(null);
        shell.QuickQuery = "survival";

        shell.CloseQuickActionsCommand.Execute(null);

        Assert.False(shell.IsQuickActionsOpen);
        Assert.Equal(string.Empty, shell.QuickQuery);
    }
}
