using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Ferrite.App.Localization;
using Ferrite.App.Services;
using Ferrite.App.ViewModels;
using Ferrite.Core.Content;
using Ferrite.Core.Platform;
using Ferrite.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Ferrite.App.Tests;

/// <summary>
/// What the browser does with a modpack result: downloads the project's own file, verifies it against
/// the checksum the provider published, and hands the archive to the right unpacker. The archive is
/// served over a real socket, so nothing here is a stub of the download path.
/// </summary>
public sealed class BrowseModpackInstallTests : IDisposable
{
    private readonly string _root;
    private readonly AppServices _services;

    public BrowseModpackInstallTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ferrite-browsemodpack-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
        _services = new AppServices(loggerFactory, AppPaths.ForRoot(_root));
        Localizer.Apply(Avalonia.Application.Current, Localizer.English);
    }

    public void Dispose()
    {
        _services.Dispose();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>A zip that is a valid archive but carries no pack index.</summary>
    private static byte[] NotAPackArchive()
    {
        var path = Path.Combine(Path.GetTempPath(), "ferrite-not-a-pack-" + Guid.NewGuid().ToString("N") + ".zip");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("readme.txt");
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write("not a modpack");
        }

        var bytes = File.ReadAllBytes(path);
        File.Delete(path);
        return bytes;
    }

    /// <summary>
    /// A browser pointed at the unconfigured provider, so nothing is fetched while the test sets the
    /// result up by hand, plus one existing instance to be the install target.
    /// </summary>
    private async Task<(BrowseViewModel ViewModel, MainWindowViewModel Shell)> PrepareAsync()
    {
        var target = await _services.Instances.CreateAsync(
            new InstanceRecord { Id = Guid.NewGuid(), Name = "Existing", MinecraftVersion = "1.21.1" },
            CancellationToken.None);

        var shell = new MainWindowViewModel(_services);
        var viewModel = new BrowseViewModel(_services, shell)
        {
            TargetInstance = target,
        };
        viewModel.SelectedProvider = viewModel.Providers.Single(provider => provider.Name == "curseforge");
        viewModel.SelectedResult = new ContentSummary(
            "modrinth",
            "packproject",
            "fixture-pack",
            "Fixture pack",
            "A pack",
            ContentProjectType.Modpack,
            10,
            null,
            "someone",
            ["adventure"],
            DateTimeOffset.UtcNow);
        return (viewModel, shell);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task A_modpack_result_is_downloaded_verified_and_dispatched()
    {
        var bytes = NotAPackArchive();
        using var server = new TestImageServer(bytes, "application/zip");
        var (viewModel, shell) = await PrepareAsync();

        viewModel.SelectedVersion = new ContentVersion(
            "modrinth",
            "versionid",
            "packproject",
            "1.0.0",
            "Fixture pack 1.0.0",
            null,
            "release",
            ["1.21.1"],
            ["fabric"],
            [new ContentFile(
                "fixture-pack.mrpack",
                server.BaseUrl + "/fixture-pack.mrpack",
                bytes.Length,
                Primary: true,
                Convert.ToHexString(SHA1.HashData(bytes)).ToLowerInvariant(),
                Sha512: null)],
            [],
            DateTimeOffset.UtcNow);

        await viewModel.InstallCommand.ExecuteAsync(null);

        // The archive was fetched from the provider's own URL...
        Assert.Equal(1, server.Requests);
        // ...and it got as far as the unpacker, whose rejection is what reaches the user.
        Assert.Contains(
            "not a modpack archive",
            shell.ErrorMessage ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);

        // No instance was created from an archive the unpacker refused.
        Assert.Empty(
            (await _services.Instances.LoadAllAsync(TestContext.Current.CancellationToken))
            .Where(record => record.Name.StartsWith("Fixture", StringComparison.OrdinalIgnoreCase))
            .ToList());
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task A_checksum_that_does_not_match_stops_the_install()
    {
        var bytes = NotAPackArchive();
        using var server = new TestImageServer(bytes, "application/zip");
        var (viewModel, shell) = await PrepareAsync();

        viewModel.SelectedVersion = new ContentVersion(
            "modrinth",
            "versionid",
            "packproject",
            "1.0.0",
            null,
            null,
            "release",
            ["1.21.1"],
            ["fabric"],
            [new ContentFile(
                "fixture-pack.mrpack",
                server.BaseUrl + "/fixture-pack.mrpack",
                bytes.Length,
                Primary: true,
                // A checksum that cannot match what the server sends.
                new string('0', 40),
                Sha512: null)],
            [],
            DateTimeOffset.UtcNow);

        await viewModel.InstallCommand.ExecuteAsync(null);

        // The file was fetched and rejected; the unpacker was never reached.
        Assert.True(server.Requests >= 1, "the archive should have been requested");
        Assert.DoesNotContain(
            "not a modpack archive",
            shell.ErrorMessage ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "Checksum mismatch",
            (viewModel.StatusNote ?? string.Empty) + " " + (shell.ErrorMessage ?? string.Empty),
            StringComparison.OrdinalIgnoreCase);
        Assert.Empty(
            (await _services.Instances.LoadAllAsync(TestContext.Current.CancellationToken))
            .Where(record => record.Name.StartsWith("Fixture", StringComparison.OrdinalIgnoreCase))
            .ToList());
    }
}
