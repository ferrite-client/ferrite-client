namespace Ferrite.Core.Java;

public sealed class JavaProvisioningException : Exception
{
    public JavaProvisioningException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
