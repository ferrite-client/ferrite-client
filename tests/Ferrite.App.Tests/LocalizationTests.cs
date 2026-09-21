using System.Text.RegularExpressions;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Ferrite.App.Localization;
using Ferrite.Core.Platform;
using Ferrite.Core.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ferrite.App.Tests;

/// <summary>
/// The interface has to be complete in both languages. A missing key renders as nothing at all,
/// which is why this checks the tables against the views rather than trusting them.
/// </summary>
public sealed class LocalizationTests
{
    private static readonly Regex ResourceKeyPattern = new(
        @"\{DynamicResource\s+(?<key>L\.[A-Za-z0-9_.]+)\s*\}",
        RegexOptions.Compiled);

    private static readonly Regex CodeKeyPattern = new(
        @"Localizer\.(?:Get|Format)\(\s*""(?<key>L\.[A-Za-z0-9_.]+)""",
        RegexOptions.Compiled);

    [Fact]
    public void Both_languages_define_exactly_the_same_keys()
    {
        var english = Localizer.TableFor(Localizer.English);
        var polish = Localizer.TableFor(Localizer.Polish);

        var missingInPolish = english.Keys.Except(polish.Keys, StringComparer.Ordinal).ToList();
        var missingInEnglish = polish.Keys.Except(english.Keys, StringComparer.Ordinal).ToList();

        Assert.True(
            missingInPolish.Count == 0,
            $"Missing from Polish: {string.Join(", ", missingInPolish)}");
        Assert.True(
            missingInEnglish.Count == 0,
            $"Missing from English: {string.Join(", ", missingInEnglish)}");
    }

    [Fact]
    public void No_translation_is_empty_and_every_key_is_namespaced()
    {
        foreach (var (language, table) in Tables())
        {
            foreach (var (key, value) in table)
            {
                Assert.False(
                    string.IsNullOrWhiteSpace(value),
                    $"{language}: '{key}' is empty");
                Assert.StartsWith("L.", key, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>A placeholder that exists in one language but not the other would break formatting.</summary>
    [Fact]
    public void Both_languages_use_the_same_placeholders()
    {
        var english = Localizer.TableFor(Localizer.English);
        var polish = Localizer.TableFor(Localizer.Polish);

        foreach (var (key, value) in english)
        {
            var expected = Placeholders(value);
            var actual = Placeholders(polish[key]);
            Assert.True(
                expected.SetEquals(actual),
                $"'{key}' uses {string.Join(',', expected.Order())} in English but "
                + $"{string.Join(',', actual.Order())} in Polish");
        }
    }

    [Fact]
    public void Polish_is_not_a_copy_of_English()
    {
        var english = Localizer.TableFor(Localizer.English);
        var polish = Localizer.TableFor(Localizer.Polish);

        // A handful of keys are legitimately identical (Java, Ferrite, MOD LOADER), so this asserts
        // that the table was actually translated rather than duplicated.
        var translated = english.Count(pair =>
            !string.Equals(pair.Value, polish[pair.Key], StringComparison.Ordinal));
        Assert.True(
            translated > english.Count * 0.8,
            $"only {translated} of {english.Count} strings differ between the languages");
    }

    /// <summary>
    /// Every key a view asks for must exist. A typo here is invisible at runtime: the control simply
    /// renders nothing.
    /// </summary>
    [Fact]
    public void Every_key_used_in_a_view_exists_in_both_tables()
    {
        var english = Localizer.TableFor(Localizer.English);
        var polish = Localizer.TableFor(Localizer.Polish);
        var used = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var file in ViewFiles())
        {
            var text = File.ReadAllText(file);
            foreach (Match match in ResourceKeyPattern.Matches(text))
            {
                used.Add(match.Groups["key"].Value);
            }
        }

        Assert.NotEmpty(used);
        foreach (var key in used)
        {
            Assert.True(english.ContainsKey(key), $"'{key}' ({key}) is missing from English");
            Assert.True(polish.ContainsKey(key), $"'{key}' is missing from Polish");
        }
    }

    /// <summary>Keys looked up from view-model code must exist too, and none should be dead.</summary>
    [Fact]
    public void Keys_used_in_code_exist_and_no_key_is_unused()
    {
        var english = Localizer.TableFor(Localizer.English);
        var used = new HashSet<string>(StringComparer.Ordinal);

        var patterns = new[] { "*.cs", "*.axaml" };
        foreach (var file in patterns.SelectMany(pattern =>
                     Directory.EnumerateFiles(SourceDirectory(), pattern, SearchOption.AllDirectories)))
        {
            var text = File.ReadAllText(file);
            foreach (Match match in ResourceKeyPattern.Matches(text))
            {
                used.Add(match.Groups["key"].Value);
            }

            foreach (Match match in CodeKeyPattern.Matches(text))
            {
                used.Add(match.Groups["key"].Value);
            }
        }

        foreach (var key in used)
        {
            Assert.True(english.ContainsKey(key), $"'{key}' is used but not defined");
        }

        var unused = english.Keys.Except(used, StringComparer.Ordinal).ToList();
        Assert.True(unused.Count == 0, $"Defined but never used: {string.Join(", ", unused)}");
    }

    [Fact]
    public void An_unknown_key_falls_back_to_the_key_instead_of_throwing()
    {
        Assert.Equal("L.Not.A.Real.Key", Localizer.Get("L.Not.A.Real.Key"));
        Assert.Equal(string.Empty, Localizer.Get(string.Empty));
    }

    [Fact]
    public void An_unsupported_language_falls_back_to_english()
    {
        Assert.Equal(Localizer.English, Localizer.Normalize("de"));
        Assert.Equal(Localizer.Polish, Localizer.Normalize("pl-PL"));
        Assert.Equal(Localizer.English, Localizer.Normalize(null));
        Assert.Same(Localizer.TableFor("en"), Localizer.TableFor("de"));
    }

    private static IEnumerable<(string Language, IReadOnlyDictionary<string, string> Table)> Tables()
    {
        yield return (Localizer.English, Localizer.TableFor(Localizer.English));
        yield return (Localizer.Polish, Localizer.TableFor(Localizer.Polish));
    }

    private static HashSet<string> Placeholders(string value) =>
        Regex.Matches(value, @"\{(?<index>\d+)")
            .Select(match => match.Groups["index"].Value)
            .ToHashSet(StringComparer.Ordinal);

    private static IEnumerable<string> ViewFiles() =>
        Directory.EnumerateFiles(Path.Combine(SourceDirectory(), "Views"), "*.axaml");

    /// <summary>Walks up from the test output to the repository, where the views and models live.</summary>
    private static string SourceDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Ferrite.App");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the application source from the test output.");
    }

    /// <summary>Renders the shell in Polish and checks the labels really changed.</summary>
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void The_shell_renders_in_polish()
    {
        var root = Path.Combine(Path.GetTempPath(), "ferrite-ui-pl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
        using var services = new Ferrite.App.Services.AppServices(loggerFactory, AppPaths.ForRoot(root));

        Localizer.Apply(Avalonia.Application.Current, Localizer.Polish);
        try
        {
            var viewModel = new Ferrite.App.ViewModels.MainWindowViewModel(services);
            var shell = new Ferrite.App.Views.MainWindow { DataContext = viewModel };
            shell.Show();

            var texts = shell.GetVisualDescendants()
                .OfType<Avalonia.Controls.TextBlock>()
                .Select(block => block.Text)
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .ToList();

            Assert.Contains("Biblioteka", texts);
            Assert.Contains("Przeglądaj", texts);
            Assert.Contains("Launcher Minecrafta", texts);
            Assert.DoesNotContain("Library", texts);

            var directory = Environment.GetEnvironmentVariable("FERRITE_UI_SHOTS");
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
                shell.CaptureRenderedFrame()?.Save(
                    Path.Combine(directory, "shell-polish.png"),
                    new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            }
        }
        finally
        {
            Localizer.Apply(Avalonia.Application.Current, Localizer.English);
        }
    }
}
