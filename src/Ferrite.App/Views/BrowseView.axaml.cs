using Avalonia.Controls;
using Ferrite.App.ViewModels;

namespace Ferrite.App.Views;

public partial class BrowseView : UserControl
{
    public BrowseView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Result icons are remote images, so they are fetched when a row is realised rather than for the
    /// whole page of results at once, and released when the row scrolls out of view.
    /// </summary>
    private static void OnResultPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        if (e.Container?.DataContext is ContentSummaryViewModel result)
        {
            _ = result.EnsureIconAsync();
        }
    }

    private static void OnResultClearing(object? sender, ContainerClearingEventArgs e)
    {
        if (e.Container?.DataContext is ContentSummaryViewModel result)
        {
            result.ReleaseIcon();
        }
    }
}
