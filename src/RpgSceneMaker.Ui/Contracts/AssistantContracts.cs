namespace RpgSceneMaker.Ui.Contracts;

// UI copies of the /assistant + /setup/assistant wire shapes (duplicated by hand per the project's
// duplicated-DTO convention — see the API's Contracts/AssistantContracts.cs).

// GET /assistant/state response. Entries is null when the poller's rev already matches (nothing changed);
// otherwise it is the FULL transcript snapshot to replace the local list.
public record AssistantStateDto(long Rev, bool Busy, bool Configured, List<AssistantEntryDto>? Entries);

// One transcript line. ToolName/ToolResult/ToolIsError are only populated when Kind == "tool".
public record AssistantEntryDto(int Seq, string Kind, string Text, string? ToolName, string? ToolResult, bool? ToolIsError);

// Mutable class — the Settings form binds the provider + key + model inputs straight to it (like
// SpotifyConfigDto). The API never echoes the key, so ApiKey stays "" on load; the user types it to
// (re)configure. Provider selects the active backend ("anthropic" | "openai" | "gemini").
public class AssistantConfigDto
{
    public string Provider { get; set; } = "anthropic";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "claude-opus-4-8";
    public bool Configured { get; set; }
}
