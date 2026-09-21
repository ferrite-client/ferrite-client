namespace Ferrite.Core.Platform;

/// <summary>
/// Resolves every directory Ferrite writes to. All storage code depends on this instead of
/// composing paths itself, so relocating the data root is a single decision.
/// </summary>
public sealed class AppPaths
{
    public const string EnvironmentVariableName = "FERRITE_HOME";

    /// <summary>
    /// A file next to the executable naming the data root. It is how the Settings page's "move data
    /// folder" is remembered without asking the user to edit an environment variable.
    /// </summary>
    public const string RootMarkerFileName = "ferrite-root.txt";

    private AppPaths(string root)
    {
        Root = Path.GetFullPath(root);
    }

    /// <summary>Launcher root directory.</summary>
    public string Root { get; }

    public string ConfigDirectory => Path.Combine(Root, "config");

    public string DataDirectory => Path.Combine(Root, "data");

    public string InstancesDirectory => Path.Combine(DataDirectory, "instances");

    public string CacheDirectory => Path.Combine(DataDirectory, "cache");

    public string StoreDirectory => Path.Combine(DataDirectory, "store");

    public string LibrariesDirectory => Path.Combine(StoreDirectory, "libraries");

    public string AssetsDirectory => Path.Combine(StoreDirectory, "assets");

    public string AssetObjectsDirectory => Path.Combine(AssetsDirectory, "objects");

    public string AssetIndexesDirectory => Path.Combine(AssetsDirectory, "indexes");

    public string LegacyVirtualAssetsDirectory => Path.Combine(AssetsDirectory, "virtual", "legacy");

    public string VersionsDirectory => Path.Combine(StoreDirectory, "versions");

    public string RuntimesDirectory => Path.Combine(StoreDirectory, "runtimes");

    public string LogsDirectory => Path.Combine(Root, "logs");

    public string LauncherLogsDirectory => Path.Combine(LogsDirectory, "launcher");

    public string TemporaryDirectory => Path.Combine(Root, "tmp");

    public string BackupsDirectory => Path.Combine(Root, "backups");

    /// <summary>
    /// Where a downloaded update is unpacked before hand-off. It lives outside the install
    /// directory so replacing a running build never touches the files that are in use.
    /// </summary>
    public string UpdateStagingDirectory => Path.Combine(Root, "staging");

    public string SettingsFile => Path.Combine(ConfigDirectory, "settings.json");

    public string AccountsFile => Path.Combine(ConfigDirectory, "accounts.json");

    public string SecretsFile => Path.Combine(ConfigDirectory, "accounts.bin");

    /// <summary>Provider projects the user saved to revisit.</summary>
    public string SavedProjectsFile => Path.Combine(ConfigDirectory, "saved-projects.json");

    /// <param name="baseDirectory">
    /// Where to look for the marker files. Defaults to the executable's directory; a test supplies its
    /// own so it never touches the real installation.
    /// </param>
    public static AppPaths CreateDefault(string? baseDirectory = null)
    {
        var overridden = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        if (!string.IsNullOrWhiteSpace(overridden))
        {
            return new AppPaths(overridden);
        }

        var directory = baseDirectory ?? AppContext.BaseDirectory;

        // A root the user chose wins over the portable layout, because it was chosen deliberately.
        var rootMarker = Path.Combine(directory, RootMarkerFileName);
        if (File.Exists(rootMarker))
        {
            try
            {
                var configured = File.ReadAllText(rootMarker).Trim();
                if (configured.Length > 0 && Path.IsPathFullyQualified(configured))
                {
                    return new AppPaths(configured);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // An unreadable marker falls through to the default rather than failing startup.
            }
        }

        var portableMarker = Path.Combine(directory, "ferrite-portable.txt");
        if (File.Exists(portableMarker))
        {
            return new AppPaths(Path.Combine(directory, "ferrite-data"));
        }

        return new AppPaths(DefaultRoot());
    }

    public static AppPaths ForRoot(string root) => new(root);

    public string InstanceDirectory(Guid instanceId) =>
        Path.Combine(InstancesDirectory, instanceId.ToString("D"));

    public string InstanceMetadataFile(Guid instanceId) =>
        Path.Combine(InstanceDirectory(instanceId), "instance.json");

    /// <summary>Records which provider project each launcher-installed file came from.</summary>
    public string InstanceContentManifestFile(Guid instanceId) =>
        Path.Combine(InstanceDirectory(instanceId), "content-manifest.json");

    /// <summary>The isolated game directory ("&lt;instance&gt;/minecraft").</summary>
    public string InstanceGameDirectory(Guid instanceId) =>
        Path.Combine(InstanceDirectory(instanceId), "minecraft");

    public string InstanceNativesDirectory(Guid instanceId, string versionId) =>
        Path.Combine(InstanceDirectory(instanceId), "natives", versionId);

    public string VersionDirectory(string versionId) =>
        Path.Combine(VersionsDirectory, versionId);

    public string VersionJsonFile(string versionId) =>
        Path.Combine(VersionDirectory(versionId), versionId + ".json");

    public string VersionClientJarFile(string versionId) =>
        Path.Combine(VersionDirectory(versionId), versionId + ".jar");

    public string RuntimeDirectory(string component) =>
        Path.Combine(RuntimesDirectory, component);

    public string CacheFile(string name) => Path.Combine(CacheDirectory, name);

    public void EnsureCreated()
    {
        foreach (var directory in AllDirectories())
        {
            Directory.CreateDirectory(directory);
        }
    }

    public IEnumerable<string> AllDirectories()
    {
        yield return Root;
        yield return ConfigDirectory;
        yield return DataDirectory;
        yield return InstancesDirectory;
        yield return CacheDirectory;
        yield return StoreDirectory;
        yield return LibrariesDirectory;
        yield return AssetsDirectory;
        yield return AssetObjectsDirectory;
        yield return AssetIndexesDirectory;
        yield return VersionsDirectory;
        yield return RuntimesDirectory;
        yield return LauncherLogsDirectory;
        yield return TemporaryDirectory;
        yield return BackupsDirectory;
        yield return UpdateStagingDirectory;
    }

    private static string DefaultRoot()
    {
        if (OperatingSystem.IsWindows())
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "Ferrite");
        }

        if (OperatingSystem.IsMacOS())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "Library", "Application Support", "Ferrite");
        }

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        var baseDirectory = string.IsNullOrWhiteSpace(xdg) ? Path.Combine(profile, ".local", "share") : xdg;
        return Path.Combine(baseDirectory, "Ferrite");
    }
}
