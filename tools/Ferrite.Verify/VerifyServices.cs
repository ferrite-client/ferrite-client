using Ferrite.Core.Download;
using Ferrite.Core.Game;
using Ferrite.Core.Content;
using Ferrite.Core.Java;
using Ferrite.Core.Loaders;
using Ferrite.Core.Minecraft;
using Ferrite.Core.Net;
using Ferrite.Core.Platform;
using Ferrite.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Ferrite.Verify;

/// <summary>
/// Composition root for the verification harness. It wires the same Core services the launcher
/// uses, so a passing scenario is evidence about the product and not about a test double.
/// </summary>
internal sealed class VerifyServices : IDisposable
{
    public VerifyServices(string? dataRoot, LogLevel minimumLevel = LogLevel.Information)
    {
        LoggerFactory = new ConsoleLoggerFactory(minimumLevel);
        Paths = string.IsNullOrWhiteSpace(dataRoot) ? AppPaths.CreateDefault() : AppPaths.ForRoot(dataRoot);
        Paths.EnsureCreated();

        Http = new HttpService(new HttpServiceOptions(), LoggerFactory.CreateLogger<HttpService>());
        Downloads = new DownloadEngine(
            Http,
            new DownloadEngineOptions { MaxConcurrency = 8 },
            LoggerFactory.CreateLogger<DownloadEngine>());
        Manifest = new VersionManifestService(Http, Paths, LoggerFactory.CreateLogger<VersionManifestService>());
        Resolver = new VersionResolver(Manifest, LoggerFactory.CreateLogger<VersionResolver>());
        Planner = new InstallPlanner(Http, Paths, LoggerFactory.CreateLogger<InstallPlanner>());
        Installer = new MinecraftInstaller(
            Downloads,
            Planner,
            Resolver,
            Paths,
            LoggerFactory.CreateLogger<MinecraftInstaller>());
        Java = new JavaDetector(Paths, LoggerFactory.CreateLogger<JavaDetector>());
        JavaProvisioner = new JavaProvisioner(
            Http,
            Downloads,
            Java,
            Paths,
            LoggerFactory.CreateLogger<JavaProvisioner>());
        Launcher = new LaunchService(LoggerFactory.CreateLogger<LaunchService>());
        Instances = new InstanceStore(Paths, LoggerFactory.CreateLogger<InstanceStore>());
        Settings = new SettingsStore(Paths, LoggerFactory.CreateLogger<SettingsStore>());
        Fabric = new FabricLoaderService(
            Http,
            Manifest,
            LoggerFactory.CreateLogger<FabricLoaderService>());
        Forge = new ForgeLoaderService(
            Http,
            Downloads,
            Manifest,
            Paths,
            LoggerFactory.CreateLogger<ForgeLoaderService>(),
            LoggerFactory);
        Modrinth = new ModrinthClient(Http, LoggerFactory.CreateLogger<ModrinthClient>());
        Content = new ContentInstaller(
            Modrinth,
            Downloads,
            LoggerFactory.CreateLogger<ContentInstaller>());
        Mods = new InstanceContentManager(new ModScanner(LoggerFactory.CreateLogger<ModScanner>()));
        Modpacks = new MrpackInstaller(
            Downloads,
            Instances,
            Fabric,
            Forge,
            Installer,
            Java,
            Paths,
            LoggerFactory.CreateLogger<MrpackInstaller>());
        ModpackExporter = new ModpackExporter(Paths, LoggerFactory.CreateLogger<ModpackExporter>());
        Worlds = new WorldService(LoggerFactory.CreateLogger<WorldService>());
        WorldsArchive = new WorldArchive(Paths.BackupsDirectory, LoggerFactory.CreateLogger<WorldArchive>());
        Servers = new ServerListService(LoggerFactory.CreateLogger<ServerListService>());
        Pinger = new ServerPinger(LoggerFactory.CreateLogger<ServerPinger>());
    }

    public ILoggerFactory LoggerFactory { get; }

    public AppPaths Paths { get; }

    public HttpService Http { get; }

    public DownloadEngine Downloads { get; }

    public VersionManifestService Manifest { get; }

    public VersionResolver Resolver { get; }

    public InstallPlanner Planner { get; }

    public MinecraftInstaller Installer { get; }

    public JavaDetector Java { get; }

    public JavaProvisioner JavaProvisioner { get; }

    public LaunchService Launcher { get; }

    public InstanceStore Instances { get; }

    public SettingsStore Settings { get; }

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

    public void Dispose()
    {
        Http.Dispose();
        LoggerFactory.Dispose();
    }
}
