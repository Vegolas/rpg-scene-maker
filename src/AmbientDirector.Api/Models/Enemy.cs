namespace AmbientDirector.Api.Models;

// Wire contract: the API serializes Enemy straight to /party (there is no Contracts/ DTO beyond the PartyDto
// envelope, like PartyMember/Board/Screen). The panel mirrors this exact shape by hand in
// AmbientDirector.Ui/Contracts/PartyContracts.cs — keep the two in sync when a field changes.

/// <summary>
/// One adversary in the current encounter whose live stats appear on the shared TV (issue #120), the enemy
/// half of the party domain. The twin of <see cref="PartyMember"/> — live table state adjusted mid-session and
/// rendered by a board's <c>kind="enemies"</c> element (never board state) — but for the opposing side. The
/// wire/route vocabulary is <em>"enemies"</em> (<c>GET /party/list</c> returns <c>enemies</c>, ids appear in
/// <c>/party/enemies/{id}/adjust</c> URLs).
/// </summary>
/// <remarks>
/// Like a member, an enemy's stats are generic <see cref="PartyCounter"/>s so any system fits: the Daggerheart
/// HP/Stress loadout is a <em>UI-side preset</em>, never hardcoded here. There is deliberately <b>no portrait</b>
/// in v1 — the design's enemy cards are text + counter tracks, so an <see cref="Enemy"/> carries no image
/// (unlike <see cref="PartyMember.Portrait"/>).
/// </remarks>
public class Enemy
{
    /// <summary>Slug id (matched case-insensitively, NOCASE collation, like members/scenes/boards), used in
    /// <c>PUT/DELETE /party/enemies/{id}</c> and in hand-typed <c>/party/enemies/{id}/adjust</c> URLs.</summary>
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>The Daggerheart-style "this one is acting / is the boss" highlight: when set, the TV renders this
    /// enemy's card with the red spotlight treatment (border + a "SPOTLIGHT" chip + a slightly larger name).</summary>
    public bool Spotlight { get; set; }

    /// <summary>Roster order: the panel and the TV render enemies ascending by this, ties broken by <see cref="Id"/>.</summary>
    public int SortOrder { get; set; }

    /// <summary>This enemy's tracked stats (one JSON column). The same generic <see cref="PartyCounter"/> a
    /// member uses. See <see cref="PartyCounter"/>.</summary>
    public List<PartyCounter> Counters { get; set; } = [];
}
