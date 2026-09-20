namespace Ferrite.Core.Loaders;

public sealed class LoaderException : Exception
{
    public LoaderException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
