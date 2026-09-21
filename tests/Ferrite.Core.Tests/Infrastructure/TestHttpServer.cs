using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Ferrite.Core.Tests.Infrastructure;

internal sealed record TestRequest(
    string Method,
    string Path,
    string Query,
    string Body,
    IReadOnlyDictionary<string, string> Headers);

internal sealed record TestResponse(int Status, string Body, string ContentType = "application/json");

/// <summary>
/// Minimal HTTP/1.1 server for tests: deterministic, supports byte ranges and request bodies, and
/// can be told to fail so retry and error-mapping behaviour is exercised for real.
/// </summary>
internal sealed partial class TestHttpServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _acceptLoop;
    private readonly Dictionary<string, Route> _routes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _counts = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<TestRequest> _requests = [];
    private readonly object _gate = new();

    public TestHttpServer()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        BaseUrl = $"http://127.0.0.1:{port}";
        _acceptLoop = Task.Run(AcceptLoopAsync);
    }

    public string BaseUrl { get; }

    public IReadOnlyList<TestRequest> Requests
    {
        get
        {
            lock (_gate)
            {
                return _requests.ToArray();
            }
        }
    }

    public void AddRoute(string path, byte[] content, string contentType = "application/octet-stream") =>
        SetRoute("GET", path, new Route(content, contentType, 0, IgnoreRange: false, Handler: null));

    public void AddTextRoute(string path, string content, string contentType = "application/json") =>
        AddRoute(path, Encoding.UTF8.GetBytes(content), contentType);

    /// <summary>Fails the first <paramref name="failures"/> requests with 500, then succeeds.</summary>
    public void AddFlakyRoute(string path, int failures, byte[] content) =>
        SetRoute("GET", path, new Route(content, "application/octet-stream", failures, IgnoreRange: false, null));

    /// <summary>Serves the whole body with 200 even when a Range header is present.</summary>
    public void AddNoRangeRoute(string path, byte[] content) =>
        SetRoute("GET", path, new Route(content, "application/octet-stream", 0, IgnoreRange: true, null));

    /// <summary>Handles a request with a scripted response.</summary>
    public void AddHandler(string method, string path, Func<TestRequest, TestResponse> handler) =>
        SetRoute(method, path, new Route([], "application/json", 0, IgnoreRange: false, handler));

    public int RequestCount(string path)
    {
        lock (_gate)
        {
            return _counts.TryGetValue(path, out var count) ? count : 0;
        }
    }

    public TestRequest? LastRequest(string path)
    {
        lock (_gate)
        {
            return _requests.LastOrDefault(request =>
                string.Equals(request.Path, path, StringComparison.OrdinalIgnoreCase));
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        _listener.Stop();
        try
        {
            await _acceptLoop;
        }
        catch (Exception)
        {
        }

        _cts.Dispose();
    }

    private void SetRoute(string method, string path, Route route)
    {
        lock (_gate)
        {
            _routes[Key(method, path)] = route;
        }
    }

    private static string Key(string method, string path) => method.ToUpperInvariant() + " " + path;

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_cts.Token);
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
                var (request, contentLength) = await ReadRequestAsync(stream);
                if (request is null)
                {
                    return;
                }

                var body = await ReadBodyAsync(stream, contentLength);
                var full = request with { Body = body };

                Route? route;
                lock (_gate)
                {
                    _requests.Add(full);
                    _counts[request.Path] = _counts.TryGetValue(request.Path, out var count) ? count + 1 : 1;
                    route = _routes.TryGetValue(Key(request.Method, request.Path), out var value)
                        ? value
                        : _routes.TryGetValue(Key("GET", request.Path), out var fallback)
                            ? fallback
                            : null;
                }

                if (route is null)
                {
                    await WriteResponseAsync(stream, 404, "text/plain", Encoding.UTF8.GetBytes("not found"), null);
                    return;
                }

                if (route.Handler is { } handler)
                {
                    var response = handler(full);
                    await WriteResponseAsync(
                        stream,
                        response.Status,
                        response.ContentType,
                        Encoding.UTF8.GetBytes(response.Body),
                        null);
                    return;
                }

                if (route.FailuresRemaining > 0)
                {
                    lock (_gate)
                    {
                        _routes[Key(request.Method, request.Path)] = route with
                        {
                            FailuresRemaining = route.FailuresRemaining - 1,
                        };
                    }

                    await WriteResponseAsync(stream, 500, "text/plain", Encoding.UTF8.GetBytes("boom"), null);
                    return;
                }

                var content = route.Content;
                if (!route.IgnoreRange
                    && full.Headers.TryGetValue("range", out var range)
                    && TryParseRange(range, content.Length, out var start, out var end))
                {
                    var slice = content[start..(end + 1)];
                    await WriteResponseAsync(stream, 206, route.ContentType, slice, (start, end, content.Length));
                    return;
                }

                await WriteResponseAsync(stream, 200, route.ContentType, content, null);
            }
            catch (Exception)
            {
                // A test server drops broken connections silently.
            }
        }
    }

    private sealed record Route(
        byte[] Content,
        string ContentType,
        int FailuresRemaining,
        bool IgnoreRange,
        Func<TestRequest, TestResponse>? Handler);
}
