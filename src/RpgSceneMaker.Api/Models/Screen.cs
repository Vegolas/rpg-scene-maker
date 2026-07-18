namespace RpgSceneMaker.Api.Models;

// Wire contract: the API serializes Screen/ScreenTile straight to /screens (there is no Contracts/ DTO). The
// panel mirrors this exact shape by hand in RpgSceneMaker.Ui/Contracts/ScreenContracts.cs (ScreenDto/
// ScreenTileDto) — keep the two in sync when a field changes.

/// <summary>
/// A named, user-arranged board that groups <em>shortcuts</em> to things that already exist — scenes,
/// events, sounds, a Spotify playlist/track, or the reset-lights command — onto one tap-friendly screen
/// (e.g. a "Fantasy" or "Horror" board). Purely organizational: a screen owns no light/music/sound
/// state of its own, and its tiles are still created and edited from their own tabs. Tapping a tile just
/// operates the underlying entity through its existing endpoint.
/// </summary>
public class Screen
{
    /// <summary>Slug id (matched case-insensitively, like scenes/events) used in <c>/screens/{id}</c> URLs.</summary>
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>The shortcuts on this board, in display order.</summary>
    public List<ScreenTile> Tiles { get; set; } = [];

    /// <summary>Stored file name of an optional full-art tile background (uploaded via <c>/images</c>), or null.</summary>
    public string? Image { get; set; }

    /// <summary>Display hint: when true the panel renders this board's tiles in a denser, smaller-tile layout
    /// so a board with many shortcuts fits more on screen. Purely cosmetic — it changes nothing about what the
    /// tiles do. Toggled from the board's Arrange page.</summary>
    public bool Compact { get; set; }
}

/// <summary>One shortcut on a <see cref="Screen"/>.</summary>
public class ScreenTile
{
    /// <summary>What the tile points at: <c>scene</c>, <c>event</c>, <c>sound</c>, <c>music</c>,
    /// <c>light-reset</c>, or <c>break</c> (a layout-only full-width line break, no target).</summary>
    public string Kind { get; set; } = "";

    /// <summary>The target: the entity id for <c>scene</c>/<c>event</c>/<c>sound</c>, a Spotify URI/link
    /// for <c>music</c>, and empty for <c>light-reset</c>/<c>break</c>.</summary>
    public string Ref { get; set; } = "";

    /// <summary>Display text. Required for <c>music</c> (there is no stored entity to read a name from);
    /// for the entity kinds the panel resolves the live name/emoji/colour and only falls back to this
    /// label when that entity no longer exists. For <c>break</c> it is the optional section heading.</summary>
    public string Label { get; set; } = "";
}
