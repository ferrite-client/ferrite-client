using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ferrite.App.Services;
using Ferrite.Core.Auth;
using Ferrite.Core.Util;

namespace Ferrite.App.ViewModels;

/// <summary>
/// Accounts: device-code sign-in, account switching, and sign-out. Sign-in needs a Microsoft client
/// id; without one the page explains exactly what to configure.
/// </summary>
public sealed partial class AccountsViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly MainWindowViewModel _shell;
    private CancellationTokenSource? _signInCancellation;

    public AccountsViewModel(AppServices services, MainWindowViewModel shell)
    {
        _services = services;
        _shell = shell;
    }

    public ObservableCollection<AccountRecord> Accounts { get; } = [];

    [ObservableProperty]
    private string? _clientId;

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

    public bool HasAccounts => Accounts.Count > 0;

    public bool HasUserCode => !string.IsNullOrEmpty(UserCode);

    public bool SecretStorageDegraded => _services.Accounts.IsSecretStorageDegraded;

    partial void OnUserCodeChanged(string? value) => OnPropertyChanged(nameof(HasUserCode));

    public void Load()
    {
        Accounts.Clear();
        foreach (var account in _services.Accounts.Accounts)
        {
            Accounts.Add(account);
        }

        ClientId = _services.Settings.Current.MicrosoftClientId;
        OnPropertyChanged(nameof(HasAccounts));
    }

    [RelayCommand]
    private async Task SaveClientIdAsync()
    {
        _services.Settings.Current.MicrosoftClientId = string.IsNullOrWhiteSpace(ClientId) ? null : ClientId.Trim();
        await _services.Settings.SaveAsync(CancellationToken.None).ConfigureAwait(true);
        StatusNote = string.IsNullOrWhiteSpace(ClientId) ? "Client id cleared" : "Client id saved";
    }

    [RelayCommand]
    private async Task SignInAsync()
    {
        var clientId = _services.Settings.Current.MicrosoftClientId;
        if (string.IsNullOrWhiteSpace(clientId))
        {
            StatusNote = "Add a Microsoft client id first; see docs/HUMAN_ACTION_REQUIRED.md.";
            return;
        }

        IsSigningIn = true;
        SignInStatus = "Requesting a sign-in code...";
        _signInCancellation = new CancellationTokenSource();
        try
        {
            var client = _services.CreateAuthClient(clientId);
            var challenge = await client.RequestDeviceCodeAsync(_signInCancellation.Token).ConfigureAwait(true);
            UserCode = challenge.UserCode;
            VerificationUrl = challenge.VerificationUriComplete ?? challenge.VerificationUri;
            SignInStatus = "Enter this code in your browser to sign in.";
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
            SignInStatus = $"Signed in as {account.DisplayName}";
            _shell.ReportStatus(SignInStatus);
        }
        catch (OperationCanceledException)
        {
            SignInStatus = "Sign-in cancelled.";
        }
        catch (Exception exception)
        {
            SignInStatus = exception.Message;
        }
        finally
        {
            IsSigningIn = false;
            UserCode = null;
            _signInCancellation?.Dispose();
            _signInCancellation = null;
        }
    }

    [RelayCommand]
    private void CancelSignIn() => _signInCancellation?.Cancel();

    [RelayCommand]
    private async Task SetActiveAsync(AccountRecord account)
    {
        _services.Settings.Current.ActiveAccountId = account.Id;
        await _services.Settings.SaveAsync(CancellationToken.None).ConfigureAwait(true);
        _shell.RefreshActiveAccount();
        StatusNote = $"Active account is {account.DisplayName}";
    }

    [RelayCommand]
    private async Task SignOutAsync(AccountRecord account)
    {
        _services.Accounts.SignOut(account.Id);
        if (_services.Settings.Current.ActiveAccountId == account.Id)
        {
            _services.Settings.Current.ActiveAccountId = null;
            await _services.Settings.SaveAsync(CancellationToken.None).ConfigureAwait(true);
        }

        _shell.RefreshActiveAccount();
        Load();
        StatusNote = "Signed out";
    }

    [RelayCommand]
    private void OpenProfileUrl()
    {
        if (VerificationUrl is { } url)
        {
            ShellOpen.Url(url);
        }
    }
}
