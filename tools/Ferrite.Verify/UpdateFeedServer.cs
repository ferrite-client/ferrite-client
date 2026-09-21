using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Ferrite.Verify;

/// <summary>
/// A minimal static file server for exercising an update feed over real HTTP on the loopback
/// interface. It exists so the checking, signature verification, and staging paths can be run against
/// a served feed instead of a test double, and so an operator can try their own feed locally.
/// </summary>
internal sealed class UpdateFeedServer : IDisposable
{
    private readonly string _directory;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;
    private int _requests;

    public UpdateFeedServer(string directory, int port)
    {
        _directory = Path.GetFullPath(directory);
        _listener = new TcpListener(IPAddress.Loopback, port);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _loop = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    public int Port { get; }

    public int Requests => Volatile.Read(ref _requests);

    public string BaseUrl => $"http://127.0.0.1:{Port}/";

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

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                return;
            }

            _ = Task.Run(() => HandleAsync(client), CancellationToken.None);
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                await using var stream = client.GetStream();
                var requestLine = await ReadLineAsync(stream).ConfigureAwait(false);
                if (requestLine is null)
                {
                    return;
                }

                // Drain the headers; nothing here uses them.
                while (await ReadLineAsync(stream).ConfigureAwait(false) is { Length: > 0 })
                {
                }

                var parts = requestLine.Split(' ');
                var target = parts.Length > 1 ? parts[1] : "/";
                var name = Path.GetFileName(Uri.UnescapeDataString(target));
                Interlocked.Increment(ref _requests);

                if (name.Length == 0)
                {
                    await WriteAsync(stream, 404, "text/plain", Encoding.UTF8.GetBytes("not found"))
                        .ConfigureAwait(false);
                    return;
                }

                // Only files directly in the served directory; no path traversal by construction.
                var path = Path.Combine(_directory, name);
                if (!File.Exists(path))
                {
                    await WriteAsync(stream, 404, "text/plain", Encoding.UTF8.GetBytes("not found"))
                        .ConfigureAwait(false);
                    return;
                }

                var bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
                var contentType = Path.GetExtension(path).ToLowerInvariant() switch
                {
                    ".json" => "application/json",
                    ".sig" => "text/plain",
                    _ => "application/octet-stream",
                };
                await WriteAsync(stream, 200, contentType, bytes).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // A dropped connection is not interesting for a feed harness.
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

        var text = Encoding.ASCII.GetString(buffer.ToArray());
        return text.TrimEnd('\r', '\n');
    }

    private static async Task WriteAsync(NetworkStream stream, int status, string contentType, byte[] body)
    {
        var header = new StringBuilder()
            .Append("HTTP/1.1 ").Append(status).Append(' ')
            .Append(status == 200 ? "OK" : "Not Found").Append("\r\n")
            .Append("Content-Type: ").Append(contentType).Append("\r\n")
            .Append("Content-Length: ").Append(body.Length).Append("\r\n")
            .Append("Connection: close\r\n\r\n")
            .ToString();

        await stream.WriteAsync(Encoding.ASCII.GetBytes(header)).ConfigureAwait(false);
        await stream.WriteAsync(body).ConfigureAwait(false);
        await stream.FlushAsync().ConfigureAwait(false);
    }
}
