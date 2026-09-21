using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Ferrite.App.ViewModels;

namespace Ferrite.App.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private async void OnExportDiagnosticsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel viewModel)
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
            Title = "Export diagnostics bundle",
            SuggestedFileName = $"ferrite-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip",
            DefaultExtension = "zip",
            FileTypeChoices =
            [
                new FilePickerFileType("Zip archive") { Patterns = ["*.zip"] },
            ],
        });

        var path = file?.TryGetLocalPath();
        if (!string.IsNullOrEmpty(path))
        {
            await viewModel.ExportDiagnosticsAsync(path);
        }
    }
}
