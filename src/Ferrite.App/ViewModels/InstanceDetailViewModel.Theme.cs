using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ferrite.App.Localization;
using Ferrite.Core.Storage;

namespace Ferrite.App.ViewModels;

/// <summary>
/// The instance's own appearance: a background image and an accent colour. The image is copied into
/// the instance directory rather than referenced where it was picked, so moving or deleting the
/// original cannot break the instance.
/// </summary>
public sealed partial class InstanceDetailViewModel
{
    [ObservableProperty]
    private string _themeAccent = string.Empty;

    [ObservableProperty]
    private string? _themeError;

    [ObservableProperty]
    private IImage? _themeBackgroundImage;

    [ObservableProperty]
    private IBrush? _themeAccentBrush;

    public bool HasThemeError => !string.IsNullOrEmpty(ThemeError);

    public bool HasThemeBackground => ThemeBackgroundImage is not null;

    public bool HasThemeAccent => ThemeAccentBrush is not null;

    /// <summary>True when anything about the theme is set, so the banner only exists when it should.</summary>
    public bool HasTheme => HasThemeBackground || HasThemeAccent;

    partial void OnThemeErrorChanged(string? value) => OnPropertyChanged(nameof(HasThemeError));

    /// <summary>Reads the stored theme into the page: the accent text, its brush, and the image.</summary>
    private void LoadTheme()
    {
        ThemeAccent = Record.ThemeAccent ?? string.Empty;
        ApplyAccentBrush(Record.ThemeAccent);
        LoadThemeBackground();
    }

    private void ApplyAccentBrush(string? accent)
    {
        ThemeAccentBrush = accent is { Length: 7 } && Color.TryParse(accent, out var colour)
            ? new SolidColorBrush(colour)
            : null;
        OnPropertyChanged(nameof(HasThemeAccent));
        OnPropertyChanged(nameof(HasTheme));
    }

    private void LoadThemeBackground()
    {
        ThemeBackgroundImage = null;
        if (Record.ThemeBackgroundPath is { Length: > 0 } relative)
        {
            var path = Path.Combine(_services.Paths.InstanceDirectory(Record.Id), relative);
            if (File.Exists(path))
            {
                try
                {
                    // The bitmap is cached by path, so a replaced file needs a fresh read.
                    using var stream = File.OpenRead(path);
                    ThemeBackgroundImage = new Bitmap(stream);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                                  or ArgumentException)
                {
                    ThemeError = exception.Message;
                }
            }
        }

        OnPropertyChanged(nameof(HasThemeBackground));
        OnPropertyChanged(nameof(HasTheme));
    }

    /// <summary>
    /// Copies a picked image into the instance and points the theme at it. Returns false when the
    /// file could not be read or copied, with the reason on the page.
    /// </summary>
    public async Task<bool> SetThemeBackgroundAsync(string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            return false;
        }

        try
        {
            var relative = InstanceTheme.BackgroundRelativePath(Path.GetExtension(sourcePath));
            var target = Path.Combine(_services.Paths.InstanceDirectory(Record.Id), relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await Task.Run(() => File.Copy(sourcePath, target, overwrite: true), CancellationToken.None)
                .ConfigureAwait(true);

            // A previously chosen image with a different extension would otherwise linger next to the
            // new one and be reintroduced by a later read.
            RemoveOtherBackgrounds(relative);

            Record.ThemeBackgroundPath = relative;
            await _services.Instances.SaveAsync(Record, CancellationToken.None).ConfigureAwait(true);
            ThemeError = null;
            LoadThemeBackground();
            StatusNote = Localizer.Get("L.Instance.ThemeBackgroundSet");
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ThemeError = exception.Message;
            return false;
        }
    }

    private void RemoveOtherBackgrounds(string keepRelative)
    {
        var themeDirectory = Path.Combine(_services.Paths.InstanceDirectory(Record.Id), "theme");
        if (!Directory.Exists(themeDirectory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(themeDirectory, "background.*"))
        {
            var relative = Path.Combine("theme", Path.GetFileName(file)).Replace('\\', '/');
            if (!string.Equals(relative, keepRelative, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    File.Delete(file);
                }
                catch (IOException)
                {
                    // Leaving an extra file behind is harmless; the stored path names the real one.
                }
            }
        }
    }

    /// <summary>Clears the background image and removes the copy from the instance.</summary>
    [RelayCommand]
    private async Task ClearThemeBackgroundAsync()
    {
        try
        {
            var themeDirectory = Path.Combine(_services.Paths.InstanceDirectory(Record.Id), "theme");
            if (Directory.Exists(themeDirectory))
            {
                foreach (var file in Directory.EnumerateFiles(themeDirectory, "background.*"))
                {
                    File.Delete(file);
                }
            }

            Record.ThemeBackgroundPath = null;
            await _services.Instances.SaveAsync(Record, CancellationToken.None).ConfigureAwait(true);
            ThemeError = null;
            LoadThemeBackground();
            StatusNote = Localizer.Get("L.Instance.ThemeBackgroundCleared");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ThemeError = exception.Message;
        }
    }

    /// <summary>
    /// Validates the accent box and applies it. Returns false when the text is not a colour, leaving
    /// the stored value alone so a typo cannot silently clear a theme the user already set.
    /// </summary>
    internal bool TryApplyThemeAccent()
    {
        if (!InstanceTheme.TryNormalizeAccent(ThemeAccent, out var accent))
        {
            ThemeError = Localizer.Get("L.Instance.ThemeAccentInvalid");
            return false;
        }

        ThemeError = null;
        Record.ThemeAccent = accent;
        ThemeAccent = accent ?? string.Empty;
        ApplyAccentBrush(accent);
        return true;
    }
}
