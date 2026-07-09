using RpgSceneMaker.Api.Contracts;

namespace RpgSceneMaker.Api.Logging;

/// <summary>
/// Bounded, thread-safe ring buffer of recent log entries, surfaced by the panel's Logs tab.
/// Registered as a singleton and fed by <see cref="InMemoryLoggerProvider"/>.
/// </summary>
public class InMemoryLogStore
{
    private const int Capacity = 500;
    private readonly Queue<LogEntry> _entries = new();
    private readonly Lock _gate = new();

    public void Add(LogEntry entry)
    {
        lock (_gate)
        {
            _entries.Enqueue(entry);
            while (_entries.Count > Capacity) _entries.Dequeue();
        }
    }

    /// <summary>Newest entry first, so the tab shows the latest activity at the top.</summary>
    public IReadOnlyList<LogEntry> Snapshot()
    {
        lock (_gate) { return _entries.Reverse().ToArray(); }
    }

    public void Clear()
    {
        lock (_gate) { _entries.Clear(); }
    }
}
