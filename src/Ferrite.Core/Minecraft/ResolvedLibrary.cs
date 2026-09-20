using Ferrite.Core.Download;

namespace Ferrite.Core.Minecraft;

/// <summary>A library that applies to this host, with its concrete download and target path.</summary>
public sealed record ResolvedLibrary(
    MavenCoordinates Coordinates,
    string TargetPath,
    DownloadRequest? Download,
    bool IsNative);

/// <summary>A native archive to extract into the instance's natives directory.</summary>
public sealed record NativeEntry(string ArchivePath, IReadOnlyList<string> Exclusions);

/// <summary>One asset object as it exists in the content-addressed store.</summary>
public sealed record AssetObjectEntry(string RelativePath, string Hash, long Size)
{
    public string ObjectKey => Hash;
}
