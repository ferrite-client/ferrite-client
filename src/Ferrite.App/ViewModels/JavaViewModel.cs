using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ferrite.App.Localization;
using Ferrite.App.Services;
using Ferrite.Core.Java;
using Ferrite.Core.Minecraft;
using Ferrite.Core.Util;

namespace Ferrite.App.ViewModels;

/// <summary>Java management: discovered runtimes, the launcher default, and Mojang provisioning.</summary>
public sealed partial class JavaViewModel : ObservableObject
{
    private static readonly string[] ComponentNames =
    [
        "java-runtime-epsilon",
        "java-runtime-delta",
        "java-runtime-gamma",
        "java-runtime-beta",
        "java-runtime-alpha",
        "jre-legacy",
    ];

    private readonly AppServices _services;
    private readonly MainWindowViewModel _shell;

    public JavaViewModel(AppServices services, MainWindowViewModel shell)
    {
        _services = services;
        _shell = shell;
        _selectedComponent = ComponentNames[0];
    }

    public ObservableCollection<JavaRuntime> Runtimes { get; } = [];

    public ObservableCollection<JavaRuntimeOption> AvailableBuilds { get; } = [];

    public IReadOnlyList<string> Components => ComponentNames;

    [ObservableProperty]
    private JavaRuntime? _selectedRuntime;

    [ObservableProperty]
    private string _selectedComponent;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _statusNote;

    public bool HasRuntimes => Runtimes.Count > 0;

    public async Task InitializeAsync()
    {
        await ScanAsync().ConfigureAwait(true);
        var defaultPath = _services.Settings.Current.DefaultJavaPath;
        SelectedRuntime = Runtimes.FirstOrDefault(runtime =>
            string.Equals(runtime.ExecutablePath, defaultPath, StringComparison.OrdinalIgnoreCase));
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        IsBusy = true;
        try
        {
            var runtimes = await _services.Java.DetectAsync(CancellationToken.None).ConfigureAwait(true);
            Runtimes.Clear();
            foreach (var runtime in runtimes)
            {
                Runtimes.Add(runtime);
            }

            StatusNote = Localizer.Format("L.Java.Found", runtimes.Count);
            OnPropertyChanged(nameof(HasRuntimes));
        }
        catch (Exception exception)
        {
            _shell.ReportError(exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SetDefaultAsync()
    {
        if (SelectedRuntime is not { } runtime)
        {
            return;
        }

        _services.Settings.Current.DefaultJavaPath = runtime.ExecutablePath;
        await _services.Settings.SaveAsync(CancellationToken.None).ConfigureAwait(true);
            StatusNote = Localizer.Format("L.Java.DefaultNow", runtime.DisplayName);
        _shell.ReportStatus(StatusNote);
    }

    [RelayCommand]
    private void OpenFolder()
    {
        if (SelectedRuntime?.HomePath is { } home)
        {
            ShellOpen.Directory(home);
        }
    }

    [RelayCommand]
    private async Task ListBuildsAsync()
    {
        IsBusy = true;
        try
        {
            var builds = await _services.JavaProvisioner
                .ListAvailableAsync(SelectedComponent, CancellationToken.None)
                .ConfigureAwait(true);
            AvailableBuilds.Clear();
            foreach (var build in builds)
            {
                AvailableBuilds.Add(build);
            }

            StatusNote = builds.Count == 0
                ? Localizer.Format("L.Java.NoBuilds", SelectedComponent)
                : Localizer.Format("L.Java.Builds", builds.Count, SelectedComponent);
        }
        catch (Exception exception)
        {
            _shell.ReportError(exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ProvisionAsync()
    {
        IsBusy = true;
        try
        {
            _shell.BeginActivity($"Downloading {SelectedComponent}...");
            var runtime = await _services.JavaProvisioner
                .ProvisionAsync(
                    SelectedComponent,
                    new Progress<InstallProgress>(_shell.ReportActivity),
                    CancellationToken.None)
                .ConfigureAwait(true);

            StatusNote = Localizer.Format("L.Java.Provisioned", runtime.DisplayName);
            await ScanAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _shell.ReportError(exception.Message);
        }
        finally
        {
            IsBusy = false;
            _shell.EndActivity();
        }
    }
}
