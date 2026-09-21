using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Ferrite.App.Services;
using Ferrite.App.ViewModels;
using Ferrite.App.Views;
using Ferrite.Core.Content;
using Ferrite.Core.Minecraft;
using Ferrite.Core.Platform;
using Ferrite.Core.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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

    /// <summary>The Content tab lists available updates with a per-item selection.</summary>
    [AvaloniaFact]
    public void Instance_content_tab_renders_available_updates()
    {
        var record = _services.Instances
            .CreateAsync(
                new InstanceRecord
                {
                    Id = Guid.NewGuid(),
                    Name = "Modded instance",
                    MinecraftVersion = "1.21.1",
                    Loader = LoaderKind.Fabric,
                    LoaderVersion = "0.15.11",
                },
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        var shell = new MainWindowViewModel(_services);
        var viewModel = new InstanceDetailViewModel(record, _services, shell);
        viewModel.Updates.Add(new ContentUpdateItemViewModel(
            new ContentUpdate
            {
                Entry = new ContentManifestEntry
                {
                    RelativePath = "mods/sodium-fabric-0.5.11+mc1.21.jar",
                    Provider = ModrinthClient.ProviderName,
                    ProjectId = "AANobbMI",
                    VersionId = "RncWhTxD",
                },
                Title = "Sodium 0.8.13 for Fabric 1.21.1",
                Available = new ContentVersion(
                    ModrinthClient.ProviderName,
                    "SMxNOGZ6",
                    "AANobbMI",
                    "mc1.21.1-0.8.13-fabric",
                    "Sodium 0.8.13 for Fabric 1.21.1",
                    null,
                    "release",
                    ["1.21.1"],
                    ["fabric"],
                    [],
                    [],
                    null),
            },
            "Modrinth"));
        viewModel.UpdateStatus = "Modrinth: 1 update(s), 0 up to date";

        var window = new Window
        {
            Content = new InstanceDetailView { DataContext = viewModel },
            Width = 1200,
            Height = 900,
        };
        window.Show();

        var tabs = window.GetVisualDescendants().OfType<TabControl>().First();
        tabs.SelectedIndex = 2;

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);

        var texts = Texts(window);
        Assert.Contains("Sodium 0.8.13 for Fabric 1.21.1", texts);
        Assert.Contains(texts, text => text.Contains("Check for updates", StringComparison.Ordinal));

        Save(frame!, "instance-updates");
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

    /// <summary>
    /// Switching the browser to a provider without a key must explain the state instead of showing
    /// an empty result list or an error dialog.
    /// </summary>
    [AvaloniaFact]
    public void Browse_page_explains_an_unconfigured_provider()
    {
        var shell = new MainWindowViewModel(_services);
        var viewModel = new BrowseViewModel(_services, shell);
        var window = new Window
        {
            Content = new BrowseView { DataContext = viewModel },
            Width = 1200,
            Height = 800,
        };
        window.Show();

        viewModel.SelectedProvider = viewModel.Providers.Single(
            provider => provider.Name == "curseforge");

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);

        var texts = Texts(window);
        Assert.Contains(texts, text => text.Contains("CurseForge needs an API key", StringComparison.Ordinal));
        Assert.Contains(texts, text => text.Contains("Search CurseForge", StringComparison.Ordinal));

        Save(frame!, "browse-curseforge");
    }

    /// <summary>The diagnostics section must render, including the empty operation list state.</summary>
    [AvaloniaFact]
    public void Settings_page_renders_the_diagnostics_section()
    {
        var shell = new MainWindowViewModel(_services);
        var viewModel = new SettingsViewModel(_services, shell);
        var window = new Window
        {
            Content = new SettingsView { DataContext = viewModel },
            Width = 1000,
            Height = 1500,
        };
        window.Show();

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);

        var texts = Texts(window);
        Assert.Contains("Diagnostics", texts);
        Assert.Contains("Export diagnostics bundle", texts);
        Assert.Contains("No operations recorded yet.", texts);

        Save(frame!, "settings-diagnostics");
    }

    /// <summary>
    /// The launcher update section must render, and must say what a feed needs rather than looking
    /// like a button that does nothing. This build carries no feed key, so the check explains that.
    /// </summary>
    [AvaloniaFact]
    public async Task Settings_page_renders_the_launcher_update_section()
    {
        var shell = new MainWindowViewModel(_services);
        var viewModel = new SettingsViewModel(_services, shell)
        {
            UpdateFeedUrl = "https://updates.example.invalid/stable/",
        };

        var window = new Window
        {
            Content = new SettingsView { DataContext = viewModel },
            Width = 1000,
            Height = 1800,
        };
        window.Show();

        var texts = Texts(window);
        Assert.Contains("Launcher updates", texts);
        Assert.Contains("Check for updates", texts);
        Assert.Contains("Download and verify", texts);

        // A build with no embedded key must say so instead of pretending the check can work.
        await viewModel.CheckForUpdatesCommand.ExecuteAsync(null);
        Assert.Contains("signing key", viewModel.UpdateStatus, StringComparison.OrdinalIgnoreCase);

        // Captured after the check so the screenshot shows the outcome, not an empty status line.
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        Save(frame!, "settings-updates");
    }

    /// <summary>
    /// With the provider unreachable and a populated cache, the browser must show the cached results
    /// and say where they came from.
    /// </summary>
    [AvaloniaFact]
    public async Task Browse_page_reports_results_served_from_the_cache()
    {
        var root = Path.Combine(Path.GetTempPath(), "ferrite-ui-cache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var paths = AppPaths.ForRoot(root);
        var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));

        // Nothing listens on port 1, so the provider cannot be reached at all.
        using var services = new AppServices(loggerFactory, paths, "http://127.0.0.1:1/v2");

        var cache = new ContentCache(paths.CacheDirectory, NullLogger<ContentCache>.Instance);
        var key = ContentCache.KeyFor(
            ModrinthClient.ProviderName,
            "search",
            string.Empty,
            ContentProjectType.Mod,
            null,
            null,
            30,
            0,
            "relevance");
        cache.Write(
            key,
            new ContentSearchResult(
                1,
                0,
                30,
                [
                    new ContentSummary(
                        ModrinthClient.ProviderName,
                        "AANobbMI",
                        "sodium",
                        "Sodium",
                        "Rendering engine",
                        ContentProjectType.Mod,
                        42,
                        null,
                        "jellysquid3",
                        [],
                        null),
                ]));

        var shell = new MainWindowViewModel(services);
        var viewModel = new BrowseViewModel(services, shell);
        await viewModel.InitializeAsync().ConfigureAwait(true);

        Assert.Single(viewModel.Results);
        Assert.Contains("cached", viewModel.CacheNote, StringComparison.OrdinalIgnoreCase);

        var window = new Window
        {
            Content = new BrowseView { DataContext = viewModel },
            Width = 1200,
            Height = 800,
        };
        window.Show();
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        Save(frame!, "browse-offline-cache");

        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>
    /// A crash report on disk must produce an analysis on the Logs tab: the report's own fields, the
    /// mod it names, and the frame that points at it.
    /// </summary>
    [AvaloniaFact]
    public async Task Instance_detail_analyses_a_crash_report()
    {
        var record = await _services.Instances
            .CreateAsync(
                new InstanceRecord
                {
                    Id = Guid.NewGuid(),
                    Name = "Crashy instance",
                    MinecraftVersion = "1.21.1",
                    Loader = LoaderKind.NeoForge,
                    LoaderVersion = "21.1.72",
                },
                CancellationToken.None)
            .ConfigureAwait(true);

        var crashDirectory = Path.Combine(_services.Paths.InstanceGameDirectory(record.Id), "crash-reports");
        Directory.CreateDirectory(crashDirectory);
        File.WriteAllText(
            Path.Combine(crashDirectory, "crash-2026-09-20_21.14.03-client.txt"),
            """
            ---- Minecraft Crash Report ----
            // Why did you do that?

            Time: 2026-09-20 21:14:03
            Description: Ticking block entity

            java.lang.IllegalStateException: Missing capability
            	at com.example.examplemod.machine.MachineTick.tick(MachineTick.java:88)

            -- System Details --
            Details:
            	Minecraft Version: 1.21.1
            	Suspected Mods: examplemod
            """);

        var shell = new MainWindowViewModel(_services);
        var viewModel = new InstanceDetailViewModel(record, _services, shell);
        await viewModel.RefreshCrashReportsAsync().ConfigureAwait(true);

        var report = Assert.Single(viewModel.CrashReports);
        Assert.Equal("Ticking block entity", report.Description);
        Assert.Contains("java.lang.IllegalStateException", viewModel.CrashAnalysisSummary, StringComparison.Ordinal);
        Assert.Contains("examplemod", viewModel.CrashAnalysisSummary, StringComparison.Ordinal);
        Assert.Contains("not a verdict", viewModel.CrashAnalysisSummary, StringComparison.Ordinal);

        var window = new Window
        {
            Content = new InstanceDetailView { DataContext = viewModel },
            Width = 1200,
            Height = 900,
        };
        window.Show();

        // Show the Logs tab so the crash list and analysis are actually rendered.
        var tabs = window.GetVisualDescendants().OfType<TabControl>().First();
        tabs.SelectedIndex = tabs.ItemCount - 1;

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        Save(frame!, "instance-logs");
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
