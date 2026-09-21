using Ferrite.Core.Java;
using Ferrite.Core.Loaders;
using Ferrite.Core.Platform;
using Ferrite.Core.Rules;
using Ferrite.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Ferrite.Core.Minecraft;

/// <summary>
/// The complete "press play" flow: verify or repair the installation, choose a compatible runtime,
/// build the command, run preflight checks, and start the game.
/// </summary>
public sealed class InstanceLauncher
{
    private readonly MinecraftInstaller _installer;
    private readonly VersionResolver _resolver;
    private readonly InstallPlanner _planner;
    private readonly JavaDetector _javaDetector;
    private readonly LaunchService _launcher;
    private readonly AppPaths _paths;
    private readonly ILogger<InstanceLauncher> _logger;

    public InstanceLauncher(
        MinecraftInstaller installer,
        VersionResolver resolver,
        InstallPlanner planner,
        JavaDetector javaDetector,
        LaunchService launcher,
        AppPaths paths,
        ILogger<InstanceLauncher> logger)
    {
        _installer = installer;
        _resolver = resolver;
        _planner = planner;
        _javaDetector = javaDetector;
        _launcher = launcher;
        _paths = paths;
        _logger = logger;
    }

    /// <summary>The version id an instance launches: the loader version, or the vanilla version.</summary>
    public static string LaunchVersionId(InstanceRecord instance) =>
        instance.Loader == LoaderKind.Vanilla || string.IsNullOrEmpty(instance.LoaderVersion)
            ? instance.MinecraftVersion
            : new LoaderVersionInfo(
                instance.Loader,
                instance.LoaderVersion,
                instance.MinecraftVersion,
                true,
                null).VersionId;

    public async Task<InstanceLaunchResult> LaunchAsync(
        InstanceLaunchRequest request,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var instance = request.Instance;
        var versionId = LaunchVersionId(instance);

        // The instance's acceptance is the source of truth; the file is derived from it, so a file
        // deleted by hand comes back on the next launch and a withdrawn acceptance is undone.
        EulaFile.Apply(_paths.InstanceGameDirectory(instance.Id), instance.AcceptEula);

        var verification = await _installer
            .VerifyAsync(instance.Id, versionId, RuleContext.ForHost(), progress: null, cancellationToken)
            .ConfigureAwait(false);

        if (!verification.IsHealthy)
        {
            if (!request.RepairBeforeLaunch)
            {
                return InstanceLaunchResult.Failed(
                    $"The installation is incomplete ({verification.Issues.Count} file(s) missing).");
            }

            _logger.LogInformation("Repairing {Count} file(s) before launch", verification.Issues.Count);
            verification = await _installer
                .RepairAsync(instance.Id, versionId, RuleContext.ForHost(), progress, cancellationToken)
                .ConfigureAwait(false);

            if (!verification.IsHealthy)
            {
                return InstanceLaunchResult.Failed(
                    $"The installation could not be repaired ({verification.Issues.Count} file(s) still missing).");
            }
        }

        var document = await _resolver.ResolveAsync(versionId, cancellationToken).ConfigureAwait(false);
        var plan = await _planner
            .CreateAsync(document, versionId, RuleContext.ForHost(), cancellationToken)
            .ConfigureAwait(false);

        var required = JavaCompatibility.RequiredMajorFor(document, versionId);
        var runtimes = await _javaDetector
            .DetectAsync(cancellationToken, request.CustomJavaPaths)
            .ConfigureAwait(false);
        var runtime = SelectRuntime(instance, request.DefaultJavaPath, runtimes, required);
        if (runtime is null)
        {
            return InstanceLaunchResult.Failed(
                $"No compatible Java runtime is installed. This version needs Java {required ?? 8}.");
        }

        var compatibility = JavaCompatibility.Evaluate(runtime, required);
        var account = request.Account ?? new LaunchAccount
        {
            PlayerName = "Player",
            Uuid = "00000000000000000000000000000000",
            AccessToken = string.Empty,
        };

        var launchRequest = new LaunchRequest
        {
            Document = document,
            Plan = plan,
            Instance = instance,
            Account = account,
            Java = runtime,
            GameDirectory = _paths.InstanceGameDirectory(instance.Id),
            NativesDirectory = _paths.InstanceNativesDirectory(instance.Id, versionId),
            AssetsRoot = _paths.AssetsDirectory,
            LibrariesDirectory = _paths.LibrariesDirectory,
            LegacyAssetsDirectory = _paths.LegacyVirtualAssetsDirectory,
            DefaultMemoryMb = instance.MemoryMb ?? LaunchPreflight.SuggestDefaultMemoryMb(),
            RequestQuickPlayMultiplayer = request.JoinLastServer,
        };

        LaunchCommand command;
        try
        {
            command = new LaunchCommandBuilder().Build(launchRequest);
        }
        catch (VersionMetadataException exception)
        {
            return InstanceLaunchResult.Failed(exception.Message);
        }

        var issues = LaunchPreflight.Check(launchRequest, command);
        var blocking = issues.FirstOrDefault(issue => issue.IsBlocking);
        if (blocking is not null)
        {
            return new InstanceLaunchResult
            {
                Started = false,
                Verification = verification,
                Compatibility = compatibility,
                Issues = issues,
                Error = blocking.Message,
            };
        }

        var process = await _launcher.StartAsync(instance.Id, command, cancellationToken).ConfigureAwait(false);
        return new InstanceLaunchResult
        {
            Started = true,
            Process = process,
            CommandPreview = command.ToDisplayString(),
            Verification = verification,
            Compatibility = compatibility,
            Issues = issues,
        };
    }

    /// <summary>
    /// Picks the runtime for a launch: the instance's own choice, then the launcher-wide default,
    /// then the best fit for the version. Internal rather than public so the precedence can be tested
    /// without a real JVM on the machine.
    /// </summary>
    internal static JavaRuntime? SelectRuntime(
        InstanceRecord instance,
        string? defaultJavaPath,
        IReadOnlyList<JavaRuntime> runtimes,
        int? required)
    {
        // The instance's own choice wins, then the launcher-wide default, then the best fit.
        foreach (var preferred in new[] { instance.JavaPath, defaultJavaPath })
        {
            if (string.IsNullOrEmpty(preferred))
            {
                continue;
            }

            var configured = runtimes.FirstOrDefault(runtime =>
                string.Equals(runtime.ExecutablePath, preferred, StringComparison.OrdinalIgnoreCase));
            if (configured is not null)
            {
                return configured;
            }
        }

        return JavaSelection.SelectBest(runtimes, required);
    }
}
