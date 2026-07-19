using System.Collections.Generic;
using System.Text.Json;
using AmbientDirector.Api.Services.Ai;
using AmbientDirector.Api.Services.Ai.Providers;
using Xunit;

namespace AmbientDirector.Tests.Unit;

/// <summary>The provider-neutral chat history is persisted as JSON, so it must survive a serialize→deserialize
/// through the exact <see cref="AiJson.Options"/> the assistant service uses — including the polymorphic
/// ChatBlock hierarchy and a JsonElement tool-input dictionary.</summary>
public class AssistantChatSerializationTests
{
    [Fact]
    public void History_with_all_block_kinds_round_trips_through_the_service_json_options()
    {
        // A nested-object tool input, carried as JsonElement values on ToolUseChatBlock.Input.
        var input = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            """{"id":"dungeon","light":{"power":true,"brightness":80}}""", AiJson.Options)!;

        var history = new List<ChatMessage>
        {
            new(ChatRole.User, new ChatBlock[] { new TextChatBlock("Make a spooky scene") }),
            new(ChatRole.Assistant, new ChatBlock[]
            {
                new TextChatBlock("Creating it now."),
                new ToolUseChatBlock("call-1", "upsert_scene", input),
            }),
            new(ChatRole.User, new ChatBlock[]
            {
                new ToolResultChatBlock("call-1", "upsert_scene", "boom", IsError: true),
            }),
        };

        var json = JsonSerializer.Serialize(history, AiJson.Options);
        var restored = JsonSerializer.Deserialize<List<ChatMessage>>(json, AiJson.Options)!;

        Assert.Equal(3, restored.Count);

        // Both roles survive, in order.
        Assert.Equal(ChatRole.User, restored[0].Role);
        Assert.Equal(ChatRole.Assistant, restored[1].Role);
        Assert.Equal(ChatRole.User, restored[2].Role);

        // Block runtime types + payloads survive, in order.
        var text0 = Assert.IsType<TextChatBlock>(Assert.Single(restored[0].Blocks));
        Assert.Equal("Make a spooky scene", text0.Text);

        Assert.Collection(restored[1].Blocks,
            b => Assert.Equal("Creating it now.", Assert.IsType<TextChatBlock>(b).Text),
            b =>
            {
                var use = Assert.IsType<ToolUseChatBlock>(b);
                Assert.Equal("call-1", use.Id);
                Assert.Equal("upsert_scene", use.Name);
                // A value read back out of the round-tripped JsonElement input (including a nested object).
                Assert.Equal("dungeon", use.Input["id"].GetString());
                Assert.Equal(80, use.Input["light"].GetProperty("brightness").GetInt32());
            });

        var result = Assert.IsType<ToolResultChatBlock>(Assert.Single(restored[2].Blocks));
        Assert.Equal("call-1", result.ToolUseId);
        Assert.Equal("upsert_scene", result.Name);
        Assert.Equal("boom", result.Content);
        Assert.True(result.IsError);
    }
}
