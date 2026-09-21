using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Ferrite.App.ViewModels;

namespace Ferrite.App.Views;

public partial class LibraryView : UserControl
{
    public LibraryView()
    {
        InitializeComponent();
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

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
            Title = "Import a modpack",
            AllowMultiple = false,
            FileTypeFilter =
            [
                // Both pack formats: the archive's own root entry decides which installer runs.
                new FilePickerFileType("Minecraft modpack") { Patterns = ["*.mrpack", "*.zip"] },
            ],
        });

        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrEmpty(path))
        {
            await viewModel.ImportModpackAsync(path);
        }
    }
}
