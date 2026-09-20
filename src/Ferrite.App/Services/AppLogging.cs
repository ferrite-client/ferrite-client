using Ferrite.Core.Diagnostics;
using Ferrite.Core.Platform;
using Microsoft.Extensions.Logging;

namespace Ferrite.App.Services;

/// <summary>Builds the launcher's logger factory: structured file logs plus console output.</summary>
public static class AppLogging
{
    public static ILoggerFactory Create(AppPaths paths, LogLevel minimum = LogLevel.Information)
    {
        var provider = new FileLoggerProvider(new FileLoggerOptions
        {
            Directory = paths.LauncherLogsDirectory,
            MinimumLevel = minimum,
        });

        return LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(minimum);
            builder.AddProvider(provider);
        });
    }
}
