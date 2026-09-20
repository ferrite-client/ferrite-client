namespace Ferrite.Core.Net;

public sealed class HttpException : Exception
{
    public HttpException(string message, int? statusCode = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }

    public int? StatusCode { get; }
}
