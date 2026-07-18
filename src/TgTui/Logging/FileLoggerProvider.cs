using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace TgTui.Logging;

/// <summary>
/// Appends structured log lines to <c>logs/tg-tui.log</c> under the user data root.
/// Never log message bodies, session material, or credentials here.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _logFile;
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new(StringComparer.Ordinal);

    public FileLoggerProvider(string logsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logsDirectory);
        Directory.CreateDirectory(logsDirectory);
        _logFile = Path.Combine(logsDirectory, "tg-tui.log");
    }

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new FileLogger(name, _logFile));

    public void Dispose()
    {
        _loggers.Clear();
    }

    private sealed class FileLogger : ILogger
    {
        private readonly string _category;
        private readonly string _logFile;

        public FileLogger(string category, string logFile)
        {
            _category = category;
            _logFile = logFile;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            var message = formatter(state, exception);
            if (string.IsNullOrWhiteSpace(message) && exception is null)
                return;

            var line = $"{DateTimeOffset.Now:O} [{logLevel}] {_category}: {message}";
            if (exception is not null)
                line += Environment.NewLine + exception;

            try
            {
                File.AppendAllText(_logFile, line + Environment.NewLine);
            }
            catch
            {
                // Logging must never crash the client.
            }
        }
    }
}
