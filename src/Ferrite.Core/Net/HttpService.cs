using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Ferrite.Core.Json;
using Microsoft.Extensions.Logging;

namespace Ferrite.Core.Net;

/// <summary>
/// One HTTP stack for the whole launcher: bounded connections, optional proxy, retry with
/// backoff, and hard size caps so a hostile or broken endpoint cannot exhaust memory.
/// </summary>
public sealed class HttpService : IDisposable
{
    private readonly HttpServiceOptions _options;
    private readonly ILogger<HttpService> _logger;
    private readonly HttpClient _client;
    private readonly bool _ownsClient;

    public HttpService(HttpServiceOptions options, ILogger<HttpService> logger, HttpClient? client = null)
    {
        _options = options;
        _logger = logger;
        _ownsClient = client is null;
        _client = client ?? CreateClient(options);
    }

    public HttpClient Client => _client;

    /// <summary>Buffered GET with a hard size cap and per-attempt timeout. Retries transient failures.</summary>
    public async Task<byte[]> GetBytesAsync(string url, int maxBytes, CancellationToken cancellationToken)
    {
        using var template = new HttpRequestMessage(HttpMethod.Get, url);
        var attempt = 0;
        while (true)
        {
            attempt++;
            using var request = await CloneAsync(template, cancellationToken).ConfigureAwait(false);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.MetadataTimeout);

            try
            {
                using var response = await _client
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                    .ConfigureAwait(false);

                if (IsTransient(response.StatusCode) && attempt < _options.MaxAttempts)
                {
                    _logger.LogDebug(
                        "Transient HTTP {Status} for {Url} on attempt {Attempt}",
                        (int)response.StatusCode,
                        url,
                        attempt);
                    var retryAfter = response.Headers.RetryAfter?.Delta;
                    response.Dispose();
                    await DelayAsync(attempt, retryAfter, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    var status = (int)response.StatusCode;
                    response.Dispose();
                    throw new HttpException($"HTTP {status} for {url}", status);
                }

                await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
                return await ReadBoundedAsync(stream, maxBytes, timeout.Token).ConfigureAwait(false);
            }
            catch (HttpRequestException exception) when (attempt < _options.MaxAttempts)
            {
                _logger.LogDebug(exception, "Transport failure for {Url} on attempt {Attempt}", url, attempt);
                await DelayAsync(attempt, retryAfter: null, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (attempt >= _options.MaxAttempts)
                {
                    throw new HttpException($"Timed out fetching {url}");
                }

                await DelayAsync(attempt, retryAfter: null, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task<string> GetStringAsync(string url, int maxBytes, CancellationToken cancellationToken)
    {
        var bytes = await GetBytesAsync(url, maxBytes, cancellationToken).ConfigureAwait(false);
        return Encoding.UTF8.GetString(bytes);
    }

    public async Task<T> GetJsonAsync<T>(string url, int maxBytes, CancellationToken cancellationToken)
    {
        var bytes = await GetBytesAsync(url, maxBytes, cancellationToken).ConfigureAwait(false);
        return Deserialize<T>(bytes, url);
    }

    /// <summary>Returns null for a 404 instead of throwing; every other failure still surfaces.</summary>
    public async Task<T?> TryGetJsonAsync<T>(string url, int maxBytes, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            return await GetJsonAsync<T>(url, maxBytes, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpException exception) when (exception.StatusCode is 404)
        {
            return null;
        }
    }

    public async Task<bool> HeadExistsAsync(string url, CancellationToken cancellationToken)
    {
        var attempt = 0;
        while (true)
        {
            attempt++;
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.MetadataTimeout);

            try
            {
                using var response = await _client
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                    .ConfigureAwait(false);

                if (IsTransient(response.StatusCode) && attempt < _options.MaxAttempts)
                {
                    var retryAfter = response.Headers.RetryAfter?.Delta;
                    response.Dispose();
                    await DelayAsync(attempt, retryAfter, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                return response.IsSuccessStatusCode;
            }
            catch (HttpRequestException) when (attempt < _options.MaxAttempts)
            {
                await DelayAsync(attempt, retryAfter: null, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Streaming send for large downloads. The caller owns the returned response and the
    /// timeout; this method only retries transient transport and status failures.
    /// </summary>
    public async Task<HttpResponseMessage> SendStreamingWithRetryAsync(
        HttpRequestMessage template,
        CancellationToken cancellationToken)
    {
        var attempt = 0;
        while (true)
        {
            attempt++;
            var request = await CloneAsync(template, cancellationToken).ConfigureAwait(false);
            try
            {
                var response = await _client
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);

                if (IsTransient(response.StatusCode) && attempt < _options.MaxAttempts)
                {
                    var retryAfter = response.Headers.RetryAfter?.Delta;
                    response.Dispose();
                    await DelayAsync(attempt, retryAfter, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    var status = (int)response.StatusCode;
                    response.Dispose();
                    throw new HttpException($"HTTP {status} for {template.RequestUri}", status);
                }

                return response;
            }
            catch (HttpRequestException exception) when (attempt < _options.MaxAttempts)
            {
                _logger.LogDebug(
                    exception,
                    "Transport failure for {Url} on attempt {Attempt}",
                    template.RequestUri,
                    attempt);
                await DelayAsync(attempt, retryAfter: null, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                request.Dispose();
            }
        }
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _client.Dispose();
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream stream, int maxBytes, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[64 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (buffer.Length + read > maxBytes)
            {
                throw new HttpException($"Response exceeded the {maxBytes} byte limit.");
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static T Deserialize<T>(byte[] bytes, string url)
    {
        try
        {
            var value = JsonSerializer.Deserialize<T>(bytes, JsonDefaults.Remote);
            if (value is null)
            {
                throw new HttpException($"Empty JSON document from {url}");
            }

            return value;
        }
        catch (JsonException exception)
        {
            throw new HttpException($"Malformed JSON from {url}: {exception.Message}", inner: exception);
        }
    }

    private static bool IsTransient(HttpStatusCode status) =>
        status is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;

    private static async Task DelayAsync(int attempt, TimeSpan? retryAfter, CancellationToken cancellationToken)
    {
        var milliseconds = 400 * Math.Pow(2, attempt - 1);
        var delay = TimeSpan.FromMilliseconds(milliseconds);
        if (retryAfter is { } serverDelay && serverDelay > TimeSpan.Zero)
        {
            delay = serverDelay;
        }

        var total = delay + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 250));
        if (total > TimeSpan.FromSeconds(30))
        {
            total = TimeSpan.FromSeconds(30);
        }

        await Task.Delay(total, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<HttpRequestMessage> CloneAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);
        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (request.Content is not null)
        {
            var bytes = await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            clone.Content = new ByteArrayContent(bytes);
            foreach (var header in request.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
    }

    private static HttpClient CreateClient(HttpServiceOptions options)
    {
        var handler = new SocketsHttpHandler
        {
            MaxConnectionsPerServer = options.MaxConnectionsPerServer,
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            ConnectTimeout = TimeSpan.FromSeconds(30),
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 8,
        };

        if (!string.IsNullOrWhiteSpace(options.ProxyUrl)
            && Uri.TryCreate(options.ProxyUrl, UriKind.Absolute, out var proxyUri))
        {
            handler.Proxy = new WebProxy(proxyUri);
            handler.UseProxy = true;
        }

        var client = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };

        if (ProductInfoHeaderValue.TryParse(options.UserAgent, out var userAgent))
        {
            client.DefaultRequestHeaders.UserAgent.Add(userAgent);
        }

        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }
}
