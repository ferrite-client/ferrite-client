using System.Security.Cryptography;
using System.Text;
using Ferrite.Core.Minecraft;
using Ferrite.Core.Update;
using Ferrite.Core.Util;
using Microsoft.Extensions.Logging;

namespace Ferrite.Verify;

/// <summary>Launcher update feed tooling: publishing a signed feed, and checking one.</summary>
internal static partial class Scenarios
{
    /// <summary>
    /// Writes a signed feed for one package. Used both to run a real feed locally and to show an
    /// operator exactly what publishing requires.
    /// </summary>
    public static async Task<int> SignUpdateAsync(
        VerifyServices services,
        string? outputDirectory,
        string? packagePath,
        string? version,
        string? privateKeyPath,
        string? runtime,
        string? kind,
        string? notes,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(packagePath) || !File.Exists(packagePath))
        {
            Console.WriteLine("--package <zip> is required and must exist.");
            return 64;
        }

        if (string.IsNullOrWhiteSpace(version))
        {
            Console.WriteLine("--version <x.y.z> is required.");
            return 64;
        }

        var output = outputDirectory ?? Path.Combine(services.Paths.TemporaryDirectory, "update-feed");
        Directory.CreateDirectory(output);

        string privateKey;
        string? publicKey;
        if (privateKeyPath is { Length: > 0 } && File.Exists(privateKeyPath))
        {
            privateKey = await File.ReadAllTextAsync(privateKeyPath, cancellationToken).ConfigureAwait(false);
            publicKey = null;
            Console.WriteLine($"Using the existing private key at {privateKeyPath}.");
        }
        else
        {
            var pair = UpdateSignature.CreateKeyPair();
            privateKey = pair.PrivateKeyPem;
            publicKey = pair.PublicKeyPem;
            await File.WriteAllTextAsync(
                    Path.Combine(output, "update-private-key.pem"),
                    privateKey,
                    cancellationToken)
                .ConfigureAwait(false);
            await File.WriteAllTextAsync(
                    Path.Combine(output, "update-public-key.pem"),
                    publicKey,
                    cancellationToken)
                .ConfigureAwait(false);
            Console.WriteLine("Generated a new ECDSA P-256 key pair.");
            Console.WriteLine($"  public key:  {Path.Combine(output, "update-public-key.pem")}");
            Console.WriteLine("  private key: written to the output directory; keep it offline, never commit it.");
        }

        // The package is published beside the manifest and referenced by name, so the feed keeps
        // working on any host without being re-signed.
        var packageName = Path.GetFileName(packagePath);
        var publishedPackage = Path.Combine(output, packageName);
        if (!string.Equals(
                Path.GetFullPath(packagePath),
                Path.GetFullPath(publishedPackage),
                StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(packagePath, publishedPackage, overwrite: true);
        }

        var info = new FileInfo(publishedPackage);
        var sha256 = await Hashing
            .HashFileAsync(publishedPackage, HashAlgorithmName.SHA256, cancellationToken)
            .ConfigureAwait(false);

        var manifest = new UpdateManifest
        {
            Version = version,
            Channel = "stable",
            PublishedAt = DateTimeOffset.UtcNow,
            Notes = notes,
            Packages =
            [
                new UpdatePackage
                {
                    Runtime = runtime ?? Ferrite.Core.Platform.PlatformInfo.CurrentRuntimeIdentifier(),
                    Kind = kind ?? "self-contained",
                    Url = packageName,
                    Sha256 = sha256,
                    Size = info.Length,
                },
            ],
        };

        var manifestBytes = Encoding.UTF8.GetBytes(manifest.ToJson());
        var signature = UpdateSignature.Sign(manifestBytes, privateKey);
        var manifestPath = Path.Combine(output, UpdateSignature.ManifestEntryName);
        var signaturePath = Path.Combine(output, UpdateSignature.SignatureEntryName);
        await File.WriteAllBytesAsync(manifestPath, manifestBytes, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(signaturePath, signature, cancellationToken).ConfigureAwait(false);

        Console.WriteLine($"Package:  {packagePath}");
        Console.WriteLine($"  size:   {ByteSize.Format(info.Length)}");
        Console.WriteLine($"  sha256: {sha256}");
        Console.WriteLine($"Manifest:  {manifestPath}");
        Console.WriteLine($"Signature: {signaturePath}");
        if (publicKey is not null)
        {
            Console.WriteLine($"  signature verifies with the new key: "
                + $"{UpdateSignature.Verify(manifestBytes, signature, publicKey)}");
        }

        return 0;
    }

    /// <summary>Checks a feed with a supplied public key, and stages the update when asked.</summary>
    public static async Task<int> UpdateCheckAsync(
        VerifyServices services,
        string? feedUrl,
        string? publicKeyPath,
        string? currentVersion,
        string? installDirectory,
        bool stage,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(feedUrl))
        {
            Console.WriteLine("--feed <url> is required.");
            return 64;
        }

        // A local directory is served over real loopback HTTP, so a feed can be exercised without
        // publishing it anywhere.
        UpdateFeedServer? server = null;
        if (Directory.Exists(feedUrl))
        {
            var directory = feedUrl;
            server = new UpdateFeedServer(directory, port: 0);
            feedUrl = server.BaseUrl;
            Console.WriteLine($"Serving {Path.GetFullPath(directory)} on {feedUrl}");
        }

        var publicKey = publicKeyPath is { Length: > 0 } && File.Exists(publicKeyPath)
            ? await File.ReadAllTextAsync(publicKeyPath, cancellationToken).ConfigureAwait(false)
            : null;

        if (publicKey is null)
        {
            Console.WriteLine("--key <pem> is required: without a key the launcher refuses to trust a feed.");
            return 64;
        }

        var service = new UpdateService(
            services.Http,
            services.Downloads,
            services.Paths,
            services.LoggerFactory.CreateLogger<UpdateService>(),
            publicKey);

        var current = currentVersion ?? "0.1.0";
        Console.WriteLine($"Feed:    {feedUrl}");
        Console.WriteLine($"Current: Ferrite {current}");

        var check = await service
            .CheckAsync(
                feedUrl,
                current,
                Ferrite.Core.Platform.PlatformInfo.CurrentRuntimeIdentifier(),
                cancellationToken)
            .ConfigureAwait(false);

        Console.WriteLine(
            $"Manifest verified. Newest release: {check.Manifest.Version} "
            + $"({(check.Manifest.PublishedAt is { } published ? published.ToString("u") : "no date")})");
        if (!string.IsNullOrWhiteSpace(check.Notes))
        {
            Console.WriteLine($"Notes: {check.Notes}");
        }

        if (!check.IsNewer)
        {
            Console.WriteLine("This build is up to date.");
            return 0;
        }

        if (check.Package is null)
        {
            Console.WriteLine("The feed publishes no package for this machine.");
            return 3;
        }

        Console.WriteLine(
            $"Package: {check.Package.Kind} {check.Package.Runtime}, {ByteSize.Format(check.Package.Size)}");

        if (!stage)
        {
            Console.WriteLine("Pass --stage to download and verify it.");
            return 0;
        }

        var result = await service
            .StageAsync(check, new Progress<InstallProgress>(ReportProgress), cancellationToken)
            .ConfigureAwait(false);

        var target = installDirectory ?? AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        Console.WriteLine();
        Console.WriteLine($"Staged {result.Version} in {result.StageDirectory}");
        Console.WriteLine($"Payload: {result.PayloadDirectory}");
        Console.WriteLine($"Hand-off script: {result.ScriptPath}");
        Console.WriteLine();
        Console.WriteLine("To apply it, close Ferrite and run:");
        Console.WriteLine("  " + UpdateHandoff.BuildCommandLine(result, Environment.ProcessId, target));

        // The local feed, when one was started, keeps serving only until this scenario ends.
        server?.Dispose();
        return 0;
    }
}
