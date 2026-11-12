using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace HanyingAgent.Tests.Support;

internal sealed class RecordingLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

    public ConcurrentQueue<LogEntry> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new RecordingLogger(this, categoryName);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    public void Dispose()
    {
    }

    internal sealed record LogEntry(
        string Category,
        LogLevel Level,
        EventId EventId,
        string Message,
        string? Exception,
        IReadOnlyDictionary<string, object?> Properties);

    private sealed class RecordingLogger(RecordingLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull =>
            provider._scopes.Push(state);

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = new Dictionary<string, object?>();
            provider._scopes.ForEachScope((scope, target) => AddProperties(scope, target), properties);
            AddProperties(state, properties);
            provider.Entries.Enqueue(new LogEntry(
                category, logLevel, eventId, formatter(state, exception), exception?.ToString(), properties));
        }

        private static void AddProperties(object? state, Dictionary<string, object?> target)
        {
            if (state is IEnumerable<KeyValuePair<string, object?>> values)
            {
                foreach (var pair in values)
                {
                    target[pair.Key] = pair.Value;
                }
            }
        }
    }
}
