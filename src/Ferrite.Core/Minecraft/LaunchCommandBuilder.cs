using Ferrite.Core.Rules;

namespace Ferrite.Core.Minecraft;

/// <summary>
/// Builds the Java command for one launch: classpath order, platform JVM arguments, the main
/// class, and game arguments. Placeholder substitution fails closed on an unknown name.
/// </summary>
public sealed class LaunchCommandBuilder
{
    private const string RedactedValue = "***redacted***";

    public LaunchCommand Build(LaunchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var classpath = BuildClasspath(request);
        var placeholders = BuildPlaceholders(request, classpath);
        var context = new RuleContext { Features = BuildFeatures(request) };

        if (string.IsNullOrWhiteSpace(request.Document.MainClass))
        {
            throw new VersionMetadataException(
                $"Version '{request.Plan.VersionId}' does not declare a main class.");
        }

        var arguments = new List<string>();
        arguments.AddRange(BuildJvmArguments(request, context, placeholders));
        arguments.Add(request.Document.MainClass);
        arguments.AddRange(BuildGameArguments(request, context, placeholders));

        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in request.Instance.EnvironmentVariables)
        {
            environment[key] = value;
        }

        return new LaunchCommand
        {
            ExecutablePath = request.Java.ExecutablePath,
            Arguments = arguments,
            WorkingDirectory = request.GameDirectory,
            EnvironmentVariables = environment,
            Classpath = classpath,
            DisplayArguments = Redact(arguments, request.Account.AccessToken),
        };
    }

    public static string BuildClasspath(LaunchRequest request)
    {
        var seen = new HashSet<string>(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var entries = new List<string>();

        foreach (var library in request.Plan.Libraries)
        {
            if (seen.Add(library.TargetPath))
            {
                entries.Add(library.TargetPath);
            }
        }

        if (request.Plan.ClientJarPath is { } clientJar && seen.Add(clientJar))
        {
            entries.Add(clientJar);
        }

        return string.Join(Path.PathSeparator, entries);
    }

    private static List<string> BuildJvmArguments(
        LaunchRequest request,
        RuleContext context,
        IReadOnlyDictionary<string, string> placeholders)
    {
        var result = new List<string>();
        var jvmEntries = VersionArgumentReader.Read(request.Document.Arguments?.Jvm);
        var defaultEntries = VersionArgumentReader.Read(request.Document.Arguments?.DefaultUserJvm);
        var memoryOverridden = request.Instance.MemoryMb is > 0;

        if (jvmEntries.Count == 0)
        {
            // Legacy versions did not ship a JVM argument list; the launcher supplied one.
            result.Add("-Djava.library.path=" + request.NativesDirectory);
            result.Add("-Dminecraft.launcher.brand=" + request.LauncherName);
            result.Add("-Dminecraft.launcher.version=" + request.LauncherVersion);
        }
        else
        {
            result.AddRange(VersionArgumentReader.Resolve(jvmEntries, context, Expand(placeholders)));
        }

        foreach (var entry in VersionArgumentReader.Resolve(defaultEntries, context, Expand(placeholders)))
        {
            if (memoryOverridden && IsMemoryFlag(entry))
            {
                continue;
            }

            result.Add(entry);
        }

        AddMemoryArguments(request, memoryOverridden, result);

        if (request.Document.Logging?.Client is { } logging
            && !string.IsNullOrEmpty(logging.Argument)
            && !string.IsNullOrEmpty(request.Plan.LoggingConfigPath))
        {
            result.Add(logging.Argument.Replace("${path}", request.Plan.LoggingConfigPath, StringComparison.Ordinal));
        }

        foreach (var argument in request.Instance.JvmArguments)
        {
            if (!string.IsNullOrWhiteSpace(argument))
            {
                result.Add(argument);
            }
        }

        // Modern metadata already contains "-cp ${classpath}"; legacy metadata does not.
        if (!result.Contains("-cp", StringComparer.Ordinal)
            && !result.Contains("-classpath", StringComparer.Ordinal))
        {
            result.Add("-cp");
            result.Add(placeholders["classpath"]);
        }

        return result;
    }

    private static void AddMemoryArguments(LaunchRequest request, bool memoryOverridden, List<string> result)
    {
        if (!memoryOverridden)
        {
            if (!result.Any(IsMemoryFlag))
            {
                result.Add($"-Xmx{request.DefaultMemoryMb}M");
            }

            return;
        }

        result.RemoveAll(IsMemoryFlag);
        if (request.Instance.MinMemoryMb is { } minimum and > 0)
        {
            result.Add($"-Xms{minimum}M");
        }

        result.Add($"-Xmx{request.Instance.MemoryMb}M");
    }

    private static bool IsMemoryFlag(string argument) =>
        argument.StartsWith("-Xmx", StringComparison.OrdinalIgnoreCase)
        || argument.StartsWith("-Xms", StringComparison.OrdinalIgnoreCase);

    private static List<string> BuildGameArguments(
        LaunchRequest request,
        RuleContext context,
        IReadOnlyDictionary<string, string> placeholders)
    {
        var expand = Expand(placeholders);
        var entries = VersionArgumentReader.Read(request.Document.Arguments?.Game);
        var result = entries.Count > 0
            ? VersionArgumentReader.Resolve(entries, context, expand)
            : VersionArgumentReader.Resolve(
                VersionArgumentReader.ReadLegacy(request.Document.MinecraftArguments),
                context,
                expand);

        foreach (var argument in request.Instance.GameArguments)
        {
            if (!string.IsNullOrWhiteSpace(argument))
            {
                result.Add(argument);
            }
        }

        return result;
    }

    private static Dictionary<string, bool> BuildFeatures(LaunchRequest request)
    {
        var instance = request.Instance;
        var hasResolution = instance.WindowWidth is > 0 && instance.WindowHeight is > 0;
        var quickPlayMultiplayer = request.RequestQuickPlayMultiplayer
            && !string.IsNullOrWhiteSpace(instance.LastServerAddress);

        return new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["is_demo_user"] = instance.DemoMode,
            ["has_custom_resolution"] = hasResolution,
            ["has_quick_plays_support"] = quickPlayMultiplayer,
            ["is_quick_play_singleplayer"] = false,
            ["is_quick_play_multiplayer"] = quickPlayMultiplayer,
            ["is_quick_play_realms"] = false,
        };
    }

    private static Dictionary<string, string> BuildPlaceholders(LaunchRequest request, string classpath)
    {
        var instance = request.Instance;
        var document = request.Document;
        var quickPlayDirectory = Path.Combine(request.GameDirectory, "quickPlay");

        var placeholders = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["auth_player_name"] = request.Account.PlayerName,
            ["version_name"] = document.Id ?? request.Plan.VersionId,
            ["game_directory"] = request.GameDirectory,
            ["assets_root"] = request.AssetsRoot,
            ["assets_index_name"] = document.Assets ?? document.AssetIndex?.Id ?? "legacy",
            ["auth_uuid"] = request.Account.Uuid.Replace("-", string.Empty, StringComparison.Ordinal),
            ["auth_access_token"] = request.Account.AccessToken,
            ["auth_session"] = request.Account.AccessToken,
            ["user_type"] = request.Account.UserType,
            ["version_type"] = document.Type ?? "release",
            ["natives_directory"] = request.NativesDirectory,
            ["launcher_name"] = request.LauncherName,
            ["launcher_version"] = request.LauncherVersion,
            ["classpath"] = classpath,
            ["classpath_separator"] = Path.PathSeparator.ToString(),
            ["library_directory"] = request.LibrariesDirectory,
            ["game_assets"] = request.LegacyAssetsDirectory,
            ["clientid"] = request.Account.ClientId ?? string.Empty,
            ["auth_xuid"] = request.Account.Xuid ?? string.Empty,
            ["resolution_width"] = instance.WindowWidth?.ToString() ?? string.Empty,
            ["resolution_height"] = instance.WindowHeight?.ToString() ?? string.Empty,
            ["quickPlayPath"] = Path.Combine(quickPlayDirectory, "quickPlayLog.json"),
            ["quickPlaySingleplayer"] = instance.LastWorld ?? string.Empty,
            ["quickPlayMultiplayer"] = instance.LastServerAddress is { Length: > 0 } address
                ? FormatServer(address, instance.LastServerPort)
                : string.Empty,
            ["quickPlayRealms"] = string.Empty,
        };

        return placeholders;
    }

    private static string FormatServer(string address, int? port) =>
        port is { } value and > 0 && !address.Contains(':', StringComparison.Ordinal)
            ? address + ":" + value
            : address;

    private static Func<string, string> Expand(IReadOnlyDictionary<string, string> placeholders) =>
        value => ExpandValue(value, placeholders);

    /// <summary>Replaces every <c>${name}</c> placeholder, throwing on an unknown name.</summary>
    public static string ExpandValue(string value, IReadOnlyDictionary<string, string> placeholders)
    {
        if (!value.Contains("${", StringComparison.Ordinal))
        {
            return value;
        }

        var result = value;
        var start = result.IndexOf("${", StringComparison.Ordinal);
        while (start >= 0)
        {
            var end = result.IndexOf('}', start);
            if (end < 0)
            {
                throw new VersionMetadataException($"Unterminated placeholder in argument: {value}");
            }

            var name = result[(start + 2)..end];
            if (!placeholders.TryGetValue(name, out var replacement))
            {
                throw new VersionMetadataException($"Unknown launch placeholder '${{{name}}}'.");
            }

            result = string.Concat(result.AsSpan(0, start), replacement, result.AsSpan(end + 1));
            start = result.IndexOf("${", start + replacement.Length, StringComparison.Ordinal);
        }

        return result;
    }

    private static List<string> Redact(IReadOnlyList<string> arguments, string accessToken)
    {
        var redacted = new List<string>(arguments.Count);
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (!string.IsNullOrEmpty(accessToken) && argument.Contains(accessToken, StringComparison.Ordinal))
            {
                redacted.Add(argument.Replace(accessToken, RedactedValue, StringComparison.Ordinal));
                continue;
            }

            if (index > 0 && IsCredentialFlag(arguments[index - 1]))
            {
                redacted.Add(RedactedValue);
                continue;
            }

            redacted.Add(argument);
        }

        return redacted;
    }

    private static bool IsCredentialFlag(string previous) =>
        string.Equals(previous, "--accessToken", StringComparison.Ordinal)
        || string.Equals(previous, "--clientId", StringComparison.Ordinal)
        || string.Equals(previous, "--uuid", StringComparison.Ordinal)
        || string.Equals(previous, "--xuid", StringComparison.Ordinal);
}
