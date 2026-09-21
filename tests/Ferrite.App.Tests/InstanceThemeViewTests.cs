using Ferrite.App.Localization;
using Ferrite.App.Services;
using Ferrite.App.ViewModels;
using Ferrite.Core.Minecraft;
using Ferrite.Core.Platform;
using Ferrite.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Ferrite.App.Tests;

/// <summary>
/// The instance's own theme on the detail page: a picked background is copied into the instance and
/// the accent is validated before it is stored.
/// </summary>
public sealed class InstanceThemeViewTests : IDisposable
{
    private readonly string _root;
    private readonly AppServices _services;

    public InstanceThemeViewTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ferrite-theme-" + Guid.NewGuid().ToString("N"));
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

    /// <summary>A real instance on the data root, plus the page that shows it.</summary>
    private async Task<(InstanceRecord Record, InstanceDetailViewModel ViewModel)> CreateAsync()
    {
        var record = await _services.Instances.CreateAsync(
            new InstanceRecord
            {
                Id = Guid.NewGuid(),
                Name = "Styled",
                MinecraftVersion = "1.21.1",
                Loader = LoaderKind.Vanilla,
            },
            CancellationToken.None);
        return (record, new InstanceDetailViewModel(record, _services, new MainWindowViewModel(_services)));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task Picking_a_background_copies_it_into_the_instance()
    {
        var source = Path.Combine(_root, "picked.png");
        TestAssets.WritePng(source, 32, 18);

        var (record, viewModel) = await CreateAsync();
        Assert.False(viewModel.HasThemeBackground);

        Assert.True(await viewModel.SetThemeBackgroundAsync(source));

        // Copied, not referenced: the image is readable after the source is gone.
        var copied = Path.Combine(
            _services.Paths.InstanceDirectory(record.Id),
            "theme",
            "background.png");
        Assert.True(File.Exists(copied));
        File.Delete(source);

        var reloaded = await _services.Instances.LoadAsync(record.Id, CancellationToken.None);
        Assert.Equal("theme/background.png", reloaded.ThemeBackgroundPath);

        var reopened = new InstanceDetailViewModel(reloaded, _services, new MainWindowViewModel(_services));
        Assert.True(reopened.HasThemeBackground);
        Assert.True(reopened.HasTheme);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task Clearing_the_background_removes_the_copy()
    {
        var source = Path.Combine(_root, "picked.png");
        TestAssets.WritePng(source, 8, 8);
        var (record, viewModel) = await CreateAsync();
        await viewModel.SetThemeBackgroundAsync(source);
        var copied = Path.Combine(_services.Paths.InstanceDirectory(record.Id), "theme", "background.png");
        Assert.True(File.Exists(copied));

        await viewModel.ClearThemeBackgroundCommand.ExecuteAsync(null);

        Assert.False(File.Exists(copied));
        Assert.False(viewModel.HasThemeBackground);
        Assert.Null((await _services.Instances.LoadAsync(record.Id, CancellationToken.None)).ThemeBackgroundPath);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task A_valid_accent_is_stored_and_renders_a_brush()
    {
        var (record, viewModel) = await CreateAsync();
        viewModel.ThemeAccent = "d08a3e";

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.False(viewModel.HasThemeError);
        Assert.Equal("#D08A3E", viewModel.ThemeAccent);
        Assert.True(viewModel.HasThemeAccent);
        Assert.True(viewModel.HasTheme);
        Assert.Equal(
            "#D08A3E",
            (await _services.Instances.LoadAsync(record.Id, CancellationToken.None)).ThemeAccent);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task A_typo_in_the_accent_is_refused_and_nothing_is_saved()
    {
        // A theme the user already has must survive a typo in the box.
        var (record, viewModel) = await CreateAsync();
        viewModel.ThemeAccent = "#123456";
        await viewModel.SaveCommand.ExecuteAsync(null);

        viewModel.ThemeAccent = "not a colour";
        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasThemeError);
        Assert.Equal("#123456", (await _services.Instances.LoadAsync(record.Id, CancellationToken.None)).ThemeAccent);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task An_empty_accent_clears_it()
    {
        var (record, viewModel) = await CreateAsync();
        viewModel.ThemeAccent = "#123456";
        await viewModel.SaveCommand.ExecuteAsync(null);

        viewModel.ThemeAccent = string.Empty;
        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.False(viewModel.HasThemeError);
        Assert.False(viewModel.HasThemeAccent);
        Assert.Null((await _services.Instances.LoadAsync(record.Id, CancellationToken.None)).ThemeAccent);
    }
}
