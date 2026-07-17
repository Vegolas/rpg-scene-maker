using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ModelContextProtocol.Server;
using RpgSceneMaker.Api.Services.Ai;
using Xunit;

namespace RpgSceneMaker.Tests.Unit;

/// <summary>
/// The MCP server (<see cref="McpTools"/> classes) and the in-panel assistant (<see cref="AssistantTools"/>)
/// must expose the <em>exact same</em> set of tool names — both are thin adapters over the one
/// <see cref="AiToolService"/> façade and are required (issue #50) to stay in lockstep. This reflects every
/// <c>[McpServerTool]</c> name out of the assembly and compares it to the assistant's definition names, so
/// adding an op to only one surface fails the build.
/// </summary>
public class AiToolSurfaceParityTests
{
    private static HashSet<string> McpToolNames() =>
        typeof(AiToolService).Assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Select(m => m.GetCustomAttribute<McpServerToolAttribute>()?.Name)
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .ToHashSet();

    private static HashSet<string> AssistantToolNames() =>
        AssistantTools.Definitions.Select(d => d.Name).ToHashSet();

    [Fact]
    public void Mcp_and_assistant_expose_the_same_tool_names()
    {
        var mcp = McpToolNames();
        var assistant = AssistantToolNames();

        // Neither surface may carry an op the other lacks (the lockstep invariant).
        Assert.Empty(mcp.Except(assistant));   // MCP-only tools
        Assert.Empty(assistant.Except(mcp));   // assistant-only tools
        Assert.Equal(mcp.Count, assistant.Count);
    }

    [Fact]
    public void Every_tool_name_is_unique_on_each_surface()
    {
        // ToHashSet() would silently swallow a duplicate name; count against the raw lists to catch collisions.
        var mcpNames = typeof(AiToolService).Assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Select(m => m.GetCustomAttribute<McpServerToolAttribute>()?.Name)
            .Where(n => !string.IsNullOrEmpty(n))
            .ToList();
        Assert.Equal(mcpNames.Count, mcpNames.Distinct().Count());

        var assistantNames = AssistantTools.Definitions.Select(d => d.Name).ToList();
        Assert.Equal(assistantNames.Count, assistantNames.Distinct().Count());
    }

    [Fact]
    public void Both_surfaces_cover_the_expanded_op_set()
    {
        var names = AssistantToolNames();
        // The music / sound / screen / status ops added for issue #50 must all be present.
        string[] expected =
        [
            "play_music", "pause_music", "resume_music", "next_track", "previous_track",
            "set_music_volume", "set_music_shuffle", "set_music_repeat", "get_music_state",
            "play_sound", "stop_sound", "stop_all_sounds", "update_sound", "get_sounds_state",
            "list_screens", "get_screen", "upsert_screen", "delete_screen",
            "get_event_state", "get_lights_status",
        ];
        foreach (var name in expected)
            Assert.Contains(name, names);
    }
}
