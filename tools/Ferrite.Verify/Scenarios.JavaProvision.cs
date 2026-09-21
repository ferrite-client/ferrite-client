using Ferrite.Core.Java;
using Ferrite.Core.Util;

namespace Ferrite.Verify;

/// <summary>Java runtime provisioning from Mojang's own catalog.</summary>
internal static partial class Scenarios
{
    /// <summary>
    /// Lists what Mojang publishes for this platform, then provisions one component and shows the
    /// runtime that came out of it.
    /// </summary>
    public static async Task<int> ProvisionJavaAsync(
        VerifyServices services,
        string? minecraftVersion,
        string? component,
        CancellationToken cancellationToken)
    {
        var catalog = await services.JavaProvisioner
            .GetCatalogAsync(forceRefresh: false, cancellationToken)
            .ConfigureAwait(false);

        var osKey = JavaProvisioner.OsKey();
        if (osKey is null || !catalog.TryGetValue(osKey, out var components) || components.Count == 0)
        {
            Console.WriteLine($"Mojang publishes no runtime for {osKey ?? "this architecture"}.");
            return 3;
        }

        Console.WriteLine($"Mojang publishes {components.Count} runtime component(s) for {osKey}:");
        foreach (var (name, releases) in components)
        {
            Console.WriteLine($"  {name,-24} {releases.Count} build(s), newest "
                + $"{(releases.Select(release => release.Version?.Released).Where(value => value is not null)
                        .OrderByDescending(value => value)
                        .FirstOrDefault() ?? "-")}");
        }

        if (minecraftVersion is { Length: > 0 })
        {
            // The component a Minecraft version needs is what the instance would select.
            var required = Ferrite.Core.Java.JavaCompatibility.RequiredMajorFor(minecraftVersion);
            Console.WriteLine();
            Console.WriteLine($"Minecraft {minecraftVersion} needs Java {required ?? 8}.");
        }

        var target = component is { Length: > 0 }
            ? component
            : components.Keys.OrderBy(name => name, StringComparer.Ordinal).First();

        var options = await services.JavaProvisioner
            .ListAvailableAsync(target, cancellationToken)
            .ConfigureAwait(false);
        Console.WriteLine();
        Console.WriteLine($"Component {target}: {options.Count} published build(s)");
        foreach (var option in options.Take(5))
        {
            Console.WriteLine(
                $"  {option.VersionName,-28} "
                + $"{(option.Size is { } size ? ByteSize.Format(size) : "-"),-12} "
                + $"{(option.Released is { } released ? released.ToString("yyyy-MM-dd") : "-")}");
        }

        if (options.Count == 0)
        {
            Console.WriteLine("That component has no downloadable build.");
            return 3;
        }

        Console.WriteLine();
        Console.WriteLine($"Provisioning {target} ({options[0].VersionName})...");
        var runtime = await services.JavaProvisioner
            .ProvisionAsync(
                target,
                new Progress<Ferrite.Core.Minecraft.InstallProgress>(ReportProgress),
                cancellationToken)
            .ConfigureAwait(false);

        Console.WriteLine();
        Console.WriteLine($"Provisioned: {runtime.DisplayName}");
        Console.WriteLine($"  executable: {runtime.ExecutablePath}");
        Console.WriteLine($"  version:    {runtime.Version}");
        Console.WriteLine($"  vendor:     {runtime.Vendor}");
        Console.WriteLine($"  source:     {runtime.Source}");
        Console.WriteLine($"  exists:     {File.Exists(runtime.ExecutablePath)}");

        // Provisioning twice must reuse what is already there rather than downloading again.
        var again = await services.JavaProvisioner
            .TryFindProvisionedAsync(target, cancellationToken)
            .ConfigureAwait(false);
        Console.WriteLine($"  reused on a second call: {again is not null}");

        return File.Exists(runtime.ExecutablePath) ? 0 : 3;
    }
}
