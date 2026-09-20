namespace Ferrite.Core.Content;

public sealed class ContentProviderException : Exception
{
    public ContentProviderException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
