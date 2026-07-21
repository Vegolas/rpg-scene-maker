namespace AmbientDirector.Api.Services;

/// <summary>One piece of content the GM has pushed to the player-facing "/tv" display. Ephemeral (held only
/// in <see cref="TvState"/>, never persisted). <see cref="Ref"/> is what the content points at: for
/// <see cref="Kind"/> "image" it is a stored image file name resolved through <see cref="ImageFileStorage"/>
/// (never a path or URL); for "board" it is a persisted <see cref="Models.Board"/> id; for "encounter" it is a
/// persisted <see cref="Models.Encounter"/> id (its heroes-left / enemies-right view is synthesized into a
/// board render model at poll time — issue #122). (PDF pages import as ordinary images, so they ride the
/// "image" kind — issue #88.)</summary>
public record TvContent(string Kind, string Ref, string? Label, DateTimeOffset PushedAtUtc);

/// <summary>Singleton holding what is currently on the single shared player-facing display, plus a short
/// history of recently pushed content so the panel can re-push quickly after a reload. Ephemeral like
/// <see cref="CurrentState"/> — survives navigation, not a restart. Thread-safe (lock), like the other
/// singletons. <see cref="Rev"/> starts at 1 and increments on every show/clear/edit; the TV polls
/// <c>/tv/state</c> and always trusts the server's revision (it resets to 1 on a restart, so pollers must
/// not assume it only grows).</summary>
public class TvState
{
    // Newest-first history the panel offers as "re-push" tiles; deduped by (kind, ref) so re-pushing the same
    // thing just moves it to the front. Kept small — it's a convenience, not a gallery.
    private const int RecentCapacity = 12;

    private readonly Lock _lock = new();
    private long _rev = 1;
    private TvContent? _current;
    private readonly List<TvContent> _recent = [];

    /// <summary>Push content to the display: <paramref name="kind"/> "image" (a stored file name), "board" (a
    /// board id) or "encounter" (an encounter id). Bumps the revision and records it at the front of Recent
    /// (deduped by the (kind, ref) pair). Returns the new revision.</summary>
    public long Show(string kind, string @ref, string? label)
    {
        lock (_lock)
        {
            var content = new TvContent(kind, @ref, label, DateTimeOffset.UtcNow);
            _current = content;
            _rev++;
            _recent.RemoveAll(c => IsSame(c, kind, @ref));
            _recent.Insert(0, content);
            if (_recent.Count > RecentCapacity)
                _recent.RemoveRange(RecentCapacity, _recent.Count - RecentCapacity);
            return _rev;
        }
    }

    /// <summary>Push an encounter (issue #122) to the display by id — a thin alias over <see cref="Show"/> with
    /// kind "encounter". The heroes-left / enemies-right view is synthesized from the live encounter at poll
    /// time (see TvEndpoints), so only the id is stored. Returns the new revision.</summary>
    public long ShowEncounter(string id, string? label) => Show("encounter", id, label);

    /// <summary>Clear the display (nothing shown). Bumps the revision. Returns the new revision.</summary>
    public long Clear()
    {
        lock (_lock)
        {
            _current = null;
            _rev++;
            return _rev;
        }
    }

    /// <summary>If the board with this id is the one currently shown, bump the revision so an open TV re-fetches
    /// it within one poll (the codebase's real-time idiom — a live edit reaches the display via the rev bump,
    /// no SSE). Returns true when the shown board matched. Does nothing for a not-shown board.</summary>
    public bool TouchBoard(string id)
    {
        lock (_lock)
        {
            if (_current is { Kind: "board" } c && string.Equals(c.Ref, id, StringComparison.OrdinalIgnoreCase))
            {
                _rev++;
                return true;
            }
            return false;
        }
    }

    /// <summary>Scrub a deleted board from the display: drop any Recent entries for it and, if it is the one
    /// currently shown, clear the display. Bumps the revision if anything changed (mirrors the sound-delete
    /// scrub — nothing dangles after a delete).</summary>
    public void ForgetBoard(string id)
    {
        lock (_lock)
        {
            var removed = _recent.RemoveAll(c => IsSame(c, "board", id));
            var wasShown = _current is { Kind: "board" } c &&
                           string.Equals(c.Ref, id, StringComparison.OrdinalIgnoreCase);
            if (wasShown) _current = null;
            if (removed > 0 || wasShown) _rev++;
        }
    }

    /// <summary>If the encounter with this id is the one currently shown, bump the revision so an open TV
    /// re-fetches it within one poll (the board <see cref="TouchBoard"/> idiom, for the "encounter" kind). Used
    /// on a live encounter edit and on any enemy-instance / hero / Fear change that the encounter view renders.
    /// Returns true when the shown encounter matched.</summary>
    public bool TouchEncounter(string id)
    {
        lock (_lock)
        {
            if (_current is { Kind: "encounter" } c && string.Equals(c.Ref, id, StringComparison.OrdinalIgnoreCase))
            {
                _rev++;
                return true;
            }
            return false;
        }
    }

    /// <summary>Scrub a deleted encounter from the display: drop any Recent entries for it and, if it is the one
    /// currently shown, clear the display. Bumps the revision if anything changed (the <see cref="ForgetBoard"/>
    /// idiom for the "encounter" kind — nothing dangles after a delete).</summary>
    public void ForgetEncounter(string id)
    {
        lock (_lock)
        {
            var removed = _recent.RemoveAll(c => IsSame(c, "encounter", id));
            var wasShown = _current is { Kind: "encounter" } c &&
                           string.Equals(c.Ref, id, StringComparison.OrdinalIgnoreCase);
            if (wasShown) _current = null;
            if (removed > 0 || wasShown) _rev++;
        }
    }

    /// <summary>Snapshot of the current revision + content for the state poll.</summary>
    public (long Rev, TvContent? Content) Snapshot()
    {
        lock (_lock)
        {
            return (_rev, _current);
        }
    }

    /// <summary>The content currently shown (null when the screen is cleared), for streaming its bytes.</summary>
    public TvContent? Current
    {
        get { lock (_lock) { return _current; } }
    }

    /// <summary>Recently pushed content, newest first, for the panel's re-push tiles.</summary>
    public IReadOnlyList<TvContent> Recent
    {
        get { lock (_lock) { return [.. _recent]; } }
    }

    private static bool IsSame(TvContent c, string kind, string @ref) =>
        string.Equals(c.Kind, kind, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(c.Ref, @ref, StringComparison.OrdinalIgnoreCase);
}
