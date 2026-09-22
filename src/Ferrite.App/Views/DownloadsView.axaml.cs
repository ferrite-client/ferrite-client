using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Ferrite.App.ViewModels;

namespace Ferrite.App.Views;

public partial class DownloadsView : UserControl
{
    public DownloadsView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>Re-reads the operation log, so a row is never a stale copy of the file on disk.</summary>
    private void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DownloadsViewModel viewModel)
        {
            viewModel.Refresh();
        }
    }
}
