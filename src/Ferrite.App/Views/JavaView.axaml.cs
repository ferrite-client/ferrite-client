using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Ferrite.App.ViewModels;

namespace Ferrite.App.Views;

public partial class JavaView : UserControl
{
    public JavaView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Picks a Java executable. The view model probes it before accepting it, so a wrong file is
    /// refused with a message instead of being stored and failing at launch time.
    /// </summary>
    private async void OnAddJavaPathClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not JavaViewModel viewModel)
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
            Title = "Add a Java runtime",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Java executable")
                {
                    Patterns = OperatingSystem.IsWindows() ? ["java.exe", "javaw.exe"] : ["java"],
                },
            ],
        });

        await viewModel.AddJavaPathAsync(files.FirstOrDefault()?.TryGetLocalPath());
    }
}
