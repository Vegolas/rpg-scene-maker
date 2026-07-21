namespace AmbientDirector.Ui.Contracts;

// Mirrors the API's Encounter/EncounterEnemy shapes (Models/Encounter.cs) — DTOs are duplicated per project by
// design, so keep the two in sync by hand when a field changes. An encounter is a prepped fight: chosen heroes
// (party member ids; empty = all players), enemy instances (each with its own live counters), a background
// image, and an optional scene + event to activate on run.
public record EncounterDto(string Id, string Name, int SortOrder, List<string>? HeroIds,
    List<EncounterEnemyDto>? Enemies, string? BackgroundImage, string? ActivateSceneId, string? ActivateEventId);

// One enemy instance in an encounter: a live copy of a bestiary statblock. InstanceId is unique within the
// encounter; EnemyId references the bestiary template it was seeded from; Name is editable (auto-suffixed for
// duplicates); Portrait is a snapshot of the template's; Spotlight is the per-instance boss flag; Hidden
// (default false) holds a prepped enemy back from the TV without deleting it (the panel shows it as a "Visible"
// toggle — stored inverted so pre-existing data stays shown); Counters are the live tracked values.
public record EncounterEnemyDto(string InstanceId, string EnemyId, string Name, string? Portrait, bool Spotlight,
    bool Hidden, List<PartyCounterDto>? Counters);

// Mutable form model for editing one encounter in the panel; converts to the immutable wire DTO on save (the
// BoardEdit pattern).
public class EncounterEdit
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
    public List<string> HeroIds { get; set; } = [];
    public List<EncounterEnemyEdit> Enemies { get; set; } = [];
    public string? BackgroundImage { get; set; }
    public string? ActivateSceneId { get; set; }
    public string? ActivateEventId { get; set; }

    public static EncounterEdit FromDto(EncounterDto dto) => new()
    {
        Id = dto.Id,
        Name = dto.Name,
        SortOrder = dto.SortOrder,
        HeroIds = [.. dto.HeroIds ?? []],
        Enemies = [.. (dto.Enemies ?? []).Select(EncounterEnemyEdit.FromDto)],
        BackgroundImage = dto.BackgroundImage,
        ActivateSceneId = dto.ActivateSceneId,
        ActivateEventId = dto.ActivateEventId,
    };

    public EncounterDto ToDto() => new(Id, Name.Trim(), SortOrder, [.. HeroIds],
        [.. Enemies.Select(e => e.ToDto())], BackgroundImage, ActivateSceneId, ActivateEventId);
}

// The /encounters/{id}/run response: the new TV revision plus best-effort per-part statuses ("ok" / "skipped"
// / "notFound" / "partial" / "error:<code>") so the panel can toast what actually fired.
public record EncounterRunResult(long Rev, string Encounter, string Scene, string Event);

public class EncounterEnemyEdit
{
    public string InstanceId { get; set; } = "";
    public string EnemyId { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Portrait { get; set; }
    public bool Spotlight { get; set; }
    public bool Hidden { get; set; }
    public List<CounterEdit> Counters { get; set; } = [];

    public static EncounterEnemyEdit FromDto(EncounterEnemyDto dto) => new()
    {
        InstanceId = dto.InstanceId,
        EnemyId = dto.EnemyId,
        Name = dto.Name,
        Portrait = dto.Portrait,
        Spotlight = dto.Spotlight,
        Hidden = dto.Hidden,
        Counters = [.. (dto.Counters ?? []).Select(CounterEdit.FromDto)],
    };

    public EncounterEnemyDto ToDto() => new(InstanceId, EnemyId, Name.Trim(), Portrait, Spotlight, Hidden,
        [.. Counters.Select(c => c.ToDto())]);
}
