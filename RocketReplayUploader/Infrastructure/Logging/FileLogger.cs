using System.Globalization;
using Microsoft.Extensions.DependencyInjection;

namespace RocketReplayUploader.Infrastructure.Logging;

// Logger a archivo con rotación diaria: un app-YYYYMMDD.log por día en
// %AppData%\RocketReplayUploader\logs, borrando los de más de N días.
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly object _gate = new();
    private readonly string _dir;
    private readonly int _keepDays;
    private string _currentDate = "";
    private string? _currentPath;
    private StreamWriter? _writer;

    public static string LogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RocketReplayUploader",
        "logs");

    public FileLoggerProvider(string directory, int keepDays = 7)
    {
        _dir = directory;
        _keepDays = keepDays;
        Directory.CreateDirectory(_dir);
        PurgeOldLogs();
    }

    private void PurgeOldLogs()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(_dir, "app-*.log"))
            {
                if (File.GetLastWriteTime(file) < DateTime.Now.AddDays(-_keepDays))
                {
                    File.Delete(file);
                }
            }
        }
        catch
        {
            // nunca debe impedir el arranque
        }
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Write(string category, LogLevel level, string message)
    {
        var now = DateTime.Now;
        lock (_gate)
        {
            var date = now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            if (_writer == null || date != _currentDate)
            {
                _writer?.Dispose();
                _currentDate = date;
                _currentPath = Path.Combine(_dir, $"app-{date}.log");
                _writer = new StreamWriter(_currentPath, append: true) { AutoFlush = true };
            }

            _writer.WriteLine($"{now:yyyy-MM-dd HH:mm:ss.fff} [{level,-11}] {category}: {message}");
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }

    private sealed class FileLogger : ILogger
    {
        private readonly FileLoggerProvider _provider;
        private readonly string _category;

        public FileLogger(FileLoggerProvider provider, string category)
        {
            _provider = provider;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Debug;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var text = formatter(state, exception);
            if (exception != null)
            {
                text += Environment.NewLine + exception;
            }

            _provider.Write(_category, logLevel, text);
        }
    }
}

public static class FileLoggingExtensions
{
    public static ILoggingBuilder AddFile(this ILoggingBuilder builder)
    {
        builder.Services.AddSingleton<ILoggerProvider>(new FileLoggerProvider(FileLoggerProvider.LogDirectory));
        return builder;
    }
}
