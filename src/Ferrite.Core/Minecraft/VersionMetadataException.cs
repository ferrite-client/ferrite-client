namespace Ferrite.Core.Minecraft;

public sealed class VersionMetadataException : Exception
{
    public VersionMetadataException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
