using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Ferrite.App.Tests;

/// <summary>
/// A one-endpoint HTTP server for the artwork path: real bytes over a real socket, so a test can
/// prove an image was fetched and decoded rather than asserting on a stub.
/// </summary>
internal sealed class TestImageServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly byte[] _payload;
    private readonly Task _loop;
    private int _requests;

    public TestImageServer(byte[] pngOrAnyBytes, string contentType = "image/png")
    {
        _payload = pngOrAnyBytes;
        ContentType = contentType;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        BaseUrl = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";
        _loop = Task.Run(AcceptLoopAsync);
    }

    public string BaseUrl { get; }

    public string ContentType { get; }

    public int Requests => Volatile.Read(ref _requests);

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

            _ = Task.Run(() => AnswerAsync(client));
        }
    }

    private async Task AnswerAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                await using var stream = client.GetStream();
                // The request is read only far enough to let the client finish sending it.
                var buffer = new byte[4096];
                var read = await stream.ReadAsync(buffer).ConfigureAwait(false);
                if (read == 0)
                {
                    return;
                }

                Interlocked.Increment(ref _requests);
                var header = "HTTP/1.1 200 OK\r\n"
                    + $"Content-Type: {ContentType}\r\n"
                    + $"Content-Length: {_payload.Length}\r\n"
                    + "Connection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(header)).ConfigureAwait(false);
                await stream.WriteAsync(_payload).ConfigureAwait(false);
                await stream.FlushAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                // A dropped connection is not interesting for an image stand-in.
            }
        }
    }
}
