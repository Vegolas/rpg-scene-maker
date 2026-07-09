namespace RpgSceneMaker.Api.Contracts;

// One captured log line, surfaced by GET /logs to the panel's Logs tab.
public record LogEntry(DateTimeOffset Timestamp, string Level, string Category, string Message, string? Exception);
