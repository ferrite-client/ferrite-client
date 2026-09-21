using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Ferrite.App.Services;
using Ferrite.Core.Content;
using Ferrite.Core.Java;
using Ferrite.Core.Minecraft;
using Ferrite.Core.Storage;

namespace Ferrite.App.ViewModels;

/// <summary>Instance detail: launch settings, installed content, and logs.</summary>
public sealed partial class InstanceDetailViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly MainWindowViewModel _shell;

    public InstanceDetailViewModel(InstanceRecord record, AppServices services, MainWindowViewModel shell)
    {
        _services = services;
        _shell = shell;
        Record = record;

        MemoryMb = record.MemoryMb;
        MinMemoryMb = record.MinMemoryMb;
        WindowWidth = record.WindowWidth;
        WindowHeight = record.WindowHeight;
        DemoMode = record.DemoMode;
        JvmArgumentsText = string.Join(Environment.NewLine, record.JvmArguments);
        GameArgumentsText = string.Join(Environment.NewLine, record.GameArguments);
        ServerAddress = record.LastServerAddress;
    }

    public InstanceRecord Record { get; }

    public string Name => Record.Name;

    public string VersionId => InstanceLauncher.LaunchVersionId(Record);

    public string GameDirectory => _services.Paths.InstanceGameDirectory(Record.Id);

    public string LoaderText => Record.Loader == LoaderKind.Vanilla
        ? "Vanilla"
        : $"{Record.Loader.ToDisplayName()} {Record.LoaderVersion}";

    public ObservableCollection<ModItemViewModel> Mods { get; } = [];

    public ObservableCollection<ContentFileEntry> ResourcePacks { get; } = [];

    public ObservableCollection<ContentFileEntry> ShaderPacks { get; } = [];

    public ObservableCollection<ContentFileEntry> Screenshots { get; } = [];

    public ObservableCollection<JavaRuntime> JavaRuntimes { get; } = [];

    [ObservableProperty]
    private int? _memoryMb;

    [ObservableProperty]
    private int? _minMemoryMb;

    [ObservableProperty]
    private int? _windowWidth;

    [ObservableProperty]
    private int? _windowHeight;

    [ObservableProperty]
    private bool _demoMode;

    [ObservableProperty]
    private string _jvmArgumentsText = string.Empty;

    [ObservableProperty]
    private string _gameArgumentsText = string.Empty;

    [ObservableProperty]
    private string? _serverAddress;

    [ObservableProperty]
    private JavaRuntime? _selectedJava;

    [ObservableProperty]
    private string _logText = string.Empty;

    [ObservableProperty]
    private string? _statusNote;

    [ObservableProperty]
    private bool _isBusy;

    public bool HasMods => Mods.Count > 0;

    public bool HasResourcePacks => ResourcePacks.Count > 0;

    public bool HasShaderPacks => ShaderPacks.Count > 0;

    public bool HasScreenshots => Screenshots.Count > 0;

    public async Task InitializeAsync()
    {
        await LoadJavaRuntimesAsync().ConfigureAwait(true);
        await RefreshModsAsync().ConfigureAwait(true);
        RefreshFolders();
        await RefreshLogAsync().ConfigureAwait(true);
        await RefreshWorldsAsync().ConfigureAwait(true);
        RefreshServers();
    }

    private async Task LoadJavaRuntimesAsync()
    {
        try
        {
            var required = JavaCompatibility.RequiredMajorFor(Record.MinecraftVersion);
            var runtimes = await _services.Java.DetectAsync(CancellationToken.None).ConfigureAwait(true);
            JavaRuntimes.Clear();
            foreach (var runtime in JavaSelection.Rank(runtimes, required))
            {
                JavaRuntimes.Add(runtime);
            }

            SelectedJava = !string.IsNullOrEmpty(Record.JavaPath)
                ? JavaRuntimes.FirstOrDefault(runtime =>
                    string.Equals(runtime.ExecutablePath, Record.JavaPath, StringComparison.OrdinalIgnoreCase))
                : JavaSelection.SelectBest(runtimes, required);
        }
        catch (Exception exception)
        {
            StatusNote = exception.Message;
        }
    }

    public async Task RefreshModsAsync()
    {
        try
        {
            var mods = await _services.Mods.ListModsAsync(GameDirectory, CancellationToken.None).ConfigureAwait(true);
            Mods.Clear();
            foreach (var mod in mods)
            {
                Mods.Add(new ModItemViewModel(mod, _services, () => _ = RefreshModsAsync()));
            }

            OnPropertyChanged(nameof(HasMods));
        }
        catch (Exception exception)
        {
            StatusNote = exception.Message;
        }
    }

    private void RefreshFolders()
    {
        Replace(ResourcePacks, InstanceContentManager.ListFolder(GameDirectory, "resourcepacks"));
        Replace(ShaderPacks, InstanceContentManager.ListFolder(GameDirectory, "shaderpacks"));
        Replace(Screenshots, InstanceContentManager.ListFolder(GameDirectory, "screenshots"));
        OnPropertyChanged(nameof(HasResourcePacks));
        OnPropertyChanged(nameof(HasShaderPacks));
        OnPropertyChanged(nameof(HasScreenshots));
    }

    private static void Replace(ObservableCollection<ContentFileEntry> target, IReadOnlyList<ContentFileEntry> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }

    public async Task RefreshLogAsync()
    {
        var logsDirectory = Path.Combine(GameDirectory, "logs");
        var latest = Directory.Exists(logsDirectory)
            ? Directory.EnumerateFiles(logsDirectory, "*.log")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault()
            : null;

        if (latest is null)
        {
            LogText = "No logs yet.";
            return;
        }

        try
        {
            LogText = await Task.Run(() => ReadTail(latest, 400), CancellationToken.None).ConfigureAwait(true);
        }
        catch (IOException exception)
        {
            LogText = exception.Message;
        }
    }

    private static string ReadTail(string path, int lineCount)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        var lines = new Queue<string>(lineCount);
        while (reader.ReadLine() is { } line)
        {
            lines.Enqueue(line);
            if (lines.Count > lineCount)
            {
                lines.Dequeue();
            }
        }

        return string.Join(Environment.NewLine, lines);
    }
}
