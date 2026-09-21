using Ferrite.Core.Java;
using Ferrite.Core.Storage;

namespace Ferrite.Core.Minecraft;

public sealed record InstanceLaunchRequest
{
    public required InstanceRecord Instance { get; init; }

    public LaunchAccount? Account { get; init; }

    /// <summary>
    /// The launcher-wide Java choice, used when the instance does not pin one of its own. Passed in
    /// rather than read from settings so the launch path stays testable without a settings store.
    /// </summary>
    public string? DefaultJavaPath { get; init; }

    /// <summary>
    /// Java executables the user added by hand. They are probed alongside the environment scan, so a
    /// path outside the usual locations can still be the runtime an instance launches on.
    /// </summary>
    public IReadOnlyList<string> CustomJavaPaths { get; init; } = [];

    public bool RepairBeforeLaunch { get; init; } = true;

    public bool JoinLastServer { get; init; }
}

public sealed record InstanceLaunchResult
{
    public required bool Started { get; init; }

    public GameProcess? Process { get; init; }

    /// <summary>
    /// The command that was started, with credentials redacted, so a diagnostic log or a bug report
    /// can show how the game was launched without carrying a token.
    /// </summary>
    public string? CommandPreview { get; init; }

    public VerificationReport? Verification { get; init; }

    public JavaCompatibilityResult? Compatibility { get; init; }

    public IReadOnlyList<PreflightIssue> Issues { get; init; } = [];

    public string? Error { get; init; }

    public static InstanceLaunchResult Failed(string error, IReadOnlyList<PreflightIssue>? issues = null) =>
        new() { Started = false, Error = error, Issues = issues ?? [] };
}
