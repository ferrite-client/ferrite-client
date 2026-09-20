using Microsoft.Extensions.Logging;

namespace Ferrite.Core.Minecraft;

/// <summary>
/// Flattens a version document chain into one document. Loader profiles and Forge installers add
/// a single <c>inheritsFrom</c> hop, but the resolver walks the whole chain and refuses cycles.
/// </summary>
public sealed class VersionResolver
{
    private const int MaxInheritanceDepth = 8;

    private readonly VersionManifestService _manifestService;
    private readonly ILogger<VersionResolver> _logger;

    public VersionResolver(VersionManifestService manifestService, ILogger<VersionResolver> logger)
    {
        _manifestService = manifestService;
        _logger = logger;
    }

    /// <summary>
    /// Resolves a version id to a flattened document. Locally installed metadata wins over Mojang
    /// metadata, which is what lets loader-installed versions resolve offline.
    /// </summary>
    public async Task<VersionDocument> ResolveAsync(string versionId, CancellationToken cancellationToken)
    {
        var chain = new List<VersionDocument>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = versionId;

        for (var depth = 0; depth < MaxInheritanceDepth; depth++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!visited.Add(current))
            {
                throw new VersionMetadataException($"Version inheritance cycle detected at '{current}'.");
            }

            var document = await LoadAsync(current, cancellationToken).ConfigureAwait(false);
            chain.Add(document);

            if (string.IsNullOrEmpty(document.InheritsFrom))
            {
                break;
            }

            current = document.InheritsFrom;
        }

        if (chain.Count >= MaxInheritanceDepth)
        {
            throw new VersionMetadataException($"Version inheritance chain for '{versionId}' is too deep.");
        }

        chain.Reverse();
        var merged = Merge(chain);
        merged.ResolvedLibraries = merged.Libraries;
        return merged;
    }

    private async Task<VersionDocument> LoadAsync(string versionId, CancellationToken cancellationToken)
    {
        var local = await _manifestService.TryReadLocalVersionAsync(versionId, cancellationToken).ConfigureAwait(false);
        if (local is not null && !string.IsNullOrEmpty(local.InheritsFrom))
        {
            return local;
        }

        var manifest = await _manifestService
            .GetManifestAsync(forceRefresh: false, cancellationToken)
            .ConfigureAwait(false);
        var entry = manifest.Find(versionId);
        if (entry is not null)
        {
            return await _manifestService.GetMojangVersionAsync(entry, cancellationToken).ConfigureAwait(false);
        }

        if (local is not null)
        {
            return local;
        }

        throw new VersionMetadataException(
            $"Version '{versionId}' is not in the Mojang manifest and is not installed locally.");
    }

    private VersionDocument Merge(IReadOnlyList<VersionDocument> chain)
    {
        var root = chain[0];
        var newest = chain[^1];

        var merged = new VersionDocument
        {
            Id = newest.Id ?? root.Id,
            Type = newest.Type ?? root.Type,
            ReleaseTime = newest.ReleaseTime ?? root.ReleaseTime,
            Time = newest.Time ?? root.Time,
            Arguments = new ArgumentsDocument(),
        };

        var libraries = new List<Library>();

        foreach (var document in chain)
        {
            merged.MainClass = document.MainClass ?? merged.MainClass;
            merged.Assets = document.Assets ?? merged.Assets;
            merged.AssetIndex = document.AssetIndex ?? merged.AssetIndex;
            merged.Downloads = document.Downloads ?? merged.Downloads;
            merged.JavaVersion = document.JavaVersion ?? merged.JavaVersion;
            merged.Logging = document.Logging ?? merged.Logging;
            merged.ComplianceLevel = document.ComplianceLevel ?? merged.ComplianceLevel;
            merged.MinimumLauncherVersion = document.MinimumLauncherVersion ?? merged.MinimumLauncherVersion;
            merged.MinecraftArguments = document.MinecraftArguments ?? merged.MinecraftArguments;

            if (document.Arguments is { } arguments)
            {
                merged.Arguments.Game.AddRange(arguments.Game);
                merged.Arguments.Jvm.AddRange(arguments.Jvm);
                merged.Arguments.DefaultUserJvm.AddRange(arguments.DefaultUserJvm);
            }

            libraries.AddRange(document.Libraries);
        }

        merged.Libraries = DeduplicateLibraries(libraries);
        _logger.LogDebug("Merged {Count} version documents into {Id}", chain.Count, merged.Id);
        return merged;
    }

    /// <summary>Later definitions of the same coordinates replace earlier ones.</summary>
    private static List<Library> DeduplicateLibraries(List<Library> libraries)
    {
        var byKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var result = new List<Library>(libraries.Count);
        foreach (var library in libraries)
        {
            var key = library.Name ?? Guid.NewGuid().ToString("N");
            if (byKey.TryGetValue(key, out var index))
            {
                result[index] = library;
            }
            else
            {
                byKey[key] = result.Count;
                result.Add(library);
            }
        }

        return result;
    }
}
