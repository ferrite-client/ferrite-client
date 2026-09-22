using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Ferrite.Core.Auth;
using Ferrite.Core.Net;

namespace Ferrite.App.ViewModels;

/// <summary>Account presentation plus small, bounded skin and cape previews.</summary>
public sealed partial class AccountItemViewModel : ObservableObject
{
    private const int MaxImageBytes = 1024 * 1024;
    private readonly HttpService _http;

    public AccountItemViewModel(AccountRecord account, HttpService http)
    {
        Account = account;
        _http = http;
    }

    public AccountRecord Account { get; }

    public string DisplayName => Account.DisplayName;

    public string? Uuid => Account.Uuid;

    public string Kind => Account.Kind;

    [ObservableProperty]
    private Bitmap? _skinPreview;

    [ObservableProperty]
    private Bitmap? _capePreview;

    /// <summary>True for the account Ferrite will launch with, so the list can say which one it is.</summary>
    [ObservableProperty]
    private bool _isActive;

    public bool HasSkinPreview => SkinPreview is not null;

    public bool HasCapePreview => CapePreview is not null;

    public async Task LoadPreviewsAsync(CancellationToken cancellationToken = default)
    {
        SkinPreview = await LoadAsync(Account.SkinUrl, cancellationToken).ConfigureAwait(true);
        CapePreview = await LoadAsync(Account.CapeUrl, cancellationToken).ConfigureAwait(true);
        OnPropertyChanged(nameof(HasSkinPreview));
        OnPropertyChanged(nameof(HasCapePreview));
    }

    private async Task<Bitmap?> LoadAsync(string? url, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        try
        {
            var bytes = await _http.GetBytesAsync(url, MaxImageBytes, cancellationToken).ConfigureAwait(true);
            using var stream = new MemoryStream(bytes, writable: false);
            return Bitmap.DecodeToWidth(stream, 128);
        }
        catch
        {
            return null;
        }
    }
}
