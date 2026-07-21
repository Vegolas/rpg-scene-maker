namespace AmbientDirector.Ui.Contracts;

// Hand-mirrored from AmbientDirector.Api/Contracts/ShareContracts.cs (no shared contracts project) —
// keep the two in sync. The share import flow: inspect an uploaded pack, then commit with a light mapping.

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

public sealed record ShareCollision(string Kind, string Id, string Name);

public sealed record ShareEntityIssue(string Kind, string Id, string Name, string Problem);

public sealed record ShareLightKeyDto(string Key, List<string> Sources);

public sealed record ShareCommitInput(
    string TempId,
    Dictionary<string, string?>? LightKeys,
    string? CollisionPolicy);

public sealed record ShareCommitResult(
    Dictionary<string, List<string>> Created,
    int MediaImported,
    List<ShareRemap> Remapped,
    List<ShareRepairNote> Repaired,
    string PrimaryKind,
    string? PrimaryId);

public sealed record ShareRemap(string Kind, string OldId, string NewId);

public sealed record ShareRepairNote(string Kind, string Id, string Note);
