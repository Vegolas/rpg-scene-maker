namespace RpgSceneMaker.Api.Contracts;

/// <summary>Wire shape for a sound-effect library entry.</summary>
public record SoundDto(string Id, string Name, string Category, double Volume, bool Loop);

/// <summary>Editable fields for a sound; each null field is left unchanged (partial update).</summary>
public record SoundUpdateInput(string? Name, string? Category, double? Volume, bool? Loop);

/// <summary>Ids of the sounds currently playing on the server, for the panel's live highlight.</summary>
public record SoundStateDto(IReadOnlyList<string> Playing);
