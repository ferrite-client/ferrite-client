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
using Ferrite.App.Localization;
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

        // The real application applies the language during startup; a headless test must do it.
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

    /// <summary>The Servers tab shows saved servers and the worlds found on the local network.</summary>
    [AvaloniaFact]
    public void Instance_servers_tab_renders_lan_discovery()
    {
        var record = _services.Instances
            .CreateAsync(
                new InstanceRecord
                {
                    Id = Guid.NewGuid(),
                    Name = "LAN instance",
                    MinecraftVersion = "1.21.1",
                    Loader = LoaderKind.Vanilla,
                },
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        var shell = new MainWindowViewModel(_services);
        var viewModel = new InstanceDetailViewModel(record, _services, shell);
        viewModel.LanWorlds.Add(new LanWorldItemViewModel(
            new Ferrite.Core.Game.LanWorld(
                "192.168.1.20",
                51234,
                "Steve's world",
                DateTimeOffset.UtcNow),
            _ => Task.CompletedTask));
        viewModel.LanStatus = "Searching the local network.";

        var window = new Window
        {
            Content = new InstanceDetailView { DataContext = viewModel },
            Width = 1200,
            Height = 900,
        };
        window.Show();

        var tabs = window.GetVisualDescendants().OfType<TabControl>().First();
        tabs.SelectedIndex = 4;

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);

        var texts = Texts(window);
        Assert.Contains("Steve's world", texts);
        Assert.Contains("192.168.1.20:51234", texts);
        Assert.Contains("Find LAN worlds", texts);
        Assert.Contains("SAVED SERVERS", texts);

        Save(frame!, "instance-servers-lan");
    }

    /// <summary>The Files tab reports what each resource pack declares about itself.</summary>
    [AvaloniaFact]
    public void Instance_files_tab_reports_pack_formats()
    {
        var record = _services.Instances
            .CreateAsync(
                new InstanceRecord
                {
                    Id = Guid.NewGuid(),
                    Name = "Pack instance",
                    MinecraftVersion = "1.21.1",
                    Loader = LoaderKind.Vanilla,
                },
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        var shell = new MainWindowViewModel(_services);
        var viewModel = new InstanceDetailViewModel(record, _services, shell);
        viewModel.ResourcePacks.Add(new InstanceContentItemViewModel(
            new ContentFileEntry(
                @"C:\packs\old-pack.zip",
                "old-pack.zip",
                1024,
                true,
                DateTimeOffset.UtcNow,
                "format 15",
                "does not list format 34, which this version uses",
                IsPackMismatch: true),
            viewModel.ContentBackupDirectory,
            () => { }));

        var window = new Window
        {
            Content = new InstanceDetailView { DataContext = viewModel },
            Width = 1200,
            Height = 900,
        };
        window.Show();

        var tabs = window.GetVisualDescendants().OfType<TabControl>().First();
        tabs.SelectedIndex = 5;

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);

        var texts = Texts(window);
        Assert.Contains("old-pack.zip", texts);
        Assert.Contains("format 15", texts);
        Assert.Contains(texts, text => text.Contains("does not list format 34", StringComparison.Ordinal));

        Save(frame!, "instance-files-packs");
    }

    [AvaloniaFact]
    public void Shell_renders_with_navigation_and_status_bar()
    {
        var window = ShowShell(out _);

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);

        var texts = Texts(window);
        Assert.Contains("Ferrite", texts);
        Assert.Contains("Library", texts);
        Assert.Contains("Discover", texts);
        Assert.Contains("Downloads", texts);
        Assert.Contains("Settings", texts);
        Assert.Contains("No account", texts);

        Save(frame!, "shell");
    }

    [AvaloniaFact]
    public void Every_page_renders_without_error()
    {
        var window = ShowShell(out var viewModel);

        foreach (var page in new[] { "Library", "Discover", "Downloads", "Accounts", "Settings" })
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
    /// The Files tab has to render the packs, the per-world datapacks, and the screenshot gallery
    /// with real files behind them, including the actions on each entry.
    /// </summary>
    [AvaloniaFact]
    public async Task Instance_files_tab_renders_packs_datapacks_and_screenshots()
    {
        var record = _services.Instances
            .CreateAsync(
                new InstanceRecord
                {
                    Id = Guid.NewGuid(),
                    Name = "Content tab instance",
                    MinecraftVersion = "1.21.1",
                },
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        var gameDirectory = _services.Paths.InstanceGameDirectory(record.Id);
        TestAssets.WritePackZip(
            Path.Combine(gameDirectory, "resourcepacks", "faithful.zip"),
            34,
            "faithful");
        TestAssets.WritePackZip(
            Path.Combine(gameDirectory, "shaderpacks", "complementary.zip"),
            34,
            "complementary");
        TestAssets.WritePng(Path.Combine(gameDirectory, "screenshots", "2026-01-01_12.00.00.png"), 320, 180);

        var shell = new MainWindowViewModel(_services);
        var viewModel = new InstanceDetailViewModel(record, _services, shell);
        await viewModel.RefreshContentAsync();

        var window = new Window
        {
            Content = new InstanceDetailView { DataContext = viewModel },
            Width = 1200,
            Height = 900,
        };
        window.Show();

        var tabs = window.GetVisualDescendants().OfType<TabControl>().First();
        tabs.SelectedIndex = 5;

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);

        var texts = Texts(window);
        Assert.Contains("faithful.zip", texts);
        Assert.Contains("complementary.zip", texts);
        Assert.Contains("2026-01-01_12.00.00.png", texts);
        Assert.Contains("RESOURCE PACKS", texts);
        Assert.Contains("DATAPACKS", texts);
        Assert.Contains("SCREENSHOTS", texts);

        Save(frame!, "instance-files-content");
    }

    /// <summary>The bundled CurseForge configuration keeps search available without Settings setup.</summary>
    [AvaloniaFact]
    public void Browse_page_keeps_curseforge_search_available()
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
        Assert.Contains(texts, text => text.Contains("Search CurseForge", StringComparison.Ordinal));

        Save(frame!, "browse-curseforge");
    }

    [AvaloniaFact]
    public void Settings_and_accounts_do_not_render_developer_credential_controls()
    {
        var shell = new MainWindowViewModel(_services);
        var settingsWindow = new Window
        {
            Content = new SettingsView { DataContext = new SettingsViewModel(_services, shell) },
            Width = 1000,
            Height = 800,
        };
        settingsWindow.Show();

        var accountsWindow = new Window
        {
            Content = new AccountsView { DataContext = new AccountsViewModel(_services, shell) },
            Width = 1000,
            Height = 800,
        };
        accountsWindow.Show();

        var text = string.Join("\n", Texts(settingsWindow).Concat(Texts(accountsWindow)));
        Assert.DoesNotContain("MICROSOFT CLIENT ID", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CURSEFORGE API KEY", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Save client id", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Paste a key", text, StringComparison.OrdinalIgnoreCase);
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

        // Diagnostics now lives in its own settings category rather than one long scrolling page.
        viewModel.SelectedCategory = viewModel.Categories.Single(category => category.Value == "diagnostics");

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

        viewModel.SelectedCategory = viewModel.Categories.Single(category => category.Value == "updates");

        // The category's controls enter the visual tree on the next layout pass.
        window.CaptureRenderedFrame();

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
        tabs.SelectedIndex = tabs.Items
            .Cast<TabItem>()
            .Select((item, index) => (item, index))
            .First(entry => string.Equals(
                entry.item.Header?.ToString(),
                Localizer.Get("L.Instance.TabLogs"),
                StringComparison.Ordinal))
            .index;
        window.CaptureRenderedFrame();

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        Save(frame!, "instance-logs");
    }

    /// <summary>
    /// The mods tab has to render the search, loader filter, and order controls with real mods behind
    /// them: a pack with hundreds of files is unusable as one unfiltered list.
    /// </summary>
    [AvaloniaFact]
    public async Task Instance_mods_tab_renders_the_search_and_filter()
    {
        var record = _services.Instances
            .CreateAsync(
                new InstanceRecord
                {
                    Id = Guid.NewGuid(),
                    Name = "Mods tab instance",
                    MinecraftVersion = "1.21.1",
                    Loader = LoaderKind.Fabric,
                    LoaderVersion = "0.19.5",
                },
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        var modsDirectory = Path.Combine(_services.Paths.InstanceGameDirectory(record.Id), "mods");
        Directory.CreateDirectory(modsDirectory);
        ModArchiveFixture.WriteFabric(
            Path.Combine(modsDirectory, "sodium.jar"), "sodium", "Sodium", "0.6.0", 96, "fabric-api");
        ModArchiveFixture.WriteFabric(
            Path.Combine(modsDirectory, "lithium.jar"), "lithium", "Lithium", "0.14.0", 48, "fabric-api");

        var shell = new MainWindowViewModel(_services);
        var viewModel = new InstanceDetailViewModel(record, _services, shell);
        await viewModel.RefreshModsAsync();
        viewModel.ModQuery = "sodium";

        var window = new Window
        {
            Content = new InstanceDetailView { DataContext = viewModel },
            Width = 1200,
            Height = 900,
        };
        window.Show();

        window.GetVisualDescendants().OfType<TabControl>().First().SelectedIndex = 1;

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);

        var texts = Texts(window);
        Assert.Contains("Sodium", texts);
        Assert.DoesNotContain("Lithium", texts);
        Assert.Contains(texts, text => text.Contains("Showing 1 of 2 mods", StringComparison.Ordinal));
        Assert.Contains(texts, text => text.Contains("All loaders", StringComparison.Ordinal));

        Save(frame!, "instance-mods-filter");
    }

    private MainWindow ShowShell(out MainWindowViewModel viewModel)
    {
        viewModel = new MainWindowViewModel(_services);
        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        return window;
    }

    /// <summary>
    /// The library grid has to render real cards: artwork for a themed instance, a fallback tile for
    /// an un-themed one, a modpack badge, and a name long enough to need trimming.
    /// </summary>
    [AvaloniaFact]
    public async Task Library_renders_the_instance_grid_and_list()
    {
        var themed = await _services.Instances
            .CreateAsync(
                new InstanceRecord
                {
                    Id = Guid.NewGuid(),
                    Name = "All the Mods 10 — Community Edition (extremely long modpack name)",
                    MinecraftVersion = "1.21.1",
                    Loader = LoaderKind.NeoForge,
                    LoaderVersion = "21.1.251",
                    ThemeAccent = "#5E9FD8",
                    LastLaunchedAt = DateTimeOffset.UtcNow.AddHours(-3),
                    Modpack = new ModpackIdentity
                    {
                        Provider = ModpackProvider.Modrinth,
                        Name = "All the Mods 10",
                        VersionName = "6.1.0",
                    },
                },
                CancellationToken.None)
            .ConfigureAwait(true);

        var themeDirectory = Path.Combine(_services.Paths.InstanceDirectory(themed.Id), "theme");
        TestAssets.WriteGradientPng(
            Path.Combine(themeDirectory, "background.png"),
            640,
            360,
            0x00509FD8,
            0x002B1A63);
        themed.ThemeBackgroundPath = "theme/background.png";
        await _services.Instances.SaveAsync(themed, CancellationToken.None).ConfigureAwait(true);

        foreach (var (name, version, loader, loaderVersion, days) in new[]
                 {
                     ("Survival 1.20", "1.20.4", LoaderKind.Vanilla, (string?)null, 1),
                     ("Create: Above and Beyond", "1.18.2", LoaderKind.Forge, "40.2.0", 5),
                     ("Fabric testing", "1.21.4", LoaderKind.Fabric, "0.16.9", 12),
                 })
        {
            var record = await _services.Instances
                .CreateAsync(
                    new InstanceRecord
                    {
                        Id = Guid.NewGuid(),
                        Name = name,
                        MinecraftVersion = version,
                        Loader = loader,
                        LoaderVersion = loaderVersion,
                        LastLaunchedAt = DateTimeOffset.UtcNow.AddDays(-days),
                    },
                    CancellationToken.None)
                .ConfigureAwait(true);
            Directory.CreateDirectory(_services.Paths.InstanceGameDirectory(record.Id));
        }

        var shell = new MainWindowViewModel(_services);
        await shell.InitializeAsync().ConfigureAwait(true);

        var window = new MainWindow { DataContext = shell, Width = 1360, Height = 860 };
        window.Show();

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);

        var texts = Texts(window);
        Assert.Contains("Survival 1.20", texts);
        Assert.Contains("Fabric testing", texts);

        Save(frame!, "library-grid");

        // The grid reflows to the window rather than assuming a column count.
        window.Width = 1024;
        window.Height = 680;
        window.CaptureRenderedFrame();
        var narrow = window.CaptureRenderedFrame();
        Assert.NotNull(narrow);
        Save(narrow!, "library-narrow");

        // The same screen in the light theme, so both themes are looked at and not only asserted.
        window.Width = 1360;
        window.Height = 860;
        Ferrite.App.App.ApplyTheme(ThemeVariant.Light);
        try
        {
            window.CaptureRenderedFrame();
            var light = window.CaptureRenderedFrame();
            Assert.NotNull(light);
            Save(light!, "library-light");
        }
        finally
        {
            Ferrite.App.App.ApplyTheme(ThemeVariant.Dark);
            window.CaptureRenderedFrame();
        }

        shell.Library.SelectedView = shell.Library.ViewChoices.Single(choice => choice.Value == "list");
        window.CaptureRenderedFrame();
        var listFrame = window.CaptureRenderedFrame();
        Assert.NotNull(listFrame);
        Save(listFrame!, "library-list");
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
