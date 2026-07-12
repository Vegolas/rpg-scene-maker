namespace RpgSceneMaker.Ui.Contracts;

// Mirrors the API's screen shapes (contracts are duplicated per project by design — keep in sync by hand).
public record ScreenDto(string Id, string Name, List<ScreenTileDto>? Tiles, string? Image, bool Compact = false);
public record ScreenTileDto(string Kind, string Ref, string Label);

// Mutable form model for arranging one screen (board) in the panel; converts to the wire DTO on save.
// Tiles are added/removed/reordered as whole records — a tile's fields never change once created — so the
// immutable ScreenTileDto is enough and no per-tile edit model is needed.
public class ScreenEdit
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<ScreenTileDto> Tiles { get; set; } = [];
    public string? Image { get; set; }

    // Denser tile layout for boards with many shortcuts; toggled on the Arrange page (see Screen.razor).
    public bool Compact { get; set; }

    public ScreenDto ToDto() => new(Id, Name, Tiles, Image, Compact);
}
