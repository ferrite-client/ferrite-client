using Ferrite.Core.Minecraft;

namespace Ferrite.Core.Storage;

/// <summary>
/// Everything Ferrite remembers about one instance. Lives at
/// <c>&lt;instance&gt;/instance.json</c> next to the isolated game directory.
/// </summary>
public sealed class InstanceRecord
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public Guid Id { get; set; }

    public string Name { get; set; } = "New instance";

    public string MinecraftVersion { get; set; } = string.Empty;

    public LoaderKind Loader { get; set; } = LoaderKind.Vanilla;

    public string? LoaderVersion { get; set; }

    /// <summary>Java executable override. Null means "use the launcher default".</summary>
    public string? JavaPath { get; set; }

    /// <summary>Provisioned runtime component bound to this instance, if any.</summary>
    public string? JavaRuntimeComponent { get; set; }

    public int? MemoryMb { get; set; }

    public int? MinMemoryMb { get; set; }

    public List<string> JvmArguments { get; set; } = [];

    public List<string> GameArguments { get; set; } = [];

    public Dictionary<string, string> EnvironmentVariables { get; set; } = new();

    public int? WindowWidth { get; set; }

    public int? WindowHeight { get; set; }

    public bool Fullscreen { get; set; }

    public bool DemoMode { get; set; }

    /// <summary>
    /// Whether the user accepted Minecraft's end user licence agreement for this instance. Off by
    /// default: the launcher writes <c>eula.txt</c> only because the user asked it to, because
    /// accepting a licence is the user's act rather than the launcher's.
    /// </summary>
    public bool AcceptEula { get; set; }

    /// <summary>Relative path (inside the instance directory) to a custom icon image.</summary>
    public string? IconPath { get; set; }

    public Guid? AccountId { get; set; }

    public ModpackIdentity? Modpack { get; set; }

    public string? Notes { get; set; }

    public string? LastServerAddress { get; set; }

    public int? LastServerPort { get; set; }

    public string? LastWorld { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastLaunchedAt { get; set; }

    public long TotalPlayTimeSeconds { get; set; }
}
