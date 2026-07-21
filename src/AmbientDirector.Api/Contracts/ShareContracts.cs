namespace AmbientDirector.Api.Contracts;

// Wire DTOs for the share import flow. The panel mirrors these by hand in
// AmbientDirector.Ui/Contracts/ShareContracts.cs — keep the two in sync.

/// <summary>What a just-uploaded pack contains, returned by <c>POST /share/import/inspect</c> without committing
/// anything. The <see cref="TempId"/> is handed back to <c>commit</c> to apply the import.</summary>
public sealed record ShareInspectResult(
    string TempId,
    string PrimaryKind,
    string PrimaryId,
    Dictionary<string, int> Counts,
    List<ShareCollision> Collisions,
    List<ShareLightKeyDto> LightKeys,
    int MediaCount,
    List<string> MediaMissing,
    List<ShareEntityIssue> Issues);

/// <summary>One bundled entity whose id already exists locally — on the default <c>copy</c> policy it will be
/// imported under a fresh id.</summary>
public sealed record ShareCollision(string Kind, string Id, string Name);

/// <summary>A bundled entity that doesn't currently pass validation (a heads-up in the preview): the localized
/// <see cref="Problem"/> says why. Import repairs what it can and rejects the rest.</summary>
public sealed record ShareEntityIssue(string Kind, string Id, string Name, string Problem);

/// <summary>A distinct source light key in the pack, with the owner labels that used it, for the remap UI.</summary>
public sealed record ShareLightKeyDto(string Key, List<string> Sources);

/// <summary>The commit request: the temp id from inspect, the chosen light-key mapping (source key → target
/// registered light key, or null/empty/"skip" to drop that binding), and an optional collision policy
/// (<c>copy</c> (default) / <c>overwrite</c> / <c>skip</c>).</summary>
public sealed record ShareCommitInput(
    string TempId,
    Dictionary<string, string?>? LightKeys,
    string? CollisionPolicy);

/// <summary>The result of a commit: created ids per kind, how many media files were recreated, the ids that had
/// to be changed to avoid a collision (so the UI can say "imported as 'tavern-2'"), any fields repaired so the
/// pack could import, and the primary entity's kind + new id so the panel can open it for review.</summary>
public sealed record ShareCommitResult(
    Dictionary<string, List<string>> Created,
    int MediaImported,
    List<ShareRemap> Remapped,
    List<ShareRepairNote> Repaired,
    string PrimaryKind,
    string? PrimaryId);

public sealed record ShareRemap(string Kind, string OldId, string NewId);

/// <summary>A fix applied on import so an entity would validate (e.g. a placeholder music link cleared); the
/// localized <see cref="Note"/> tells the GM what to finish in the editor.</summary>
public sealed record ShareRepairNote(string Kind, string Id, string Note);
