using System.Text.Json;
using RpgSceneMaker.Api.Services;
using RpgSceneMaker.Api.Services.Ai.Providers;

namespace RpgSceneMaker.Api.Services.Ai;

/// <summary>One line of the assistant transcript the panel polls (kinds: user | assistant | tool | error).</summary>
public sealed class AssistantEntry
{
    public int Seq { get; set; }
    public string Kind { get; set; } = "";
    public string Text { get; set; } = "";
    public string? ToolName { get; set; }
    public string? ToolInputJson { get; set; }
    public string? ToolResult { get; set; }
    public bool? ToolIsError { get; set; }
}

/// <summary>Snapshot returned by <see cref="AssistantService.GetState"/>. Entries is populated only when the
/// caller's revision is stale (the panel long-poll idiom); otherwise null so the poll is a no-op.</summary>
public record AssistantState(long Rev, bool Busy, bool Configured, List<AssistantEntry>? Entries);

/// <summary>
/// Server-side chat assistant: a singleton that runs a provider-agnostic agentic tool loop over the same 23
/// façade operations as the MCP server. It owns the transcript, conversation history, cancel/busy state and
/// revision counter; the actual model turn is delegated to the configured <see cref="IAssistantProvider"/>
/// (Anthropic / OpenAI / Gemini), selected per run from <see cref="AssistantStore"/>. One run at a time; the
/// loop appends to an in-memory transcript and bumps a revision so the panel's ~1 s poll of
/// <c>/assistant/state</c> sees turns and tool calls appear live. Conversation history is kept across sends
/// (reset by <see cref="Clear"/>). The API key is read from <see cref="AssistantStore"/> per run and never
/// enters the transcript or the system prompt.
/// </summary>
public sealed class AssistantService(
    AssistantStore store,
    AssistantTools toolExec,
    IEnumerable<IAssistantProvider> providers,
    ILogger<AssistantService> logger)
{
    private const int MaxTokens = 8192;
    private const int TranscriptCap = 500;          // entries kept (drop oldest)
    private const int TranscriptResultCap = 2000;   // tool-result chars stored for the panel
    private const int ToolResultSendCap = 30_000;   // tool-result chars sent back to the model

    private const string SystemPrompt =
        "You are the assistant for RPG Scene Maker, a control panel that sets a tabletop RPG's mood — lights " +
        "and music — with one tap. You manage its scenes (a whole table state: per-light control + optional " +
        "Spotify music + one-shot sounds), one-shot events (a light flash and/or overlaid sounds, optionally " +
        "an ms timeline), and the reusable Light FX library (named keyframe animations scenes and events " +
        "reference). Work through the provided tools only. Before creating anything, inspect what exists " +
        "(list_scenes / list_events / list_light_fx) and the available hardware/media (list_lights, " +
        "list_sounds, list_spotify_playlists, search_spotify_tracks) — a scene light's lightKey must be a real " +
        "key from list_lights, soundEffects are ids from list_sounds, and music.playId is a spotify: URI from " +
        "the Spotify tools. Use lowercase-slug ids ([a-z0-9-_]); prefer editing an existing entity over " +
        "creating a near-duplicate. Note that activate_scene, trigger_event, test_light_fx and reset_lights " +
        "act on the real lights/speakers immediately — only use them when the user asks to try something. Keep " +
        "replies brief and practical.";

    private readonly Lock _lock = new();
    private readonly List<AssistantEntry> _transcript = [];
    private readonly List<ChatMessage> _history = [];
    private long _rev;
    private bool _busy;
    private int _nextSeq;
    private CancellationTokenSource? _cts;

    /// <summary>Queue a user message and start a run. Throws on blank text or when the key is unset; returns
    /// false if a run is already in progress (the endpoint turns that into 409).</summary>
    public bool TrySend(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Message text is required.");
        if (!store.Current.IsConfigured)
            throw new InvalidOperationException("AI provider API key is not set — add it on the Settings page.");

        CancellationToken token;
        lock (_lock)
        {
            if (_busy) return false;
            var trimmed = text.Trim();
            AppendLocked(new AssistantEntry { Kind = "user", Text = trimmed });
            _history.Add(new ChatMessage(ChatRole.User, [new TextChatBlock(trimmed)]));
            _busy = true;
            _cts = new CancellationTokenSource();
            token = _cts.Token;
        }
        _ = Task.Run(() => RunLoopAsync(token));
        return true;
    }

    /// <summary>Current state; entries only when the revision moved past <paramref name="sinceRev"/>.</summary>
    public AssistantState GetState(long sinceRev)
    {
        lock (_lock)
        {
            var entries = _rev != sinceRev ? _transcript.Select(Clone).ToList() : null;
            return new AssistantState(_rev, _busy, store.Current.IsConfigured, entries);
        }
    }

    /// <summary>Cancel the running loop. Returns whether a run was cancelled.</summary>
    public bool Stop()
    {
        lock (_lock)
        {
            if (!_busy || _cts is null) return false;
            _cts.Cancel();
            return true;
        }
    }

    /// <summary>Reset transcript + conversation history. Refuses (returns false → 409) while a run is active.</summary>
    public bool Clear()
    {
        lock (_lock)
        {
            if (_busy) return false;
            _transcript.Clear();
            _history.Clear();
            _rev++;
            return true;
        }
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        try
        {
            var config = store.Current;
            var provider = providers.FirstOrDefault(p => p.Id == config.Provider)
                ?? throw new InvalidOperationException(
                    $"Unknown AI provider '{config.Provider}' — pick a supported provider on the Settings page.");

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                var request = new AssistantRequest(
                    ApiKey: config.ApiKey,
                    Model: config.Model,
                    SystemPrompt: SystemPrompt,
                    MaxTokens: MaxTokens,
                    History: SnapshotHistory(),
                    Tools: AssistantTools.Definitions);

                AssistantTurn turn = await provider.CreateTurnAsync(request, ct);

                if (!string.IsNullOrEmpty(turn.Text))
                    AddEntry(new AssistantEntry { Kind = "assistant", Text = turn.Text });

                // Echo the assistant turn back into history (text first, then any tool-use blocks).
                var assistantBlocks = new List<ChatBlock>();
                if (!string.IsNullOrEmpty(turn.Text))
                    assistantBlocks.Add(new TextChatBlock(turn.Text));
                foreach (var call in turn.ToolCalls)
                    assistantBlocks.Add(new ToolUseChatBlock(call.Id, call.Name, call.Input));
                AddHistory(new ChatMessage(ChatRole.Assistant, assistantBlocks));

                if (turn.ToolCalls.Count == 0)
                    break; // no tool calls → the model is done

                var resultBlocks = new List<ChatBlock>();
                foreach (var call in turn.ToolCalls)
                {
                    ct.ThrowIfCancellationRequested();

                    var inputJson = JsonSerializer.Serialize(call.Input, AiJson.Options);
                    var entry = new AssistantEntry
                    {
                        Kind = "tool", Text = call.Name, ToolName = call.Name, ToolInputJson = inputJson,
                    };
                    AddEntry(entry);

                    var (resultJson, isError) = await toolExec.ExecuteAsync(call.Name, call.Input, ct);

                    lock (_lock)
                    {
                        entry.ToolResult = Truncate(resultJson, TranscriptResultCap);
                        entry.ToolIsError = isError;
                        _rev++;
                    }

                    resultBlocks.Add(new ToolResultChatBlock(call.Id, call.Name, Truncate(resultJson, ToolResultSendCap), isError));
                }

                // All tool_results for a turn go in ONE user message, then loop for the model's next turn.
                AddHistory(new ChatMessage(ChatRole.User, resultBlocks));
            }
        }
        catch (OperationCanceledException)
        {
            AddEntry(new AssistantEntry { Kind = "error", Text = "Stopped." });
        }
        catch (AiProviderException ex)
        {
            AddEntry(new AssistantEntry { Kind = "error", Text = ex.Message });
            logger.LogWarning(ex, "Assistant run failed talking to the {Provider} provider.", ex.Provider);
        }
        catch (Exception ex)
        {
            AddEntry(new AssistantEntry { Kind = "error", Text = "The assistant run failed: " + ex.Message });
            logger.LogError(ex, "Assistant run crashed.");
        }
        finally
        {
            lock (_lock)
            {
                _busy = false;
                _cts?.Dispose();
                _cts = null;
                _rev++;
            }
        }
    }

    // ---- transcript/history helpers ----

    private void AddEntry(AssistantEntry entry)
    {
        lock (_lock) { AppendLocked(entry); }
    }

    private void AppendLocked(AssistantEntry entry)
    {
        entry.Seq = _nextSeq++;
        _transcript.Add(entry);
        if (_transcript.Count > TranscriptCap)
            _transcript.RemoveRange(0, _transcript.Count - TranscriptCap);
        _rev++;
    }

    private void AddHistory(ChatMessage message)
    {
        lock (_lock) { _history.Add(message); }
    }

    private List<ChatMessage> SnapshotHistory()
    {
        lock (_lock) { return [.. _history]; }
    }

    private static AssistantEntry Clone(AssistantEntry e) => new()
    {
        Seq = e.Seq, Kind = e.Kind, Text = e.Text,
        ToolName = e.ToolName, ToolInputJson = e.ToolInputJson,
        ToolResult = e.ToolResult, ToolIsError = e.ToolIsError,
    };

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : string.Concat(s.AsSpan(0, max), $"… [truncated, {s.Length} chars total]");
}
