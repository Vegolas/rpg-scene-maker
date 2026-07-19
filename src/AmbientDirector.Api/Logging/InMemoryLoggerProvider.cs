using Microsoft.Extensions.Logging;
using AmbientDirector.Api.Contracts;

namespace AmbientDirector.Api.Logging;

/// <summary>Logger provider that mirrors every log entry into <see cref="InMemoryLogStore"/> for the Logs tab.</summary>
[ProviderAlias("InMemory")]
public sealed class InMemoryLoggerProvider(InMemoryLogStore store) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new InMemoryLogger(categoryName, store);

    public void Dispose() { }
}

internal sealed class InMemoryLogger(string category, InMemoryLogStore store) : ILogger
{
    // The logging framework applies category/level filters before calling Log, so this stays trivial.
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;
        // Trim the namespace off the category for a compact display (e.g. "SceneActivator").
        var shortCategory = category[(category.LastIndexOf('.') + 1)..];
        store.Add(new LogEntry(DateTimeOffset.Now, logLevel.ToString(), shortCategory,
            formatter(state, exception), exception?.ToString()));
    }
}
