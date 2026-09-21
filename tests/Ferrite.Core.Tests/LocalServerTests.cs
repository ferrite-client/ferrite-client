using System.IO.Compression;
using System.Security.Cryptography;
using Ferrite.Core.Download;
using Ferrite.Core.Java;
using Ferrite.Core.Minecraft;
using Ferrite.Core.Net;
using Ferrite.Core.Platform;
using Ferrite.Core.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ferrite.Core.Tests;

/// <summary>
/// A local server: the settings file is merged rather than rewritten, the jar is verified, the
/// command is an argument list, and the export is a real zip.
/// </summary>
public sealed class LocalServerTests : IAsyncLifetime
{
    private readonly string _workspace;
    private readonly Guid _instanceId = Guid.NewGuid();
    private TestHttpServer _server = null!;
    private HttpService _http = null!;
    private DownloadEngine _downloads = null!;
    private AppPaths _paths = null!;
    private LocalServerService _service = null!;

    public LocalServerTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), "ferrite-server-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspace);
    }

    public ValueTask InitializeAsync()
    {
        _server = new TestHttpServer();
        _http = new HttpService(new HttpServiceOptions(), NullLogger<HttpService>.Instance);
        _downloads = new DownloadEngine(_http, new DownloadEngineOptions(), NullLogger<DownloadEngine>.Instance);
        _paths = AppPaths.ForRoot(Path.Combine(_workspace, "root"));
        _paths.EnsureCreated();
        Directory.CreateDirectory(_paths.InstanceDirectory(_instanceId));
        _service = new LocalServerService(_downloads, _paths, NullLogger<LocalServerService>.Instance);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await _server.DisposeAsync();
        try
        {
            Directory.Delete(_workspace, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static string Sha1Of(byte[] bytes) => Convert.ToHexString(SHA1.HashData(bytes)).ToLowerInvariant();

    private VersionDocument DocumentWithServer(byte[] jar) => new()
    {
        Id = "1.21.1",
        Downloads = new VersionDownloads
        {
            Server = new DownloadArtifact
            {
                Url = _server.BaseUrl + "/server.jar",
                Sha1 = Sha1Of(jar),
                Size = jar.Length,
            },
        },
    };

    [Fact]
    public void Defaults_are_written_and_the_users_own_keys_survive()
    {
        var document = ServerPropertiesDocument.Parse(
            "# my notes" + Environment.NewLine
            + "difficulty=hard" + Environment.NewLine
            + "motd=old" + Environment.NewLine);

        var read = LocalServerService.ReadOptions(document);
        Assert.Equal("old", read.Motd);

        document.Set("motd", "A Ferrite server");
        document.Set("server-port", "25566");
        var text = document.ToText();

        Assert.Contains("# my notes", text, StringComparison.Ordinal);
        Assert.Contains("difficulty=hard", text, StringComparison.Ordinal);
        Assert.Contains("motd=A Ferrite server", text, StringComparison.Ordinal);
        Assert.Contains("server-port=25566", text, StringComparison.Ordinal);
        Assert.DoesNotContain("motd=old", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_value_with_a_newline_round_trips()
    {
        var document = ServerPropertiesDocument.Parse(null);
        document.Set("motd", "line one\nline two");

        var reparsed = ServerPropertiesDocument.Parse(document.ToText());

        Assert.Equal("line one\nline two", reparsed.Get("motd"));
        // The escape must not have produced a second line in the file.
        Assert.DoesNotContain("line two\n", document.ToText().Split('\n')[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Preparing_downloads_and_verifies_the_server_jar()
    {
        var jar = new byte[4096];
        Random.Shared.NextBytes(jar);
        _server.AddRoute("/server.jar", jar);

        var layout = await _service.PrepareAsync(
            _instanceId,
            DocumentWithServer(jar),
            new LocalServerOptions { LevelName = "survival", Port = 25570, AcceptEula = true },
            null,
            TestContext.Current.CancellationToken);

        Assert.True(File.Exists(layout.JarPath));
        Assert.Equal(jar, await File.ReadAllBytesAsync(layout.JarPath, TestContext.Current.CancellationToken));

        var properties = ServerPropertiesDocument.Parse(
            await File.ReadAllTextAsync(layout.PropertiesPath, TestContext.Current.CancellationToken));
        Assert.Equal("survival", properties.Get("level-name"));
        Assert.Equal("25570", properties.Get("server-port"));
        Assert.Equal("true", properties.Get("online-mode"));

        Assert.Contains(
            "eula=true",
            await File.ReadAllTextAsync(layout.EulaPath, TestContext.Current.CancellationToken),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_jar_that_does_not_match_its_hash_is_refused()
    {
        var jar = new byte[512];
        _server.AddRoute("/server.jar", jar);
        var document = DocumentWithServer(jar);
        document.Downloads!.Server!.Sha1 = new string('0', 40);

        await Assert.ThrowsAnyAsync<Exception>(() => _service.PrepareAsync(
            _instanceId,
            document,
            new LocalServerOptions(),
            null,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_version_without_a_server_download_is_reported()
    {
        var document = new VersionDocument { Id = "1.21.1" };

        var exception = await Assert.ThrowsAsync<InstallFailedException>(() => _service.PrepareAsync(
            _instanceId,
            document,
            new LocalServerOptions(),
            null,
            TestContext.Current.CancellationToken));

        Assert.Contains("no server download", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_eula_is_written_as_false_until_the_user_accepts_it()
    {
        var jar = new byte[256];
        _server.AddRoute("/server.jar", jar);

        var layout = await _service.PrepareAsync(
            _instanceId,
            DocumentWithServer(jar),
            new LocalServerOptions { AcceptEula = false },
            null,
            TestContext.Current.CancellationToken);

        var eula = await File.ReadAllTextAsync(layout.EulaPath, TestContext.Current.CancellationToken);
        Assert.Contains("eula=false", eula, StringComparison.Ordinal);
        Assert.DoesNotContain("eula=true", eula, StringComparison.Ordinal);
    }

    [Fact]
    public void The_command_runs_the_jar_in_the_server_folder()
    {
        var layout = new LocalServerLayout(
            Path.Combine(_workspace, "server"),
            Path.Combine(_workspace, "server", "server.jar"),
            Path.Combine(_workspace, "server", "server.properties"),
            Path.Combine(_workspace, "server", "eula.txt"),
            "world");
        var java = new JavaRuntime
        {
            ExecutablePath = "java.exe",
            MajorVersion = 21,
            Is64Bit = true,
        };

        var command = LocalServerService.BuildCommand(
            layout,
            java,
            new LocalServerOptions { MemoryMb = 3072 },
            ["-Dexample=1"]);

        Assert.Equal("java.exe", command.ExecutablePath);
        Assert.Equal(layout.Directory, command.WorkingDirectory);
        Assert.Equal(["-Xmx3072M", "-Dexample=1", "-jar", "server.jar", "nogui"], command.Arguments);
    }

    [Fact]
    public async Task The_export_is_a_real_zip_of_the_server_folder()
    {
        var jar = new byte[1024];
        _server.AddRoute("/server.jar", jar);
        var layout = await _service.PrepareAsync(
            _instanceId,
            DocumentWithServer(jar),
            new LocalServerOptions { AcceptEula = true },
            null,
            TestContext.Current.CancellationToken);
        var worldDirectory = Path.Combine(layout.Directory, "world");
        Directory.CreateDirectory(worldDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(worldDirectory, "level.dat"),
            "world bytes",
            TestContext.Current.CancellationToken);

        var archivePath = Path.Combine(_workspace, "export.zip");
        await _service.ExportAsync(layout, archivePath, TestContext.Current.CancellationToken);

        using var archive = ZipFile.OpenRead(archivePath);
        var names = archive.Entries.Select(entry => entry.FullName).ToList();
        Assert.Contains("server.jar", names);
        Assert.Contains("server.properties", names);
        Assert.Contains("eula.txt", names);
        Assert.Contains("world/level.dat", names);
    }
}
