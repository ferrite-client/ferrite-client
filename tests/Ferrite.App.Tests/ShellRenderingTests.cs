using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Ferrite.App.Services;
using Ferrite.App.ViewModels;
using Ferrite.App.Views;
using Ferrite.Core.Minecraft;
using Ferrite.Core.Platform;
using Ferrite.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Ferrite.App.Tests;

/// <summary>
/// Renders the real shell and pages headlessly. These tests catch layout-time failures, missing
/// bindings, and resource errors that a compile alone cannot.
/// </summary>
public sealed class ShellRenderingTests : IDisposable
{
    private readonly string _root;
    private readonly AppServices _services;

    public ShellRenderingTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ferrite-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
        _services = new AppServices(loggerFactory, AppPaths.ForRoot(_root));
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

    [AvaloniaFact]
    public void Shell_renders_with_navigation_and_status_bar()
    {
        var window = ShowShell(out _);

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);

        var texts = Texts(window);
        Assert.Contains("Ferrite", texts);
        Assert.Contains("Minecraft launcher", texts);
        Assert.Contains("Library", texts);
        Assert.Contains("Browse", texts);
        Assert.Contains("Java", texts);
        Assert.Contains("Accounts", texts);
        Assert.Contains("Settings", texts);
        Assert.Contains("No account", texts);

        Save(frame!, "shell");
    }

    [AvaloniaFact]
    public void Every_page_renders_without_error()
    {
        var window = ShowShell(out var viewModel);

        foreach (var page in new[] { "Library", "Browse", "Java", "Accounts", "Settings" })
        {
            viewModel.NavigateCommand.Execute(page);
            var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Save(frame!, "page-" + page.ToLowerInvariant());
        }
    }

    [AvaloniaFact]
    public void Instance_detail_renders_with_all_tabs()
    {
        var record = new InstanceRecord
        {
            Id = Guid.NewGuid(),
            Name = "Rendered instance",
            MinecraftVersion = "1.21.1",
            Loader = LoaderKind.NeoForge,
            LoaderVersion = "21.1.251",
        };

        var shell = new MainWindowViewModel(_services);
        var viewModel = new InstanceDetailViewModel(record, _services, shell);
        var window = new Window
        {
            Content = new InstanceDetailView { DataContext = viewModel },
            Width = 1200,
            Height = 800,
        };
        window.Show();

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);

        var texts = Texts(window);
        Assert.Contains("Rendered instance", texts);
        Assert.Contains("Settings", texts);
        Assert.Contains("Mods", texts);
        Assert.Contains("Content", texts);
        Assert.Contains("Logs", texts);

        Save(frame!, "instance-detail");
    }

    [AvaloniaFact]
    public void Light_theme_renders()
    {
        Ferrite.App.App.ApplyTheme(ThemeVariant.Light);
        try
        {
            var window = ShowShell(out _);
            var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Save(frame!, "shell-light");
        }
        finally
        {
            Ferrite.App.App.ApplyTheme(ThemeVariant.Dark);
        }
    }

    [AvaloniaFact]
    public void Minimum_window_size_renders()
    {
        var viewModel = new MainWindowViewModel(_services);
        var window = new MainWindow
        {
            DataContext = viewModel,
            Width = 1024,
            Height = 680,
        };
        window.Show();

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        Save(frame!, "shell-minimum");
    }

    private MainWindow ShowShell(out MainWindowViewModel viewModel)
    {
        viewModel = new MainWindowViewModel(_services);
        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        return window;
    }

    private static List<string> Texts(Control root) =>
        root.GetVisualDescendants()
            .OfType<TextBlock>()
            .Select(block => block.Text)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => text!)
            .ToList();

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
