using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Ferrite.Core.Net;

namespace Ferrite.App.ViewModels;

/// <summary>
/// One image from a project's gallery. Remote artwork is fetched on demand and decoded to a bounded
/// size; a broken or missing image is reported on the frame rather than breaking the panel.
/// </summary>
public sealed partial class GalleryImageViewModel : ObservableObject
{
    private const int MaxImageBytes = 8 * 1024 * 1024;
    private const int DecodeWidth = 480;

    private readonly HttpService _http;

    public GalleryImageViewModel(string url, HttpService http)
    {
        Url = url;
        _http = http;
    }

    public string Url { get; }

    [ObservableProperty]
    private Bitmap? _image;

    public bool HasImage => Image is not null;

    [ObservableProperty]
    private string? _note;

    [ObservableProperty]
    private bool _isLoading;

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        try
        {
            var bytes = await _http
                .GetBytesAsync(Url, MaxImageBytes, cancellationToken)
                .ConfigureAwait(true);
            using var stream = new MemoryStream(bytes, writable: false);
            Image = Bitmap.DecodeToWidth(stream, DecodeWidth);
            Note = null;
        }
        catch (Exception exception)
        {
            Image = null;
            Note = exception.Message;
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(HasImage));
        }
    }
}
