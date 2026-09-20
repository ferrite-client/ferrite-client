using Ferrite.Core.Download;

namespace Ferrite.Core.Minecraft;

/// <summary>Projects download progress into the installer's single progress channel.</summary>
public static class DownloadProgressAdapter
{
    public static IProgress<DownloadProgress>? Create(IProgress<InstallProgress>? progress) =>
        progress is null ? null : new Adapter(progress);

    private sealed class Adapter : IProgress<DownloadProgress>
    {
        private readonly IProgress<InstallProgress> _inner;

        public Adapter(IProgress<InstallProgress> inner)
        {
            _inner = inner;
        }

        public void Report(DownloadProgress value) => _inner.Report(new InstallProgress
        {
            Stage = InstallStage.Downloading,
            Download = value,
            Message = value.CurrentItem,
        });
    }
}
