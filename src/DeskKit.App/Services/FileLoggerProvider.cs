using Microsoft.Extensions.Logging;

namespace DeskKit.App.Services;

/// <summary>
/// Appends log lines to a per-day file under the application data folder.
/// <para>
/// The file is opened and closed per write. That is slower than holding a
/// handle, but a desktop widget runs for days, and leaving a handle open would
/// make the log impossible to read while the application is running — which is
/// exactly when it is needed.
/// </para>
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _directory;
    private readonly LogLevel _minimumLevel;
    private readonly Lock _gate = new();

    public FileLoggerProvider(string directory, LogLevel minimumLevel = LogLevel.Information)
    {
        _directory = directory;
        _minimumLevel = minimumLevel;
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose()
    {
    }

    private void Write(string categoryName, LogLevel level, string message, Exception? exception)
    {
        if (level < _minimumLevel)
            return;

        var line =
            $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{Shorten(level)}] {categoryName}: {message}";

        if (exception is not null)
            line += Environment.NewLine + exception;

        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(_directory);
                var path = Path.Combine(_directory, $"deskkit-{DateTime.Now:yyyyMMdd}.log");
                File.AppendAllText(path, line + Environment.NewLine);
            }
            catch (IOException)
            {
                // Logging must never take the application down.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static string Shorten(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "???",
    };

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= provider._minimumLevel;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            provider.Write(category, logLevel, formatter(state, exception), exception);
        }
    }
}
