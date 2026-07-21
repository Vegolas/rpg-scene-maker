namespace AmbientDirector.Ui.Contracts;

// Mirrors the API's Board shapes (Models/Board.cs) — contracts are duplicated per project by design, so keep
// the two in sync by hand when a field changes. Geometry is percent-of-a-fixed-16:9-stage (X/Y 0–100, W/H
// 0.1–100); the Elements list order IS the z-order (index 0 = bottom). Text size is a percent of stage height
// (null → renderer default), align is left|center|right (null → left), colours are "#RRGGBB" (server-normalized).
public record BoardDto(string Id, string Name, string? BackgroundColor, string? BackgroundImage, List<BoardElementDto>? Elements);

public record BoardElementDto(string Kind, double X, double Y, double W, double H,
    string? Image, string? Text, string? Color, double? Size, string? Align);

// Mutable form model for editing one board in the panel; converts to the immutable wire DTO on save. Unlike a
// ScreenTile (added/removed/reordered as a whole record), a board element needs per-field editing — X/Y/W/H,
// text/colour/size/align — so each element gets its own mutable model (BoardElementEdit).
public class BoardEdit
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? BackgroundColor { get; set; }
    public string? BackgroundImage { get; set; }
    public List<BoardElementEdit> Elements { get; set; } = [];

    public static BoardEdit FromDto(BoardDto dto) => new()
    {
        Id = dto.Id,
        Name = dto.Name,
        BackgroundColor = dto.BackgroundColor,
        BackgroundImage = dto.BackgroundImage,
        Elements = [.. (dto.Elements ?? []).Select(BoardElementEdit.FromDto)],
    };

    public BoardDto ToDto() => new(Id, Name, BackgroundColor, BackgroundImage, [.. Elements.Select(e => e.ToDto())]);
}

public class BoardElementEdit
{
    public string Kind { get; set; } = "text";
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; }
    public double H { get; set; }
    public string? Image { get; set; }
    public string? Text { get; set; }
    public string? Color { get; set; }
    public double? Size { get; set; }
    public string? Align { get; set; }

    public static BoardElementEdit FromDto(BoardElementDto e) => new()
    {
        Kind = e.Kind, X = e.X, Y = e.Y, W = e.W, H = e.H,
        Image = e.Image, Text = e.Text, Color = e.Color, Size = e.Size, Align = e.Align,
    };

    public BoardElementDto ToDto() => new(Kind, X, Y, W, H, Image, Text, Color, Size, Align);
}

// One shared board → render-model mapper so a SINGLE component (BoardCanvas) draws boards everywhere. The TV
// gets its render model straight from the API (image refs already resolved to the open /tv/content/board/{name}
// routes, party element already carrying the live roster); the panel builds the very same TvBoardDto shape from
// a board it holds, mapping stored image file names through Api.ImageUrl (key-bearing /images/{name} urls) and
// attaching the party render model it fetched from /party/list (via PartyRender). Pass Api.ImageUrl as imageUrl,
// and the live party (built with PartyRender.ToRenderModel) so kind="party" elements draw the real roster.
public static class BoardRender
{
    public static TvBoardDto ToRenderModel(BoardDto board, Func<string?, string?> imageUrl, TvPartyDto? party = null) =>
        new(board.BackgroundColor,
            imageUrl(board.BackgroundImage),
            [.. (board.Elements ?? []).Select(e => new TvBoardElementDto(
                e.Kind, e.X, e.Y, e.W, e.H,
                // image element → a ready-to-fetch url; text element → text fields; party/enemies/fear element →
                // the live table model (fear reads its fear-keyed counter — issue #144); url null for all but
                // image (mirrors the API's TvBoardElementDto projection).
                e.Kind == "image" ? imageUrl(e.Image) : null,
                e.Kind == "text" ? e.Text : null,
                e.Kind == "text" ? e.Color : null,
                e.Kind == "text" ? e.Size : null,
                e.Kind == "text" ? e.Align : null,
                e.Kind is "party" or "enemies" or "fear" ? party : null))]);
}
