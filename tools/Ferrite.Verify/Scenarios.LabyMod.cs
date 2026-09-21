using Ferrite.Core.Minecraft;
using Ferrite.Core.Storage;

namespace Ferrite.Verify;

/// <summary>Installing LabyMod and launching the instance it was installed into.</summary>
internal static partial class Scenarios
{
    /// <summary>
    /// Installs LabyMod for the instance's Minecraft version, points the instance at it, and - with
    /// <paramref name="launch"/> - runs it, because a loader is not proven until a real instance
    /// launches on it.
    /// </summary>
    public static async Task<int> LabyModAsync(
        VerifyServices services,
        string minecraftVersion,
        int seconds,
        bool launch,
        string? instanceName,
        CancellationToken cancellationToken)
    {
        var instance = instanceName is { Length: > 0 }
            ? (await services.Instances.LoadAllAsync(cancellationToken).ConfigureAwait(false))
                .FirstOrDefault(record => string.Equals(record.Name, instanceName, StringComparison.OrdinalIgnoreCase))
            : await GetOrCreateInstanceAsync(services, minecraftVersion, cancellationToken).ConfigureAwait(false);

        // A named instance that does not exist yet is created, so a verification run never reuses an
        // instance someone else is using.
        if (instance is null && instanceName is { Length: > 0 })
        {
            instance = await services.Instances
                .CreateAsync(
                    new InstanceRecord
                    {
                        Id = Guid.NewGuid(),
                        Name = instanceName,
                        MinecraftVersion = minecraftVersion,
                        Loader = LoaderKind.Vanilla,
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            Console.WriteLine($"Created instance {instance.Name}");
        }

        if (instance is null)
        {
            Console.WriteLine($"No instance named '{instanceName}'.");
            return 2;
        }

        Console.WriteLine($"Instance: {instance.Name} (Minecraft {instance.MinecraftVersion})");
        var manifest = await services.LabyMod.GetManifestAsync(cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"LabyMod {manifest.LabyModVersion} (commit {manifest.CommitReference})");
        Console.WriteLine($"Assets published: {manifest.Assets.Count}");
        Console.WriteLine(
            $"Supports this version: "
            + manifest.MinecraftVersions.Any(version =>
                string.Equals(version.Tag, instance.MinecraftVersion, StringComparison.OrdinalIgnoreCase)));

        var result = await services.LabyMod
            .InstallAsync(
                instance.Id,
                instance.MinecraftVersion,
                new Progress<InstallProgress>(ReportProgress),
                cancellationToken)
            .ConfigureAwait(false);
        Console.WriteLine();
        Console.WriteLine($"Installed version id: {result.VersionId}");
        Console.WriteLine($"Libraries in the profile: {result.LibraryCount}");
        Console.WriteLine($"Assets downloaded: {result.AssetCount}");

        instance.Loader = LoaderKind.LabyMod;
        instance.LoaderVersion = result.VersionId;
        await services.Instances.SaveAsync(instance, cancellationToken).ConfigureAwait(false);

        var profile = services.Paths.VersionJsonFile(result.VersionId);
        Console.WriteLine($"Profile written: {File.Exists(profile)} ({profile})");
        var assetDirectory = Path.Combine(
            services.Paths.InstanceGameDirectory(instance.Id),
            Ferrite.Core.Loaders.LabyModInstaller.AssetFolder);
        var assets = Directory.Exists(assetDirectory)
            ? Directory.EnumerateFiles(assetDirectory, "*.jar").Count()
            : 0;
        Console.WriteLine($"Asset files on disk: {assets}");

        if (!launch)
        {
            return 0;
        }

        Console.WriteLine();
        Console.WriteLine("Launching the instance on LabyMod...");
        return await InstanceLaunchWithPinnedJavaAsync(
                services,
                instance.MinecraftVersion,
                seconds,
                null,
                false,
                null,
                null,
                null,
                instance.Name,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
