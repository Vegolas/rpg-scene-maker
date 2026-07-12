namespace RpgSceneMaker.Api.Data;

/// <summary>
/// The in-panel assistant's single persisted conversation, stored as a single row (Id = 1) in SQLite so it
/// survives a server restart. <see cref="TranscriptJson"/> is the panel-polled transcript and
/// <see cref="HistoryJson"/> the provider-neutral chat history, each a JSON array serialized by
/// <see cref="Services.AssistantConversationStore"/> (default "[]" when empty). Clearing the conversation
/// empties both. Deliberately plain string columns — not EF owned-JSON mapping — because the history's
/// ChatBlock is a polymorphic hierarchy EF can't map; the service owns the (de)serialization.
/// </summary>
public class AssistantConversation
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    /// <summary>The panel-polled transcript (a list of AssistantEntry) as a JSON array; "[]" when empty.</summary>
    public string TranscriptJson { get; set; } = "[]";

    /// <summary>The provider-neutral conversation history (a list of ChatMessage) as a JSON array; "[]" when empty.</summary>
    public string HistoryJson { get; set; } = "[]";
}
