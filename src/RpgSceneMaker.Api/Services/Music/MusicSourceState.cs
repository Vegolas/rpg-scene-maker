namespace RpgSceneMaker.Api.Services.Music;

/// <summary>
/// Remembers which music source is "active" — the source of the last successful play — so the bare transport
/// endpoints (pause/resume/next/…) and <c>/music/state</c> target it without the caller naming a source.
/// In-memory and ephemeral, exactly like <c>CurrentState</c> (the active scene): a restart forgets it, and
/// the fallback resolver takes over (Spotify if connected, else local). Registered as a singleton.
/// </summary>
public class MusicSourceState
{
    /// <summary>The active source key ("spotify" | "local"), or null if nothing has been played yet this run.</summary>
    public string? ActiveSource { get; set; }
}
