using Ferrite.Core.Download;
using Ferrite.Core.Net;
using Ferrite.Core.Tests.Infrastructure;
using Ferrite.Core.Util;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ferrite.Core.Tests;

public sealed class DownloadEngineTests : IAsyncLifetime
{
    private readonly string _workspace;
    private TestHttpServer _server = null!;
    private HttpService _http = null!;

    public DownloadEngineTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), "ferrite-download-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspace);
    }

    public Task InitializeAsync()
    {
        _server = new TestHttpServer();
        _http = new HttpService(new HttpServiceOptions(), NullLogger<HttpService>.Instance);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
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
    public async Task Download_writes_and_verifies_a_file()
    {
        var payload = CreatePayload(256 * 1024);
        _server.AddRoute("/payload.bin", payload);

        var target = Path.Combine(_workspace, "payload.bin");
        var engine = CreateEngine();
        var summaries = new InlineProgress<DownloadProgress>();

        var summary = await engine.DownloadAsync(
            [
                new DownloadRequest
                {
                    Url = _server.BaseUrl + "/payload.bin",
                    TargetPath = target,
                    ExpectedSha1 = Hashing.Sha1(payload),
                    ExpectedSize = payload.Length,
                },
            ],
            summaries,
            CancellationToken.None);

        Assert.True(summary.Success);
        Assert.Equal(1, summary.DownloadedFiles);
        Assert.Equal(payload, await File.ReadAllBytesAsync(target));
        Assert.Empty(Directory.GetFiles(_workspace, "*.part"));
        Assert.NotEmpty(summaries.Items);
        Assert.True(summaries.Items[^1].CompletedBytes > 0);
    }

    [Fact]
    public async Task Download_skips_files_that_already_verify()
    {
        var payload = CreatePayload(64 * 1024);
        _server.AddRoute("/skip.bin", payload);
        var target = Path.Combine(_workspace, "skip.bin");
        await File.WriteAllBytesAsync(target, payload);

        var summary = await CreateEngine().DownloadAsync(
            [
                new DownloadRequest
                {
                    Url = _server.BaseUrl + "/skip.bin",
                    TargetPath = target,
                    ExpectedSha1 = Hashing.Sha1(payload),
                    ExpectedSize = payload.Length,
                },
            ],
            progress: null,
            CancellationToken.None);

        Assert.True(summary.Success);
        Assert.Equal(1, summary.SkippedFiles);
        Assert.Equal(0, summary.DownloadedFiles);
        Assert.Equal(0, _server.RequestCount("/skip.bin"));
    }

    [Fact]
    public async Task Download_repairs_a_corrupt_existing_file()
    {
        var payload = CreatePayload(48 * 1024);
        _server.AddRoute("/repair.bin", payload);
        var target = Path.Combine(_workspace, "repair.bin");
        await File.WriteAllBytesAsync(target, new byte[payload.Length]);

        var summary = await CreateEngine().DownloadAsync(
            [
                new DownloadRequest
                {
                    Url = _server.BaseUrl + "/repair.bin",
                    TargetPath = target,
                    ExpectedSha1 = Hashing.Sha1(payload),
                    ExpectedSize = payload.Length,
                },
            ],
            progress: null,
            CancellationToken.None);

        Assert.True(summary.Success);
        Assert.Equal(payload, await File.ReadAllBytesAsync(target));
    }

    [Fact]
    public async Task Download_fails_and_leaves_nothing_when_the_hash_is_wrong()
    {
        var payload = CreatePayload(32 * 1024);
        _server.AddRoute("/wrong.bin", payload);
        var target = Path.Combine(_workspace, "wrong.bin");

        var summary = await CreateEngine().DownloadAsync(
            [
                new DownloadRequest
                {
                    Url = _server.BaseUrl + "/wrong.bin",
                    TargetPath = target,
                    ExpectedSha1 = Hashing.Sha1("different"u8),
                    ExpectedSize = payload.Length,
                },
            ],
            progress: null,
            CancellationToken.None);

        Assert.False(summary.Success);
        Assert.Single(summary.Failures);
        Assert.False(File.Exists(target));
        Assert.Empty(Directory.GetFiles(_workspace, "*.part"));
    }

    [Fact]
    public async Task Download_retries_transient_server_failures()
    {
        var payload = CreatePayload(16 * 1024);
        _server.AddFlakyRoute("/flaky.bin", failures: 2, payload);
        var target = Path.Combine(_workspace, "flaky.bin");

        var summary = await CreateEngine().DownloadAsync(
            [
                new DownloadRequest
                {
                    Url = _server.BaseUrl + "/flaky.bin",
                    TargetPath = target,
                    ExpectedSha1 = Hashing.Sha1(payload),
                    ExpectedSize = payload.Length,
                },
            ],
            progress: null,
            CancellationToken.None);

        Assert.True(summary.Success);
        Assert.Equal(payload, await File.ReadAllBytesAsync(target));
        Assert.Equal(3, _server.RequestCount("/flaky.bin"));
    }

    [Fact]
    public async Task Download_resumes_an_interrupted_partial_file()
    {
        var payload = CreatePayload(300 * 1024);
        _server.AddRoute("/resume.bin", payload);
        var target = Path.Combine(_workspace, "resume.bin");
        var part = target + ".part";

        var half = payload.Length / 2;
        await File.WriteAllBytesAsync(part, payload[..half]);

        var summary = await CreateEngine().DownloadAsync(
            [
                new DownloadRequest
                {
                    Url = _server.BaseUrl + "/resume.bin",
                    TargetPath = target,
                    ExpectedSha1 = Hashing.Sha1(payload),
                    ExpectedSize = payload.Length,
                },
            ],
            progress: null,
            CancellationToken.None);

        Assert.True(summary.Success);
        Assert.Equal(payload, await File.ReadAllBytesAsync(target));
    }

    [Fact]
    public async Task Download_restarts_when_the_server_ignores_range()
    {
        var payload = CreatePayload(200 * 1024);
        _server.AddNoRangeRoute("/norange.bin", payload);
        var target = Path.Combine(_workspace, "norange.bin");
        var part = target + ".part";
        await File.WriteAllBytesAsync(part, payload[..(payload.Length / 4)]);

        var summary = await CreateEngine().DownloadAsync(
            [
                new DownloadRequest
                {
                    Url = _server.BaseUrl + "/norange.bin",
                    TargetPath = target,
                    ExpectedSha1 = Hashing.Sha1(payload),
                    ExpectedSize = payload.Length,
                },
            ],
            progress: null,
            CancellationToken.None);

        Assert.True(summary.Success);
        Assert.Equal(payload, await File.ReadAllBytesAsync(target));
    }

    [Fact]
    public async Task Download_uses_fallback_mirror_when_primary_is_missing()
    {
        var payload = CreatePayload(24 * 1024);
        _server.AddRoute("/mirror.bin", payload);
        var target = Path.Combine(_workspace, "mirror.bin");

        var summary = await CreateEngine().DownloadAsync(
            [
                new DownloadRequest
                {
                    Url = _server.BaseUrl + "/missing.bin",
                    FallbackUrls = [_server.BaseUrl + "/mirror.bin"],
                    TargetPath = target,
                    ExpectedSha1 = Hashing.Sha1(payload),
                    ExpectedSize = payload.Length,
                },
            ],
            progress: null,
            CancellationToken.None);

        Assert.True(summary.Success);
        Assert.Equal(payload, await File.ReadAllBytesAsync(target));
    }

    [Fact]
    public async Task Download_honours_bounded_concurrency_across_many_files()
    {
        const int fileCount = 24;
        var requests = new List<DownloadRequest>(fileCount);
        for (var index = 0; index < fileCount; index++)
        {
            var payload = CreatePayload(8 * 1024, index);
            var path = $"/many/{index}.bin";
            _server.AddRoute(path, payload);
            requests.Add(new DownloadRequest
            {
                Url = _server.BaseUrl + path,
                TargetPath = Path.Combine(_workspace, "many", $"{index}.bin"),
                ExpectedSha1 = Hashing.Sha1(payload),
                ExpectedSize = payload.Length,
            });
        }

        var summary = await CreateEngine(maxConcurrency: 4).DownloadAsync(requests, progress: null, CancellationToken.None);

        Assert.True(summary.Success);
        Assert.Equal(fileCount, summary.DownloadedFiles);
        for (var index = 0; index < fileCount; index++)
        {
            Assert.True(File.Exists(Path.Combine(_workspace, "many", $"{index}.bin")));
        }
    }

    [Fact]
    public async Task Download_is_cancellable()
    {
        var payload = CreatePayload(4 * 1024 * 1024);
        _server.AddRoute("/big.bin", payload);
        var target = Path.Combine(_workspace, "big.bin");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateEngine().DownloadAsync(
            [
                new DownloadRequest
                {
                    Url = _server.BaseUrl + "/big.bin",
                    TargetPath = target,
                    ExpectedSha1 = Hashing.Sha1(payload),
                    ExpectedSize = payload.Length,
                },
            ],
            progress: null,
            cts.Token));
    }

    [Fact]
    public async Task Download_reports_failure_for_a_missing_host_without_throwing()
    {
        var target = Path.Combine(_workspace, "unreachable.bin");
        var summary = await CreateEngine().DownloadAsync(
            [
                new DownloadRequest
                {
                    Url = "http://127.0.0.1:1/never.bin",
                    TargetPath = target,
                    ExpectedSize = 10,
                },
            ],
            progress: null,
            CancellationToken.None);

        Assert.False(summary.Success);
        Assert.Single(summary.Failures);
    }

    private DownloadEngine CreateEngine(int maxConcurrency = 8) =>
        new(
            _http,
            new DownloadEngineOptions
            {
                MaxConcurrency = maxConcurrency,
                MaxAttempts = 2,
                PerAttemptTimeout = TimeSpan.FromSeconds(20),
            },
            NullLogger<DownloadEngine>.Instance);

    private static byte[] CreatePayload(int size, int seed = 1234)
    {
        var payload = new byte[size];
        new Random(seed).NextBytes(payload);
        return payload;
    }

    /// <summary>Reports inline so tests observe every progress update deterministically.</summary>
    private sealed class InlineProgress<T> : IProgress<T>
    {
        public List<T> Items { get; } = [];

        public void Report(T value)
        {
            lock (Items)
            {
                Items.Add(value);
            }
        }
    }
}
