namespace RpgSceneMaker.Ui.Contracts;

// Mirrors the API's event shapes (contracts are duplicated per project by design — keep in sync by hand).
public record EventDto(string Id, string Name, EventFlashDto? Flash, List<string>? SoundEffects);
public record EventFlashDto(string Color, int Brightness, int DurationMs);
public record EventTriggerDto(string Event, string Light, string Sound, bool FullySucceeded);

// Mutable form model for editing one event in the panel; converts to the wire DTO on save.
public class EventEdit
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool FlashEnabled { get; set; }
    public string FlashColor { get; set; } = "#ffffff";
    public int FlashBrightness { get; set; } = 100;
    public int FlashDurationMs { get; set; } = 200;
    public List<string> SoundEffects { get; set; } = [];

    public EventDto ToDto() => new(Id, Name,
        FlashEnabled ? new EventFlashDto(FlashColor, FlashBrightness, FlashDurationMs) : null,
        SoundEffects);
}
