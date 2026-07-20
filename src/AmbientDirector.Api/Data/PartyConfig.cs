using AmbientDirector.Api.Models;

namespace AmbientDirector.Api.Data;

/// <summary>
/// The table-level half of the party domain (issue #88, Phase 3): stats that belong to the whole table rather
/// than to one <see cref="PartyMember"/> — Daggerheart's Fear is the motivating example. Stored as a single
/// row (Id = 1) in SQLite, like <see cref="LightingConfig"/> and <see cref="AssistantConversation"/>. Edited
/// via <c>PUT /party/counters</c> and adjusted via <c>/party/counters/adjust</c>; the database is the source
/// of truth. Reuses the same generic <see cref="PartyCounter"/> value object as a member's counters, so the
/// panel and TV render both the same way (Fear is just an unowned counter).
/// </summary>
public class PartyConfig
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    /// <summary>The table-level counters (one JSON column). Empty by default — the panel seeds Fear (or not).</summary>
    public List<PartyCounter> Counters { get; set; } = [];
}
