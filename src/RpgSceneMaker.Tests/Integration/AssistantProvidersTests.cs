using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RpgSceneMaker.Api.Services.Ai;
using RpgSceneMaker.Api.Services.Ai.Providers;
using Xunit;

namespace RpgSceneMaker.Tests.Integration;

[Collection("integration")]
public class AssistantProvidersTests
{
    [Fact]
    public void All_three_backends_are_registered_with_stable_ids()
    {
        using var factory = new ApiFactory();
        using var scope = factory.Services.CreateScope();

        var ids = scope.ServiceProvider.GetServices<IAssistantProvider>()
            .Select(p => p.Id).OrderBy(i => i).ToArray();

        Assert.Equal(["anthropic", "gemini", "openai"], ids);
    }

    [Fact]
    public void Tool_definitions_are_provider_neutral_json_schema_objects()
    {
        var tools = AssistantTools.Definitions;

        // 23 façade ops, matching the MCP surface, with unique names.
        Assert.Equal(23, tools.Count);
        Assert.Equal(tools.Count, tools.Select(t => t.Name).Distinct().Count());

        foreach (var tool in tools)
        {
            Assert.False(string.IsNullOrWhiteSpace(tool.Name));
            Assert.False(string.IsNullOrWhiteSpace(tool.Description));
            Assert.Equal(JsonValueKind.Object, tool.InputSchema.ValueKind);
            Assert.Equal("object", tool.InputSchema.GetProperty("type").GetString());
            // Every schema carries a properties object (empty for the no-arg tools).
            Assert.Equal(JsonValueKind.Object, tool.InputSchema.GetProperty("properties").ValueKind);
        }

        // The neutral schema distinguishes no-arg tools (empty properties) from ones that take arguments —
        // GeminiProvider relies on this to decide whether to attach a parameters schema.
        var listScenes = tools.Single(t => t.Name == "list_scenes");
        Assert.Empty(listScenes.InputSchema.GetProperty("properties").EnumerateObject());

        var upsertScene = tools.Single(t => t.Name == "upsert_scene");
        Assert.NotEmpty(upsertScene.InputSchema.GetProperty("properties").EnumerateObject());
    }
}
