using Ferrite.Core.Download;

namespace Ferrite.Core.Minecraft;

/// <summary>
/// Everything required to materialise one version: the exact downloads, the classpath order,
/// the natives to extract, and the asset index contents.
/// </summary>
public sealed class InstallPlan
{
    public required string VersionId { get; init; }

    public required VersionDocument Document { get; init; }

    public required IReadOnlyList<DownloadRequest> Downloads { get; init; }

    public required IReadOnlyList<ResolvedLibrary> Libraries { get; init; }

    public required IReadOnlyList<NativeEntry> Natives { get; init; }

    public required IReadOnlyList<AssetObjectEntry> Assets { get; init; }

    public AssetIndexReference? AssetIndex { get; init; }

    public string? AssetIndexPath { get; init; }

    public string? ClientJarPath { get; init; }

    public LoggingFile? LoggingConfig { get; init; }

    public string? LoggingConfigPath { get; init; }

    public long TotalBytes { get; init; }

    /// <summary>True when the asset index mirrors objects into a virtual legacy tree.</summary>
    public bool VirtualAssets { get; init; }

    public int FileCount => Downloads.Count;
}
