using System.Diagnostics;

namespace Ferrite.Core.Util;

/// <summary>
/// Opens files, folders, and URLs with the operating system's default handler. Arguments go through
/// an argument list, never a shell command string.
/// </summary>
public static class ShellOpen
{
    public static bool Directory(string path)
    {
        if (!System.IO.Directory.Exists(path))
        {
            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            var startInfo = new ProcessStartInfo { FileName = "explorer.exe", UseShellExecute = true };
            startInfo.ArgumentList.Add(path);
            return Start(startInfo);
        }

        return Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
    }

    public static bool File(string path) =>
        System.IO.File.Exists(path)
        && Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });

    public static bool Url(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return false;
        }

        return Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
    }

    private static bool Start(ProcessStartInfo startInfo)
    {
        try
        {
            using var process = Process.Start(startInfo);
            return process is not null;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
