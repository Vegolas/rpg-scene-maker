namespace AmbientDirector.Api.Models;

// Wire contract: the API serializes Board/BoardElement straight to /boards (there is no Contracts/ DTO, like
// Screen). The panel mirrors this exact shape by hand in AmbientDirector.Ui/Contracts/BoardContracts.cs —
// keep the two in sync when a field changes.

/// <summary>
/// Persisted, composable player-facing TV content: a 16:9 layout (a background colour or image plus
/// positioned image/text/party/enemies/fear elements) the GM pushes to the key-free <c>/tv</c> display.
/// Distinct from a <see cref="Screen"/> (a panel-side shortcut-launcher grid): a board owns no
/// light/music/sound state, it is just what appears on the shared table screen.
/// </summary>
/// <remarks>
/// All element coordinates are <em>percentages of a fixed 16:9 stage</em>, never pixels — the renderer scales
/// the stage to whatever TV is showing it (letterboxing as needed), so there is no per-resolution math
/// anywhere. The <see cref="Elements"/> list order <em>is</em> the z-order: index 0 draws at the bottom and
/// later elements paint on top; there is deliberately no Z field.
/// </remarks>
public class Board
{
    /// <summary>Slug id (matched case-insensitively, like scenes/screens), used in <c>PUT/DELETE /boards/{id}</c>
    /// and in hand-typed <c>/tv/show?board=</c> URLs.</summary>
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>Stage background colour as normalized <c>"#RRGGBB"</c>, or null to fall back to the renderer
    /// default (black).</summary>
    public string? BackgroundColor { get; set; }

    /// <summary>Stored image file name of a full-stage background (uploaded via <c>/images</c>, resolved
    /// through <see cref="Services.ImageFileStorage"/>), or null. Never a path or URL.</summary>
    public string? BackgroundImage { get; set; }

    /// <summary>The board's elements, in paint order (index 0 = bottom, later = on top — see the class
    /// remarks: the list order IS the z-order).</summary>
    public List<BoardElement> Elements { get; set; } = [];

    /// <summary>Stored image file names this board references (the background plus every image element),
    /// skipping null/empty. The key-free TV gate serves exactly this set while the board is shown, and the
    /// upsert/delete cleanup diffs it to drop images that are no longer referenced.</summary>
    public IEnumerable<string> ReferencedFiles()
    {
        if (!string.IsNullOrEmpty(BackgroundImage)) yield return BackgroundImage;
        foreach (var element in Elements ?? [])
            if (!string.IsNullOrEmpty(element.Image)) yield return element.Image!;
    }
}

/// <summary>One positioned element on a <see cref="Board"/>. Coordinates and sizes are percentages of the
/// fixed 16:9 stage (see <see cref="Board"/>'s remarks).</summary>
public class BoardElement
{
    /// <summary>What this element is: <c>image</c>, <c>text</c>, <c>party</c>, <c>enemies</c> or <c>fear</c>.
    /// The last three are live placeholders that render live table data from <see cref="Services.PartyStore"/>
    /// at TV-render time — carrying geometry only, no stored content fields: <c>party</c> the player roster,
    /// <c>enemies</c> the bestiary, and <c>fear</c> the table's fear-keyed counter as a 12-slot skull track
    /// (issue #144).</summary>
    public string Kind { get; set; } = "";

    /// <summary>Left edge, as a percent of stage width (0–100).</summary>
    public double X { get; set; }

    /// <summary>Top edge, as a percent of stage height (0–100).</summary>
    public double Y { get; set; }

    /// <summary>Width, as a percent of stage width (0.1–100).</summary>
    public double W { get; set; }

    /// <summary>Height, as a percent of stage height (0.1–100).</summary>
    public double H { get; set; }

    /// <summary>kind=image: the stored image file name (resolved through <see cref="Services.ImageFileStorage"/>).</summary>
    public string? Image { get; set; }

    /// <summary>kind=text: the text content (newlines allowed — the renderer treats it as pre-wrap).</summary>
    public string? Text { get; set; }

    /// <summary>kind=text: text colour as normalized <c>"#RRGGBB"</c>, or null for the renderer default.</summary>
    public string? Color { get; set; }

    /// <summary>kind=text: font size as a percent of stage height (1–100), or null for the renderer default.</summary>
    public double? Size { get; set; }

    /// <summary>kind=text: horizontal alignment — <c>left</c>, <c>center</c> or <c>right</c>; null = left.</summary>
    public string? Align { get; set; }
}
