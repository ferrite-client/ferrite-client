using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Ferrite.App.ViewModels;

namespace Ferrite.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Copies the error the banner is showing. Producing text for the clipboard is a platform act, so
    /// it lives here rather than in the view model.
    /// </summary>
    private async void OnCopyErrorClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel { ErrorMessage: { Length: > 0 } message }
            && Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(message);
        }
    }
}
