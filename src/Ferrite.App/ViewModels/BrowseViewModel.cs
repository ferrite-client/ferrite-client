using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ferrite.App.Localization;
using Ferrite.App.Services;
using Ferrite.Core.Content;
using Ferrite.Core.Loaders;
using Ferrite.Core.Minecraft;
using Ferrite.Core.Storage;

namespace Ferrite.App.ViewModels;

/// <summary>Content browser: search a provider, pick a version, and install into an instance.</summary>
public sealed partial class BrowseViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly MainWindowViewModel _shell;

    public BrowseViewModel(AppServices services, MainWindowViewModel shell)
    {
        _services = services;
        _shell = shell;
        Providers = services.ContentProviders
            .Select(provider => new ProviderOption(provider.Name, ContentProviderNames.DisplayNameFor(provider.Name)))
            .ToList();
        ProjectTypes = ["mod", "modpack", "resourcepack", "shader"];
        SortOptions = ["relevance", "downloads", "follows", "newest", "updated"];
        // Assigned to the field so construction does not kick off a search before instances load.
        _selectedProvider = Providers.FirstOrDefault();
        SelectedProjectType = "mod";
        SelectedSort = "relevance";
    }

    /// <summary>A provider the user can browse, labelled for display.</summary>
    public sealed record ProviderOption(string Name, string DisplayName);

    public ObservableCollection<ContentSummary> Results { get; } = [];

    public ObservableCollection<ContentVersion> Versions { get; } = [];

    public ObservableCollection<InstanceRecord> Instances { get; } = [];

    public IReadOnlyList<string> ProjectTypes { get; }

    public IReadOnlyList<string> SortOptions { get; }

    public IReadOnlyList<ProviderOption> Providers { get; }

    [ObservableProperty]
    private ProviderOption? _selectedProvider;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string? _selectedProjectType;

    [ObservableProperty]
    private string? _selectedSort;

    [ObservableProperty]
    private string? _gameVersionFilter;

    [ObservableProperty]
    private string? _loaderFilter;

    [ObservableProperty]
    private ContentSummary? _selectedResult;

    [ObservableProperty]
    private ContentVersion? _selectedVersion;

    [ObservableProperty]
    private InstanceRecord? _targetInstance;

    [ObservableProperty]
    private bool _includeOptionalDependencies;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _statusNote;

    [ObservableProperty]
    private string? _resultSummary;

    /// <summary>Set when the last response came from the cache instead of the network.</summary>
    [ObservableProperty]
    private string? _cacheNote;

    public IContentProvider ActiveProvider => _services.ContentProviders
        .FirstOrDefault(provider => provider.Name == (SelectedProvider?.Name ?? string.Empty))
        ?? _services.ContentProviders[0];

    /// <summary>Set when the active provider cannot be queried, such as a missing API key.</summary>
    public string? ProviderNote => ActiveProvider.UnavailableReason;

    public string SearchPlaceholder => Localizer.Format(
        "L.Browse.SearchPlaceholder",
        SelectedProvider?.DisplayName ?? string.Empty);

    public bool HasResults => Results.Count > 0;

    public bool HasSelection => SelectedResult is not null;

    public bool CanInstall => TargetInstance is not null && SelectedVersion is not null;

    partial void OnSelectedProviderChanged(ProviderOption? value)
    {
        OnPropertyChanged(nameof(ProviderNote));
        OnPropertyChanged(nameof(SearchPlaceholder));
        Results.Clear();
        Versions.Clear();
        SelectedResult = null;
        SelectedVersion = null;
        ResultSummary = null;
        CacheNote = null;
        StatusNote = null;
        OnPropertyChanged(nameof(HasResults));
        _ = SearchAsync();
    }

    partial void OnSelectedResultChanged(ContentSummary? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        _ = LoadVersionsAsync();
    }

    partial void OnSelectedVersionChanged(ContentVersion? value) => OnPropertyChanged(nameof(CanInstall));

    partial void OnTargetInstanceChanged(InstanceRecord? value) => OnPropertyChanged(nameof(CanInstall));

    public async Task InitializeAsync()
    {
        await LoadInstancesAsync().ConfigureAwait(true);
        if (Results.Count == 0)
        {
            await SearchAsync().ConfigureAwait(true);
        }
    }

    private async Task LoadInstancesAsync()
    {
        try
        {
            var records = await _services.Instances.LoadAllAsync(CancellationToken.None).ConfigureAwait(true);
            Instances.Clear();
            foreach (var record in records.OrderBy(record => record.Name))
            {
                Instances.Add(record);
            }

            TargetInstance = Instances.FirstOrDefault();
            if (TargetInstance is { } instance)
            {
                GameVersionFilter = instance.MinecraftVersion;
                LoaderFilter = instance.Loader.ToContentProviderToken();
            }
        }
        catch (Exception exception)
        {
            _shell.ReportError(exception.Message);
        }
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        var provider = ActiveProvider;
        if (!provider.IsConfigured)
        {
            // ProviderNote already carries the explanation; a duplicate status line would only
            // repeat it.
            StatusNote = null;
            return;
        }

        IsBusy = true;
        try
        {
            StatusNote = null;
            var type = SelectedProjectType is { } text ? ContentProjectTypes.Parse(text) : (ContentProjectType?)null;
            var result = await provider
                .SearchAsync(
                    new ContentSearchQuery(
                        SearchText ?? string.Empty,
                        type,
                        string.IsNullOrWhiteSpace(GameVersionFilter) ? null : GameVersionFilter,
                        string.IsNullOrWhiteSpace(LoaderFilter) ? null : LoaderFilter,
                        Limit: 30,
                        SortBy: SelectedSort ?? "relevance"),
                    CancellationToken.None)
                .ConfigureAwait(true);

            Results.Clear();
            foreach (var hit in result.Hits)
            {
                Results.Add(hit);
            }

            ResultSummary = Localizer.Format("L.Browse.Results", result.TotalHits);
            RefreshCacheNote();
            OnPropertyChanged(nameof(HasResults));
            SelectedResult = Results.FirstOrDefault();
        }
        catch (Exception exception)
        {
            StatusNote = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadVersionsAsync()
    {
        Versions.Clear();
        SelectedVersion = null;
        if (SelectedResult is not { } project)
        {
            return;
        }

        try
        {
            var loader = string.IsNullOrWhiteSpace(LoaderFilter) ? null : LoaderFilter;
            var gameVersion = string.IsNullOrWhiteSpace(GameVersionFilter) ? null : GameVersionFilter;
            var provider = ActiveProvider;
            var versions = await provider
                .GetVersionsAsync(project.ProjectId, gameVersion, loader, CancellationToken.None)
                .ConfigureAwait(true);

            foreach (var version in versions.Take(50))
            {
                Versions.Add(version);
            }

            SelectedVersion = provider.SelectBestVersion(versions, gameVersion, loader);
            RefreshCacheNote();
            if (SelectedVersion is null && versions.Count > 0)
            {
                StatusNote = Localizer.Format("L.Browse.NoCompatible", project.Title);
            }
        }
        catch (Exception exception)
        {
            StatusNote = exception.Message;
        }
    }

    /// <summary>
    /// Says so when a provider answered from the cache. A stale result presented as live would be
    /// worse than an empty list, because the user would trust it.
    /// </summary>
    private void RefreshCacheNote()
    {
        CacheNote = ActiveProvider is CachedContentProvider { LastCacheHit: { } hit } cached
            ? Localizer.Format(
                "L.Browse.CacheNote",
                ContentProviderNames.DisplayNameFor(cached.Name),
                hit.AgeText)
            : null;
    }
}
