namespace Ferrite.Core.Download;

public sealed class DownloadFailedException : Exception
{
    public DownloadFailedException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
