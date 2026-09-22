using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Ferrite.App.Services;
using Ferrite.App.Localization;
using Ferrite.Core.Content;
using Ferrite.Core.Diagnostics;
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
        AcceptEula = record.AcceptEula;
        Group = record.Group ?? string.Empty;
        JvmArgumentsText = string.Join(Environment.NewLine, record.JvmArguments);
        GameArgumentsText = string.Join(Environment.NewLine, record.GameArguments);
        ServerAddress = record.LastServerAddress;
        LoadTheme();
        LoadServerOptions();
    }

    /// <summary>
    /// The instance the page is showing. Settable because a modpack install can change its loader and
    /// its pack identity underneath the page, and the page has to show what is now true.
    /// </summary>
    public InstanceRecord Record { get; private set; }

    /// <summary>Replaces the record after something rewrote it, and refreshes what the page shows.</summary>
    private void ApplyRecord(InstanceRecord updated)
    {
        Record = updated;
        OnPropertyChanged(nameof(Record));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(VersionId));
        OnPropertyChanged(nameof(LoaderText));
        OnPropertyChanged(nameof(LoaderSummary));
        RefreshHero();
    }

    public string Name => Record.Name;

    public string VersionId => InstanceLauncher.LaunchVersionId(Record);

    public string GameDirectory => _services.Paths.InstanceGameDirectory(Record.Id);

    public string LoaderText => Record.Loader == LoaderKind.Vanilla
        ? "Vanilla"
        : $"{Record.Loader.ToDisplayName()} {Record.LoaderVersion}";

    public ObservableCollection<ModItemViewModel> Mods { get; } = [];

    public ObservableCollection<InstanceContentItemViewModel> ResourcePacks { get; } = [];

    public ObservableCollection<InstanceContentItemViewModel> ShaderPacks { get; } = [];

    /// <summary>The datapacks of each saved world, in world order.</summary>
    public ObservableCollection<WorldDatapacksViewModel> WorldDatapacks { get; } = [];

    public ObservableCollection<ScreenshotItemViewModel> Screenshots { get; } = [];

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

    /// <summary>Written to <c>eula.txt</c> so the game will run; off unless the user turns it on.</summary>
    [ObservableProperty]
    private bool _acceptEula;

    [ObservableProperty]
    private string _jvmArgumentsText = string.Empty;

    [ObservableProperty]
    private string _gameArgumentsText = string.Empty;

    [ObservableProperty]
    private string? _serverAddress;

    /// <summary>The folder this instance is filed under. Blank means it is not in any folder.</summary>
    [ObservableProperty]
    private string _group = string.Empty;

    [ObservableProperty]
    private JavaRuntime? _selectedJava;

    [ObservableProperty]
    private string _logText = string.Empty;

    /// <summary>Parsed crash reports found in the instance, newest first.</summary>
    public ObservableCollection<CrashReport> CrashReports { get; } = [];

    [ObservableProperty]
    private CrashReport? _selectedCrashReport;

    [ObservableProperty]
    private string _crashAnalysisSummary = string.Empty;

    public bool HasCrashReports => CrashReports.Count > 0;

    partial void OnSelectedCrashReportChanged(CrashReport? value) => _ = AnalyzeCrashAsync(value);

    [ObservableProperty]
    private string? _statusNote;

    [ObservableProperty]
    private bool _isBusy;

    public bool HasMods => Mods.Count > 0;

    public bool HasResourcePacks => ResourcePacks.Count > 0;

    public bool HasShaderPacks => ShaderPacks.Count > 0;

    public bool HasScreenshots => Screenshots.Count > 0;

    /// <summary>
    /// Rebuilds the packs, the per-world datapacks, and the screenshot gallery. Called after the
    /// view model opens and after a content item is enabled, disabled, or removed.
    /// </summary>
    public async Task RefreshContentAsync()
    {
        RefreshFolders();
        await RefreshWorldsAsync().ConfigureAwait(true);
    }

    public async Task InitializeAsync()
    {
        await LoadJavaRuntimesAsync().ConfigureAwait(true);
        await RefreshModsAsync().ConfigureAwait(true);
        RefreshFolders();
        await RefreshLogAsync().ConfigureAwait(true);
        await RefreshCrashReportsAsync().ConfigureAwait(true);
        await RefreshWorldsAsync().ConfigureAwait(true);
        RefreshServers();
        await RefreshStructuresAsync().ConfigureAwait(true);
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
            SetScannedMods(mods);
        }
        catch (Exception exception)
        {
            StatusNote = exception.Message;
        }
    }

    private void RefreshFolders()
    {
        // Pack formats come from the version's own client file, so an unknown version stays unknown
        // rather than being guessed from a table that would drift.
        var formats = _services.PackFormats.Read(Record.MinecraftVersion);
        Replace(
            ResourcePacks,
            InstanceContentManager.ListPacks(GameDirectory, "resourcepacks", formats?.Resource));
        Replace(
            ShaderPacks,
            InstanceContentManager.ListPacks(GameDirectory, "shaderpacks", null));

        Screenshots.Clear();
        foreach (var screenshot in InstanceContentManager.ListFolder(GameDirectory, "screenshots"))
        {
            var item = new ScreenshotItemViewModel(screenshot);
            item.LoadThumbnail();
            Screenshots.Add(item);
        }

        OnPropertyChanged(nameof(HasResourcePacks));
        OnPropertyChanged(nameof(HasShaderPacks));
        OnPropertyChanged(nameof(HasScreenshots));
    }

    private void Replace(
        ObservableCollection<InstanceContentItemViewModel> target,
        IReadOnlyList<ContentFileEntry> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(new InstanceContentItemViewModel(item, ContentBackupDirectory, RefreshFolders));
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
            LogText = Localizer.Get("L.Instance.NoLogs");
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

    /// <summary>Reads and parses the instance's crash reports, newest first.</summary>
    public async Task RefreshCrashReportsAsync()
    {
        var crashDirectory = Path.Combine(GameDirectory, "crash-reports");
        var reports = await Task.Run(() => ReadCrashReports(crashDirectory), CancellationToken.None)
            .ConfigureAwait(true);

        var previous = SelectedCrashReport?.Path;
        CrashReports.Clear();
        foreach (var report in reports)
        {
            CrashReports.Add(report);
        }

        OnPropertyChanged(nameof(HasCrashReports));
        SelectedCrashReport = CrashReports.FirstOrDefault(report => report.Path == previous)
            ?? CrashReports.FirstOrDefault();
        if (SelectedCrashReport is null)
        {
            CrashAnalysisSummary = string.Empty;
            return;
        }

        await AnalyzeCrashAsync(SelectedCrashReport).ConfigureAwait(true);
    }

    private async Task AnalyzeCrashAsync(CrashReport? report)
    {
        if (report is null)
        {
            CrashAnalysisSummary = string.Empty;
            return;
        }

        var mods = Mods.Select(item => item.Metadata).ToList();
        var analysis = await Task
            .Run(() => CrashAnalyzer.Analyze(report, mods), CancellationToken.None)
            .ConfigureAwait(true);
        CrashAnalysisSummary = CrashAnalysisText.Render(analysis, Record);
    }

    private static IReadOnlyList<CrashReport> ReadCrashReports(string crashDirectory)
    {
        if (!Directory.Exists(crashDirectory))
        {
            return [];
        }

        var reports = new List<CrashReport>();
        try
        {
            foreach (var path in Directory
                         .EnumerateFiles(crashDirectory, "*.txt")
                         .OrderByDescending(File.GetLastWriteTimeUtc)
                         .Take(20))
            {
                string text;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(stream))
                {
                    var buffer = new char[512 * 1024];
                    var read = reader.ReadBlock(buffer, 0, buffer.Length);
                    text = new string(buffer, 0, read);
                }

                if (CrashReportParser.LooksLikeCrashReport(text))
                {
                    reports.Add(CrashReportParser.Parse(path, text));
                }
            }
        }
        catch (IOException)
        {
            return reports;
        }

        return reports;
    }
}
