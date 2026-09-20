using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Ferrite.Core.Java;

/// <summary>
/// Reads a Java installation's properties by running it. Uses an argument list, a hard timeout,
/// and never interprets the executable path through a shell.
/// </summary>
public static partial class JavaProcessProbe
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(20);

    public static async Task<JavaProbeResult?> ProbeAsync(
        string executablePath,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            return null;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-XshowSettings:properties");
        startInfo.ArgumentList.Add("-version");

        using var process = new Process { StartInfo = startInfo };
        var output = new StringBuilder();
        try
        {
            if (!process.Start())
            {
                return null;
            }
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }

        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout ?? DefaultTimeout);

        try
        {
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return null;
        }

        output.Append(await stdout.ConfigureAwait(false));
        output.Append(await stderr.ConfigureAwait(false));
        return Parse(output.ToString());
    }

    public static JavaProbeResult Parse(string output)
    {
        string? version = null;
        string? vendor = null;
        string? home = null;
        string? architecture = null;

        foreach (var line in output.Split('\n'))
        {
            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            switch (key)
            {
                case "java.version":
                    version = value;
                    break;
                case "java.vendor":
                    vendor = value;
                    break;
                case "java.home":
                    home = value;
                    break;
                case "os.arch":
                    architecture = value;
                    break;
            }
        }

        if (version is null)
        {
            var match = VersionPattern().Match(output);
            if (match.Success)
            {
                version = match.Groups[1].Value;
            }
        }

        var major = ParseMajorVersion(version);
        var is64Bit = architecture is not null
            && (architecture.Contains("64", StringComparison.OrdinalIgnoreCase)
                || architecture.Contains("aarch64", StringComparison.OrdinalIgnoreCase));

        if (version is null && vendor is null && home is null)
        {
            return new JavaProbeResult(null, null, null, null, architecture, is64Bit);
        }

        return new JavaProbeResult(version, major, vendor, home, architecture, is64Bit);
    }

    /// <summary>Parses "25.0.3", "1.8.0_402" and "21" into a major version.</summary>
    public static int? ParseMajorVersion(string? versionText)
    {
        if (string.IsNullOrWhiteSpace(versionText))
        {
            return null;
        }

        var match = MajorPattern().Match(versionText);
        if (!match.Success)
        {
            return null;
        }

        if (!int.TryParse(match.Groups[1].Value, out var first))
        {
            return null;
        }

        if (first != 1)
        {
            return first;
        }

        return int.TryParse(match.Groups[2].Value, out var second) ? second : null;
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

    [GeneratedRegex("\"([^\"]+)\"")]
    private static partial Regex VersionPattern();

    [GeneratedRegex(@"^(\d+)(?:\.(\d+))?")]
    private static partial Regex MajorPattern();
}
