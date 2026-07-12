using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;

namespace RpgSceneMaker.Api.Services.Ai.Providers;

/// <summary>
/// Anthropic backend (official <c>Anthropic</c> SDK, non-streaming <c>Messages.Create</c> per turn). Maps the
/// neutral <see cref="ChatMessage"/> history to <see cref="MessageParam"/> and the neutral
/// <see cref="AiToolDefinition"/>s to Anthropic <see cref="Tool"/>s, then parses the response's content
/// blocks back into an <see cref="AssistantTurn"/>. This is the logic that previously lived inline in
/// <see cref="AssistantService"/>.
/// </summary>
public sealed class AnthropicProvider : IAssistantProvider
{
    public string Id => "anthropic";

    public async Task<AssistantTurn> CreateTurnAsync(AssistantRequest request, CancellationToken ct)
    {
        // A fresh client per turn picks up the currently stored key.
        var client = new AnthropicClient { ApiKey = request.ApiKey };

        var createParams = new MessageCreateParams
        {
            Model = request.Model,
            MaxTokens = request.MaxTokens,
            System = request.SystemPrompt,
            Tools = request.Tools.Select(ToTool).Select(t => (ToolUnion)t).ToList(),
            Messages = request.History.Select(ToMessageParam).ToList(),
        };

        Message message;
        try
        {
            message = await client.Messages.Create(createParams, ct);
        }
        catch (Anthropic.Exceptions.AnthropicUnauthorizedException ex)
        {
            throw new AiProviderException(Id,
                "Anthropic rejected the API key (401) — check the key on the Settings page. " + ex.Message);
        }
        catch (Anthropic.Exceptions.AnthropicException ex)
        {
            throw new AiProviderException(Id, "Anthropic API error: " + ex.Message);
        }

        var text = new StringBuilder();
        var toolCalls = new List<AssistantToolCall>();
        foreach (var block in message.Content)
        {
            if (block.TryPickText(out var t))
            {
                if (!string.IsNullOrEmpty(t.Text)) text.Append(t.Text);
            }
            else if (block.TryPickToolUse(out var tu))
            {
                toolCalls.Add(new AssistantToolCall(tu.ID, tu.Name, tu.Input));
            }
        }

        return new AssistantTurn(text.Length > 0 ? text.ToString() : null, toolCalls);
    }

    // ---- neutral → Anthropic ----

    private static Tool ToTool(AiToolDefinition def) => new()
    {
        Name = def.Name,
        Description = def.Description,
        InputSchema = ToInputSchema(def.InputSchema),
    };

    private static InputSchema ToInputSchema(JsonElement schema)
    {
        // The neutral schema is a JSON-Schema object: { "type": "object", "properties": {...}, "required": [...] }.
        var properties = schema.TryGetProperty("properties", out var props)
            ? props.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone())
            : [];
        var required = schema.TryGetProperty("required", out var req)
            ? req.EnumerateArray().Select(e => e.GetString()!).ToList()
            : [];

        return new InputSchema
        {
            Type = JsonSerializer.SerializeToElement("object"),
            Properties = properties,
            Required = required,
        };
    }

    private static MessageParam ToMessageParam(ChatMessage message)
    {
        var blocks = new List<ContentBlockParam>();
        foreach (var block in message.Blocks)
        {
            switch (block)
            {
                case TextChatBlock text:
                    blocks.Add(new TextBlockParam(text.Text));
                    break;
                case ToolUseChatBlock use:
                    blocks.Add(new ToolUseBlockParam { ID = use.Id, Name = use.Name, Input = use.Input });
                    break;
                case ToolResultChatBlock result:
                    blocks.Add(new ToolResultBlockParam(result.ToolUseId)
                    {
                        Content = result.Content,
                        IsError = result.IsError,
                    });
                    break;
            }
        }

        return new MessageParam
        {
            Role = message.Role == ChatRole.User ? Role.User : Role.Assistant,
            Content = blocks,
        };
    }
}
