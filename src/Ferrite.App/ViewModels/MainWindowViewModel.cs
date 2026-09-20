using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ferrite.App.Services;
using Ferrite.Core.Minecraft;

namespace Ferrite.App.ViewModels;

public enum AppPage
{
    Library,
    Browse,
    Java,
    Accounts,
    Settings,
}

/// <summary>
/// Shell view model: navigation, the shared activity strip, and the active account. Pages are
/// created once and reused so their state survives navigation.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly AppServices _services;

    [ObservableProperty]
    private AppPage _currentPage = AppPage.Library;

    [ObservableProperty]
    private object? _detailPage;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private string? _activityText;

    [ObservableProperty]
    private double _activityFraction;

    [ObservableProperty]
    private bool _isActivityVisible;

    [ObservableProperty]
    private bool _isActivityIndeterminate = true;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string _activeAccountName = "No account";

    [ObservableProperty]
    private string? _runningInstanceName;

    public MainWindowViewModel(AppServices services)
    {
        _services = services;
        Library = new LibraryViewModel(services, this);
        Browse = new BrowseViewModel(services, this);
        Java = new JavaViewModel(services, this);
        Accounts = new AccountsViewModel(services, this);
        Settings = new SettingsViewModel(services, this);
    }

    public LibraryViewModel Library { get; }

    public BrowseViewModel Browse { get; }

    public JavaViewModel Java { get; }

    public AccountsViewModel Accounts { get; }

    public SettingsViewModel Settings { get; }

    public bool IsLibrarySelected => CurrentPage == AppPage.Library;

    public bool IsBrowseSelected => CurrentPage == AppPage.Browse;

    public bool IsJavaSelected => CurrentPage == AppPage.Java;

    public bool IsAccountsSelected => CurrentPage == AppPage.Accounts;

    public bool IsSettingsSelected => CurrentPage == AppPage.Settings;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public bool IsDetailOpen => DetailPage is not null;

    /// <summary>Index form of <see cref="CurrentPage"/>, for the navigation list.</summary>
    public int SelectedPageIndex
    {
        get => (int)CurrentPage;
        set
        {
            if (value >= 0 && value <= (int)AppPage.Settings && value != (int)CurrentPage)
            {
                CurrentPage = (AppPage)value;
            }
        }
    }

    partial void OnCurrentPageChanged(AppPage value)
    {
        DetailPage = null;
        OnPropertyChanged(nameof(SelectedPageIndex));
        OnPropertyChanged(nameof(IsLibrarySelected));
        OnPropertyChanged(nameof(IsBrowseSelected));
        OnPropertyChanged(nameof(IsJavaSelected));
        OnPropertyChanged(nameof(IsAccountsSelected));
        OnPropertyChanged(nameof(IsSettingsSelected));
    }

    partial void OnDetailPageChanged(object? value) => OnPropertyChanged(nameof(IsDetailOpen));

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));

    public async Task InitializeAsync()
    {
        await Settings.LoadAsync().ConfigureAwait(true);
        Accounts.Load();
        RefreshActiveAccount();
        await Library.RefreshAsync().ConfigureAwait(true);
        StatusText = "Ready";
    }

    public void RefreshActiveAccount()
    {
        var activeId = _services.Settings.Current.ActiveAccountId;
        var account = activeId is { } id
            ? _services.Accounts.Accounts.FirstOrDefault(candidate => candidate.Id == id)
            : null;
        ActiveAccountName = account?.DisplayName ?? "No account";
    }

    public void ReportStatus(string text) => StatusText = text;

    public void ReportError(string message)
    {
        ErrorMessage = message;
        StatusText = "Something went wrong";
    }

    public void ClearError() => ErrorMessage = null;

    public void BeginActivity(string text, bool indeterminate = true)
    {
        ActivityText = text;
        IsActivityVisible = true;
        IsActivityIndeterminate = indeterminate;
        ActivityFraction = 0;
    }

    public void ReportActivity(InstallProgress progress)
    {
        ActivityText = progress.Stage switch
        {
            InstallStage.ResolvingMetadata => "Resolving metadata...",
            InstallStage.Planning => $"Planning {progress.Message}...",
            InstallStage.Downloading => progress.Download is { } download
                ? $"Downloading {download.CurrentItem ?? string.Empty} ({download.Fraction:P0})"
                : "Downloading...",
            InstallStage.ExtractingNatives => $"Extracting natives ({progress.Message})",
            _ => "Finishing...",
        };

        IsActivityVisible = true;
        IsActivityIndeterminate = false;
        ActivityFraction = progress.Download?.Fraction ?? 0;
    }

    public void EndActivity(string? message = null)
    {
        IsActivityVisible = false;
        ActivityFraction = 0;
        if (message is not null)
        {
            StatusText = message;
        }
    }

    [RelayCommand]
    private void Navigate(string page)
    {
        if (Enum.TryParse<AppPage>(page, ignoreCase: true, out var parsed))
        {
            CurrentPage = parsed;
        }
    }

    [RelayCommand]
    private void DismissError() => ClearError();
}
