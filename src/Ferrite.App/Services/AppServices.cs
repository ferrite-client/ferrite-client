using Ferrite.Core.Auth;
using Ferrite.Core.Content;
using Ferrite.Core.Download;
using Ferrite.Core.Game;
using Ferrite.Core.Java;
using Ferrite.Core.Loaders;
using Ferrite.Core.Minecraft;
using Ferrite.Core.Net;
using Ferrite.Core.Platform;
using Ferrite.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Ferrite.App.Services;

/// <summary>
/// Composition root for the application. Every service is constructed once, here, and handed to view
/// models explicitly; there is no service locator and no mutable static state.
/// </summary>
public sealed class AppServices : IDisposable
{
    private readonly ILoggerFactory _loggerFactory;

    public AppServices(ILoggerFactory loggerFactory, AppPaths paths)
    {
        _loggerFactory = loggerFactory;
        Paths = paths;
        Paths.EnsureCreated();

        var secrets = new ProtectedSecretStore(paths.SecretsFile, loggerFactory.CreateLogger<ProtectedSecretStore>());
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
        Modrinth = new ModrinthClient(Http, loggerFactory.CreateLogger<ModrinthClient>());
        Content = new ContentInstaller(Modrinth, Downloads, loggerFactory.CreateLogger<ContentInstaller>());
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
        Worlds = new WorldService(loggerFactory.CreateLogger<WorldService>());
        WorldsArchive = new WorldArchive(paths.BackupsDirectory, loggerFactory.CreateLogger<WorldArchive>());
        Servers = new ServerListService(loggerFactory.CreateLogger<ServerListService>());
        Pinger = new ServerPinger(loggerFactory.CreateLogger<ServerPinger>());
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

    public JavaDetector Java { get; }

    public JavaProvisioner JavaProvisioner { get; }

    public FabricLoaderService Fabric { get; }

    public ForgeLoaderService Forge { get; }

    public ModrinthClient Modrinth { get; }

    public ContentInstaller Content { get; }

    public InstanceContentManager Mods { get; }

    public MrpackInstaller Modpacks { get; }

    public ModpackExporter ModpackExporter { get; }

    public WorldService Worlds { get; }

    public WorldArchive WorldsArchive { get; }

    public ServerListService Servers { get; }

    public ServerPinger Pinger { get; }

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
