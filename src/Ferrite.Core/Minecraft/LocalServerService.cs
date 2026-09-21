using System.IO.Compression;
using Ferrite.Core.Download;
using Ferrite.Core.Java;
using Ferrite.Core.Platform;
using Ferrite.Core.Util;
using Microsoft.Extensions.Logging;

namespace Ferrite.Core.Minecraft;

/// <summary>The settings the launcher owns for a local server; everything else stays the user's.</summary>
public sealed record LocalServerOptions
{
    public string LevelName { get; init; } = "world";

    public int MaxPlayers { get; init; } = 20;

    public int Port { get; init; } = 25565;

    public bool OnlineMode { get; init; } = true;

    public string Motd { get; init; } = "A Ferrite server";

    public int MemoryMb { get; init; } = 2048;

    /// <summary>Whether the user accepted Minecraft's EULA, which the server refuses to start without.</summary>
    public bool AcceptEula { get; init; }
}

/// <summary>Where a prepared local server lives.</summary>
public sealed record LocalServerLayout(
    string Directory,
    string JarPath,
    string PropertiesPath,
    string EulaPath,
    string LevelName);

/// <summary>
/// A local server for an instance: the vanilla server jar for the instance's own version, a
/// <c>server.properties</c> that keeps the user's own keys, and the EULA only when the user accepted
/// it. Launching reuses the launcher's process handling, so the server gets the same log streaming
/// and exit tracking as the game.
/// </summary>
public sealed class LocalServerService
{
    public const string ServerJarName = "server.jar";
    public const string PropertiesName = "server.properties";
    public const string EulaName = "eula.txt";

    private readonly DownloadEngine _downloads;
    private readonly AppPaths _paths;
    private readonly ILogger<LocalServerService> _logger;

    public LocalServerService(
        DownloadEngine downloads,
        AppPaths paths,
        ILogger<LocalServerService> logger)
    {
        _downloads = downloads;
        _paths = paths;
        _logger = logger;
    }

    public string ServerDirectory(Guid instanceId) =>
        Path.Combine(_paths.InstanceDirectory(instanceId), "server");

    /// <summary>
    /// Downloads the server jar for the version document and writes the settings. The jar is verified
    /// against the version document's own SHA-1 before it is used.
    /// </summary>
    public async Task<LocalServerLayout> PrepareAsync(
        Guid instanceId,
        VersionDocument document,
        LocalServerOptions options,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);

        var server = document.Downloads?.Server;
        if (server?.Url is not { Length: > 0 } url)
        {
            throw new InstallFailedException(
                "This Minecraft version publishes no server download, so a local server cannot be "
                + "installed for it.");
        }

        var directory = ServerDirectory(instanceId);
        Directory.CreateDirectory(directory);
        var jarPath = Path.Combine(directory, ServerJarName);

        var summary = await _downloads
            .DownloadAsync(
                [
                    new DownloadRequest
                    {
                        Url = url,
                        TargetPath = jarPath,
                        ExpectedSha1 = server.Sha1,
                        ExpectedSize = server.Size > 0 ? server.Size : null,
                        Label = "Minecraft server",
                    },
                ],
                DownloadProgressAdapter.Create(progress),
                cancellationToken)
            .ConfigureAwait(false);

        if (!summary.Success)
        {
            throw new InstallFailedException(
                "The server jar could not be downloaded or did not match the version's own SHA-1: "
                + (summary.Failures.FirstOrDefault()?.Message ?? "unknown failure"));
        }

        var propertiesPath = Path.Combine(directory, PropertiesName);
        var properties = ServerPropertiesDocument.Parse(
            File.Exists(propertiesPath) ? await File.ReadAllTextAsync(propertiesPath, cancellationToken)
                .ConfigureAwait(false) : null);
        WriteSettings(properties, options);
        await File.WriteAllTextAsync(propertiesPath, properties.ToText(), cancellationToken)
            .ConfigureAwait(false);

        var eulaPath = Path.Combine(directory, EulaName);
        await File.WriteAllTextAsync(
                eulaPath,
                options.AcceptEula
                    ? "# Accepted through Ferrite." + Environment.NewLine + "eula=true" + Environment.NewLine
                    : "eula=false" + Environment.NewLine,
                cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Prepared a local server for instance {Instance} in {Directory}",
            instanceId,
            directory);

        return new LocalServerLayout(directory, jarPath, propertiesPath, eulaPath, options.LevelName);
    }

    /// <summary>
    /// The command that starts the server. It is an argument list, so a MOTD or a path cannot be
    /// reinterpreted by a shell.
    /// </summary>
    public static LaunchCommand BuildCommand(
        LocalServerLayout layout,
        JavaRuntime java,
        LocalServerOptions options,
        IReadOnlyList<string>? extraJvmArguments = null)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(java);
        ArgumentNullException.ThrowIfNull(options);

        var arguments = new List<string> { $"-Xmx{Math.Max(512, options.MemoryMb)}M" };
        if (extraJvmArguments is { Count: > 0 })
        {
            arguments.AddRange(extraJvmArguments);
        }

        arguments.Add("-jar");
        arguments.Add(Path.GetFileName(layout.JarPath));
        arguments.Add("nogui");

        return new LaunchCommand
        {
            ExecutablePath = java.ExecutablePath,
            Arguments = arguments,
            // The working directory is the server folder, so the world and the jar stay inside it.
            WorkingDirectory = layout.Directory,
            EnvironmentVariables = new Dictionary<string, string>(StringComparer.Ordinal),
            Classpath = layout.JarPath,
            DisplayArguments = arguments,
        };
    }

    /// <summary>
    /// Zips a prepared server, including the world, so it can be handed to someone else. The archive
    /// is written through a temporary file, so a failure cannot leave a half-written export.
    /// </summary>
    public async Task<string> ExportAsync(
        LocalServerLayout layout,
        string outputPath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        if (!Directory.Exists(layout.Directory))
        {
            throw new InstallFailedException("There is no prepared server to export yet.");
        }

        var target = Path.GetFullPath(outputPath);
        var parent = Path.GetDirectoryName(target);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        var temporary = target + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 128 * 1024,
                useAsync: true))
            {
                using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
                foreach (var file in Directory.EnumerateFiles(layout.Directory, "*", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var relative = Path.GetRelativePath(layout.Directory, file).Replace('\\', '/');
                    var entry = archive.CreateEntry(relative, CompressionLevel.Optimal);
                    await using var entryStream = entry.Open();
                    await using var source = new FileStream(
                        file,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite,
                        bufferSize: 128 * 1024,
                        useAsync: true);
                    await source.CopyToAsync(entryStream, 128 * 1024, cancellationToken).ConfigureAwait(false);
                }
            }

            File.Move(temporary, target, overwrite: true);
        }
        catch
        {
            AtomicFile.TryDelete(temporary);
            throw;
        }

        _logger.LogInformation("Exported the local server of {Directory} to {Path}", layout.Directory, target);
        return target;
    }

    /// <summary>Reads the settings the launcher owns back out of a prepared server.</summary>
    public static LocalServerOptions ReadOptions(ServerPropertiesDocument properties) => new()
    {
        LevelName = properties.Get("level-name") ?? "world",
        MaxPlayers = Parse(properties.Get("max-players"), 20),
        Port = Parse(properties.Get("server-port"), 25565),
        OnlineMode = !string.Equals(properties.Get("online-mode"), "false", StringComparison.OrdinalIgnoreCase),
        Motd = properties.Get("motd") ?? "A Ferrite server",
        AcceptEula = false,
    };

    private static void WriteSettings(ServerPropertiesDocument properties, LocalServerOptions options)
    {
        properties.Set("level-name", options.LevelName);
        properties.Set("max-players", options.MaxPlayers.ToString(System.Globalization.CultureInfo.InvariantCulture));
        properties.Set("server-port", options.Port.ToString(System.Globalization.CultureInfo.InvariantCulture));
        properties.Set("online-mode", options.OnlineMode ? "true" : "false");
        properties.Set("motd", options.Motd);
        properties.Set("enable-command-block", "true");
    }

    private static int Parse(string? value, int fallback) =>
        int.TryParse(value, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
}
