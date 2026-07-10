using Microsoft.Extensions.Logging;
using RpgSceneMaker.Api.Contracts;
using RpgSceneMaker.Api.Logging;
using Xunit;

namespace RpgSceneMaker.Tests.Unit;

public class InMemoryLogStoreTests
{
    private static LogEntry Entry(string message) =>
        new(DateTimeOffset.Now, "Information", "Test", message, null);

    [Fact]
    public void Caps_at_500_and_evicts_oldest()
    {
        var store = new InMemoryLogStore();
        for (var i = 0; i < 600; i++) store.Add(Entry($"msg-{i}"));

        var snapshot = store.Snapshot();
        Assert.Equal(500, snapshot.Count);
        // Newest first: entry 599 at the top, and the surviving oldest is 100 (0-99 evicted).
        Assert.Equal("msg-599", snapshot[0].Message);
        Assert.Equal("msg-100", snapshot[^1].Message);
    }

    [Fact]
    public void Snapshot_is_newest_first()
    {
        var store = new InMemoryLogStore();
        store.Add(Entry("first"));
        store.Add(Entry("second"));
        store.Add(Entry("third"));

        Assert.Equal(["third", "second", "first"], store.Snapshot().Select(e => e.Message));
    }

    [Fact]
    public void Clear_empties_the_buffer()
    {
        var store = new InMemoryLogStore();
        store.Add(Entry("a"));
        store.Clear();
        Assert.Empty(store.Snapshot());
    }

    [Fact]
    public void Logger_forwards_to_store_with_short_category_and_formatted_message()
    {
        var store = new InMemoryLogStore();
        ILogger logger = new InMemoryLogger("RpgSceneMaker.Api.Services.SceneActivator", store);

        logger.Log(LogLevel.Warning, new EventId(1), "hello", null, (state, _) => state);

        var entry = Assert.Single(store.Snapshot());
        Assert.Equal("SceneActivator", entry.Category);    // namespace trimmed to last segment
        Assert.Equal("hello", entry.Message);
        Assert.Equal("Warning", entry.Level);
    }
}
