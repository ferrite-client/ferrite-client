namespace Ferrite.Core.Minecraft;

/// <summary>
/// Maven coordinates as used by Minecraft libraries, including the optional classifier and the
/// optional <c>@extension</c> suffix used by a few legacy entries.
/// </summary>
public sealed record MavenCoordinates(
    string Group,
    string Artifact,
    string Version,
    string? Classifier = null,
    string Extension = "jar")
{
    public string FileName =>
        Artifact + "-" + Version + (Classifier is null ? string.Empty : "-" + Classifier) + "." + Extension;

    public string DirectoryPath => Group.Replace('.', '/') + "/" + Artifact + "/" + Version;

    /// <summary>Relative path with forward slashes, suitable for a maven repository layout.</summary>
    public string RelativePath => DirectoryPath + "/" + FileName;

    public static bool TryParse(string? name, out MavenCoordinates coordinates)
    {
        coordinates = default!;
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var value = name.Trim();
        var extension = "jar";
        var atIndex = value.IndexOf('@', StringComparison.Ordinal);
        if (atIndex > 0)
        {
            extension = value[(atIndex + 1)..];
            value = value[..atIndex];
        }

        var parts = value.Split(':');
        if (parts.Length < 3)
        {
            return false;
        }

        var group = parts[0];
        var artifact = parts[1];
        var version = parts[2];
        string? classifier = parts.Length >= 4 && parts[3].Length > 0 ? parts[3] : null;

        if (group.Length == 0 || artifact.Length == 0 || version.Length == 0)
        {
            return false;
        }

        coordinates = new MavenCoordinates(group, artifact, version, classifier, extension);
        return true;
    }

    /// <summary>True when the classifier marks this entry as a native library bundle.</summary>
    public bool IsNativeClassifier =>
        Classifier is not null
        && Classifier.StartsWith("natives", StringComparison.OrdinalIgnoreCase);
}
