namespace Ferrite.Core.Storage;

public sealed class InstanceNotFoundException : Exception
{
    public InstanceNotFoundException(Guid id)
        : base($"Instance {id} was not found.")
    {
        InstanceId = id;
    }

    public Guid InstanceId { get; }
}
