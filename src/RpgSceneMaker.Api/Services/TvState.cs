namespace RpgSceneMaker.Api.Services;

/// <summary>One piece of content the GM has pushed to the player-facing "/tv" display. Ephemeral (held only
/// in <see cref="TvState"/>, never persisted). <see cref="File"/> is a stored image file name resolved
/// through <see cref="ImageFileStorage"/> — never a path or URL. <see cref="Kind"/> is always "image" for the
/// MVP (PDF pages and a lite-VTT map are the documented growth path — issue #80).</summary>
public record TvContent(string Kind, string File, string? Label, DateTimeOffset PushedAtUtc);

/// <summary>Singleton holding what is currently on the single shared player-facing display, plus a short
/// history of recently pushed content so the panel can re-push quickly after a reload. Ephemeral like
/// <see cref="CurrentState"/> — survives navigation, not a restart. Thread-safe (lock), like the other
/// singletons. <see cref="Rev"/> starts at 1 and increments on every show/clear; the TV polls
/// <c>/tv/state</c> and always trusts the server's revision (it resets to 1 on a restart, so pollers must
/// not assume it only grows).</summary>
public class TvState
{
    // Newest-first history the panel offers as "re-push" tiles; deduped by file so re-pushing an image just
    // moves it to the front. Kept small — it's a convenience, not a gallery.
    private const int RecentCapacity = 12;

    private readonly Lock _lock = new();
    private long _rev = 1;
    private TvContent? _current;
    private readonly List<TvContent> _recent = [];

    /// <summary>Push an image (by stored file name) to the display. Bumps the revision and records it at the
    /// front of Recent (deduped by file). Returns the new revision.</summary>
    public long Show(string file, string? label)
    {
        lock (_lock)
        {
            var content = new TvContent("image", file, label, DateTimeOffset.UtcNow);
            _current = content;
            _rev++;
            _recent.RemoveAll(c => string.Equals(c.File, file, StringComparison.OrdinalIgnoreCase));
            _recent.Insert(0, content);
            if (_recent.Count > RecentCapacity)
                _recent.RemoveRange(RecentCapacity, _recent.Count - RecentCapacity);
            return _rev;
        }
    }

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
}
