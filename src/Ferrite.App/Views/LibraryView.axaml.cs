using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Ferrite.App.Localization;
using Ferrite.App.ViewModels;

namespace Ferrite.App.Views;

public partial class LibraryView : UserControl
{
    private const double CardWidth = 238;
    private const double CardGap = 14;
    private const double HorizontalChrome = 34;

    public LibraryView()
    {
        InitializeComponent();
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        SizeChanged += OnSizeChanged;
    }

    /// <summary>
    /// The grid reflows to the window instead of assuming a column count: the view measures itself and
    /// tells the view model how many cards fit, which is also what keeps the row chunking correct.
    /// </summary>
    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is not LibraryViewModel viewModel)
        {
            return;
        }

        var usable = Math.Max(0, e.NewSize.Width - HorizontalChrome);
        var columns = Math.Max(1, (int)((usable + CardGap) / (CardWidth + CardGap)));
        if (columns != viewModel.GridColumns)
        {
            viewModel.GridColumns = columns;
        }
    }

    /// <summary>
    /// Artwork is decoded when a container is realised rather than when the library loads, so a
    /// collection of hundreds of instances only holds the images the viewport is showing.
    /// </summary>
    private static void OnCardRowPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        foreach (var card in CardsOf(e.Container))
        {
            _ = card.EnsureArtworkAsync();
        }
    }

    private static void OnCardRowClearing(object? sender, ContainerClearingEventArgs e)
    {
        foreach (var card in CardsOf(e.Container))
        {
            card.ReleaseArtwork();
        }
    }

    private static IEnumerable<InstanceCardViewModel> CardsOf(Control? container) => container?.DataContext switch
    {
        InstanceRowViewModel row => row.Cards,
        InstanceCardViewModel card => [card],
        _ => [],
    };

    /// <summary>
    /// A pack archive can be dropped onto the library instead of hunted down in a dialog. Both pack
    /// formats are accepted because the archive itself says which one it is.
    /// </summary>
    private static void OnDragOver(object? sender, DragEventArgs e) =>
        e.DragEffects = DropCandidates(e.DataTransfer).Count > 0
            ? DragDropEffects.Copy
            : DragDropEffects.None;

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not LibraryViewModel viewModel)
        {
            return;
        }

        var archive = DropCandidates(e.DataTransfer).FirstOrDefault();
        if (archive is not null)
        {
            await viewModel.ImportModpackAsync(archive);
        }
    }

    /// <summary>
    /// The local paths in a drag payload that could be a pack archive. Reading the payload is
    /// defensive: a drag from another application can carry anything.
    /// </summary>
    private static List<string> DropCandidates(IDataTransfer? transfer)
    {
        if (transfer is null)
        {
            return [];
        }

        var paths = new List<string>();
        foreach (var item in transfer.Items)
        {
            try
            {
                if (item.TryGetRaw(DataFormat.File) is IStorageItem file
                    && file.TryGetLocalPath() is { Length: > 0 } path)
                {
                    paths.Add(path);
                }
            }
            catch (Exception exception) when (exception is NotSupportedException or InvalidOperationException)
            {
            }
        }

        return paths
            .Where(path => path.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase)
                           || path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private async void OnImportModpackClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not LibraryViewModel viewModel)
        {
            return;
        }

        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null)
        {
            return;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Localizer.Get("L.Library.ImportModpack"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                // Both pack formats: the archive's own root entry decides which installer runs.
                new FilePickerFileType(Localizer.Get("L.Picker.MinecraftModpack"))
                {
                    Patterns = ["*.mrpack", "*.zip"],
                },
            ],
        });

        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrEmpty(path))
        {
            await viewModel.ImportModpackAsync(path);
        }
    }
}
