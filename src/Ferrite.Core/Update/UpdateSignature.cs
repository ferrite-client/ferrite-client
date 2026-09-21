using System.Security.Cryptography;

namespace Ferrite.Core.Update;

/// <summary>
/// Detached-signature handling for the update feed. An unsigned or unverifiable manifest is never
/// accepted: the launcher only replaces its own binaries, so a forged feed would be a direct route
/// to running someone else's code.
/// </summary>
/// <remarks>
/// ECDSA P-256 (SHA-256) and RSA (SHA-256, PKCS#1 v1.5) keys are both accepted; the PEM header
/// decides which. Signatures travel as base64 text so a feed can be published with ordinary tools.
/// </remarks>
public static class UpdateSignature
{
    public const string ManifestEntryName = "manifest.json";
    public const string SignatureEntryName = "manifest.json.sig";

    /// <summary>Verifies a base64 detached signature over the exact manifest bytes.</summary>
    public static bool Verify(byte[] manifestBytes, string signatureBase64, string publicKeyPem)
    {
        ArgumentNullException.ThrowIfNull(manifestBytes);

        if (string.IsNullOrWhiteSpace(signatureBase64) || string.IsNullOrWhiteSpace(publicKeyPem))
        {
            return false;
        }

        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(Collapse(signatureBase64));
        }
        catch (FormatException)
        {
            return false;
        }

        try
        {
            if (LooksLikeRsa(publicKeyPem))
            {
                using var rsa = RSA.Create();
                rsa.ImportFromPem(publicKeyPem);
                return rsa.VerifyData(
                    manifestBytes,
                    signature,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1);
            }

            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(publicKeyPem);
            return ecdsa.VerifyData(manifestBytes, signature, HashAlgorithmName.SHA256);
        }
        catch (CryptographicException)
        {
            // A key this build cannot read is a verification failure, not a reason to continue.
            return false;
        }
    }

    /// <summary>Produces the base64 signature a feed publishes beside the manifest.</summary>
    public static string Sign(byte[] manifestBytes, string privateKeyPem)
    {
        ArgumentNullException.ThrowIfNull(manifestBytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(privateKeyPem);

        byte[] signature;
        if (LooksLikeRsa(privateKeyPem))
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(privateKeyPem);
            signature = rsa.SignData(manifestBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        else
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(privateKeyPem);
            signature = ecdsa.SignData(manifestBytes, HashAlgorithmName.SHA256);
        }

        return Convert.ToBase64String(signature);
    }

    /// <summary>Creates a fresh ECDSA P-256 key pair, for an operator setting up a feed.</summary>
    public static (string PublicKeyPem, string PrivateKeyPem) CreateKeyPair()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (ecdsa.ExportSubjectPublicKeyInfoPem(), ecdsa.ExportPkcs8PrivateKeyPem());
    }

    private static bool LooksLikeRsa(string pem) =>
        pem.Contains("RSA", StringComparison.OrdinalIgnoreCase);

    private static string Collapse(string text) =>
        new(text.Where(character => !char.IsWhiteSpace(character)).ToArray());
}
