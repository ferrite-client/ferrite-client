using Ferrite.Core.Content;

namespace Ferrite.Verify;

/// <summary>Pack metadata: what each installed version expects, and what installed packs declare.</summary>
internal static partial class Scenarios
{
    public static async Task<int> PackMetadataAsync(
        VerifyServices services,
        string? gameDirectory,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"Versions directory: {services.Paths.VersionsDirectory}");
        var versions = Directory.Exists(services.Paths.VersionsDirectory)
            ? Directory.GetDirectories(services.Paths.VersionsDirectory)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList()
            : [];

        var read = 0;
        foreach (var version in versions)
        {
            var formats = services.PackFormats.Read(version!);
            if (formats is null)
            {
                var jar = services.Paths.VersionClientJarFile(version!);
                Console.WriteLine(File.Exists(jar)
                    ? $"  {version}: the client file declares no pack format this build recognises"
                    : $"  {version}: no client file installed");
                continue;
            }

            read++;
            Console.WriteLine($"  {version}: resource format {formats.ResourceText}, data format {formats.DataText}");
        }

        Console.WriteLine();
        if (read == 0)
        {
            Console.WriteLine("No installed client file declared a pack format.");
            return 3;
        }

        var target = gameDirectory;
        if (target is null)
        {
            var instances = await services.Instances.LoadAllAsync(cancellationToken).ConfigureAwait(false);
            var newest = instances.OrderByDescending(instance => instance.CreatedAt).FirstOrDefault();
            if (newest is not null)
            {
                target = services.Paths.InstanceGameDirectory(newest.Id);
                Console.WriteLine($"Instance {newest.Name} ({newest.MinecraftVersion})");
            }
        }

        if (target is null || !Directory.Exists(target))
        {
            Console.WriteLine("No game directory to inspect for installed packs.");
            return 0;
        }

        var instanceFormat = ResolveInstancePackFormat(services, target, cancellationToken);
        Console.WriteLine($"Instance resource pack format: {instanceFormat?.ToString() ?? "unknown"}");

        var any = false;
        foreach (var folder in new[] { "resourcepacks", "shaderpacks", "datapacks" })
        {
            foreach (var entry in InstanceContentManager.ListPacks(target, folder, instanceFormat))
            {
                any = true;
                Console.WriteLine($"  {folder}/{entry.FileName}");
                Console.WriteLine($"      {entry.PackFormatText ?? "no pack.mcmeta"}");
                if (entry.CompatibilityText is { Length: > 0 } note)
                {
                    Console.WriteLine($"      {note}");
                }
            }
        }

        if (!any)
        {
            Console.WriteLine("No packs installed in that game directory.");
        }

        return 0;
    }

    /// <summary>
    /// The format an instance's packs are compared against comes from its Minecraft version's client
    /// file, which is the same source the launcher uses.
    /// </summary>
    private static int? ResolveInstancePackFormat(
        VerifyServices services,
        string gameDirectory,
        CancellationToken cancellationToken)
    {
        var instances = services.Instances.LoadAllAsync(cancellationToken).GetAwaiter().GetResult();
        var instance = instances.FirstOrDefault(candidate =>
            string.Equals(
                services.Paths.InstanceGameDirectory(candidate.Id),
                Path.GetFullPath(gameDirectory),
                StringComparison.OrdinalIgnoreCase));

        var formats = instance?.MinecraftVersion is { } version ? services.PackFormats.Read(version) : null;
        return formats?.Resource;
    }
}
