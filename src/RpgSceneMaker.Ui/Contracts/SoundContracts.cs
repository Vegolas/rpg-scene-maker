namespace RpgSceneMaker.Ui.Contracts;

// Mirrors the API's sound contracts (contracts are duplicated per project by design — keep in sync by hand).
public record SoundDto(string Id, string Name, string Category, double Volume, bool Loop);
public record SoundStateDto(List<string> Playing);

// Mutable form model for editing one sound in the panel.
public class SoundEdit
{
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public int VolumePercent { get; set; } = 100;
    public bool Loop { get; set; }
}
