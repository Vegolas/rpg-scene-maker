using System.Text.Json;

namespace RpgSceneMaker.Api.Services.Ai.Providers;

/// <summary>Who authored a <see cref="ChatMessage"/>.</summary>
public enum ChatRole
{
    User,
    Assistant,
}

/// <summary>
/// Provider-neutral conversation history kept by <see cref="AssistantService"/>. Each provider adapter
/// translates this into (and out of) its own SDK's message types, so the loop itself never touches a
/// vendor SDK. A message is a role plus an ordered list of content blocks (text / tool-use / tool-result),
/// mirroring the block model every current chat API shares.
/// </summary>
public sealed record ChatMessage(ChatRole Role, IReadOnlyList<ChatBlock> Blocks);

/// <summary>One content block of a <see cref="ChatMessage"/>.</summary>
public abstract record ChatBlock;

/// <summary>Plain assistant/user text.</summary>
public sealed record TextChatBlock(string Text) : ChatBlock;

/// <summary>An assistant request to call a tool: the call id, tool name and JSON arguments.</summary>
public sealed record ToolUseChatBlock(string Id, string Name, IReadOnlyDictionary<string, JsonElement> Input) : ChatBlock;

/// <summary>The result of a tool call, sent back to the model on the next turn. <see cref="Name"/> is the
/// tool's name — redundant for id-based backends (Anthropic/OpenAI) but required by Gemini, which pairs a
/// function response to its call by name, so carrying it keeps the history portable across providers.</summary>
public sealed record ToolResultChatBlock(string ToolUseId, string Name, string Content, bool IsError) : ChatBlock;
