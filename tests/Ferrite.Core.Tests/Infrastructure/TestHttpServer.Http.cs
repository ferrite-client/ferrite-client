using System.Text;

namespace Ferrite.Core.Tests.Infrastructure;

internal sealed partial class TestHttpServer
{
    private static async Task<(TestRequest? Request, int ContentLength)> ReadRequestAsync(Stream stream)
    {
        var headerBytes = new List<byte>(512);
        var matched = 0;
        var buffer = new byte[1];
        while (matched < 4)
        {
            var read = await stream.ReadAsync(buffer);
            if (read == 0)
            {
                return (null, 0);
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

        var requestLine = lines.Length > 0 ? lines[0] : string.Empty;
        var parts = requestLine.Split(' ');
        var method = parts.Length > 0 ? parts[0] : "GET";
        var target = parts.Length > 1 ? parts[1] : "/";
        // Routing matches on the path alone; the query string is exposed separately so a handler can
        // assert on it without having to register a route per query combination.
        var separatorIndex = target.IndexOf('?');
        var path = separatorIndex >= 0 ? target[..separatorIndex] : target;
        var query = separatorIndex >= 0 ? target[(separatorIndex + 1)..] : string.Empty;
        var contentLength = headers.TryGetValue("content-length", out var lengthText)
            && int.TryParse(lengthText, out var parsed)
                ? parsed
                : 0;

        return (new TestRequest(method, path, query, string.Empty, headers), contentLength);
    }

    private static async Task<string> ReadBodyAsync(Stream stream, int contentLength)
    {
        if (contentLength <= 0)
        {
            return string.Empty;
        }

        var buffer = new byte[contentLength];
        var offset = 0;
        while (offset < contentLength)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset));
            if (read == 0)
            {
                break;
            }

            offset += read;
        }

        return Encoding.UTF8.GetString(buffer, 0, offset);
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
        400 => "Bad Request",
        401 => "Unauthorized",
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

        if (!int.TryParse(spec[..dash], out start))
        {
            return false;
        }

        var endText = spec[(dash + 1)..];
        if (!string.IsNullOrEmpty(endText) && int.TryParse(endText, out var parsedEnd))
        {
            end = Math.Min(parsedEnd, length - 1);
        }

        return start < length && start <= end;
    }
}
