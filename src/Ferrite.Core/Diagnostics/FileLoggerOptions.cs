using Microsoft.Extensions.Logging;

namespace Ferrite.Core.Diagnostics;

public sealed class FileLoggerOptions
{
    public required string Directory { get; init; }

    public string FileName { get; init; } = "ferrite.log";

    public LogLevel MinimumLevel { get; init; } = LogLevel.Information;

    public long MaxFileBytes { get; init; } = 4L * 1024 * 1024;

    public int RetainedFiles { get; init; } = 5;

    public SecretRedactor Redactor { get; init; } = SecretRedactor.Shared;
}
