using AmbientDirector.Api.Services.Ai;

namespace AmbientDirector.Api.Contracts;

// Wire DTOs for the /assistant endpoints. The internal AssistantEntry/AssistantState carry a couple of
// fields the panel doesn't need; these are the trimmed shapes the panel polls (mirror them by hand in the
// UI's Contracts/ per the project's duplicated-DTO convention).

// Body of POST /assistant/send.
public record AssistantSendInput(string Text);

// One transcript line as the panel sees it.
public record AssistantEntryDto(
    int Seq,
    string Kind,
    string Text,
    string? ToolName,
    string? ToolResult,
    bool? ToolIsError);

// GET /assistant/state response. Entries is null when the poller's rev already matches (nothing changed).
public record AssistantStateDto(
    long Rev,
    bool Busy,
    bool Configured,
    List<AssistantEntryDto>? Entries)
{
    public static AssistantStateDto From(AssistantState state) => new(
        state.Rev,
        state.Busy,
        state.Configured,
        state.Entries?.Select(e => new AssistantEntryDto(
            e.Seq, e.Kind, e.Text, e.ToolName, e.ToolResult, e.ToolIsError)).ToList());
}
