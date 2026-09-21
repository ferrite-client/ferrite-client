using Avalonia.Media.Imaging;
using Ferrite.App.Services;
using Ferrite.App.ViewModels;
using Ferrite.Core.Net;
using Ferrite.Core.Platform;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ferrite.App.Tests;

/// <summary>
/// Project artwork: fetched over HTTP, decoded at a bounded size, and reported rather than thrown
/// when the URL is broken or the bytes are not an image.
/// </summary>
public sealed class GalleryImageTests : IDisposable
{
    private readonly string _root;
    private readonly AppServices _services;
    private readonly HttpService _http;

    public GalleryImageTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ferrite-gallery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
        _services = new AppServices(loggerFactory, AppPaths.ForRoot(_root));
        _http = _services.Http;
    }

    public void Dispose()
    {
        _services.Dispose();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static byte[] Png(int width, int height)
    {
        var path = Path.Combine(Path.GetTempPath(), "ferrite-gallery-src-" + Guid.NewGuid().ToString("N") + ".png");
        TestAssets.WritePng(path, width, height);
        var bytes = File.ReadAllBytes(path);
        File.Delete(path);
        return bytes;
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task An_image_url_is_fetched_and_decoded()
    {
        using var server = new TestImageServer(Png(960, 540));
        var frame = new GalleryImageViewModel(server.BaseUrl + "/gallery/shot.png", _http);

        await frame.LoadAsync(TestContext.Current.CancellationToken);

        Assert.True(frame.HasImage, frame.Note);
        Assert.Equal(1, server.Requests);
        Assert.True(
            frame.Image!.PixelSize.Width <= 480,
            $"decoded width was {frame.Image.PixelSize.Width}");
        Assert.Null(frame.Note);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task Bytes_that_are_not_an_image_are_reported_on_the_frame()
    {
        using var server = new TestImageServer("this is not a png"u8.ToArray(), "text/plain");
        var frame = new GalleryImageViewModel(server.BaseUrl + "/gallery/broken.png", _http);

        await frame.LoadAsync(TestContext.Current.CancellationToken);

        Assert.False(frame.HasImage);
        Assert.False(string.IsNullOrWhiteSpace(frame.Note));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task An_unreachable_url_is_reported_rather_than_thrown()
    {
        // Port 9 is the discard port: nothing is listening, so the connection is refused.
        var frame = new GalleryImageViewModel("http://127.0.0.1:9/gallery/missing.png", _http);

        await frame.LoadAsync(TestContext.Current.CancellationToken);

        Assert.False(frame.HasImage);
        Assert.False(string.IsNullOrWhiteSpace(frame.Note));
    }
}
