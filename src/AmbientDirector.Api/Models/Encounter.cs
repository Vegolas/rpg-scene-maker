namespace AmbientDirector.Api.Models;

// Wire contract: the API serializes Encounter/EncounterEnemy straight to /encounters (there is no Contracts/
// DTO, like Board/Screen/PartyMember). The panel mirrors this exact shape by hand in
// AmbientDirector.Ui/Contracts/EncounterContracts.cs — keep the two in sync when a field changes.

/// <summary>
/// A prepped fight the GM builds ahead of time and "runs" at the table (issue #122): a chosen subset of the
/// party (<see cref="HeroIds"/>) versus a set of enemy <see cref="Enemies">instances</see>, over a background
/// image, optionally activating a scene and/or event on run. Running it drives the player-facing <c>/tv</c>
/// display in a standard heroes-left / enemies-right / background layout (synthesized server-side into the
/// board render model — see <see cref="Services.TvState"/> and <c>TvEndpoints</c>, reusing <c>BoardCanvas</c>).
/// </summary>
/// <remarks>
/// The split between base and live stats is the whole point: heroes keep their <b>persistent</b> counters on
/// the party (a PC's HP is the PC's, across fights), so an encounter references them by id only; enemy stats
/// are <b>per-fight</b>, so each <see cref="EncounterEnemy"/> instance carries its own live counters (two
/// encounters running the same statblock never share HP). An empty <see cref="HeroIds"/> means "all current
/// players". Only the <see cref="BackgroundImage"/> is an owned upload (captured-old / delete-on-replace like a
/// <see cref="Board"/>); instance portraits are snapshots of the template's file, not separately owned.
/// </remarks>
public class Encounter
{
    /// <summary>Slug id (matched case-insensitively, NOCASE collation, like scenes/boards), used in
    /// <c>PUT/DELETE /encounters/{id}</c> and in hand-typed <c>/tv/show?encounter=</c> / <c>/encounters/{id}/run</c> URLs.</summary>
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>List order: the panel lists encounters ascending by this, ties broken by <see cref="Id"/>.</summary>
    public int SortOrder { get; set; }

    /// <summary>The party members fighting in this encounter, by <see cref="PartyMember.Id"/>. Empty means "all
    /// current players" (the TV resolves ids against the live roster at render time — a since-deleted id just
    /// drops out).</summary>
    public List<string> HeroIds { get; set; } = [];

    /// <summary>The enemy instances in this fight, each tracked independently (one JSON column). See
    /// <see cref="EncounterEnemy"/>.</summary>
    public List<EncounterEnemy> Enemies { get; set; } = [];

    /// <summary>Stored image file name of the fight's background (uploaded via <c>/images</c>, resolved through
    /// <see cref="Services.ImageFileStorage"/>), or null. Never a path or URL — the encounter <b>owns</b> this
    /// file (captured-old / delete-on-replace like <see cref="Board.BackgroundImage"/>).</summary>
    public string? BackgroundImage { get; set; }

    /// <summary>Optional scene id to activate (best-effort) when the encounter is run, or null.</summary>
    public string? ActivateSceneId { get; set; }

    /// <summary>Optional event id to trigger (best-effort) when the encounter is run, or null.</summary>
    public string? ActivateEventId { get; set; }

    /// <summary>Stored image file names this encounter <b>owns</b> — just the background (instance portraits are
    /// snapshots of the bestiary template's file, owned there, so they are deliberately excluded). The
    /// upsert/delete cleanup diffs this set to drop images that are no longer referenced, mirroring
    /// <see cref="Board.ReferencedFiles"/>.</summary>
    public IEnumerable<string> ReferencedFiles()
    {
        if (!string.IsNullOrEmpty(BackgroundImage)) yield return BackgroundImage;
    }

    /// <summary>Every stored image file name this encounter <b>renders</b> on the TV — the background plus each
    /// hero's and each enemy instance's portrait — for the key-free TV gate (served only while this encounter is
    /// shown). Hero portraits are supplied by the caller (they live on the live party, not the encounter). The
    /// membership check runs before any disk access, exactly like the party-board portrait gate.</summary>
    public IEnumerable<string> PortraitFiles(IEnumerable<string> heroPortraits)
    {
        if (!string.IsNullOrEmpty(BackgroundImage)) yield return BackgroundImage;
        foreach (var portrait in heroPortraits)
            if (!string.IsNullOrEmpty(portrait)) yield return portrait;
        foreach (var enemy in Enemies ?? [])
            if (!string.IsNullOrEmpty(enemy.Portrait)) yield return enemy.Portrait!;
    }
}

/// <summary>One enemy <em>instance</em> in an <see cref="Encounter"/> (JSON in the encounter's Enemies column):
/// a live copy of a bestiary <see cref="Enemy"/> statblock with its own tracked counters, so multiple instances
/// of the same template each track their own HP/Stress. Seeded from the template when added; decoupled
/// afterwards (editing the template does not change a live instance).</summary>
public class EncounterEnemy
{
    /// <summary>Unique id within the owning encounter (the panel generates it; used in
    /// <c>/encounters/{id}/enemies/{instanceId}/adjust</c> URLs). Not global — two encounters may reuse the
    /// same instance id.</summary>
    public string InstanceId { get; set; } = "";

    /// <summary>The bestiary <see cref="Enemy.Id"/> this instance was seeded from. Kept for reference (e.g. the
    /// panel can re-seed on "reset"); a since-deleted template just means the instance stands alone.</summary>
    public string EnemyId { get; set; } = "";

    /// <summary>Display name (editable): defaults to the template's name, auto-suffixed "Goblin 2/3…" when the
    /// same template is instanced more than once in one encounter.</summary>
    public string Name { get; set; } = "";

    /// <summary>Stored image file name copied from the template's portrait at add-time (a snapshot, not
    /// separately owned — the template owns the file). Null when the template had none.</summary>
    public string? Portrait { get; set; }

    /// <summary>The Daggerheart-style "this one is acting / is the boss" highlight, <b>per instance</b> (moved
    /// off the template): when set, the TV renders this enemy's card with the red spotlight treatment.</summary>
    public bool Spotlight { get; set; }

    /// <summary>This instance's live counters, seeded from the template at add-time. The generic
    /// <see cref="PartyCounter"/>; adjusted mid-fight via <c>/encounters/{id}/enemies/{instanceId}/adjust</c>.</summary>
    public List<PartyCounter> Counters { get; set; } = [];
}
