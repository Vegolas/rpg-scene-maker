namespace RpgSceneMaker.Ui.Contracts;

public record LogEntryDto(DateTimeOffset Timestamp, string Level, string Category, string Message, string? Exception);
