using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Ferrite.Core.Tests.Infrastructure;

/// <summary>
/// Minimal HTTP/1.1 server for tests: deterministic, supports byte ranges, and can be told to
/// fail a number of times so retry behaviour is exercised for real rather than mocked away.
/// </summary>
internal sealed class TestHttpServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _acceptLoop;
    private readonly Dictionary<string, Route> _routes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _counts = new(StringComparer.OrdinalIgnoreCase);
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

    public void AddRoute(string path, byte[] content, string contentType = "application/octet-stream")
    {
        lock (_gate)
        {
            _routes[path] = new Route(content, contentType, FailuresRemaining: 0, IgnoreRange: false);
        }
    }

    public void AddTextRoute(string path, string content, string contentType = "application/json")
    {
        AddRoute(path, Encoding.UTF8.GetBytes(content), contentType);
    }

    /// <summary>Fails the first <paramref name="failures"/> requests with 500, then succeeds.</summary>
    public void AddFlakyRoute(string path, int failures, byte[] content)
    {
        lock (_gate)
        {
            _routes[path] = new Route(content, "application/octet-stream", failures, IgnoreRange: false);
        }
    }

    /// <summary>Serves the whole body with 200 even when a Range header is present.</summary>
    public void AddNoRangeRoute(string path, byte[] content)
    {
        lock (_gate)
        {
            _routes[path] = new Route(content, "application/octet-stream", FailuresRemaining: 0, IgnoreRange: true);
        }
    }

    public int RequestCount(string path)
    {
        lock (_gate)
        {
            return _counts.TryGetValue(path, out var count) ? count : 0;
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
                var (requestLine, headers) = await ReadRequestAsync(stream);
                if (requestLine is null)
                {
                    return;
                }

                var parts = requestLine.Split(' ');
                var path = parts.Length > 1 ? parts[1] : "/";

                Route? route;
                lock (_gate)
                {
                    _counts[path] = _counts.TryGetValue(path, out var count) ? count + 1 : 1;
                    route = _routes.TryGetValue(path, out var value) ? value : null;
                }

                if (route is null)
                {
                    await WriteResponseAsync(stream, 404, "text/plain", Encoding.UTF8.GetBytes("not found"), null);
                    return;
                }

                if (route.FailuresRemaining > 0)
                {
                    lock (_gate)
                    {
                        route = route with { FailuresRemaining = route.FailuresRemaining - 1 };
                        _routes[path] = route;
                    }

                    await WriteResponseAsync(stream, 500, "text/plain", Encoding.UTF8.GetBytes("boom"), null);
                    return;
                }

                var content = route.Content;
                if (!route.IgnoreRange
                    && headers.TryGetValue("range", out var range)
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

    private static async Task<(string? RequestLine, Dictionary<string, string> Headers)> ReadRequestAsync(Stream stream)
    {
        var buffer = new byte[1];
        var headerBytes = new List<byte>(512);
        var matched = 0;
        while (matched < 4)
        {
            var read = await stream.ReadAsync(buffer);
            if (read == 0)
            {
                return (null, new Dictionary<string, string>());
            }

            headerBytes.Add(buffer[0]);
            matched = buffer[0] switch
            {
                (byte)'\r' when matched % 2 == 0 => matched + 1,
                (byte)'\n' when matched % 2 == 1 => matched + 1,
                (byte)'\r' => 1,
                _ => 0,
            };
        }

        var text = Encoding.ASCII.GetString(headerBytes.ToArray());
        var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 1; index < lines.Length; index++)
        {
            var separator = lines[index].IndexOf(':');
            if (separator > 0)
            {
                headers[lines[index][..separator].Trim()] = lines[index][(separator + 1)..].Trim();
            }
        }

        return (lines.Length > 0 ? lines[0] : null, headers);
    }

    private static async Task WriteResponseAsync(
        Stream stream,
        int status,
        string contentType,
        byte[] body,
        (int Start, int End, int Total)? range)
    {
        var header = new StringBuilder();
        header.Append("HTTP/1.1 ").Append(status).Append(' ').Append(StatusText(status)).Append("\r\n");
        header.Append("Content-Type: ").Append(contentType).Append("\r\n");
        header.Append("Content-Length: ").Append(body.Length).Append("\r\n");
        header.Append("Accept-Ranges: bytes\r\n");
        if (range is { } value)
        {
            header.Append("Content-Range: bytes ")
                .Append(value.Start).Append('-').Append(value.End).Append('/').Append(value.Total).Append("\r\n");
        }

        header.Append("Connection: close\r\n\r\n");
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header.ToString()));
        await stream.WriteAsync(body);
        await stream.FlushAsync();
    }

    private static string StatusText(int status) => status switch
    {
        200 => "OK",
        206 => "Partial Content",
        404 => "Not Found",
        500 => "Internal Server Error",
        _ => "Status",
    };

    private static bool TryParseRange(string header, int length, out int start, out int end)
    {
        start = 0;
        end = length - 1;
        if (!header.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var spec = header["bytes=".Length..];
        var dash = spec.IndexOf('-');
        if (dash < 0)
        {
            return false;
        }

        var startText = spec[..dash];
        var endText = spec[(dash + 1)..];
        if (!int.TryParse(startText, out start))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(endText) && int.TryParse(endText, out var parsedEnd))
        {
            end = Math.Min(parsedEnd, length - 1);
        }

        return start < length && start <= end;
    }

    private sealed record Route(byte[] Content, string ContentType, int FailuresRemaining, bool IgnoreRange);
}
