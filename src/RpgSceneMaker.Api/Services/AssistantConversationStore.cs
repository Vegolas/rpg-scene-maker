using Microsoft.EntityFrameworkCore;
using RpgSceneMaker.Api.Data;

namespace RpgSceneMaker.Api.Services;

/// <summary>
/// Persists the in-panel assistant's single conversation in SQLite (one row, Id = 1) so it survives a
/// server restart. Mirrors <see cref="AssistantStore"/>: an <see cref="IDbContextFactory{AppDbContext}"/>
/// plus a private lock for the infrequent, synchronous read/write. The two payloads are opaque JSON strings
/// produced by <see cref="Ai.AssistantService"/> (the polled transcript and the provider-neutral history) —
/// this store never interprets them.
/// </summary>
public class AssistantConversationStore(IDbContextFactory<AppDbContext> dbFactory)
{
    private readonly Lock _lock = new();

    /// <summary>Load the persisted (transcript, history) JSON, or the empty "[]" defaults when no row exists yet.</summary>
    public (string TranscriptJson, string HistoryJson) Load()
    {
        lock (_lock)
        {
            using var db = dbFactory.CreateDbContext();
            var row = db.AssistantConversations.AsNoTracking()
                .SingleOrDefault(c => c.Id == AssistantConversation.SingletonId);
            return row is null ? ("[]", "[]") : (row.TranscriptJson, row.HistoryJson);
        }
    }

    /// <summary>Upsert the single conversation row with the given transcript + history JSON.</summary>
    public void Save(string transcriptJson, string historyJson)
    {
        lock (_lock)
        {
            using var db = dbFactory.CreateDbContext();
            var row = db.AssistantConversations.SingleOrDefault(c => c.Id == AssistantConversation.SingletonId);
            if (row is null)
            {
                row = new AssistantConversation();
                db.AssistantConversations.Add(row);
            }

            row.TranscriptJson = transcriptJson;
            row.HistoryJson = historyJson;
            db.SaveChanges();
        }
    }
}
