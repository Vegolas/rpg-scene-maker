using System.Text.Json;

namespace AmbientDirector.Api.Services.Ai.Providers;

/// <summary>
/// A provider-neutral tool definition: the name, human description and a JSON-Schema object describing the
/// arguments (<c>{ "type": "object", "properties": {…}, "required": [...] }</c>). <see cref="AssistantTools"/>
/// builds one of these per façade op; each provider adapter maps it to its SDK's tool/function type
/// (Anthropic <c>Tool</c>, OpenAI <c>ChatTool</c>, Gemini <c>FunctionDeclaration</c>).
/// </summary>
public sealed record AiToolDefinition(string Name, string Description, JsonElement InputSchema);
