using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Ferrite.App.Localization;
using Ferrite.App.Services;
using Ferrite.Core.Content;
using Ferrite.Core.Net;

namespace Ferrite.App.ViewModels;

/// <summary>
/// One search result, with the provider's own icon decoded on demand.
/// </summary>
/// <remarks>
/// A page of results is a few dozen projects and the icon is a remote fetch, so the icon is loaded
/// when a row is realised and released when it scrolls away rather than decoded for the whole page at
/// once. A missing or broken icon is simply no icon: the row falls back to a type glyph.
/// </remarks>
public sealed partial class ContentSummaryViewModel : ObservableObject
{
    private const int MaxIconBytes = 2 * 1024 * 1024;
    private const int IconDecodeWidth = 128;

    private readonly AppServices _services;
    private bool _iconRequested;

    public ContentSummaryViewModel(ContentSummary summary, AppServices services)
    {
        Summary = summary;
        _services = services;
    }

    /// <summary>The provider's own record, for anything that needs the raw fields.</summary>
    public ContentSummary Summary { get; }

    public string Title => Summary.Title;

    public string? Description => Summary.Description;

    public string? Author => Summary.Author;

    public string Provider => ContentProviderNames.DisplayNameFor(Summary.Provider);

    public string ProjectId => Summary.ProjectId;

    public string Slug => Summary.Slug;

    public ContentProjectType ProjectType => Summary.ProjectType;

    public long Downloads => Summary.Downloads;

    /// <summary>The download count as a phrase, so the wording is translated with the rest of the UI.</summary>
    public string DownloadsText => Localizer.Format("L.Browse.DownloadsCount", Summary.Downloads);

    /// <summary>The content type as a word the interface uses, not the enumeration member's name.</summary>
    public string TypeText => Summary.ProjectType switch
    {
        ContentProjectType.Modpack => Localizer.Get("L.Browse.TypeModpack"),
        ContentProjectType.ResourcePack => Localizer.Get("L.Browse.TypeResourcePack"),
        ContentProjectType.Shader => Localizer.Get("L.Browse.TypeShader"),
        ContentProjectType.Datapack => Localizer.Get("L.Browse.TypeDatapack"),
        ContentProjectType.Plugin => Localizer.Get("L.Browse.TypePlugin"),
        ContentProjectType.Mod => Localizer.Get("L.Browse.TypeMod"),
        _ => Localizer.Get("L.Browse.TypeUnknown"),
    };

    [ObservableProperty]
    private Bitmap? _icon;

    public bool HasIcon => Icon is not null;

    /// <summary>Fetches the icon once. A second call while it is already loaded does nothing.</summary>
    public async Task EnsureIconAsync()
    {
        if (_iconRequested || string.IsNullOrWhiteSpace(Summary.IconUrl))
        {
            return;
        }

        _iconRequested = true;
        try
        {
            var bytes = await _services.Http
                .GetBytesAsync(Summary.IconUrl!, MaxIconBytes, CancellationToken.None)
                .ConfigureAwait(true);
            using var stream = new MemoryStream(bytes, writable: false);
            Icon = Bitmap.DecodeToWidth(stream, IconDecodeWidth);
            OnPropertyChanged(nameof(HasIcon));
        }
        catch (Exception exception) when (exception is IOException or HttpException or ArgumentException)
        {
            // A result with no icon is a result with a type glyph, not an error on the page.
        }
    }

    /// <summary>Drops the decoded icon when the row scrolls out of view, keeping memory bounded.</summary>
    public void ReleaseIcon()
    {
        _iconRequested = false;
        Icon?.Dispose();
        Icon = null;
        OnPropertyChanged(nameof(HasIcon));
    }
}
