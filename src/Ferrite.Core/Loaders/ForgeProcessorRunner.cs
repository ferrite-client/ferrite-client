using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using Ferrite.Core.Download;
using Ferrite.Core.Java;
using Ferrite.Core.Minecraft;
using Ferrite.Core.Platform;
using Ferrite.Core.Util;
using Microsoft.Extensions.Logging;

namespace Ferrite.Core.Loaders;

/// <summary>
/// Runs a Forge/NeoForge installer's processor chain. Processors are the official mechanism the
/// loader authors publish for producing a launchable client, so they are executed rather than
/// imitated. Every process is started with an argument list and a working directory inside the
/// managed store.
/// </summary>
internal sealed class ForgeProcessorRunner
{
    private const string ClientSide = "client";
    private static readonly TimeSpan ProcessorTimeout = TimeSpan.FromMinutes(10);

    private readonly DownloadEngine _downloads;
    private readonly AppPaths _paths;
    private readonly ILogger _logger;

    public ForgeProcessorRunner(DownloadEngine downloads, AppPaths paths, ILogger logger)
    {
        _downloads = downloads;
        _paths = paths;
        _logger = logger;
    }

    public async Task RunAsync(
        ForgeInstallProfile profile,
        string installerPath,
        string minecraftClientJarPath,
        JavaRuntime java,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        var workDirectory = Path.Combine(_paths.TemporaryDirectory, "forge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDirectory);

        try
        {
            var data = await ResolveDataAsync(profile, installerPath, workDirectory, progress, cancellationToken)
                .ConfigureAwait(false);

            var placeholders = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["SIDE"] = ClientSide,
                ["MINECRAFT_JAR"] = minecraftClientJarPath,
                ["ROOT"] = _paths.StoreDirectory,
                ["INSTALLER"] = installerPath,
                ["LIBRARY_DIR"] = _paths.LibrariesDirectory,
            };

            foreach (var (key, value) in data)
            {
                placeholders[key] = value;
            }

            var processors = profile.Processors ?? [];
            var index = 0;
            foreach (var processor in processors)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!processor.AppliesTo(ClientSide))
                {
                    _logger.LogDebug("Skipping processor {Jar} (not for {Side})", processor.Jar, ClientSide);
                    continue;
                }

                index++;
                if (OutputsExist(processor, placeholders))
                {
                    _logger.LogDebug("Skipping processor {Index} ({Jar}): outputs already present", index, processor.Jar);
                    continue;
                }

                progress?.Report(new InstallProgress
                {
                    Stage = InstallStage.ExtractingNatives,
                    Message = $"Processor {index}/{processors.Count}",
                });

                await RunProcessorAsync(processor, placeholders, java, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            TryDeleteDirectory(workDirectory);
        }
    }

    private async Task<Dictionary<string, string>> ResolveDataAsync(
        ForgeInstallProfile profile,
        string installerPath,
        string workDirectory,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        var resolved = new Dictionary<string, string>(StringComparer.Ordinal);
        if (profile.Data is not { Count: > 0 } data)
        {
            return resolved;
        }

        var dataDirectory = Path.Combine(workDirectory, "data");
        Directory.CreateDirectory(dataDirectory);
        var downloads = new List<DownloadRequest>();
        var pending = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (key, element) in data)
        {
            var value = SelectSide(element);
            switch (value.ValueKind)
            {
                case JsonValueKind.String:
                {
                    var text = value.GetString() ?? string.Empty;
                    if (text.StartsWith('/'))
                    {
                        var entryName = text.TrimStart('/');
                        var target = Path.Combine(
                            dataDirectory,
                            PathSafety.SanitizeFileName(Path.GetFileName(entryName)));
                        ExtractEntry(installerPath, entryName, target);
                        resolved[key] = target;
                    }
                    else
                    {
                        resolved[key] = text;
                    }

                    break;
                }

                // A single-element array is a maven coordinate. Those artefacts are produced by the
                // processor chain inside the library store, so the value is a path, not a download.
                case JsonValueKind.Array when value.GetArrayLength() == 1:
                {
                    var coordinates = value[0].GetString();
                    if (string.IsNullOrEmpty(coordinates))
                    {
                        break;
                    }

                    resolved[key] = LibraryPathFor(coordinates);
                    break;
                }

                case JsonValueKind.Array when value.GetArrayLength() == 2:
                {
                    var url = value[0].GetString();
                    var sha1 = value[1].GetString();
                    if (string.IsNullOrEmpty(url))
                    {
                        break;
                    }

                    var fileName = Uri.TryCreate(url, UriKind.Absolute, out var uri)
                        ? PathSafety.SanitizeFileName(Path.GetFileName(uri.LocalPath))
                        : key.ToLowerInvariant();
                    var target = Path.Combine(dataDirectory, fileName);
                    downloads.Add(new DownloadRequest
                    {
                        Url = url,
                        TargetPath = target,
                        ExpectedSha1 = string.IsNullOrEmpty(sha1) ? null : sha1,
                        Label = fileName,
                    });
                    pending[key] = target;
                    break;
                }
            }
        }

        if (downloads.Count > 0)
        {
            progress?.Report(new InstallProgress
            {
                Stage = InstallStage.Downloading,
                Message = "Installer data files",
            });
            var summary = await _downloads
                .DownloadAsync(downloads, DownloadProgressAdapter.Create(progress), cancellationToken)
                .ConfigureAwait(false);
            if (!summary.Success)
            {
                throw new LoaderException(
                    $"Forge installer data files could not be downloaded ({summary.Failures.Count} failures).");
            }
        }

        foreach (var (key, value) in pending)
        {
            resolved[key] = value;
        }

        return resolved;
    }

    private async Task RunProcessorAsync(
        ForgeProcessor processor,
        IReadOnlyDictionary<string, string> placeholders,
        JavaRuntime java,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(processor.Jar))
        {
            throw new LoaderException("A Forge processor did not declare its jar.");
        }

        var processorJar = LibraryPathFor(processor.Jar);
        if (!File.Exists(processorJar))
        {
            throw new LoaderException($"Processor jar is missing: {processorJar}");
        }

        var classpathEntries = new List<string>();
        foreach (var entry in processor.Classpath)
        {
            var path = LibraryPathFor(entry);
            if (File.Exists(path))
            {
                classpathEntries.Add(path);
            }
            else
            {
                _logger.LogWarning("Processor classpath entry missing: {Path}", path);
            }
        }

        if (classpathEntries.Count == 0)
        {
            classpathEntries.Add(processorJar);
        }

        var mainClass = ReadMainClass(processorJar);
        var arguments = new List<string>
        {
            "-cp",
            string.Join(Path.PathSeparator, classpathEntries),
            mainClass,
        };
        foreach (var argument in processor.Args)
        {
            arguments.Add(Substitute(argument, placeholders));
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = java.ExecutablePath,
            WorkingDirectory = _paths.StoreDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        _logger.LogInformation("Running Forge processor {MainClass}", mainClass);
        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new LoaderException($"Processor {mainClass} could not be started.");
        }

        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProcessorTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw new LoaderException($"Processor {mainClass} timed out.");
        }

        var output = await stdout.ConfigureAwait(false);
        var error = await stderr.ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            _logger.LogError("Processor {MainClass} failed with {Code}: {Error}", mainClass, process.ExitCode, error);
            throw new LoaderException(
                $"Processor {mainClass} exited with code {process.ExitCode}. {Truncate(error)}");
        }

        if (!string.IsNullOrWhiteSpace(output))
        {
            _logger.LogDebug("Processor {MainClass} output: {Output}", mainClass, Truncate(output));
        }
    }

    /// <summary>
    /// Data entries may be per-side objects; the client value wins, falling back to the only
    /// available side.
    /// </summary>
    private static JsonElement SelectSide(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return element;
        }

        if (element.TryGetProperty(ClientSide, out var client))
        {
            return client;
        }

        foreach (var property in element.EnumerateObject())
        {
            return property.Value;
        }

        return default;
    }

    private bool OutputsExist(ForgeProcessor processor, IReadOnlyDictionary<string, string> placeholders)
    {
        if (processor.Outputs is not { Count: > 0 } outputs)
        {
            return false;
        }

        foreach (var value in outputs.Values)
        {
            if (!File.Exists(Substitute(value, placeholders)))
            {
                return false;
            }
        }

        return true;
    }

    private string LibraryPathFor(string mavenCoordinates)
    {
        if (!MavenCoordinates.TryParse(mavenCoordinates, out var coordinates))
        {
            throw new LoaderException($"Unparsable processor coordinate: {mavenCoordinates}");
        }

        return Path.Combine(
            _paths.LibrariesDirectory,
            coordinates.RelativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    /// <summary>
    /// Replaces <c>{KEY}</c> data placeholders and resolves bracketed maven coordinates such as
    /// <c>[net.neoforged:neoform:1.21.1-20240808.144430@zip]</c> to their path in the library store.
    /// </summary>
    internal string Substitute(string value, IReadOnlyDictionary<string, string> placeholders)
    {
        var result = value;
        if (result.Contains('{', StringComparison.Ordinal))
        {
            foreach (var (key, replacement) in placeholders)
            {
                result = result.Replace("{" + key + "}", replacement, StringComparison.Ordinal);
            }
        }

        if (result.Length > 2
            && result[0] == '['
            && result[^1] == ']'
            && !result.Contains(' ', StringComparison.Ordinal))
        {
            var coordinates = result[1..^1];
            var path = LibraryPathFor(coordinates);
            if (!File.Exists(path))
            {
                _logger.LogWarning("Processor input {Coordinates} is not present at {Path}", coordinates, path);
            }

            result = path;
        }

        return result;
    }

    private static string ReadMainClass(string jarPath)
    {
        using var archive = ZipFile.OpenRead(jarPath);
        var manifest = archive.GetEntry("META-INF/MANIFEST.MF")
            ?? throw new LoaderException($"Processor jar has no manifest: {jarPath}");
        using var reader = new StreamReader(manifest.Open());
        while (reader.ReadLine() is { } line)
        {
            if (line.StartsWith("Main-Class:", StringComparison.OrdinalIgnoreCase))
            {
                var mainClass = line["Main-Class:".Length..].Trim();
                if (mainClass.Length > 0)
                {
                    return mainClass;
                }
            }
        }

        throw new LoaderException($"Processor jar declares no main class: {jarPath}");
    }

    private static void ExtractEntry(string archivePath, string entryName, string destinationPath)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var entry = archive.GetEntry(entryName)
            ?? throw new LoaderException($"Installer is missing the data entry '{entryName}'.");
        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        entry.ExtractToFile(destinationPath, overwrite: true);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    private static string Truncate(string value) => value.Length > 600 ? value[..600] + "..." : value;
}
