using Ferrite.Core.Download;

namespace Ferrite.Core.Minecraft;

public sealed class InstallFailedException : Exception
{
    public InstallFailedException(string message, IReadOnlyList<DownloadFailure> failures)
        : base(message)
    {
        Failures = failures;
    }

    public InstallFailedException(string message, Exception? inner = null)
        : base(message, inner)
    {
        Failures = [];
    }

    public IReadOnlyList<DownloadFailure> Failures { get; }
}
