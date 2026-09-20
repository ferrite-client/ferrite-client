using Ferrite.Core.Java;
using Ferrite.Core.Storage;

namespace Ferrite.Core.Minecraft;

public sealed record InstanceLaunchRequest
{
    public required InstanceRecord Instance { get; init; }

    public LaunchAccount? Account { get; init; }

    public bool RepairBeforeLaunch { get; init; } = true;

    public bool JoinLastServer { get; init; }
}

public sealed record InstanceLaunchResult
{
    public required bool Started { get; init; }

    public GameProcess? Process { get; init; }

    public VerificationReport? Verification { get; init; }

    public JavaCompatibilityResult? Compatibility { get; init; }

    public IReadOnlyList<PreflightIssue> Issues { get; init; } = [];

    public string? Error { get; init; }

    public static InstanceLaunchResult Failed(string error, IReadOnlyList<PreflightIssue>? issues = null) =>
        new() { Started = false, Error = error, Issues = issues ?? [] };
}
