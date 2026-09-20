using Microsoft.Extensions.Logging;

namespace Ferrite.Verify;

/// <summary>
/// Minimal console logger. The verification harness deliberately avoids extra logging packages so
/// it exercises the same Core code the launcher ships.
/// </summary>
internal sealed class ConsoleLoggerFactory : ILoggerFactory
{
    private readonly LogLevel _minimum;

    public ConsoleLoggerFactory(LogLevel minimum)
    {
        _minimum = minimum;
    }

    public ILogger CreateLogger(string categoryName) => new ConsoleLogger(categoryName, _minimum);

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public void Dispose()
    {
    }

    private sealed class ConsoleLogger : ILogger
    {
        private readonly string _category;
        private readonly LogLevel _minimum;

        public ConsoleLogger(string category, LogLevel minimum)
        {
            _category = category;
            _minimum = minimum;
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= _minimum;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var shortCategory = _category.Split('.')[^1];
            Console.WriteLine($"[{logLevel.ToString()[..4].ToLowerInvariant()}] {shortCategory}: {formatter(state, exception)}");
            if (exception is not null)
            {
                Console.WriteLine("        " + exception.Message);
            }
        }
    }
}
