using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Ferrite.App.ViewModels;

namespace Ferrite.App.Views;

public partial class InstanceDetailView : UserControl
{
    public InstanceDetailView()
    {
        InitializeComponent();
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = TryGetFiles(e.DataTransfer).Count > 0
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not InstanceDetailViewModel viewModel)
        {
            return;
        }

        await viewModel.InstallModFilesAsync(LocalPaths(TryGetFiles(e.DataTransfer)));
    }

    private async void OnAddModsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not InstanceDetailViewModel viewModel)
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
            Title = "Add mods",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("Minecraft mods") { Patterns = ["*.jar", "*.zip"] },
            ],
        });

        await viewModel.InstallModFilesAsync(LocalPaths(files));
    }

    private static List<string> LocalPaths(IEnumerable<IStorageItem> items) =>
        items.Select(item => item.TryGetLocalPath())
            .Where(path => !string.IsNullOrEmpty(path))
            .Select(path => path!)
            .ToList();

    /// <summary>Reads file items from a drag payload, tolerating payloads without files.</summary>
    private static IReadOnlyList<IStorageItem> TryGetFiles(IDataTransfer? transfer)
    {
        if (transfer is null)
        {
            return [];
        }

        var files = new List<IStorageItem>();
        foreach (var item in transfer.Items)
        {
            try
            {
                if (item.TryGetRaw(DataFormat.File) is IStorageItem file)
                {
                    files.Add(file);
                }
            }
            catch (Exception exception) when (exception is NotSupportedException or InvalidOperationException)
            {
            }
        }

        return files;
    }
}
