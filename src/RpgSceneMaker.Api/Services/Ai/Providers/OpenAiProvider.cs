using System.ClientModel;
using System.Text;
using System.Text.Json;
using OpenAI.Chat;
using Oa = OpenAI.Chat;

namespace RpgSceneMaker.Api.Services.Ai.Providers;

/// <summary>
/// OpenAI backend (official <c>OpenAI</c> SDK, non-streaming <c>ChatClient.CompleteChatAsync</c> per turn).
/// Maps the neutral <see cref="ChatMessage"/> history to OpenAI's flat message list (system + user +
/// assistant-with-tool_calls + tool-role results, keyed by <c>tool_call_id</c>) and the neutral
/// <see cref="AiToolDefinition"/>s to <see cref="ChatTool"/> function tools, then parses the response into an
/// <see cref="AssistantTurn"/>.
/// </summary>
public sealed class OpenAiProvider : IAssistantProvider
{
    public string Id => "openai";

    public async Task<AssistantTurn> CreateTurnAsync(AssistantRequest request, CancellationToken ct)
    {
        var client = new ChatClient(request.Model, request.ApiKey);

        var options = new ChatCompletionOptions { MaxOutputTokenCount = request.MaxTokens };
        foreach (var tool in request.Tools)
            options.Tools.Add(ChatTool.CreateFunctionTool(
                tool.Name, tool.Description, BinaryData.FromString(tool.InputSchema.GetRawText())));

        var messages = ToMessages(request.SystemPrompt, request.History);

        ChatCompletion completion;
        try
        {
            completion = await client.CompleteChatAsync(messages, options, ct);
        }
        catch (ClientResultException ex) when (ex.Status == 401)
        {
            throw new AiProviderException(Id,
                "OpenAI rejected the API key (401) — check the key on the Settings page. " + ex.Message,
                request.ApiKey);
        }
        catch (ClientResultException ex)
        {
            throw new AiProviderException(Id, $"OpenAI API error ({ex.Status}): " + ex.Message, request.ApiKey);
        }

        var text = new StringBuilder();
        foreach (var part in completion.Content)
            if (part.Kind == ChatMessageContentPartKind.Text && !string.IsNullOrEmpty(part.Text))
                text.Append(part.Text);

        var toolCalls = completion.ToolCalls
            .Select(tc => new AssistantToolCall(tc.Id, tc.FunctionName, ParseArgs(tc.FunctionArguments)))
            .ToList();

        return new AssistantTurn(text.Length > 0 ? text.ToString() : null, toolCalls);
    }

    // ---- neutral → OpenAI ----

    private static List<Oa.ChatMessage> ToMessages(string systemPrompt, IReadOnlyList<ChatMessage> history)
    {
        var messages = new List<Oa.ChatMessage> { new SystemChatMessage(systemPrompt) };

        foreach (var message in history)
        {
            if (message.Role == ChatRole.Assistant)
            {
                var toolCalls = message.Blocks.OfType<ToolUseChatBlock>()
                    .Select(u => ChatToolCall.CreateFunctionToolCall(
                        u.Id, u.Name, BinaryData.FromString(JsonSerializer.Serialize(u.Input, AiJson.Options))))
                    .ToList();
                var text = string.Concat(message.Blocks.OfType<TextChatBlock>().Select(t => t.Text));

                if (toolCalls.Count > 0)
                {
                    var assistant = new AssistantChatMessage(toolCalls);
                    if (!string.IsNullOrEmpty(text))
                        assistant.Content.Add(ChatMessageContentPart.CreateTextPart(text));
                    messages.Add(assistant);
                }
                else
                {
                    messages.Add(new AssistantChatMessage(text));
                }
            }
            else // User: either plain text, or a batch of tool results (each its own tool-role message).
            {
                foreach (var result in message.Blocks.OfType<ToolResultChatBlock>())
                    messages.Add(new ToolChatMessage(result.ToolUseId, result.Content));

                var text = string.Concat(message.Blocks.OfType<TextChatBlock>().Select(t => t.Text));
                if (!string.IsNullOrEmpty(text))
                    messages.Add(new UserChatMessage(text));
            }
        }

        return messages;
    }

    private static IReadOnlyDictionary<string, JsonElement> ParseArgs(BinaryData functionArguments)
    {
        var json = functionArguments.ToString();
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, JsonElement>();
        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, AiJson.Options)
               ?? new Dictionary<string, JsonElement>();
    }
}
