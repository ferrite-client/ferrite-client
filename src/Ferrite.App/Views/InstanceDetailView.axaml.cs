using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Ferrite.App.Localization;
using Ferrite.App.ViewModels;
using Ferrite.Core.Util;

namespace Ferrite.App.Views;

public partial class InstanceDetailView : UserControl
{
    private InstanceDetailViewModel? _boundViewModel;

    public InstanceDetailView()
    {
        InitializeComponent();
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        DataContextChanged += OnDataContextChanged;
    }

    /// <summary>
    /// Keeps the newest log line in view while "follow" is on. The subscription follows the view
    /// model rather than the page, because the same view is reused for the next instance the user opens.
    /// </summary>
    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_boundViewModel is { } previous)
        {
            previous.VisibleLogLines.CollectionChanged -= OnLogLinesChanged;
        }

        _boundViewModel = DataContext as InstanceDetailViewModel;
        if (_boundViewModel is { } current)
        {
            current.VisibleLogLines.CollectionChanged += OnLogLinesChanged;
        }
    }

    private void OnLogLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_boundViewModel is not { FollowTail: true })
        {
            return;
        }

        if (this.FindControl<ListBox>("LogList") is { ItemCount: > 0 } list)
        {
            list.ScrollIntoView(list.ItemCount - 1);
        }
    }

    private void OnOpenLogsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is InstanceDetailViewModel viewModel)
        {
            ShellOpen.Directory(Path.Combine(viewModel.GameDirectory, "logs"));
        }
    }

    /// <summary>Copies the whole log, so a problem can be pasted somewhere it can be read.</summary>
    private async void OnCopyLogClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not InstanceDetailViewModel viewModel
            || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            return;
        }

        await clipboard.SetTextAsync(viewModel.LogText);
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
            Title = Localizer.Get("L.Instance.AddMods"),
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType(Localizer.Get("L.Picker.MinecraftMods"))
                {
                    Patterns = ["*.jar", "*.zip"],
                },
            ],
        });

        await viewModel.InstallModFilesAsync(LocalPaths(files));
    }

    private static List<string> LocalPaths(IEnumerable<IStorageItem> items) =>
        items.Select(item => item.TryGetLocalPath())
            .Where(path => !string.IsNullOrEmpty(path))
            .Select(path => path!)
            .ToList();

    /// <summary>
    /// Picks a pack archive to apply over this instance. Both pack formats are offered because the
    /// archive's own root entry decides which installer runs.
    /// </summary>
    private async void OnUpdateFromPackClick(object? sender, RoutedEventArgs e)
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
            Title = Localizer.Get("L.Instance.UpdateFromPack"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(Localizer.Get("L.Picker.MinecraftModpack"))
                {
                    Patterns = ["*.mrpack", "*.zip"],
                },
            ],
        });

        await viewModel.UpdateFromArchiveAsync(files.FirstOrDefault()?.TryGetLocalPath());
    }

    private async void OnExportModpackClick(object? sender, RoutedEventArgs e)
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

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Localizer.Get("L.Instance.ExportPackTitle"),
            SuggestedFileName = viewModel.Name + ".mrpack",
            DefaultExtension = "mrpack",
            FileTypeChoices =
            [
                new FilePickerFileType(Localizer.Get("L.Picker.ModrinthModpack"))
                {
                    Patterns = ["*.mrpack"],
                },
            ],
        });

        var path = file?.TryGetLocalPath();
        if (!string.IsNullOrEmpty(path))
        {
            await viewModel.ExportModpackAsync(path);
        }
    }

    /// <summary>
    /// Picks an OptiFine installer the user downloaded. OptiFine requires a manual download, so the
    /// launcher cannot fetch it; it can only run what the user already has.
    /// </summary>
    private async void OnInstallOptiFineClick(object? sender, RoutedEventArgs e)
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
            Title = Localizer.Get("L.Instance.SelectOptiFine"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(Localizer.Get("L.Picker.OptiFineInstaller"))
                {
                    Patterns = ["*.jar"],
                },
            ],
        });

        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrEmpty(path))
        {
            await viewModel.InstallOptiFineAsync(path);
        }
    }

    private void OnOpenSavesClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is InstanceDetailViewModel viewModel)
        {
            ShellOpen.Directory(Path.Combine(viewModel.GameDirectory, "saves"));
        }
    }

    /// <summary>Picks a structure file to preview, from anywhere on disk.</summary>
    private async void OnPickStructureFileClick(object? sender, RoutedEventArgs e)
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
            Title = Localizer.Get("L.Instance.StructureChoose"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(Localizer.Get("L.Picker.MinecraftStructure"))
                {
                    Patterns = ["*.nbt"],
                },
            ],
        });

        await viewModel.LoadStructureAsync(files.FirstOrDefault()?.TryGetLocalPath());
    }

    /// <summary>
    /// A click on the map selects the chunk under the pointer. The image is drawn at its own pixel
    /// size, so the pointer's position in the control is a position in the map.
    /// </summary>
    private void OnWorldMapPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not InstanceDetailViewModel viewModel
            || sender is not Control visual)
        {
            return;
        }

        var position = e.GetPosition(visual);
        viewModel.ToggleChunkAt((int)position.X, (int)position.Y);
    }

    /// <summary>Exports the prepared local server, world included, as a zip the user chooses.</summary>
    private async void OnExportServerClick(object? sender, RoutedEventArgs e)
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

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Localizer.Get("L.Instance.ServerExport"),
            SuggestedFileName = viewModel.Name + "-server.zip",
            DefaultExtension = "zip",
            FileTypeChoices =
            [
                new FilePickerFileType(Localizer.Get("L.Picker.ZipArchive"))
                {
                    Patterns = ["*.zip"],
                },
            ],
        });

        await viewModel.ExportServerAsync(file?.TryGetLocalPath());
    }

    /// <summary>
    /// Picks a background image for the instance's theme. The file is copied into the instance, so it
    /// keeps working after the original is moved or deleted.
    /// </summary>
    private async void OnPickThemeBackgroundClick(object? sender, RoutedEventArgs e)
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
            Title = Localizer.Get("L.Instance.ChooseThemeBackground"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(Localizer.Get("L.Picker.Images"))
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp"],
                },
            ],
        });

        await viewModel.SetThemeBackgroundAsync(files.FirstOrDefault()?.TryGetLocalPath());
    }

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
