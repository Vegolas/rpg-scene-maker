using System.Linq;
using RpgSceneMaker.Api.Services;
using Xunit;

namespace RpgSceneMaker.Tests.Store;

/// <summary>The single-row store backing the persisted assistant conversation: round-trip and upsert.</summary>
public class AssistantConversationStoreTests
{
    [Fact]
    public void Fresh_store_loads_the_empty_array_defaults()
    {
        using var db = new SqliteTestDb();

        var (transcriptJson, historyJson) = new AssistantConversationStore(db).Load();

        Assert.Equal("[]", transcriptJson);
        Assert.Equal("[]", historyJson);
    }

    [Fact]
    public void Save_then_Load_returns_the_same_strings()
    {
        using var db = new SqliteTestDb();
        var store = new AssistantConversationStore(db);

        const string transcript = """[{"seq":0,"kind":"user","text":"hi"}]""";
        const string history = """[{"role":0,"blocks":[{"kind":"text","text":"hi"}]}]""";
        store.Save(transcript, history);

        var (loadedTranscript, loadedHistory) = store.Load();
        Assert.Equal(transcript, loadedTranscript);
        Assert.Equal(history, loadedHistory);

        // A second store instance loads the same row from SQLite.
        var (reloadedTranscript, reloadedHistory) = new AssistantConversationStore(db).Load();
        Assert.Equal(transcript, reloadedTranscript);
        Assert.Equal(history, reloadedHistory);
    }

    [Fact]
    public void Saving_twice_upserts_the_single_row()
    {
        using var db = new SqliteTestDb();
        var store = new AssistantConversationStore(db);

        store.Save("""["first"]""", "[]");
        store.Save("""["second"]""", """["h"]""");

        // The latest values win...
        var (transcriptJson, historyJson) = store.Load();
        Assert.Equal("""["second"]""", transcriptJson);
        Assert.Equal("""["h"]""", historyJson);

        // ...and there is still exactly one row.
        using var ctx = db.CreateDbContext();
        Assert.Equal(1, ctx.AssistantConversations.Count());
    }
}
