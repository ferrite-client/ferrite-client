using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Ferrite.App.Localization;
using Ferrite.App.Services;
using Ferrite.App.ViewModels;
using Ferrite.App.Views;
using Ferrite.Core.Download;
using Ferrite.Core.Minecraft;
using Ferrite.Core.Platform;
using Ferrite.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Ferrite.App.Tests;

/// <summary>
/// The states the brief asks to be designed rather than left to chance: the launch sequence, a list at
/// real modded scale, and text that is longer or stranger than the layout expects.
/// </summary>
public sealed class InterfaceStatesTests : IDisposable
{
    private readonly string _root;
    private readonly AppServices _services;

    public InterfaceStatesTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ferrite-states-" + Guid.NewGuid().ToString("N"));
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

    private static IReadOnlyList<string> Texts(Control root) =>
        root.GetVisualDescendants()
            .OfType<TextBlock>()
            .Select(block => block.Text)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => text!)
            .ToList();

    /// <summary>
    /// The instance page's hero action with the given name. There is one Play and one Stop on the
    /// page, and which of them is visible is the state the hero is reporting.
    /// </summary>
    private static Button? HeroAction(Control root, string name) => root
        .GetVisualDescendants()
        .OfType<Button>()
        .FirstOrDefault(button => Avalonia.Automation.AutomationProperties.GetName(button) == name);

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

    /// <summary>
    /// Ready, then launching with real progress, then running, then back to ready. The hero is the one
    /// place the launcher's state lives, so it has to carry all four without a dead Play button.
    /// </summary>
    [AvaloniaFact]
    public void The_instance_hero_reports_every_launch_state()
    {
        var record = new InstanceRecord
        {
            Id = Guid.NewGuid(),
            Name = "State instance",
            MinecraftVersion = "1.21.1",
            Loader = LoaderKind.Fabric,
            LoaderVersion = "0.16.9",
        };
        var shell = new MainWindowViewModel(_services);
        var detail = new InstanceDetailViewModel(record, _services, shell);
        var window = new Window
        {
            Content = new InstanceDetailView { DataContext = detail },
            Width = 1200,
            Height = 800,
        };
        window.Show();

        // Ready: Play is offered and nothing pretends to be happening.
        var ready = Texts(window);
        Assert.Contains("Ready to play", ready);
        Assert.True(HeroAction(window, "Play")!.IsVisible);
        Assert.False(HeroAction(window, "Stop")!.IsVisible);

        // Launching: the hero says so and shows the launcher's own operation and measured progress.
        // These are the same property transitions the Play command makes.
        detail.IsLaunching = true;
        shell.BeginActivity("Preparing State instance...", indeterminate: false);
        shell.ReportActivity(new InstallProgress
        {
            Stage = InstallStage.Downloading,
            Message = "client.jar",
            Download = new DownloadProgress
            {
                TotalFiles = 40,
                CompletedFiles = 12,
                FailedFiles = 0,
                SkippedFiles = 0,
                TotalBytes = 40_000_000,
                CompletedBytes = 12_000_000,
                BytesPerSecond = 2_400_000,
                CurrentItem = "client.jar",
            },
        });
        window.CaptureRenderedFrame();
        var launching = window.CaptureRenderedFrame();
        Assert.NotNull(launching);

        var launchingTexts = Texts(window);
        Assert.Contains("Starting Minecraft", launchingTexts);
        // Play is not merely absent from the text: the control itself is gone, so there is no dead
        // button sitting there while the launcher works.
        Assert.False(HeroAction(window, "Play")!.IsVisible);
        Assert.False(HeroAction(window, "Stop")!.IsVisible);
        Save(launching!, "instance-launching");

        // Running: Stop replaces Play, and the state line names the instance.
        detail.IsLaunching = false;
        detail.IsRunning = true;
        window.CaptureRenderedFrame();
        var running = window.CaptureRenderedFrame();
        Assert.NotNull(running);

        var runningTexts = Texts(window);
        Assert.True(HeroAction(window, "Stop")!.IsVisible);
        Assert.False(HeroAction(window, "Play")!.IsVisible);
        Assert.Contains(runningTexts, text => text.Contains("State instance is running", StringComparison.Ordinal));
        Save(running!, "instance-running");

        // Back to ready, with no progress left behind.
        shell.EndActivity();
        detail.IsRunning = false;
        window.CaptureRenderedFrame();
        var settled = Texts(window);
        Assert.Contains("Ready to play", settled);
        Assert.True(HeroAction(window, "Play")!.IsVisible);
    }

    /// <summary>
    /// A real modded pack. The list has to hold the whole collection in the model while rendering only
    /// the rows on screen, or a pack of a few hundred mods becomes unusable.
    /// </summary>
    [AvaloniaFact]
    public async Task A_large_mod_list_is_virtualised()
    {
        const int modCount = 250;

        var record = await _services.Instances
            .CreateAsync(
                new InstanceRecord
                {
                    Id = Guid.NewGuid(),
                    Name = "Big pack",
                    MinecraftVersion = "1.21.1",
                    Loader = LoaderKind.Fabric,
                    LoaderVersion = "0.16.9",
                },
                CancellationToken.None)
            .ConfigureAwait(true);

        var modsDirectory = Path.Combine(_services.Paths.InstanceGameDirectory(record.Id), "mods");
        Directory.CreateDirectory(modsDirectory);
        for (var index = 0; index < modCount; index++)
        {
            ModArchiveFixture.WriteFabric(
                Path.Combine(modsDirectory, $"mod-{index:000}.jar"),
                $"mod-{index:000}",
                $"Mod {index:000}",
                "1.0.0",
                64,
                "fabric-api");
        }

        var shell = new MainWindowViewModel(_services);
        var detail = new InstanceDetailViewModel(record, _services, shell);
        await detail.RefreshModsAsync().ConfigureAwait(true);

        Assert.Equal(modCount, detail.Mods.Count);
        Assert.Contains(
            $"Showing {modCount} of {modCount} mods",
            detail.ModFilterNote,
            StringComparison.Ordinal);

        var window = new Window
        {
            Content = new InstanceDetailView { DataContext = detail },
            Width = 1200,
            Height = 900,
        };
        window.Show();
        window.GetVisualDescendants().OfType<TabControl>().First().SelectedIndex = 1;
        window.CaptureRenderedFrame();

        // The model holds every mod; the visual tree holds the rows the viewport fits.
        var realised = window.GetVisualDescendants()
            .OfType<ListBoxItem>()
            .Count(item => item.DataContext is ModItemViewModel);
        Assert.InRange(realised, 1, modCount / 4);

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        Save(frame!, "instance-mods-large");
    }

    /// <summary>
    /// Long, wide, and unusual text. A card keeps its size and trims its labels, and the full value is
    /// still reachable from the tooltip, so nothing overflows and nothing is silently lost.
    /// </summary>
    [AvaloniaFact]
    public async Task Pathological_text_does_not_change_the_card_layout()
    {
        var longName = new string('W', 120) + " — 非常に長い名前 🎮 " + new string('ف', 40);

        var record = await _services.Instances
            .CreateAsync(
                new InstanceRecord
                {
                    Id = Guid.NewGuid(),
                    Name = longName,
                    MinecraftVersion = "1.21.1",
                    Loader = LoaderKind.NeoForge,
                    LoaderVersion = "21.1.251-a-very-long-loader-version-string-0000",
                    Modpack = new ModpackIdentity
                    {
                        Provider = ModpackProvider.Modrinth,
                        Name = new string('M', 80),
                        VersionName = "1.0.0",
                    },
                },
                CancellationToken.None)
            .ConfigureAwait(true);
        Directory.CreateDirectory(_services.Paths.InstanceGameDirectory(record.Id));

        var shell = new MainWindowViewModel(_services);
        await shell.InitializeAsync().ConfigureAwait(true);

        var window = new MainWindow { DataContext = shell, Width = 1360, Height = 860 };
        window.Show();
        window.CaptureRenderedFrame();

        var card = window.GetVisualDescendants()
            .OfType<Border>()
            .Single(border => border.Classes.Contains("instance-card"));

        // The card is a fixed size whatever it is asked to display.
        Assert.Equal(238, card.Bounds.Width);
        Assert.Equal(286, card.Bounds.Height);

        // The labels trim rather than wrap, and each carries the whole value in its tooltip.
        var labels = card.GetVisualDescendants().OfType<TextBlock>().ToList();
        var name = labels.First(block => block.Text == longName);
        Assert.Equal(TextTrimming.CharacterEllipsis, name.TextTrimming);
        Assert.Equal(TextWrapping.NoWrap, name.TextWrapping);
        Assert.Equal(longName, ToolTip.GetTip(name));

        // The badge for the modpack identity is bounded too, so one long pack name cannot widen a card.
        var packText = new string('M', 80) + " 1.0.0";
        var packLabel = labels.First(block => block.Text == packText);
        Assert.NotNull(ToolTip.GetTip(packLabel));
        Assert.True(packLabel.Bounds.Width <= 200, $"the pack badge measured {packLabel.Bounds.Width}");

        // And the card itself never exceeds the space the grid computed for it.
        Assert.True(card.Bounds.Right <= window.Bounds.Width, "the card ran off the window");

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        Save(frame!, "library-pathological-text");
    }
}
