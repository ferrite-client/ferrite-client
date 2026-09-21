using System.Reflection;

namespace Ferrite.App.Services;

/// <summary>
/// Loads the update feed's public key from the assembly. The key must ship inside the binary: a key
/// read from the data directory could be replaced alongside a forged feed, which would defeat the
/// point of signing.
/// </summary>
/// <remarks>
/// The key is not a secret. It is embedded only when the build supplies one, so a build without a
/// key reports that updates cannot be verified instead of silently accepting anything.
/// </remarks>
public static class UpdateFeedKey
{
    public const string ResourceName = "Ferrite.App.update-public-key.pem";

    /// <summary>The PEM-encoded public key, or null when this build carries none.</summary>
    public static string? Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        var pem = reader.ReadToEnd().Trim();
        return pem.Contains("KEY-----", StringComparison.Ordinal) ? pem : null;
    }
}
