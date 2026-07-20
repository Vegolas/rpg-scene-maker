namespace AmbientDirector.Api.Models;

// Wire contract: the API serializes Enemy straight to /party/enemies (there is no Contracts/ DTO beyond the
// PartyDto envelope, like PartyMember/Board/Screen). The panel mirrors this exact shape by hand in
// AmbientDirector.Ui/Contracts/PartyContracts.cs — keep the two in sync when a field changes.

/// <summary>
/// One reusable adversary <em>statblock</em> in the bestiary (issue #122): a name, a portrait and its base
/// counter definitions (max HP/Stress/…). Base stats only — an <see cref="Enemy"/> carries <b>no live
/// tracking</b>. Live per-fight values live on an <see cref="EncounterEnemy"/> instance inside an
/// <see cref="Encounter"/> (seeded from this template at add-time); the same template can be instanced many
/// times in one fight (2× Goblin), each tracked separately. The wire/route vocabulary is still
/// <em>"enemies"</em> (<c>GET /party/list</c> returns <c>enemies</c>; ids appear in <c>/party/enemies/{id}</c> URLs).
/// </summary>
/// <remarks>
/// Like a member, an enemy's stats are generic <see cref="PartyCounter"/>s so any system fits: the Daggerheart
/// HP/Stress loadout is a <em>UI-side preset</em>, never hardcoded here. As a bestiary template these
/// <see cref="Counters"/> are the <b>base definitions</b> (each <see cref="PartyCounter.Value"/> is the starting
/// value, typically equal to <see cref="PartyCounter.Max"/>) — the values an encounter instance is seeded with.
/// The per-fight <em>spotlight</em> (boss) flag lives on the instance (<see cref="EncounterEnemy.Spotlight"/>),
/// never on the template.
/// </remarks>
public class Enemy
{
    /// <summary>Slug id (matched case-insensitively, NOCASE collation, like members/scenes/boards), used in
    /// <c>PUT/DELETE /party/enemies/{id}</c> and referenced from an <see cref="EncounterEnemy.EnemyId"/>.</summary>
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>Stored image file name of this statblock's portrait (uploaded via <c>/images</c>, resolved
    /// through <see cref="Services.ImageFileStorage"/>), or null. Never a path or URL — like
    /// <see cref="PartyMember.Portrait"/>. Copied onto an <see cref="EncounterEnemy.Portrait"/> when an instance
    /// is added (a snapshot, not separately owned).</summary>
    public string? Portrait { get; set; }

    /// <summary>Bestiary order: the panel lists templates ascending by this, ties broken by <see cref="Id"/>.</summary>
    public int SortOrder { get; set; }

    /// <summary>This statblock's base counter definitions (one JSON column). The same generic
    /// <see cref="PartyCounter"/> a member uses; each <see cref="PartyCounter.Value"/> is the starting value
    /// an encounter instance is seeded with. See <see cref="PartyCounter"/>.</summary>
    public List<PartyCounter> Counters { get; set; } = [];
}
