using Ferrite.Core.Content;
using Ferrite.Core.Java;
using Ferrite.Core.Minecraft;
using Ferrite.Core.Rules;
using Ferrite.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Ferrite.Core.Loaders;

/// <summary>What a loader switch ended up doing.</summary>
public sealed record LoaderSwitchResult(
    InstanceRecord Instance,
    LoaderVersionInfo Version,
    string VersionId,
    bool Changed);

/// <summary>
/// Moves an instance onto another loader version. The order matters and is the reason this is one
/// place rather than two: the loader is installed into the store, the instance's own layout for that
/// version is installed (natives above all), and only then is the instance pointed at it. A failure
/// anywhere leaves the instance on the version it was running.
/// </summary>
public sealed class InstanceLoaderSwitcher
{
    private readonly InstanceStore _instances;
    private readonly FabricLoaderService _fabric;
    private readonly ForgeLoaderService _forge;
    private readonly MinecraftInstaller _installer;
    private readonly JavaDetector _java;
    private readonly ILogger<InstanceLoaderSwitcher> _logger;

    public InstanceLoaderSwitcher(
        InstanceStore instances,
        FabricLoaderService fabric,
        ForgeLoaderService forge,
        MinecraftInstaller installer,
        JavaDetector java,
        ILogger<InstanceLoaderSwitcher> logger)
    {
        _instances = instances;
        _fabric = fabric;
        _forge = forge;
        _installer = installer;
        _java = java;
        _logger = logger;
    }

    public async Task<LoaderSwitchResult> SwitchAsync(
        InstanceRecord instance,
        LoaderVersionInfo version,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(version);

        if (instance.Loader == LoaderKind.Vanilla)
        {
            throw new ContentProviderException(
                "This instance runs vanilla Minecraft, so it has no loader version to change.");
        }

        if (string.Equals(version.Version, instance.LoaderVersion, StringComparison.OrdinalIgnoreCase)
            && version.Kind == instance.Loader)
        {
            return new LoaderSwitchResult(instance, version, version.VersionId, Changed: false);
        }

        await InstallLoaderAsync(instance, version, progress, cancellationToken).ConfigureAwait(false);

        await _installer
            .InstallAsync(
                instance.Id,
                version.VersionId,
                RuleContext.ForHost(),
                progress,
                cancellationToken)
            .ConfigureAwait(false);

        // Everything that could fail has succeeded, so the instance can now name it.
        instance.Loader = version.Kind;
        instance.LoaderVersion = version.Version;
        await _instances.SaveAsync(instance, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Instance {Instance} switched to {Loader} {Version}",
            instance.Name,
            version.Kind,
            version.Version);
        return new LoaderSwitchResult(instance, version, version.VersionId, Changed: true);
    }

    private async Task InstallLoaderAsync(
        InstanceRecord instance,
        LoaderVersionInfo version,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (version.Kind is LoaderKind.Fabric or LoaderKind.Quilt)
        {
            await _fabric
                .InstallAsync(version.Kind, instance.MinecraftVersion, version.Version, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        // A processor-based loader needs a runtime to run its installer with.
        var required = JavaCompatibility.RequiredMajorFor(instance.MinecraftVersion);
        var runtime = JavaSelection.SelectBest(
                await _java.DetectAsync(cancellationToken).ConfigureAwait(false),
                required)
            ?? throw new ContentProviderException(
                $"Installing {version.Kind.ToDisplayName()} needs Java {required ?? 8}, which is not installed.");

        await _forge
            .InstallAsync(
                version.Kind,
                instance.MinecraftVersion,
                version.Version,
                runtime,
                progress,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
