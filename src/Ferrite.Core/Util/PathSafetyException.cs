namespace Ferrite.Core.Util;

/// <summary>Raised when a path derived from untrusted input would escape its container.</summary>
public sealed class PathSafetyException : Exception
{
    public PathSafetyException(string message)
        : base(message)
    {
    }
}
