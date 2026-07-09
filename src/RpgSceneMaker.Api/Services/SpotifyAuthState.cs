using System.Collections.Concurrent;

namespace RpgSceneMaker.Api.Services;

/// <summary>
/// Short-lived store for in-flight PKCE login attempts, keyed by the OAuth <c>state</c> value.
/// Entries older than 10 minutes are pruned. Registered as a singleton.
/// </summary>
public class SpotifyAuthState
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, Entry> _pending = new();

    public record Entry(string Verifier, string RedirectUri, DateTimeOffset CreatedAt);

    public void Add(string state, string verifier, string redirectUri)
    {
        Prune();
        _pending[state] = new Entry(verifier, redirectUri, DateTimeOffset.UtcNow);
    }

    /// <summary>Consume the entry for a state value (single use). Returns null when unknown or expired.</summary>
    public Entry? Take(string state)
    {
        Prune();
        if (_pending.TryRemove(state, out var entry) && DateTimeOffset.UtcNow - entry.CreatedAt <= Ttl)
            return entry;
        return null;
    }

    private void Prune()
    {
        var cutoff = DateTimeOffset.UtcNow - Ttl;
        foreach (var (key, entry) in _pending)
            if (entry.CreatedAt < cutoff)
                _pending.TryRemove(key, out _);
    }
}
