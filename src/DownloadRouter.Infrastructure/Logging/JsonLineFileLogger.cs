using System.Text.Json;
using DownloadRouter.Core.Models;
using Microsoft.Extensions.Logging;

namespace DownloadRouter.Infrastructure.Logging;

public sealed class JsonLineFileLoggerProvider(string logsDirectory) : ILoggerProvider
{
    private const long MaximumFileBytes = 5 * 1024 * 1024;
    private readonly object sync = new();
    private readonly string path = Path.Combine(logsDirectory, "download-router.jsonl");

    public ILogger CreateLogger(string categoryName) => new JsonLineFileLogger(categoryName, Write);

    public void Dispose()
    {
    }

    private void Write(string line)
    {
        lock (sync)
        {
            Directory.CreateDirectory(logsDirectory);
            RotateIfNeeded();
            File.AppendAllText(path, line + Environment.NewLine);
        }
    }

    private void RotateIfNeeded()
    {
        if (!File.Exists(path) || new FileInfo(path).Length < MaximumFileBytes)
        {
            return;
        }

        var archived = Path.Combine(logsDirectory, "download-router.1.jsonl");
        if (File.Exists(archived))
        {
            File.Delete(archived);
        }

        File.Move(path, archived);
    }

    private sealed class JsonLineFileLogger(string category, Action<string> writer) : ILogger
    {
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
            {
                return;
            }

            var message = Redact(formatter(state, exception));
            var payload = new
            {
                timestamp = DateTimeOffset.UtcNow,
                level = logLevel.ToString(),
                category,
                eventId = eventId.Id,
                message,
                exception = exception is null ? null : Redact(exception.GetType().Name + ": " + exception.Message),
            };
            writer(JsonSerializer.Serialize(payload, ProtocolJson.Options));
        }

        private static string Redact(string value)
        {
            var result = value;
            foreach (var marker in new[] { "Authorization:", "Bearer ", "token=", "sig=", "signature=" })
            {
                var index = result.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (index >= 0)
                {
                    var end = result.IndexOfAny([' ', '&', '\r', '\n'], index + marker.Length);
                    end = end < 0 ? result.Length : end;
                    result = result[..(index + marker.Length)] + "[REDACTED]" + result[end..];
                }
            }

            return result;
        }
    }
}
