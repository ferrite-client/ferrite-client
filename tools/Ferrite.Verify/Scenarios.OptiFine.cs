using Ferrite.Core.Loaders;
using Microsoft.Extensions.Logging;

namespace Ferrite.Verify;

/// <summary>OptiFine: recognising the installer and adopting an installed version.</summary>
internal static partial class Scenarios
{
    public static async Task<int> OptiFineAsync(
        VerifyServices services,
        string? jarPath,
        string? gameDirectory,
        string? versionId,
        CancellationToken cancellationToken)
    {
        var candidates = new List<string>();
        if (jarPath is { Length: > 0 })
        {
            candidates.Add(jarPath);
        }
        else
        {
            var minecraft = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                ".minecraft");
            if (Directory.Exists(minecraft))
            {
                // OptiFine's file name may carry a preview_ prefix or a .temp suffix.
                candidates.AddRange(Directory
                    .EnumerateFiles(minecraft, "*OptiFine_*.jar*", SearchOption.AllDirectories)
                    .Take(10));
            }
        }

        if (candidates.Count == 0)
        {
            Console.WriteLine("No OptiFine installer was found. Pass --jar <path> to point at one.");
            return 2;
        }

        var recognised = 0;
        foreach (var candidate in candidates)
        {
            var installer = OptiFineInstaller.Inspect(candidate);
            if (installer is null)
            {
                var withoutManifest = OptiFineInstaller.Inspect(candidate, requireInstallerMainClass: false);
                Console.WriteLine(withoutManifest is null
                    ? $"  not OptiFine: {candidate}"
                    : $"  OptiFine {withoutManifest.OptiFineVersion}, but not the installer: {candidate}");
                continue;
            }

            recognised++;
            Console.WriteLine($"  {installer.DisplayName}");
            Console.WriteLine($"      version id: {installer.VersionId}");
            Console.WriteLine($"      file:       {candidate}");
        }

        Console.WriteLine();
        Console.WriteLine($"{recognised} OptiFine installer(s) recognised.");

        // Adoption is exercised when a game directory holding an installed version is supplied.
        var target = gameDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ".minecraft");
        var installed = Directory.Exists(target) ? OptiFineInstaller.FindInstalledVersions(target) : [];
        Console.WriteLine($"{installed.Count} OptiFine version(s) installed in {target}");
        foreach (var version in installed)
        {
            Console.WriteLine($"  {version}");
        }

        if (versionId is { Length: > 0 })
        {
            var adapter = new OptiFineInstaller(
                services.Paths,
                services.LoggerFactory.CreateLogger<OptiFineInstaller>());
            var result = adapter.Adopt(target, versionId, cancellationToken);
            Console.WriteLine();
            Console.WriteLine($"Adopted {result.VersionId} into {result.VersionDirectory}");
            Console.WriteLine($"  library files: {result.LibraryFiles}");
            foreach (var warning in result.Warnings)
            {
                Console.WriteLine($"  warn: {warning}");
            }
        }
        else if (installed.Count > 0)
        {
            Console.WriteLine("Pass --version <id> to adopt one of these into the launcher's store.");
        }

        return recognised > 0 ? 0 : 3;
    }
}
