using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ferrite.App.Services;
using Ferrite.Core.Content;
using Ferrite.Core.Loaders;
using Ferrite.Core.Minecraft;
using Ferrite.Core.Storage;

namespace Ferrite.App.ViewModels;

/// <summary>Modrinth browser: search, versions, and installation into an instance.</summary>
public sealed partial class BrowseViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly MainWindowViewModel _shell;

    public BrowseViewModel(AppServices services, MainWindowViewModel shell)
    {
        _services = services;
        _shell = shell;
        ProjectTypes = ["mod", "modpack", "resourcepack", "shader"];
        SortOptions = ["relevance", "downloads", "follows", "newest", "updated"];
        SelectedProjectType = "mod";
        SelectedSort = "relevance";
    }

    public ObservableCollection<ContentSummary> Results { get; } = [];

    public ObservableCollection<ContentVersion> Versions { get; } = [];

    public ObservableCollection<InstanceRecord> Instances { get; } = [];

    public IReadOnlyList<string> ProjectTypes { get; }

    public IReadOnlyList<string> SortOptions { get; }

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

    public bool HasResults => Results.Count > 0;

    public bool HasSelection => SelectedResult is not null;

    public bool CanInstall => TargetInstance is not null && SelectedVersion is not null;

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
        IsBusy = true;
        try
        {
            StatusNote = null;
            var type = SelectedProjectType is { } text ? ContentProjectTypes.Parse(text) : (ContentProjectType?)null;
            var result = await _services.Modrinth
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

            ResultSummary = $"{result.TotalHits:N0} results";
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
            var versions = await _services.Modrinth
                .GetVersionsAsync(project.ProjectId, gameVersion, loader, CancellationToken.None)
                .ConfigureAwait(true);

            foreach (var version in versions.Take(50))
            {
                Versions.Add(version);
            }

            SelectedVersion = _services.Modrinth.SelectBestVersion(versions, gameVersion, loader);
            if (SelectedVersion is null && versions.Count > 0)
            {
                StatusNote = $"No {project.Title} version matches this instance's game version or loader.";
            }
        }
        catch (Exception exception)
        {
            StatusNote = exception.Message;
        }
    }
}
