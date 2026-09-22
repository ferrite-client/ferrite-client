using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ferrite.App.Localization;
using Ferrite.App.Services;
using Ferrite.Core.Auth;
using Ferrite.Core.Util;

namespace Ferrite.App.ViewModels;

/// <summary>Accounts: Microsoft/Yggdrasil sign-in, account switching, and sign-out.</summary>
public sealed partial class AccountsViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly MainWindowViewModel _shell;
    private CancellationTokenSource? _signInCancellation;
    private AuthorizationCodeFlow? _browserFlow;

    public AccountsViewModel(AppServices services, MainWindowViewModel shell)
    {
        _services = services;
        _shell = shell;
    }

    public ObservableCollection<AccountItemViewModel> Accounts { get; } = [];

    [ObservableProperty]
    private bool _isSigningIn;

    [ObservableProperty]
    private string? _userCode;

    [ObservableProperty]
    private string? _verificationUrl;

    [ObservableProperty]
    private string? _signInStatus;

    [ObservableProperty]
    private string? _statusNote;

    [ObservableProperty]
    private string? _yggdrasilServerUrl;

    [ObservableProperty]
    private string? _yggdrasilUsername;

    [ObservableProperty]
    private string? _yggdrasilPassword;

    public bool HasAccounts => Accounts.Count > 0;

    public bool HasUserCode => !string.IsNullOrEmpty(UserCode);

    public bool SecretStorageDegraded => _services.Accounts.IsSecretStorageDegraded;

    partial void OnUserCodeChanged(string? value) => OnPropertyChanged(nameof(HasUserCode));

    public void Load()
    {
        Accounts.Clear();
        var activeId = _services.Settings.Current.ActiveAccountId;
        foreach (var account in _services.Accounts.Accounts)
        {
            var item = new AccountItemViewModel(account, _services.Http)
            {
                IsActive = activeId is { } id && id == account.Id,
            };
            Accounts.Add(item);
            _ = item.LoadPreviewsAsync();
        }

        OnPropertyChanged(nameof(HasAccounts));
    }

    [RelayCommand]
    private async Task SignInAsync()
    {
        IsSigningIn = true;
        SignInStatus = Localizer.Get("L.Accounts.RequestingCode");
        _signInCancellation = new CancellationTokenSource();
        try
        {
            var challenge = await _services.Accounts
                .StartSignInAsync(_signInCancellation.Token)
                .ConfigureAwait(true);
            UserCode = challenge.UserCode;
            VerificationUrl = challenge.VerificationUriComplete ?? challenge.VerificationUri;
            SignInStatus = Localizer.Get("L.Accounts.EnterCode");
            ShellOpen.Url(VerificationUrl);

            var account = await _services.Accounts
                .CompleteSignInAsync(
                    challenge,
                    new Progress<string>(text => SignInStatus = text),
                    _signInCancellation.Token)
                .ConfigureAwait(true);

            _services.Settings.Current.ActiveAccountId = account.Id;
            await _services.Settings.SaveAsync(CancellationToken.None).ConfigureAwait(true);
            _shell.RefreshActiveAccount();
            Load();
            SignInStatus = Localizer.Format("L.Accounts.SignedInAs", account.DisplayName);
            _shell.ReportStatus(SignInStatus);
        }
        catch (OperationCanceledException)
        {
            SignInStatus = Localizer.Get("L.Accounts.SignInCancelled");
        }
        catch (Exception exception)
        {
            SignInStatus = exception.Message;
        }
        finally
        {
            CompleteSignIn();
        }
    }

    [RelayCommand]
    private void CancelSignIn() => _signInCancellation?.Cancel();

    [RelayCommand]
    private async Task BrowserSignInAsync()
    {
        IsSigningIn = true;
        _signInCancellation = new CancellationTokenSource();
        try
        {
            _browserFlow = _services.Accounts.StartBrowserSignIn();
            VerificationUrl = _browserFlow.AuthorizationUrl;
            SignInStatus = Localizer.Get("L.Accounts.BrowserSignInWaiting");
            ShellOpen.Url(VerificationUrl);
            var account = await _services.Accounts
                .CompleteBrowserSignInAsync(
                    _browserFlow,
                    new Progress<string>(text => SignInStatus = text),
                    _signInCancellation.Token)
                .ConfigureAwait(true);
            await ActivateNewAccountAsync(account).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            SignInStatus = Localizer.Get("L.Accounts.SignInCancelled");
        }
        catch (Exception exception)
        {
            SignInStatus = exception.Message;
        }
        finally
        {
            CompleteSignIn();
        }
    }

    [RelayCommand]
    private async Task YggdrasilSignInAsync()
    {
        IsSigningIn = true;
        try
        {
            var account = await _services.Accounts
                .SignInYggdrasilAsync(
                    YggdrasilServerUrl ?? string.Empty,
                    YggdrasilUsername ?? string.Empty,
                    YggdrasilPassword ?? string.Empty,
                    CancellationToken.None)
                .ConfigureAwait(true);
            await ActivateNewAccountAsync(account).ConfigureAwait(true);
            StatusNote = Localizer.Format("L.Accounts.SignedInAs", account.DisplayName);
        }
        catch (Exception exception)
        {
            StatusNote = exception.Message;
        }
        finally
        {
            YggdrasilPassword = null;
            IsSigningIn = false;
        }
    }

    [RelayCommand]
    private async Task SetActiveAsync(AccountItemViewModel item)
    {
        var account = item.Account;
        _services.Settings.Current.ActiveAccountId = account.Id;
        await _services.Settings.SaveAsync(CancellationToken.None).ConfigureAwait(true);
        _shell.RefreshActiveAccount();
        foreach (var candidate in Accounts)
        {
            candidate.IsActive = candidate.Account.Id == account.Id;
        }

        StatusNote = Localizer.Format("L.Accounts.Active", account.DisplayName);
    }

    [RelayCommand]
    private async Task SignOutAsync(AccountItemViewModel item)
    {
        var account = item.Account;
        var confirmed = await _shell.ConfirmAsync(
            Localizer.Get("L.Confirm.SignOutTitle"),
            Localizer.Format("L.Confirm.SignOutMessage", account.DisplayName),
            Localizer.Get("L.Confirm.SignOutConfirm"))
            .ConfigureAwait(true);
        if (!confirmed)
        {
            return;
        }

        _services.Accounts.SignOut(account.Id);
        if (_services.Settings.Current.ActiveAccountId == account.Id)
        {
            _services.Settings.Current.ActiveAccountId = null;
            await _services.Settings.SaveAsync(CancellationToken.None).ConfigureAwait(true);
        }

        _shell.RefreshActiveAccount();
        Load();
        StatusNote = Localizer.Get("L.Accounts.SignedOut");
    }

    [RelayCommand]
    private void OpenProfileUrl()
    {
        if (VerificationUrl is { } url)
        {
            ShellOpen.Url(url);
        }
    }

    private async Task ActivateNewAccountAsync(AccountRecord account)
    {
        _services.Settings.Current.ActiveAccountId = account.Id;
        await _services.Settings.SaveAsync(CancellationToken.None).ConfigureAwait(true);
        _shell.RefreshActiveAccount();
        Load();
        SignInStatus = Localizer.Format("L.Accounts.SignedInAs", account.DisplayName);
        _shell.ReportStatus(SignInStatus);
    }

    private void CompleteSignIn()
    {
        IsSigningIn = false;
        UserCode = null;
        _browserFlow?.Dispose();
        _browserFlow = null;
        _signInCancellation?.Dispose();
        _signInCancellation = null;
    }
}
