using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ferrite.App.Localization;
using Ferrite.App.Services;
using Ferrite.Core.Diagnostics;
using Ferrite.Core.Download;
using Ferrite.Core.Minecraft;
using Microsoft.Extensions.Logging;

namespace Ferrite.App.ViewModels;

/// <summary>
/// The application's global destinations. Everything that belongs to one instance lives inside that
/// instance instead, which is why this list is short.
/// </summary>
public enum AppPage
{
    Library,
    Discover,
    Downloads,
    Accounts,
    Settings,
}

/// <summary>
/// Shell view model: navigation, the shared activity strip, notifications, the account switcher, and
/// the active account. Pages are created once and reused so their state survives navigation.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private const int PrimaryPageCount = 3;
    private static readonly TimeSpan ToastLifetime = TimeSpan.FromSeconds(6);

    private readonly AppServices _services;
    private OperationLog.OperationScope? _currentOperation;

    [ObservableProperty]
    private AppPage _currentPage = AppPage.Library;

    [ObservableProperty]
    private object? _detailPage;

    [ObservableProperty]
    private string _statusText = Localizer.Get("L.Common.Ready");

    [ObservableProperty]
    private string? _activityText;

    [ObservableProperty]
    private double _activityFraction;

    [ObservableProperty]
    private bool _isActivityVisible;

    [ObservableProperty]
    private bool _isActivityIndeterminate = true;

    /// <summary>
    /// The last download progress the launcher reported, so the downloads view can show what is
    /// actually moving rather than only that something is. Null when no transfer is in flight.
    /// </summary>
    [ObservableProperty]
    private DownloadProgress? _transfer;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string _activeAccountName = Localizer.Get("L.Shell.NoAccount");

    [ObservableProperty]
    private string? _runningInstanceName;

    [ObservableProperty]
    private bool _isRailCollapsed;

    [ObservableProperty]
    private bool _isDownloadsDrawerOpen;

    /// <summary>The decision currently waiting for an answer, if any.</summary>
    [ObservableProperty]
    private ConfirmationViewModel? _confirmation;

    /// <summary>The signed-in account whose avatar and name the rail shows, when there is one.</summary>
    [ObservableProperty]
    private AccountItemViewModel? _activeAccount;

    public MainWindowViewModel(AppServices services)
    {
        _services = services;
        Library = new LibraryViewModel(services, this);
        Browse = new BrowseViewModel(services, this);
        Java = new JavaViewModel(services, this);
        Accounts = new AccountsViewModel(services, this);
        Settings = new SettingsViewModel(services, this);
        Downloads = new DownloadsViewModel(services, this);
    }

    public LibraryViewModel Library { get; }

    public BrowseViewModel Browse { get; }

    public JavaViewModel Java { get; }

    public AccountsViewModel Accounts { get; }

    public SettingsViewModel Settings { get; }

    public DownloadsViewModel Downloads { get; }

    /// <summary>Transient notifications, newest last so a new one appears below its predecessors.</summary>
    public ObservableCollection<ToastViewModel> Toasts { get; } = [];

    public bool IsLibrarySelected => CurrentPage == AppPage.Library;

    public bool IsDiscoverSelected => CurrentPage == AppPage.Discover;

    public bool IsDownloadsSelected => CurrentPage == AppPage.Downloads;

    public bool IsAccountsSelected => CurrentPage == AppPage.Accounts;

    public bool IsSettingsSelected => CurrentPage == AppPage.Settings;

    public bool IsPrimaryPageSelected =>
        CurrentPage is AppPage.Library or AppPage.Discover or AppPage.Downloads;

    /// <summary>The page name as shown in the shell header, in the current language.</summary>
    public string CurrentPageTitle => Localizer.Get(CurrentPage switch
    {
        AppPage.Library => "L.Nav.Library",
        AppPage.Discover => "L.Nav.Discover",
        AppPage.Downloads => "L.Nav.Downloads",
        AppPage.Accounts => "L.Nav.Accounts",
        _ => "L.Nav.Settings",
    });

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public bool IsDetailOpen => DetailPage is not null;

    public bool IsRailExpanded => !IsRailCollapsed;

    /// <summary>The rail toggle's own label, which is why it changes with the rail's state.</summary>
    public string RailToggleText => IsRailCollapsed
        ? Localizer.Get("L.Shell.ExpandRail")
        : Localizer.Get("L.Shell.CollapseRail");

    public bool HasActiveAccount => ActiveAccount is not null;

    /// <summary>Whether the downloads indicator should call attention to itself.</summary>
    public bool HasActiveOperations => IsActivityVisible;

    public string AccountStatusText => ActiveAccount is { } account
        ? account.Kind
        : Localizer.Get("L.Shell.SignedOut");

    /// <summary>
    /// Index of the selected primary destination, or -1 when the current page is not in the primary
    /// rail. This is what the rail's list binds to, so the footer entries never fight it for selection.
    /// </summary>
    public int PrimaryPageIndex
    {
        get => CurrentPage switch
        {
            AppPage.Library => 0,
            AppPage.Discover => 1,
            AppPage.Downloads => 2,
            _ => -1,
        };
        set
        {
            if (value >= 0 && value < PrimaryPageCount)
            {
                CurrentPage = (AppPage)value;
            }
        }
    }

    partial void OnCurrentPageChanged(AppPage value)
    {
        DetailPage = null;
        IsDownloadsDrawerOpen = false;
        OnPropertyChanged(nameof(CurrentPageTitle));
        OnPropertyChanged(nameof(PrimaryPageIndex));
        OnPropertyChanged(nameof(IsPrimaryPageSelected));
        OnPropertyChanged(nameof(IsLibrarySelected));
        OnPropertyChanged(nameof(IsDiscoverSelected));
        OnPropertyChanged(nameof(IsDownloadsSelected));
        OnPropertyChanged(nameof(IsAccountsSelected));
        OnPropertyChanged(nameof(IsSettingsSelected));

        if (value == AppPage.Downloads)
        {
            Downloads.Refresh();
        }
    }

    partial void OnDetailPageChanged(object? value) => OnPropertyChanged(nameof(IsDetailOpen));

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));

    partial void OnActiveAccountChanged(AccountItemViewModel? value)
    {
        OnPropertyChanged(nameof(HasActiveAccount));
        OnPropertyChanged(nameof(AccountStatusText));
    }

    partial void OnIsRailCollapsedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsRailExpanded));
        OnPropertyChanged(nameof(RailToggleText));
    }

    partial void OnIsActivityVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(HasActiveOperations));
        Downloads.Refresh();
    }

    partial void OnTransferChanged(DownloadProgress? value) => Downloads.Refresh();

    public async Task InitializeAsync()
    {
        await _services.Operations.LoadAsync(CancellationToken.None).ConfigureAwait(true);
        await Settings.LoadAsync().ConfigureAwait(true);
        Library.ShowSnapshots = _services.Settings.Current.ShowSnapshotsInVersionList;
        Library.ShowHistoricalVersions = _services.Settings.Current.ShowHistoricalVersions;
        Accounts.Load();
        RefreshActiveAccount();
        await Library.RefreshAsync().ConfigureAwait(true);
        Downloads.Refresh();
        if (_services.Settings.Current.LastSelectedInstanceId is { } lastId
            && Library.Instances.FirstOrDefault(item => item.Record.Id == lastId) is { } last)
        {
            ShowInstance(last.Record);
        }

        _services.Logger<MainWindowViewModel>().LogInformation(
            "Launcher ready; {Count} instance(s), {Accounts} account(s)",
            Library.Instances.Count,
            Accounts.Accounts.Count);
        StatusText = Localizer.Get("L.Common.Ready");
        _ = Settings.CheckForUpdatesOnStartupAsync();
    }

    public void ShowInstance(Ferrite.Core.Storage.InstanceRecord record)
    {
        DetailPage = new InstanceDetailViewModel(record, _services, this);
        _services.Settings.Current.LastSelectedInstanceId = record.Id;
        _ = _services.Settings.SaveAsync(CancellationToken.None);
    }

    public void CloseInstance()
    {
        DetailPage = null;
        _services.Settings.Current.LastSelectedInstanceId = null;
        _ = _services.Settings.SaveAsync(CancellationToken.None);
    }

    public void RefreshActiveAccount()
    {
        var activeId = _services.Settings.Current.ActiveAccountId;
        var account = activeId is { } id
            ? _services.Accounts.Accounts.FirstOrDefault(candidate => candidate.Id == id)
            : null;
        ActiveAccountName = account?.DisplayName ?? Localizer.Get("L.Shell.NoAccount");

        var item = ActiveAccount is { } current && current.Account.Id == account?.Id
            ? current
            : Accounts.Accounts.FirstOrDefault(candidate => candidate.Account.Id == account?.Id);
        ActiveAccount = item;
    }

    public void ReportStatus(string text) => StatusText = text;

    /// <summary>Reports a completed outcome: the status line plus a transient notification.</summary>
    public void ReportSuccess(string title, string? detail = null)
    {
        StatusText = title;
        Notify(ToastKind.Success, title, detail);
    }

    public void ReportError(string message)
    {
        _currentOperation?.Fail(message);
        ErrorMessage = message;
        StatusText = Localizer.Get("L.Shell.Error");
    }

    public void ClearError() => ErrorMessage = null;

    /// <summary>Shows a transient notification. Nothing here blocks the interface.</summary>
    public void Notify(ToastKind kind, string title, string? detail = null)
    {
        var toast = new ToastViewModel(kind, title, detail, DismissToast);
        Toasts.Add(toast);
        OnPropertyChanged(nameof(HasToasts));

        // A toast that cannot expire would pile up on a long session, so each one schedules its own
        // removal. In a headless host the timer simply never fires, which is why the collection is
        // also bounded below.
        var timer = new DispatcherTimer { Interval = ToastLifetime };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            DismissToast(toast);
        };
        timer.Start();

        while (Toasts.Count > 4)
        {
            Toasts.RemoveAt(0);
        }
    }

    public bool HasToasts => Toasts.Count > 0;

    public bool IsConfirming => Confirmation is not null;

    partial void OnConfirmationChanged(ConfirmationViewModel? value) => OnPropertyChanged(nameof(IsConfirming));

    /// <summary>
    /// Asks a blocking question and returns the answer. A second request supersedes the first rather
    /// than stacking dialogs, and a superseded question is answered "no" so its caller stops.
    /// </summary>
    public async Task<bool> ConfirmAsync(string title, string message, string confirmLabel, bool destructive = true)
    {
        Confirmation?.Dismiss();
        var confirmation = new ConfirmationViewModel(title, message, confirmLabel, destructive);
        Confirmation = confirmation;
        try
        {
            return await confirmation.Result.ConfigureAwait(true);
        }
        finally
        {
            if (ReferenceEquals(Confirmation, confirmation))
            {
                Confirmation = null;
            }
        }
    }

    private void DismissToast(ToastViewModel toast)
    {
        Toasts.Remove(toast);
        OnPropertyChanged(nameof(HasToasts));
    }

    public void BeginActivity(string text, bool indeterminate = true)
    {
        _currentOperation?.Dispose();
        _currentOperation = _services.Operations.Begin(text);
        ActivityText = text;
        IsActivityVisible = true;
        IsActivityIndeterminate = indeterminate;
        ActivityFraction = 0;
        Transfer = null;
    }

    public void ReportActivity(InstallProgress progress)
    {
        ActivityText = progress.Stage switch
        {
            InstallStage.ResolvingMetadata => Localizer.Get("L.Activity.Resolving"),
            InstallStage.Planning => Localizer.Format("L.Activity.Planning", progress.Message),
            InstallStage.Downloading => progress.Download is { } download
                ? Localizer.Format(
                    "L.Activity.DownloadingItem",
                    download.CurrentItem ?? string.Empty,
                    download.Fraction.ToString("P0"))
                : Localizer.Get("L.Activity.Downloading"),
            InstallStage.ExtractingNatives => Localizer.Format(
                "L.Activity.ExtractingNatives",
                progress.Message),
            _ => Localizer.Get("L.Activity.Finishing"),
        };

        IsActivityVisible = true;
        IsActivityIndeterminate = false;
        ActivityFraction = progress.Download?.Fraction ?? 0;
        Transfer = progress.Download;
        Downloads.Refresh();
    }

    public void EndActivity(string? message = null)
    {
        if (_currentOperation is { } operation)
        {
            operation.Note(message ?? ActivityText);
            operation.Dispose();
            _currentOperation = null;
        }

        IsActivityVisible = false;
        ActivityFraction = 0;
        Transfer = null;

        Downloads.Refresh();

        if (message is not null)
        {
            StatusText = message;
        }

        // Only an explicit completion message becomes a notification. The activity text describes work
        // in progress, and toasting "Downloading..." after it finished would say the opposite of what
        // happened. Callers that know the outcome call ReportSuccess instead.
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
    private void ToggleRail() => IsRailCollapsed = !IsRailCollapsed;

    [RelayCommand]
    private void ToggleDownloadsDrawer() => IsDownloadsDrawerOpen = !IsDownloadsDrawerOpen;

    [RelayCommand]
    private void CloseDownloadsDrawer() => IsDownloadsDrawerOpen = false;

    [RelayCommand]
    private void OpenDownloads()
    {
        IsDownloadsDrawerOpen = false;
        CurrentPage = AppPage.Downloads;
    }

    [RelayCommand]
    private void DismissError() => ClearError();

    /// <summary>
    /// Takes the user to the diagnostics category, where the support bundle that includes this failure
    /// can be exported. The banner explains what happened; this is the way to hand it to someone.
    /// </summary>
    [RelayCommand]
    private void OpenDiagnostics()
    {
        Settings.SelectedCategory = Settings.Categories
            .FirstOrDefault(category => string.Equals(category.Value, "diagnostics", StringComparison.Ordinal))
            ?? Settings.SelectedCategory;
        CurrentPage = AppPage.Settings;
        ClearError();
    }
}
