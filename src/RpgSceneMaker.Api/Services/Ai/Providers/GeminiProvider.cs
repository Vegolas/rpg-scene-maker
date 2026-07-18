using System.Text.Json;
using System.Text.Json.Nodes;
using Mscc.GenerativeAI;
using Mscc.GenerativeAI.Types;

namespace RpgSceneMaker.Api.Services.Ai.Providers;

/// <summary>
/// Google Gemini backend (<c>Mscc.GenerativeAI</c> SDK, one <c>GenerateContent</c> call per turn). Maps the
/// neutral <see cref="ChatMessage"/> history to Gemini <see cref="Content"/> turns (role "user"/"model",
/// <c>functionCall</c>/<c>functionResponse</c> parts) and the neutral <see cref="AiToolDefinition"/>s to
/// <see cref="FunctionDeclaration"/>s, then reads back text + function calls as an <see cref="AssistantTurn"/>.
/// Gemini has no tool-call ids and pairs a function response to its call by name, so the neutral tool-call id
/// is set to the function name here and <see cref="ToolResultChatBlock.Name"/> drives the response.
/// </summary>
public sealed class GeminiProvider : IAssistantProvider
{
    public string Id => "gemini";

    public async Task<AssistantTurn> CreateTurnAsync(AssistantRequest request, CancellationToken ct)
    {
        GenerateContentResponse response;
        try
        {
            // Client creation is inside the try: GoogleAI validates the key (e.g. its length) at construction,
            // so a bad key must surface as an AiProviderException like the other backends, not a generic crash.
            var googleAi = new GoogleAI(apiKey: request.ApiKey);
            var model = googleAi.GenerativeModel(model: request.Model);

            var genRequest = new GenerateContentRequest
            {
                SystemInstruction = new Content { Role = "user", Parts = [new Part { Text = request.SystemPrompt }] },
                Tools = [new Tool { FunctionDeclarations = request.Tools.Select(ToDeclaration).ToList() }],
                Contents = request.History.Select(ToContent).ToList(),
            };

            response = await model.GenerateContent(genRequest, cancellationToken: ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new AiProviderException(Id, "Gemini API error: " + ex.Message, request.ApiKey);
        }

        string? text = null;
        try { text = response.Text; }
        catch { /* a function-call-only response has no text part */ }

        var toolCalls = (response.FunctionCalls ?? [])
            .Where(fc => !string.IsNullOrEmpty(fc.Name))
            .Select(fc => new AssistantToolCall(fc.Name!, fc.Name!, ArgsToDict(fc.Args)))
            .ToList();

        return new AssistantTurn(string.IsNullOrEmpty(text) ? null : text, toolCalls);
    }

    // ---- neutral → Gemini ----

    private static FunctionDeclaration ToDeclaration(AiToolDefinition def)
    {
        var decl = new FunctionDeclaration { Name = def.Name, Description = def.Description };

        // Gemini rejects an object schema with no properties, so only attach parameters for tools that take some.
        if (def.InputSchema.TryGetProperty("properties", out var props) &&
            props.ValueKind == JsonValueKind.Object && props.EnumerateObject().Any())
        {
            decl.Parameters = Schema.FromString(def.InputSchema.GetRawText());
        }

        return decl;
    }

    private static Content ToContent(ChatMessage message)
    {
        var parts = new List<IPart>();
        foreach (var block in message.Blocks)
        {
            switch (block)
            {
                case TextChatBlock text when !string.IsNullOrEmpty(text.Text):
                    parts.Add(new Part { Text = text.Text });
                    break;
                case ToolUseChatBlock use:
                    parts.Add(new Part
                    {
                        FunctionCall = new FunctionCall
                        {
                            Name = use.Name,
                            Args = JsonSerializer.SerializeToNode(use.Input, AiJson.Options),
                        },
                    });
                    break;
                case ToolResultChatBlock result:
                    parts.Add(new Part
                    {
                        FunctionResponse = new FunctionResponse
                        {
                            Name = result.Name,
                            Response = ToResponseObject(result),
                        },
                    });
                    break;
            }
        }

        // "model" for the assistant's own turns; "user" for the human and for tool (function) results.
        return new Content { Role = message.Role == ChatRole.Assistant ? "model" : "user", Parts = parts };
    }

    // Gemini's functionResponse.response must be a JSON object; wrap non-object/plain results under a key.
    private static JsonNode ToResponseObject(ToolResultChatBlock result)
    {
        var key = result.IsError ? "error" : "result";
        try
        {
            var node = JsonNode.Parse(result.Content);
            if (node is JsonObject obj) return obj;
            return new JsonObject { [key] = node };
        }
        catch (JsonException)
        {
            return new JsonObject { [key] = result.Content };
        }
    }

    private static IReadOnlyDictionary<string, JsonElement> ArgsToDict(object? args)
    {
        if (args is null) return new Dictionary<string, JsonElement>();
        var el = JsonSerializer.SerializeToElement(args, AiJson.Options);
        return el.ValueKind == JsonValueKind.Object
            ? el.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone())
            : new Dictionary<string, JsonElement>();
    }
}
