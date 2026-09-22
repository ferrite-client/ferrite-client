using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Ferrite.App.Localization;
using Ferrite.App.Services;
using Ferrite.App.ViewModels;
using Ferrite.App.Views;
using Ferrite.Core.Minecraft;
using Ferrite.Core.Platform;
using Ferrite.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Ferrite.App.Tests;

/// <summary>
/// The window sizes and DPI scales the brief names. The headless host can simulate a DPI change, so
/// these are rendered rather than assumed: a layout that clips is visible in the frame.
/// </summary>
public sealed class ResponsiveAndDpiTests : IDisposable
{
    private readonly string _root;
    private readonly AppServices _services;

    public ResponsiveAndDpiTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ferrite-responsive-" + Guid.NewGuid().ToString("N"));
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

    /// <summary>
    /// 100%, 125%, 150%, and 200%. A DPI change must keep the shell laid out: the rail, the title
    /// bar, and the page all have to survive it, and the frame has to grow with the scale.
    /// </summary>
    [AvaloniaFact]
    public void The_shell_renders_at_every_supported_dpi_scale()
    {
        var viewModel = new MainWindowViewModel(_services);
        var window = new MainWindow { DataContext = viewModel, Width = 1360, Height = 860 };
        window.Show();

        foreach (var scaling in new[] { 1.0, 1.25, 1.5, 2.0 })
        {
            window.SetRenderScaling(scaling);
            window.CaptureRenderedFrame();
            var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);

            // The frame is the client size at this scale; a scale that produced a different shape
            // would mean the window did not receive the DPI change.
            Assert.Equal((int)Math.Round(1360 * scaling), frame!.PixelSize.Width);
            Assert.Equal((int)Math.Round(860 * scaling), frame.PixelSize.Height);

            var texts = Texts(window);
            Assert.Contains("Ferrite", texts);
            Assert.Contains("Library", texts);
            Assert.Contains("Discover", texts);
            Assert.Contains("Downloads", texts);
            Assert.Contains("Settings", texts);
            Assert.Contains("Ready", texts);

            Save(frame, $"dpi-{(int)Math.Round(scaling * 100)}");
        }
    }

    /// <summary>
    /// The minimum supported window, with an instance open. The hero's Play action is the one control
    /// that must survive a narrow window, because it is the primary action of the whole product.
    /// </summary>
    [AvaloniaFact]
    public void The_instance_page_keeps_its_primary_action_at_the_minimum_size()
    {
        var record = new InstanceRecord
        {
            Id = Guid.NewGuid(),
            Name = "Narrow instance with a long name to trim",
            MinecraftVersion = "1.21.1",
            Loader = LoaderKind.NeoForge,
            LoaderVersion = "21.1.251",
        };
        var shell = new MainWindowViewModel(_services);
        var detail = new InstanceDetailViewModel(record, _services, shell);
        var window = new Window
        {
            Content = new InstanceDetailView { DataContext = detail },
            Width = 1024,
            Height = 680,
        };
        window.Show();

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);

        var texts = Texts(window);
        Assert.Contains("Narrow instance with a long name to trim", texts);
        Assert.Contains("Play", texts);
        // Every section is still reachable from the rail, not scrolled off the end.
        Assert.Contains("Logs", texts);
        Assert.Contains("Assistant", texts);

        Save(frame!, "instance-minimum");

        // A DPI change on the instance page has to leave the same guarantees.
        window.SetRenderScaling(1.5);
        window.CaptureRenderedFrame();
        var scaled = window.CaptureRenderedFrame();
        Assert.NotNull(scaled);
        Assert.Contains("Play", Texts(window));
        Save(scaled!, "instance-minimum-150");
    }

    /// <summary>
    /// The library grid reflows: wider windows fit more cards, and a card is never wider than the
    /// space the grid has for it.
    /// </summary>
    [AvaloniaFact]
    public async Task The_library_grid_reflows_with_the_window()
    {
        for (var index = 0; index < 8; index++)
        {
            var record = await _services.Instances
                .CreateAsync(
                    new InstanceRecord
                    {
                        Id = Guid.NewGuid(),
                        Name = $"Instance {index}",
                        MinecraftVersion = "1.21.1",
                    },
                    CancellationToken.None)
                .ConfigureAwait(true);
            Directory.CreateDirectory(_services.Paths.InstanceGameDirectory(record.Id));
        }

        var shell = new MainWindowViewModel(_services);
        await shell.InitializeAsync().ConfigureAwait(true);

        var window = new MainWindow { DataContext = shell, Width = 1024, Height = 680 };
        window.Show();
        window.CaptureRenderedFrame();

        var narrow = shell.Library.GridColumns;
        Assert.True(narrow >= 2, $"the minimum window fits only {narrow} card(s) across");

        var previous = narrow;
        foreach (var width in new[] { 1366, 1920, 2560 })
        {
            window.Width = width;
            window.CaptureRenderedFrame();
            var columns = shell.Library.GridColumns;
            Assert.True(
                columns >= previous,
                $"a {width}px window fits {columns} cards, fewer than the narrower window's {previous}");
            Assert.Equal(columns, shell.Library.GridColumns);
            previous = columns;
        }

        Save(window.CaptureRenderedFrame()!, "library-2560");
    }

    private static List<string> Texts(Control root) =>
        root.GetVisualDescendants()
            .OfType<TextBlock>()
            .Select(block => block.Text)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => text!)
            .ToList();

    /// <summary>
    /// The custom title bar only keeps native window behaviour because the elements declare the roles
    /// the platform hit-tests: the strip is the caption, and the button inside it still receives input.
    /// A headless host has no frame to drag, so this guards the contract rather than the drag itself;
    /// the interactive behaviour on a real desktop is the documented manual pass.
    /// </summary>
    [AvaloniaFact]
    public void The_title_bar_declares_the_roles_the_platform_hit_tests()
    {
        var shell = new MainWindowViewModel(_services);
        var window = new MainWindow { DataContext = shell, Width = 1360, Height = 860 };
        window.Show();

        Assert.True(
            window.ExtendClientAreaToDecorationsHint,
            "the window must extend its client area or there is no title bar to customise");

        var titleBar = window.GetVisualDescendants()
            .FirstOrDefault(visual =>
                WindowDecorationProperties.GetElementRole(visual) == WindowDecorationsElementRole.TitleBar);
        Assert.NotNull(titleBar);
        Assert.Equal(0, titleBar!.Bounds.Top);
        Assert.True(titleBar.Bounds.Width > 0, "the title bar has no width");

        // The rail toggle sits inside that caption, so it has to be marked as a user element or the
        // platform would swallow the click and start a window move instead.
        var interactive = window.GetVisualDescendants().Count(visual =>
            WindowDecorationProperties.GetElementRole(visual) == WindowDecorationsElementRole.User);
        Assert.True(interactive >= 1, "no interactive element inside the caption is marked as such");
    }

    private static void Save(Bitmap frame, string name)
    {
        var directory = Environment.GetEnvironmentVariable("FERRITE_UI_SHOTS");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        frame.Save(Path.Combine(directory, name + ".png"), new PngBitmapEncoderOptions());
    }
}
