namespace AmbientDirector.Ui.Contracts;

// Mirrors the API's sound contracts (contracts are duplicated per project by design — keep in sync by hand).
// Image is an optional full-art tile background. DurationMs is the file's natural length in ms; the
// server uses null (not yet measured) and 0 (tried, couldn't measure) as "unknown" sentinels — read it
// through NaturalMs, which folds both to null. Waveform is a compact amplitude preview (peaks 0–255,
// base64 over the wire) drawn on timeline sound clips; null/empty when not measured or undecodable.
// Author/License/LicenseUrl/SourceUrl are attribution for a library-imported sound (all null for a plain
// upload); they are read-only (set only at import) and surfaced on the sound editor.
public record SoundDto(string Id, string Name, string Category, double Volume, bool Loop, string? Image = null, int? DurationMs = null, byte[]? Waveform = null,
    string? Author = null, string? License = null, string? LicenseUrl = null, string? SourceUrl = null)
{
    // The file's natural length in ms, or null when unknown (server sentinel: null or <= 0).
    public int? NaturalMs => DurationMs is { } d && d > 0 ? d : null;

    // Waveform peaks (0–255) when measured and decodable, else null (never measured or empty sentinel).
    public byte[]? Peaks => Waveform is { Length: > 0 } w ? w : null;

    // Whether this sound carries library attribution worth surfacing (author and/or license present).
    public bool HasAttribution => !string.IsNullOrWhiteSpace(Author) || !string.IsNullOrWhiteSpace(License);
}
public record SoundStateDto(List<string> Playing);

// Mutable form model for editing one sound in the panel.
public class SoundEdit
{
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public int VolumePercent { get; set; } = 100;
    public bool Loop { get; set; }
    public string? Image { get; set; }
}
