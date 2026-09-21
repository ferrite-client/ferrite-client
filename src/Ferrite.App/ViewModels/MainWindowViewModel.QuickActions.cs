using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ferrite.App.Localization;
using Ferrite.Core.Loaders;
using Ferrite.Core.Minecraft;
using Ferrite.Core.Storage;

namespace Ferrite.App.ViewModels;

/// <summary>
/// The quick-action palette: the pages, plus every instance, reachable without leaving the page you
/// are on. It is a filter over a list built at the moment it opens, so it never shows an instance
/// that has since been deleted.
/// </summary>
public sealed partial class MainWindowViewModel
{
    [ObservableProperty]
    private bool _isQuickActionsOpen;

    [ObservableProperty]
    private string _quickQuery = string.Empty;

    public ObservableCollection<QuickActionItem> QuickResults { get; } = [];

    public bool HasQuickResults => QuickResults.Count > 0;

    public bool HasNoQuickResults => QuickResults.Count == 0;

    [RelayCommand]
    private void OpenQuickActions()
    {
        QuickQuery = string.Empty;
        IsQuickActionsOpen = true;
    }

    [RelayCommand]
    private void CloseQuickActions()
    {
        IsQuickActionsOpen = false;
        QuickQuery = string.Empty;
    }

    partial void OnQuickQueryChanged(string value) => RebuildQuickResults();

    partial void OnIsQuickActionsOpenChanged(bool value)
    {
        if (value)
        {
            RebuildQuickResults();
        }
    }

    /// <summary>
    /// Rebuilds the results from the current query. Navigation and creation come first so they stay
    /// reachable while the query is still being typed; instances follow.
    /// </summary>
    private void RebuildQuickResults()
    {
        var query = QuickQuery?.Trim() ?? string.Empty;
        var items = new List<QuickActionItem>
        {
            NavigateItem(AppPage.Library),
            NavigateItem(AppPage.Browse),
            NavigateItem(AppPage.Java),
            NavigateItem(AppPage.Accounts),
            NavigateItem(AppPage.Settings),
            Item(
                Localizer.Get("L.Library.NewInstance"),
                Localizer.Get("L.Nav.Library"),
                () => _ = Library.OpenCreateCommand.ExecuteAsync(null)),
        };

        foreach (var record in Library.Instances.Select(card => card.Record))
        {
            items.Add(Item(
                Localizer.Format("L.Quick.OpenInstance", record.Name),
                Describe(record),
                () => OpenInstance(record)));

            if (Library.Instances.FirstOrDefault(card => card.Record.Id == record.Id) is { IsRunning: false } card)
            {
                items.Add(Item(
                    Localizer.Format("L.Quick.LaunchInstance", record.Name),
                    Describe(record),
                    () => _ = card.PlayCommand.ExecuteAsync(null)));
            }
        }

        QuickResults.Clear();
        foreach (var item in items)
        {
            if (query.Length == 0
                || item.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            {
                QuickResults.Add(item);
            }
        }

        OnPropertyChanged(nameof(HasQuickResults));
        OnPropertyChanged(nameof(HasNoQuickResults));
    }

    private QuickActionItem NavigateItem(AppPage page)
    {
        var title = Localizer.Format("L.Quick.GoTo", Localizer.Get(page switch
        {
            AppPage.Library => "L.Nav.Library",
            AppPage.Browse => "L.Nav.Browse",
            AppPage.Java => "L.Nav.Java",
            AppPage.Accounts => "L.Nav.Accounts",
            _ => "L.Nav.Settings",
        }));
        return Item(title, null, () => CurrentPage = page);
    }

    private QuickActionItem Item(string title, string? detail, Action action) =>
        new(title, detail, new RelayCommand(() =>
        {
            CloseQuickActions();
            action();
        }));

    private void OpenInstance(InstanceRecord record)
    {
        CurrentPage = AppPage.Library;
        DetailPage = new InstanceDetailViewModel(record, _services, this);
    }

    private static string Describe(InstanceRecord record) => record.Loader == LoaderKind.Vanilla
        ? $"Minecraft {record.MinecraftVersion}"
        : $"Minecraft {record.MinecraftVersion} · {record.Loader.ToDisplayName()} {record.LoaderVersion}";
}
