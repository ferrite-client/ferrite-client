using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;
using Ferrite.App.Localization;
using Ferrite.App.Services;
using Ferrite.App.ViewModels;
using Ferrite.App.Views;
using Ferrite.Core.Platform;
using Microsoft.Extensions.Logging;

namespace Ferrite.App.Tests;

/// <summary>
/// The accessibility requirements the brief names: contrast that holds up, controls a screen reader
/// can name, and a focus state a keyboard user can see. Each one is checked against the real palette,
/// the real views, and a real rendered frame rather than against an intention.
/// </summary>
public sealed class AccessibilityTests : IDisposable
{
    private readonly string _root;
    private readonly AppServices _services;

    public AccessibilityTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ferrite-a11y-" + Guid.NewGuid().ToString("N"));
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

    private static string SourceDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Ferrite.slnx")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(directory!.FullName, "src", "Ferrite.App");
    }

    /// <summary>Every colour the palette defines, per theme.</summary>
    private static Dictionary<string, Dictionary<string, string>> Palette()
    {
        var text = File.ReadAllText(Path.Combine(SourceDirectory(), "Styles", "Palette.axaml"));
        var themes = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        var themePattern = new Regex(
            """<ResourceDictionary x:Key="(?<theme>Dark|Light)">(?<body>.*?)</ResourceDictionary>""",
            RegexOptions.Singleline);
        var colourPattern = new Regex(
            "x:Key=\"(?<key>Ferrite[A-Za-z]+)\" Color=\"(?<colour>#[0-9A-Fa-f]{6})\"");

        foreach (Match theme in themePattern.Matches(text))
        {
            var colours = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Match colour in colourPattern.Matches(theme.Groups["body"].Value))
            {
                colours[colour.Groups["key"].Value] = colour.Groups["colour"].Value;
            }

            themes[theme.Groups["theme"].Value] = colours;
        }

        return themes;
    }

    private static double RelativeLuminance(string hex)
    {
        var value = int.Parse(hex[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var channels = new[] { (value >> 16) & 0xFF, (value >> 8) & 0xFF, value & 0xFF };
        var linear = channels.Select(channel =>
        {
            var srgb = channel / 255.0;
            return srgb <= 0.03928 ? srgb / 12.92 : Math.Pow((srgb + 0.055) / 1.055, 2.4);
        }).ToArray();

        return (0.2126 * linear[0]) + (0.7152 * linear[1]) + (0.0722 * linear[2]);
    }

    private static double Contrast(string first, string second)
    {
        var a = RelativeLuminance(first);
        var b = RelativeLuminance(second);
        var lighter = Math.Max(a, b);
        var darker = Math.Min(a, b);
        return (lighter + 0.05) / (darker + 0.05);
    }

    [Fact]
    public void Every_text_colour_clears_wcag_aa_on_both_surfaces()
    {
        var themes = Palette();
        Assert.Equal(2, themes.Count);

        var textTokens = new[] { "FerriteText", "FerriteTextMuted", "FerriteTextFaint" };
        var stateTokens = new[] { "FerriteDanger", "FerriteSuccess", "FerriteWarning" };

        foreach (var (theme, colours) in themes)
        {
            foreach (var surface in new[] { "FerriteSurface", "FerriteSurfaceRaised", "FerriteSurfaceSunken" })
            {
                foreach (var token in textTokens.Concat(stateTokens))
                {
                    var ratio = Contrast(colours[token], colours[surface]);
                    Assert.True(
                        ratio >= 4.5,
                        $"{theme}: {token} on {surface} is {ratio:F2}:1, below the 4.5:1 AA threshold");
                }
            }

            // Accent buttons carry their own text colour and have to be readable too.
            var accentRatio = Contrast(colours["FerriteAccentText"], colours["FerriteAccent"]);
            Assert.True(
                accentRatio >= 4.5,
                $"{theme}: accent button text is {accentRatio:F2}:1 against the accent fill");

            // The filled destructive button in the confirmation dialog is the other filled control.
            var dangerRatio = Contrast(colours["FerriteDangerText"], colours["FerriteDanger"]);
            Assert.True(
                dangerRatio >= 4.5,
                $"{theme}: danger button text is {dangerRatio:F2}:1 against the danger fill");
        }
    }

    /// <summary>
    /// A control a screen reader cannot name is a control a blind user cannot use. Every button has to
    /// carry text (or a name), and every input has to carry a name or a placeholder.
    /// </summary>
    [Fact]
    public void Every_interactive_control_is_named_in_every_view()
    {
        var offenders = new List<string>();
        var views = Directory.EnumerateFiles(
            Path.Combine(SourceDirectory(), "Views"),
            "*.axaml",
            SearchOption.AllDirectories);

        foreach (var view in views)
        {
            var text = File.ReadAllText(view);
            // The tag has to be a whole element, not a property such as <ComboBox.ItemTemplate>.
            foreach (Match match in Regex.Matches(
                         text,
                         @"<(?<tag>Button|TextBox|ComboBox|NumericUpDown|CheckBox)(?=[\s/>])(?<attrs>[^>]*)>"))
            {
                var tag = match.Groups["tag"].Value;
                var attributes = match.Groups["attrs"].Value;
                var named = attributes.Contains("AutomationProperties.Name", StringComparison.Ordinal)
                    || attributes.Contains("PlaceholderText", StringComparison.Ordinal)
                    || attributes.Contains("ToolTip.Tip", StringComparison.Ordinal)
                    || attributes.Contains("Content=", StringComparison.Ordinal)
                    || attributes.Contains("ItemsSource=", StringComparison.Ordinal);
                if (!named)
                {
                    var line = text[..match.Index].Count(character => character == '\n') + 1;
                    offenders.Add($"{Path.GetFileName(view)}:{line} <{tag}>");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "controls without a name or label: " + string.Join(", ", offenders));
    }

    /// <summary>
    /// Focus has to be visible, so focusing a control must change what is drawn.
    /// </summary>
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void Keyboard_focus_changes_what_is_drawn()
    {
        var shell = new MainWindowViewModel(_services);
        var viewModel = new SettingsViewModel(_services, shell);
        var window = new Window
        {
            Content = new SettingsView { DataContext = viewModel },
            Width = 1000,
            Height = 1200,
        };
        window.Show();

        var before = Snapshot(window);

        // Focus is reached the way a keyboard user reaches it, so the focus-visible styles apply.
        window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
        var after = Snapshot(window);

        var focused = window.GetVisualDescendants().OfType<Control>().FirstOrDefault(control => control.IsFocused);
        Assert.NotNull(focused);
        Assert.False(
            before.SequenceEqual(after),
            $"tabbing to {focused!.GetType().Name} did not change the rendered frame, so focus is invisible");
    }

    private static byte[] Snapshot(Window window)
    {
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        using var stream = new MemoryStream();
        frame!.Save(stream, PngBitmapEncoderOptions.Default);
        return stream.ToArray();
    }

    /// <summary>
    /// The primary workflow has to be completable without a mouse. A keyboard-only user reaches the
    /// shell's own action by tabbing and activates it with the keyboard, and the quick-action palette
    /// has a shortcut rather than relying on a button someone has to find.
    /// </summary>
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void The_primary_action_is_reachable_and_activatable_from_the_keyboard()
    {
        var shell = new MainWindowViewModel(_services);
        var window = new MainWindow { DataContext = shell };
        window.Show();

        var label = Localizer.Get("L.Library.NewInstance");
        var reached = false;
        for (var press = 0; press < 80 && !reached; press++)
        {
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            reached = window.GetVisualDescendants()
                .OfType<Button>()
                .Any(button => button.IsFocused && button.Content as string == label);
        }

        Assert.True(reached, $"tabbing never reached \"{label}\"");

        // Activate it the way a keyboard user does, and confirm it ran.
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Assert.True(shell.Library.IsCreating, "the focused action did not run on Space");

        // The quick-action palette is reachable by shortcut, not only by finding its button.
        window.KeyPress(Key.K, RawInputModifiers.Control, PhysicalKey.K, "k");
        Assert.True(shell.IsQuickActionsOpen, "Ctrl+K did not open the quick actions");
    }
}
