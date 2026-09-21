using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Ferrite.App.Services;
using Ferrite.App.Localization;
using Ferrite.App.ViewModels;
using Ferrite.App.Views;
using Ferrite.Core.Platform;
using Ferrite.Core.Storage;
using Microsoft.Extensions.Logging;
using StorageThemeVariant = Ferrite.Core.Storage.ThemeVariant;

namespace Ferrite.App;

public partial class App : Application
{
    private AppServices? _services;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var paths = AppPaths.CreateDefault();
            var loggerFactory = AppLogging.Create(paths);
            _services = new AppServices(loggerFactory, paths);
            _services.Logger<App>().LogInformation(
                "Ferrite starting; version {Version}, data root {Root}, secret protection {Protection}",
                typeof(App).Assembly.GetName().Version?.ToString() ?? "unknown",
                paths.Root,
                _services.Accounts.IsSecretStorageDegraded ? "degraded" : "os-backed");

            var settings = _services.Settings.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
            ApplyTheme(settings.Theme);
            // Interface text must be in place before the first view is constructed.
            Localizer.Apply(this, settings.Language);
            _services.Logger<App>().LogInformation(
                "Settings loaded; theme {Theme}, language {Language}",
                settings.Theme,
                Localizer.Language);

            var viewModel = new MainWindowViewModel(_services);
            var window = new MainWindow { DataContext = viewModel };
            desktop.MainWindow = window;
            desktop.Exit += (_, _) => _services?.Dispose();

            _ = viewModel.InitializeAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Applies the persisted theme, including the "follow the system" option.</summary>
    public static void ApplyTheme(StorageThemeVariant variant)
    {
        if (Current is null)
        {
            return;
        }

        Current.RequestedThemeVariant = variant switch
        {
            StorageThemeVariant.Light => Avalonia.Styling.ThemeVariant.Light,
            StorageThemeVariant.System => Avalonia.Styling.ThemeVariant.Default,
            _ => Avalonia.Styling.ThemeVariant.Dark,
        };
    }
}
