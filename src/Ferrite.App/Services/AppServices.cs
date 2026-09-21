using Ferrite.Core.Auth;
using Ferrite.Core.Content;
using Ferrite.Core.Diagnostics;
using Ferrite.Core.Download;
using Ferrite.Core.Game;
using Ferrite.Core.Java;
using Ferrite.Core.Loaders;
using Ferrite.Core.Minecraft;
using Ferrite.Core.Net;
using Ferrite.Core.Platform;
using Ferrite.Core.Storage;
using Ferrite.Core.Update;
using Microsoft.Extensions.Logging;

namespace Ferrite.App.Services;

/// <summary>
/// Composition root for the application. Every service is constructed once, here, and handed to view
/// models explicitly; there is no service locator and no mutable static state.
/// </summary>
public sealed class AppServices : IDisposable
{
    private readonly ILoggerFactory _loggerFactory;

    /// <param name="modrinthApiBase">
    /// Overrides the Modrinth endpoint. Used by tests that need a provider which cannot be reached.
    /// </param>
    public AppServices(ILoggerFactory loggerFactory, AppPaths paths, string? modrinthApiBase = null)
    {
        _loggerFactory = loggerFactory;
        Paths = paths;
        Paths.EnsureCreated();

        var secrets = new ProtectedSecretStore(paths.SecretsFile, loggerFactory.CreateLogger<ProtectedSecretStore>());
        secrets.Load();
        Settings = new SettingsStore(paths, loggerFactory.CreateLogger<SettingsStore>());
        Http = new HttpService(new HttpServiceOptions(), loggerFactory.CreateLogger<HttpService>());
        Downloads = new DownloadEngine(Http, new DownloadEngineOptions(), loggerFactory.CreateLogger<DownloadEngine>());
        Manifest = new VersionManifestService(Http, paths, loggerFactory.CreateLogger<VersionManifestService>());
        Resolver = new VersionResolver(Manifest, loggerFactory.CreateLogger<VersionResolver>());
        Planner = new InstallPlanner(Http, paths, loggerFactory.CreateLogger<InstallPlanner>());
        Installer = new MinecraftInstaller(
            Downloads,
            Planner,
            Resolver,
            paths,
            loggerFactory.CreateLogger<MinecraftInstaller>());
        Launcher = new LaunchService(loggerFactory.CreateLogger<LaunchService>());
        Instances = new InstanceStore(paths, loggerFactory.CreateLogger<InstanceStore>());
        InstanceManager = new InstanceManager(
            Instances,
            paths,
            loggerFactory.CreateLogger<InstanceManager>());
        Java = new JavaDetector(paths, loggerFactory.CreateLogger<JavaDetector>());
        InstanceLauncher = new InstanceLauncher(
            Installer,
            Resolver,
            Planner,
            Java,
            Launcher,
            paths,
            loggerFactory.CreateLogger<InstanceLauncher>());
        JavaProvisioner = new JavaProvisioner(
            Http,
            Downloads,
            Java,
            paths,
            loggerFactory.CreateLogger<JavaProvisioner>());
        Fabric = new FabricLoaderService(Http, Manifest, loggerFactory.CreateLogger<FabricLoaderService>());
        Forge = new ForgeLoaderService(
            Http,
            Downloads,
            Manifest,
            paths,
            loggerFactory.CreateLogger<ForgeLoaderService>(),
            loggerFactory);
        Cache = new ContentCache(paths.CacheDirectory, loggerFactory.CreateLogger<ContentCache>());
        Manifests = new ContentManifestStore(loggerFactory.CreateLogger<ContentManifestStore>());
        Modrinth = new CachedContentProvider(
            new ModrinthClient(
                Http,
                loggerFactory.CreateLogger<ModrinthClient>(),
                modrinthApiBase ?? ModrinthClient.ApiBase),
            Cache,
            loggerFactory.CreateLogger<CachedContentProvider>());
        Content = new ContentInstaller(
            Modrinth,
            Downloads,
            paths,
            Manifests,
            loggerFactory.CreateLogger<ContentInstaller>());
        Credentials = new ProviderCredentialStore(secrets);
        CurseForgeApi = new CurseForgeClient(
            Http,
            loggerFactory.CreateLogger<CurseForgeClient>(),
            () => Credentials.CurseForgeApiKey);
        CurseForge = new CachedContentProvider(
            CurseForgeApi,
            Cache,
            loggerFactory.CreateLogger<CachedContentProvider>());
        CurseForgeContent = new ContentInstaller(
            CurseForge,
            Downloads,
            paths,
            Manifests,
            loggerFactory.CreateLogger<ContentInstaller>());
        ContentUpdates = new ContentUpdater(
            Downloads,
            Manifests,
            paths,
            loggerFactory.CreateLogger<ContentUpdater>());
        Updates = new UpdateService(
            Http,
            Downloads,
            paths,
            loggerFactory.CreateLogger<UpdateService>(),
            UpdateFeedKey.Load());
        ContentProviders = [Modrinth, CurseForge];
        Mods = new InstanceContentManager(new ModScanner(loggerFactory.CreateLogger<ModScanner>()));
        Modpacks = new MrpackInstaller(
            Downloads,
            Instances,
            Fabric,
            Forge,
            Installer,
            Java,
            paths,
            loggerFactory.CreateLogger<MrpackInstaller>());
        ModpackExporter = new ModpackExporter(paths, loggerFactory.CreateLogger<ModpackExporter>());
        CurseForgePacks = new CurseForgePackInstaller(
            CurseForgeApi,
            Downloads,
            Instances,
            Fabric,
            Forge,
            Installer,
            Java,
            paths,
            loggerFactory.CreateLogger<CurseForgePackInstaller>());
        Worlds = new WorldService(loggerFactory.CreateLogger<WorldService>());
        WorldsArchive = new WorldArchive(paths.BackupsDirectory, loggerFactory.CreateLogger<WorldArchive>());
        Servers = new ServerListService(loggerFactory.CreateLogger<ServerListService>());
        Pinger = new ServerPinger(loggerFactory.CreateLogger<ServerPinger>());
        LanWorlds = new LanWorldDiscovery(loggerFactory.CreateLogger<LanWorldDiscovery>());
        Operations = new OperationLog(
            Path.Combine(paths.LauncherLogsDirectory, "operations.jsonl"),
            loggerFactory.CreateLogger<OperationLog>());
        Diagnostics = new DiagnosticsBundleExporter(
            paths,
            SecretRedactor.Shared,
            Java,
            Mods,
            loggerFactory.CreateLogger<DiagnosticsBundleExporter>());
        Accounts = new AccountService(
            new AccountStore(paths, secrets, loggerFactory.CreateLogger<AccountStore>()),
            new MicrosoftAuthClient(Http, loggerFactory.CreateLogger<MicrosoftAuthClient>(), clientId: null),
            loggerFactory.CreateLogger<AccountService>());
    }

    public AppPaths Paths { get; }

    public SettingsStore Settings { get; }

    public HttpService Http { get; }

    public DownloadEngine Downloads { get; }

    public VersionManifestService Manifest { get; }

    public VersionResolver Resolver { get; }

    public InstallPlanner Planner { get; }

    public MinecraftInstaller Installer { get; }

    public LaunchService Launcher { get; }

    public InstanceLauncher InstanceLauncher { get; }

    public InstanceStore Instances { get; }

    public InstanceManager InstanceManager { get; }

    public JavaDetector Java { get; }

    public JavaProvisioner JavaProvisioner { get; }

    public FabricLoaderService Fabric { get; }

    public ForgeLoaderService Forge { get; }

    public ContentCache Cache { get; }

    public ContentManifestStore Manifests { get; }

    public CachedContentProvider Modrinth { get; }

    public ContentInstaller Content { get; }

    public ProviderCredentialStore Credentials { get; }

    public CachedContentProvider CurseForge { get; }

    /// <summary>
    /// The unwrapped client. Modpack manifests need its bulk file resolution, which is CurseForge
    /// specific and therefore not part of the provider contract.
    /// </summary>
    public CurseForgeClient CurseForgeApi { get; }

    public ContentInstaller CurseForgeContent { get; }

    public ContentUpdater ContentUpdates { get; }

    public UpdateService Updates { get; }

    /// <summary>Providers the browser can switch between, in display order.</summary>
    public IReadOnlyList<IContentProvider> ContentProviders { get; }

    /// <summary>The installer that places files from <paramref name="provider"/> into an instance.</summary>
    public ContentInstaller InstallerFor(IContentProvider provider) =>
        ReferenceEquals(provider, CurseForge) ? CurseForgeContent : Content;

    public InstanceContentManager Mods { get; }

    public MrpackInstaller Modpacks { get; }

    public ModpackExporter ModpackExporter { get; }

    public CurseForgePackInstaller CurseForgePacks { get; }

    public WorldService Worlds { get; }

    public WorldArchive WorldsArchive { get; }

    public ServerListService Servers { get; }

    public ServerPinger Pinger { get; }

    public LanWorldDiscovery LanWorlds { get; }

    public OperationLog Operations { get; }

    public DiagnosticsBundleExporter Diagnostics { get; }

    public AccountService Accounts { get; }

    public ILogger<T> Logger<T>()
        where T : class => _loggerFactory.CreateLogger<T>();

    /// <summary>Rebuilds the authentication client after the client id setting changes.</summary>
    public MicrosoftAuthClient CreateAuthClient(string? clientId) => new(
        Http,
        _loggerFactory.CreateLogger<MicrosoftAuthClient>(),
        clientId);

    public void Dispose()
    {
        Http.Dispose();
        _loggerFactory.Dispose();
    }
}
