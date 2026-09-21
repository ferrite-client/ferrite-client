using Ferrite.Core.Content;
using Ferrite.Core.Minecraft;
using Ferrite.Core.Util;

namespace Ferrite.Verify;

/// <summary>Content update checking and application.</summary>
internal static partial class Scenarios
{
    /// <summary>
    /// Checks launcher-installed content for updates and, with <c>--apply</c>, installs them.
    /// </summary>
    public static async Task<int> CheckContentUpdatesAsync(
        VerifyServices services,
        string? instanceName,
        bool apply,
        CancellationToken cancellationToken)
    {
        var instances = await services.Instances.LoadAllAsync(cancellationToken).ConfigureAwait(false);
        var instance = instanceName is { Length: > 0 }
            ? instances.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, instanceName, StringComparison.OrdinalIgnoreCase))
            : instances.OrderByDescending(candidate => candidate.CreatedAt).FirstOrDefault();

        if (instance is null)
        {
            Console.WriteLine(instanceName is { Length: > 0 }
                ? $"No instance named '{instanceName}'."
                : "No instances to check.");
            return 2;
        }

        Console.WriteLine(
            $"Instance: {instance.Name} ({instance.MinecraftVersion}, "
            + $"{instance.Loader.ToDisplayName()} {instance.LoaderVersion ?? "-"})");

        var manifestPath = services.Paths.InstanceContentManifestFile(instance.Id);
        var manifest = services.Manifests.Load(manifestPath);
        Console.WriteLine($"Tracked content: {manifest.Entries.Count} file(s)");
        foreach (var entry in manifest.Entries)
        {
            Console.WriteLine($"  {entry.RelativePath} <- {entry.Provider}:{entry.ProjectId}@{entry.VersionId}");
        }

        if (manifest.Entries.Count == 0)
        {
            Console.WriteLine("Nothing is tracked, so there is nothing to update.");
            return 0;
        }

        var updates = new List<ContentUpdate>();
        foreach (var provider in new IContentProvider[] { services.Modrinth, services.CurseForge })
        {
            if (!provider.IsConfigured)
            {
                Console.WriteLine($"{provider.Name}: skipped ({provider.UnavailableReason})");
                continue;
            }

            var report = await services.ContentUpdates
                .CheckAsync(instance, provider, cancellationToken)
                .ConfigureAwait(false);

            Console.WriteLine(
                $"{provider.Name}: {report.Updates.Count} update(s), {report.UpToDate.Count} up to date, "
                + $"{report.Skipped.Count} skipped");
            foreach (var warning in report.Warnings)
            {
                Console.WriteLine($"  warn: {warning}");
            }

            foreach (var update in report.Updates)
            {
                Console.WriteLine(
                    $"  update: {update.Entry.RelativePath} {update.Entry.VersionId} -> "
                    + $"{update.AvailableVersionName} ({update.Title})");
            }

            updates.AddRange(report.Updates);
        }

        if (!apply)
        {
            Console.WriteLine("Pass --apply to install these updates.");
            return 0;
        }

        if (updates.Count == 0)
        {
            Console.WriteLine("No updates to install.");
            return 0;
        }

        var result = await services.ContentUpdates
            .ApplyAsync(
                instance,
                updates,
                new Progress<InstallProgress>(ReportProgress),
                cancellationToken)
            .ConfigureAwait(false);

        Console.WriteLine();
        Console.WriteLine($"Applied {result.Updated} update(s)");
        foreach (var warning in result.Warnings)
        {
            Console.WriteLine($"  warn: {warning}");
        }

        var gameDirectory = services.Paths.InstanceGameDirectory(instance.Id);
        var after = services.Manifests.Load(manifestPath);
        Console.WriteLine($"Tracked content now: {after.Entries.Count} file(s)");
        foreach (var entry in after.Entries)
        {
            var target = Path.Combine(gameDirectory, entry.RelativePath);
            var size = File.Exists(target) ? ByteSize.Format(new FileInfo(target).Length) : "missing";
            Console.WriteLine($"  {entry.RelativePath} @ {entry.VersionId} ({size})");
        }

        return result.Updated > 0 ? 0 : 3;
    }
}
