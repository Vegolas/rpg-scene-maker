using System.Text.Json;

namespace AmbientDirector.Api.Services.Ai.Providers;

/// <summary>
/// One AI backend the in-panel assistant can talk to (Anthropic / OpenAI / Gemini). A provider is a
/// stateless singleton: it builds its SDK client per call from <see cref="AssistantRequest.ApiKey"/>,
/// runs exactly one model turn, and maps the response into a neutral <see cref="AssistantTurn"/>.
/// <see cref="AssistantService"/> owns the loop (history, tool execution, transcript); the provider only
/// bridges to the vendor SDK. On any SDK/transport failure a provider throws
/// <see cref="AiProviderException"/> so the service can surface one message shape for every backend.
/// </summary>
public interface IAssistantProvider
{
    /// <summary>Stable id matching the stored <c>AssistantConfig.Provider</c> ("anthropic" | "openai" | "gemini").</summary>
    string Id { get; }

    /// <summary>Run a single model turn and return its text plus any tool calls it requested.</summary>
    Task<AssistantTurn> CreateTurnAsync(AssistantRequest request, CancellationToken ct);
}

/// <summary>Everything a provider needs for one turn. History and Tools are provider-neutral.</summary>
public sealed record AssistantRequest(
    string ApiKey,
    string Model,
    string SystemPrompt,
    int MaxTokens,
    IReadOnlyList<ChatMessage> History,
    IReadOnlyList<AiToolDefinition> Tools);

/// <summary>A model turn: assistant text (may be empty) and the tool calls it wants run before continuing.</summary>
public sealed record AssistantTurn(string? Text, IReadOnlyList<AssistantToolCall> ToolCalls);

/// <summary>A single tool call requested by the model.</summary>
public sealed record AssistantToolCall(string Id, string Name, IReadOnlyDictionary<string, JsonElement> Input);
