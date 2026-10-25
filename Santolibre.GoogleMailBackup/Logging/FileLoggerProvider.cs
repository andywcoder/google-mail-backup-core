using Microsoft.Extensions.Logging;
using System;
using System.IO;

namespace Santolibre.GoogleMailBackup.Logging
{
    /// <summary>Appends log lines to a single file. The framework has no file logger of its own.</summary>
    public sealed class FileLoggerProvider : ILoggerProvider
    {
        private readonly StreamWriter _writer;
        private readonly object _lock = new object();

        public FileLoggerProvider(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            _writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
        }

        public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

        public void Dispose()
        {
            lock (_lock)
            {
                _writer.Dispose();
            }
        }

        private void Write(string category, LogLevel level, string message, Exception? exception)
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}|{level.ToString().ToUpperInvariant()}|{category}|{message}";
            if (exception != null)
            {
                line += $"|{exception}";
            }

            lock (_lock)
            {
                _writer.WriteLine(line);
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

            public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (IsEnabled(logLevel))
                {
                    _provider.Write(_category, logLevel, formatter(state, exception), exception);
                }
            }
        }
    }
}
