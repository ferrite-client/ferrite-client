using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using Ferrite.Core.Json;
using Ferrite.Core.Minecraft;
using Ferrite.Core.Platform;
using Ferrite.Core.Util;
using Microsoft.Extensions.Logging;

namespace Ferrite.Core.Loaders;

/// <summary>What an OptiFine file turned out to be.</summary>
public sealed record OptiFineInstallerInfo(
    string JarPath,
    string OptiFineVersion,
    string MinecraftVersion,
    string VersionId)
{
    public string DisplayName => $"OptiFine {OptiFineVersion} for Minecraft {MinecraftVersion}";
}

/// <summary>Where an adopted OptiFine version ended up in the launcher's own store.</summary>
public sealed record OptiFineInstallResult(
    string VersionId,
    string VersionDirectory,
    string JarPath,
    int LibraryFiles,
    IReadOnlyList<string> Warnings);

/// <summary>
/// OptiFine support. OptiFine publishes no API: its installer is a downloadable JAR with a user
/// interface, and running it is the user's action. Ferrite invokes that installer with the right Java
/// and a working directory it controls, then adopts whatever it produced into the launcher's own
/// version store, so an instance never depends on OptiFine having written into the game directory.
/// </summary>
public sealed class OptiFineInstaller
{
    /// <summary>The main class OptiFine's installer declares. This is what identifies the JAR.</summary>
    public const string InstallerMainClass = "optifine.InstallerFrame";

    private const int MaxManifestBytes = 64 * 1024;

    private readonly AppPaths _paths;
    private readonly ILogger<OptiFineInstaller> _logger;

    public OptiFineInstaller(AppPaths paths, ILogger<OptiFineInstaller> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    /// <summary>
    /// Reads an OptiFine artifact. The version comes from OptiFine's own artifact name, which is the
    /// naming it publishes; the manifest decides whether the file is the installer rather than a mod
    /// JAR of the same vintage.
    /// </summary>
    public static OptiFineInstallerInfo? Inspect(string jarPath, bool requireInstallerMainClass = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jarPath);
        if (!File.Exists(jarPath))
        {
            return null;
        }

        // Explorer appends .temp to a partial download. It has to be removed from the full name
        // before the extension, or the extension being stripped is ".temp" rather than ".jar".
        var fileName = Path.GetFileName(jarPath);
        if (fileName.EndsWith(".temp", StringComparison.OrdinalIgnoreCase))
        {
            fileName = fileName[..^".temp".Length];
        }

        var name = Path.GetFileNameWithoutExtension(fileName);

        // The name can carry a prefix ("preview_"), so the OptiFine token is located rather than
        // assumed. Everything after it is the minecraft version followed by the OptiFine version.
        var parts = name.Split('_', StringSplitOptions.RemoveEmptyEntries);
        var marker = Array.FindIndex(parts, part =>
            part.Equals("OptiFine", StringComparison.OrdinalIgnoreCase));
        if (marker < 0 || parts.Length - marker < 3)
        {
            return null;
        }

        var minecraft = parts[marker + 1];
        var version = string.Join('_', parts[(marker + 2)..]);
        // Some download paths leave the marker in the middle of the name ("..._pre2.temp.jar").
        if (version.EndsWith(".temp", StringComparison.OrdinalIgnoreCase))
        {
            version = version[..^".temp".Length];
        }

        if (minecraft.Length == 0 || version.Length == 0)
        {
            return null;
        }

        if (requireInstallerMainClass)
        {
            string? mainClass;
            try
            {
                mainClass = ReadMainClass(jarPath);
            }
            catch (Exception exception) when (
                exception is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                return null;
            }

            if (!string.Equals(mainClass, InstallerMainClass, StringComparison.Ordinal))
            {
                return null;
            }
        }

        return new OptiFineInstallerInfo(jarPath, version, minecraft, $"{minecraft}-OptiFine_{version}");
    }

    /// <summary>
    /// Runs OptiFine's installer with the given Java, in a staging directory the launcher owns. The
    /// installer shows its own window: it is the user's click, not the launcher's, that installs.
    /// </summary>
    public async Task<int> RunInstallerAsync(
        string installerPath,
        string stagingDirectory,
        Java.JavaRuntime java,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installerPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingDirectory);
        ArgumentNullException.ThrowIfNull(java);

        Directory.CreateDirectory(stagingDirectory);
        _logger.LogInformation(
            "Starting the OptiFine installer {Installer} with {Java} in {Directory}",
            installerPath,
            java.ExecutablePath,
            stagingDirectory);

        var startInfo = new ProcessStartInfo
        {
            FileName = java.ExecutablePath,
            // OptiFine's installer writes into its working directory, which is why it is staged.
            WorkingDirectory = stagingDirectory,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("-jar");
        startInfo.ArgumentList.Add(installerPath);

        using var process = Process.Start(startInfo)
            ?? throw new LoaderException("The OptiFine installer could not be started.");

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("The OptiFine installer exited with code {Code}", process.ExitCode);
        return process.ExitCode;
    }

    /// <summary>
    /// Adopts an OptiFine version that exists in some game directory: the version document is copied
    /// into the launcher's store and its OptiFine libraries are copied with it, so the instance
    /// launches from the store rather than from the directory the installer wrote to.
    /// </summary>
    public OptiFineInstallResult Adopt(
        string sourceGameDirectory,
        string versionId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceGameDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(versionId);

        var sourceVersions = Path.Combine(sourceGameDirectory, "versions", versionId);
        var sourceJson = Path.Combine(sourceVersions, versionId + ".json");
        if (!File.Exists(sourceJson))
        {
            throw new LoaderException(
                $"OptiFine version '{versionId}' was not found in "
                + $"{Path.Combine(sourceGameDirectory, "versions")}.");
        }

        VersionDocument document;
        try
        {
            document = JsonSerializer.Deserialize<VersionDocument>(
                    File.ReadAllText(sourceJson),
                    JsonDefaults.Remote)
                ?? throw new LoaderException($"OptiFine version '{versionId}' has an empty version document.");
        }
        catch (JsonException exception)
        {
            throw new LoaderException($"OptiFine version '{versionId}' is not valid JSON.", exception);
        }

        var warnings = new List<string>();
        var libraryFiles = 0;

        foreach (var library in document.Libraries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!MavenCoordinates.TryParse(library.Name, out var coordinates)
                || !IsOptiFineLibrary(coordinates))
            {
                continue;
            }

            var source = Path.Combine(
                Path.Combine(sourceGameDirectory, "libraries"),
                coordinates.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(source))
            {
                warnings.Add($"The OptiFine library {coordinates.FileName} was not found to copy.");
                continue;
            }

            var destination = Path.Combine(
                _paths.LibrariesDirectory,
                coordinates.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: true);
            libraryFiles++;
        }

        if (libraryFiles == 0)
        {
            warnings.Add(
                "No OptiFine library was found beside the version document, so the version may not launch.");
        }

        var targetDirectory = _paths.VersionDirectory(versionId);
        Directory.CreateDirectory(targetDirectory);
        AtomicFile.WriteAllText(
            _paths.VersionJsonFile(versionId),
            JsonSerializer.Serialize(document, JsonDefaults.Document));

        _logger.LogInformation(
            "Adopted OptiFine {VersionId}: {Libraries} library file(s) copied into the store",
            versionId,
            libraryFiles);

        return new OptiFineInstallResult(
            versionId,
            targetDirectory,
            _paths.VersionClientJarFile(versionId),
            libraryFiles,
            warnings);
    }

    /// <summary>The version ids OptiFine has installed into a game directory.</summary>
    public static IReadOnlyList<string> FindInstalledVersions(string gameDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDirectory);
        var versionsDirectory = Path.Combine(gameDirectory, "versions");
        if (!Directory.Exists(versionsDirectory))
        {
            return [];
        }

        return Directory
            .EnumerateDirectories(versionsDirectory)
            .Select(Path.GetFileName)
            .Where(name => name is { Length: > 0 }
                && name.Contains("OptiFine", StringComparison.OrdinalIgnoreCase))
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsOptiFineLibrary(MavenCoordinates coordinates) =>
        coordinates.Group.Contains("optifine", StringComparison.OrdinalIgnoreCase)
        || coordinates.Artifact.Contains("optifine", StringComparison.OrdinalIgnoreCase);

    private static string? ReadMainClass(string jarPath)
    {
        using var archive = ZipFile.OpenRead(jarPath);
        var entry = archive.Entries.FirstOrDefault(candidate =>
            string.Equals(candidate.FullName, "META-INF/MANIFEST.MF", StringComparison.OrdinalIgnoreCase));
        if (entry is null || entry.Length > MaxManifestBytes)
        {
            return null;
        }

        using var reader = new StreamReader(entry.Open());
        while (reader.ReadLine() is { } line)
        {
            if (line.StartsWith("Main-Class:", StringComparison.OrdinalIgnoreCase))
            {
                return line["Main-Class:".Length..].Trim();
            }
        }

        return null;
    }
}
