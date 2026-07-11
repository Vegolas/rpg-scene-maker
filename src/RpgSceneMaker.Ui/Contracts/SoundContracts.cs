namespace RpgSceneMaker.Ui.Contracts;

// Mirrors the API's sound contracts (contracts are duplicated per project by design — keep in sync by hand).
// Image is an optional full-art tile background. DurationMs is the file's natural length in ms; the
// server uses null (not yet measured) and 0 (tried, couldn't measure) as "unknown" sentinels — read it
// through NaturalMs, which folds both to null.
public record SoundDto(string Id, string Name, string Category, double Volume, bool Loop, string? Image = null, int? DurationMs = null)
{
    // The file's natural length in ms, or null when unknown (server sentinel: null or <= 0).
    public int? NaturalMs => DurationMs is { } d && d > 0 ? d : null;
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
