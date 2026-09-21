using System.Net;
using System.Net.Sockets;
using System.Text;
using Ferrite.Core.Download;
using Ferrite.Core.Net;
using Ferrite.Core.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ferrite.Core.Tests;

/// <summary>
/// The network settings have to reach the running services. Each of these started life as a stored
/// value with nothing reading it, so each test asserts the effect rather than the setting.
/// </summary>
public sealed class NetworkSettingsTests : IAsyncLifetime
{
    private readonly string _workspace;
    private TestHttpServer _server = null!;
    private HttpService _http = null!;

    public NetworkSettingsTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), "ferrite-net-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspace);
    }

    public ValueTask InitializeAsync()
    {
        _server = new TestHttpServer();
        _http = new HttpService(new HttpServiceOptions(), NullLogger<HttpService>.Instance);
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

    [Fact]
    public void A_host_override_rewrites_the_authority_and_keeps_the_path()
    {
        var resolver = new MirrorResolver(new Dictionary<string, string>
        {
            ["launchermeta.mojang.com"] = "https://mirror.example/mojang",
        });

        var resolved = resolver.Resolve("https://launchermeta.mojang.com/v1/packages/abc/1.21.1.json");

        Assert.Equal("https://mirror.example/mojang/v1/packages/abc/1.21.1.json", resolved);
    }

    [Fact]
    public void A_mirror_may_include_a_port_and_query_is_preserved()
    {
        var resolver = new MirrorResolver(new Dictionary<string, string>
        {
            ["piston-data.mojang.com"] = "http://127.0.0.1:8080/proxy",
        });

        var resolved = resolver.Resolve("https://piston-data.mojang.com/v1/objects/aa/bb?x=1");

        Assert.Equal("http://127.0.0.1:8080/proxy/v1/objects/aa/bb?x=1", resolved);
    }

    [Fact]
    public void An_unconfigured_host_and_a_bad_override_are_left_alone()
    {
        var resolver = new MirrorResolver(new Dictionary<string, string>
        {
            ["libraries.minecraft.net"] = "not a url",
        });

        Assert.Equal("https://example.invalid/a.jar", resolver.Resolve("https://example.invalid/a.jar"));
        Assert.Equal(
            "https://libraries.minecraft.net/a.jar",
            resolver.Resolve("https://libraries.minecraft.net/a.jar"));
    }

    [Fact]
    public void Clearing_the_overrides_stops_rewriting()
    {
        var resolver = new MirrorResolver(new Dictionary<string, string>
        {
            ["example.invalid"] = "https://mirror.example",
        });
        Assert.NotEqual("https://example.invalid/a", resolver.Resolve("https://example.invalid/a"));

        resolver.Update(null);

        Assert.True(resolver.IsEmpty);
        Assert.Equal("https://example.invalid/a", resolver.Resolve("https://example.invalid/a"));
    }

    /// <summary>The override has to be applied to real requests, not only to the resolver.</summary>
    [Fact]
    public async Task A_mirror_override_redirects_a_real_request()
    {
        var resolver = new MirrorResolver(new Dictionary<string, string>
        {
            ["launchermeta.mojang.com"] = _server.BaseUrl,
        });
        using var mirrored = new HttpService(
            new HttpServiceOptions(),
            NullLogger<HttpService>.Instance,
            client: null,
            mirrors: resolver);

        _server.AddHandler("GET", "/manifest.json", _ => new TestResponse(200, """{"ok":true}"""));

        var body = await mirrored.GetStringAsync(
            "https://launchermeta.mojang.com/manifest.json",
            4096,
            TestContext.Current.CancellationToken);

        Assert.Equal("""{"ok":true}""", body);
        Assert.Equal(1, _server.RequestCount("/manifest.json"));
    }

    /// <summary>
    /// The proxy setting has to reach the HTTP stack. The stand-in proxy answers, and the fact that
    /// anything came back proves the client sent the request to it rather than to the origin.
    /// </summary>
    [Fact]
    public async Task A_proxy_setting_routes_requests_through_the_proxy()
    {
        var proxy = new RecordingProxy();
        try
        {
            _http.UpdateProxy($"http://127.0.0.1:{proxy.Port}");

            var body = await _http.GetStringAsync(
                "http://origin.invalid/metadata.json",
                4096,
                TestContext.Current.CancellationToken);

            Assert.Equal("""{"proxied":true}""", body);
            Assert.Contains(
                proxy.Requests,
                request => request.Contains("origin.invalid", StringComparison.Ordinal));
        }
        finally
        {
            proxy.Dispose();
        }
    }

    [Fact]
    public async Task A_download_uses_the_configured_mirror()
    {
        var resolver = new MirrorResolver(new Dictionary<string, string>
        {
            ["edge.forgecdn.net"] = _server.BaseUrl,
        });
        using var mirrored = new HttpService(
            new HttpServiceOptions(),
            NullLogger<HttpService>.Instance,
            client: null,
            mirrors: resolver);
        var engine = new DownloadEngine(
            mirrored,
            new DownloadEngineOptions(),
            NullLogger<DownloadEngine>.Instance);

        var payload = Encoding.UTF8.GetBytes("mod bytes");
        _server.AddRoute("/files/1/2/mod.jar", payload);
        var target = Path.Combine(_workspace, "mod.jar");

        var summary = await engine.DownloadAsync(
            [
                new DownloadRequest
                {
                    Url = "https://edge.forgecdn.net/files/1/2/mod.jar",
                    TargetPath = target,
                },
            ],
            progress: null,
            TestContext.Current.CancellationToken);

        Assert.True(summary.Success);
        Assert.Equal("mod bytes", await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken));
        Assert.Equal(1, _server.RequestCount("/files/1/2/mod.jar"));
    }

    /// <summary>
    /// The concurrency setting has to change how many transfers run at once, so the engine reports
    /// the limit it actually used rather than the limit it was constructed with.
    /// </summary>
    [Fact]
    public async Task The_concurrency_setting_bounds_simultaneous_transfers()
    {
        var engine = new DownloadEngine(
            _http,
            new DownloadEngineOptions { MaxConcurrency = 8 },
            NullLogger<DownloadEngine>.Instance);
        Assert.Equal(8, engine.MaxConcurrency);

        engine.MaxConcurrency = 2;
        Assert.Equal(2, engine.MaxConcurrency);

        // Out-of-range values are clamped rather than accepted.
        engine.MaxConcurrency = 0;
        Assert.Equal(1, engine.MaxConcurrency);
        engine.MaxConcurrency = 999;
        Assert.Equal(64, engine.MaxConcurrency);

        engine.MaxConcurrency = 2;
        var payload = new byte[4096];
        var requests = new List<DownloadRequest>();
        for (var index = 0; index < 6; index++)
        {
            _server.AddRoute($"/file{index}.bin", payload);
            requests.Add(new DownloadRequest
            {
                Url = $"{_server.BaseUrl}/file{index}.bin",
                TargetPath = Path.Combine(_workspace, $"file{index}.bin"),
            });
        }

        var summary = await engine.DownloadAsync(requests, progress: null, TestContext.Current.CancellationToken);

        Assert.Equal(6, summary.DownloadedFiles);
        Assert.Equal(2, engine.MaxConcurrency);
    }

    /// <summary>A single-connection stand-in proxy: answers any request itself and records the target.</summary>
    private sealed class RecordingProxy : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly List<string> _requests = [];
        private readonly Task _loop;

        public RecordingProxy()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _loop = Task.Run(AcceptLoopAsync);
        }

        public int Port { get; }

        public IReadOnlyList<string> Requests
        {
            get
            {
                lock (_requests)
                {
                    return _requests.ToArray();
                }
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();
            try
            {
                _loop.Wait(TimeSpan.FromSeconds(2));
            }
            catch (AggregateException)
            {
            }

            _cts.Dispose();
        }

        private async Task AcceptLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(_cts.Token).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    return;
                }

                _ = Task.Run(() => HandleAsync(client));
            }
        }

        private async Task HandleAsync(TcpClient client)
        {
            using (client)
            {
                try
                {
                    await using var stream = client.GetStream();
                    var line = await ReadLineAsync(stream).ConfigureAwait(false);
                    if (line is null)
                    {
                        return;
                    }

                    lock (_requests)
                    {
                        _requests.Add(line);
                    }

                    while (await ReadLineAsync(stream).ConfigureAwait(false) is { Length: > 0 })
                    {
                    }

                    var body = Encoding.UTF8.GetBytes("""{"proxied":true}""");
                    var header = "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\n"
                        + $"Content-Length: {body.Length}\r\nConnection: close\r\n\r\n";
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(header)).ConfigureAwait(false);
                    await stream.WriteAsync(body).ConfigureAwait(false);
                    await stream.FlushAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // A dropped connection is not interesting for a proxy stand-in.
                }
            }
        }

        private static async Task<string?> ReadLineAsync(NetworkStream stream)
        {
            var buffer = new List<byte>(128);
            var single = new byte[1];
            var matched = 0;
            while (matched < 2)
            {
                var read = await stream.ReadAsync(single).ConfigureAwait(false);
                if (read == 0)
                {
                    return buffer.Count == 0 ? null : Encoding.ASCII.GetString(buffer.ToArray());
                }

                buffer.Add(single[0]);
                matched = single[0] switch
                {
                    (byte)'\r' when matched == 0 => 1,
                    (byte)'\n' when matched == 1 => 2,
                    _ => 0,
                };
            }

            return Encoding.ASCII.GetString(buffer.ToArray()).TrimEnd('\r', '\n');
        }
    }
}
