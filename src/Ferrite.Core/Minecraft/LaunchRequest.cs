using Ferrite.Core.Java;
using Ferrite.Core.Storage;

namespace Ferrite.Core.Minecraft;

/// <summary>Account material the launch pipeline needs. Tokens never reach a log.</summary>
public sealed record LaunchAccount
{
    public required string PlayerName { get; init; }

    public required string Uuid { get; init; }

    public required string AccessToken { get; init; }

    public string? Xuid { get; init; }

    public string? ClientId { get; init; }

    /// <summary>Legacy user type token: "msa" for Microsoft accounts, "mojang" for Yggdrasil.</summary>
    public string UserType { get; init; } = "msa";
}

/// <summary>Everything needed to build one launch command.</summary>
public sealed class LaunchRequest
{
    public required VersionDocument Document { get; init; }

    public required InstallPlan Plan { get; init; }

    public required InstanceRecord Instance { get; init; }

    public required LaunchAccount Account { get; init; }

    public required JavaRuntime Java { get; init; }

    public required string GameDirectory { get; init; }

    public required string NativesDirectory { get; init; }

    public required string AssetsRoot { get; init; }

    public string LibrariesDirectory { get; init; } = string.Empty;

    /// <summary>Legacy virtual asset directory used by the pre-1.7 <c>game_assets</c> placeholder.</summary>
    public string LegacyAssetsDirectory { get; init; } = string.Empty;

    public string LauncherName { get; init; } = "Ferrite";

    public string LauncherVersion { get; init; } = "0.1.0";

    public int DefaultMemoryMb { get; init; } = 4096;

    /// <summary>Join a server straight away using the modern quick-play argument.</summary>
    public bool RequestQuickPlayMultiplayer { get; init; }
}

/// <summary>A fully resolved launch command. Arguments are a list, never a shell string.</summary>
public sealed class LaunchCommand
{
    public required string ExecutablePath { get; init; }

    public required IReadOnlyList<string> Arguments { get; init; }

    public required string WorkingDirectory { get; init; }

    public required IReadOnlyDictionary<string, string> EnvironmentVariables { get; init; }

    public required string Classpath { get; init; }

    /// <summary>Same command with credential-bearing arguments replaced, safe to display.</summary>
    public required IReadOnlyList<string> DisplayArguments { get; init; }

    public string ToDisplayString() =>
        "\"" + ExecutablePath + "\" " + string.Join(' ', DisplayArguments.Select(QuoteIfNeeded));

    private static string QuoteIfNeeded(string value) =>
        value.Contains(' ', StringComparison.Ordinal) ? "\"" + value + "\"" : value;
}
