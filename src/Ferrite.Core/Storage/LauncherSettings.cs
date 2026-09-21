namespace Ferrite.Core.Storage;

public enum ThemeVariant
{
    Dark,
    Light,
    System,
}

public sealed class LauncherSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public ThemeVariant Theme { get; set; } = ThemeVariant.Dark;

    public string Language { get; set; } = "en";

    public int MaxConcurrentDownloads { get; set; } = 8;

    public string? ProxyUrl { get; set; }

    /// <summary>Per-endpoint host overrides, keyed by the canonical host name.</summary>
    public Dictionary<string, string> MirrorOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Azure public-client application id used for Microsoft sign-in.</summary>
    public string? MicrosoftClientId { get; set; }

    public string? DefaultJavaPath { get; set; }

    /// <summary>
    /// Java executables the user added by hand. They are re-probed on every scan, so a path that
    /// stops working is dropped from the list rather than offered as a broken choice.
    /// </summary>
    public List<string> CustomJavaPaths { get; set; } = [];

    public int? DefaultMemoryMb { get; set; }

    public Guid? ActiveAccountId { get; set; }

    public Guid? LastSelectedInstanceId { get; set; }

    public bool CheckForUpdatesOnStartup { get; set; } = true;

    public string? UpdateFeedUrl { get; set; }

    public bool ShowSnapshotsInVersionList { get; set; }

    public bool ShowHistoricalVersions { get; set; }

    public int LogRetentionDays { get; set; } = 14;

    public WindowPlacement Window { get; set; } = new();
}

public sealed class WindowPlacement
{
    public double Width { get; set; } = 1360;

    public double Height { get; set; } = 860;

    public bool Maximized { get; set; }
}
