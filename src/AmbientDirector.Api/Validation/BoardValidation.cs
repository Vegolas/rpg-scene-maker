using AmbientDirector.Api.Errors;
using AmbientDirector.Api.Models;
using AmbientDirector.Api.Services;

namespace AmbientDirector.Api.Validation;

/// <summary>Guards a board coming from the editor before it reaches the store; failures map to HTTP 400.
/// Element problems report a 1-based position so the GM can find the offending element.</summary>
public static class BoardValidation
{
    // A board with more than this many elements stops being a readable slide; also bounds the JSON payload.
    private const int MaxElements = 50;

    // The longest a single text element may be — enough for a handout paragraph, bounded so one element can't
    // balloon the stored JSON.
    private const int MaxTextLength = 2000;

    // The element kinds the renderer knows how to draw. "party", "enemies" and "fear" carry geometry only — they
    // render live table data (loaded at TV-render time from PartyStore), not stored board state: the roster, the
    // bestiary, and the fear-keyed table counter (as a skull track — issue #144) respectively.
    private static readonly HashSet<string> Kinds = new(StringComparer.Ordinal) { "image", "text", "party", "enemies", "fear" };

    private static readonly HashSet<string> Aligns = new(StringComparer.Ordinal) { "left", "center", "right" };

    public static void Validate(Board board)
    {
        if (string.IsNullOrWhiteSpace(board.Id))
            throw new ValidationException("error.common.idRequired");
        if (!LightValidation.IsSlug(board.Id))
            throw new ValidationException("error.common.idSlug");
        if (string.IsNullOrWhiteSpace(board.Name))
            throw new ValidationException("error.common.nameRequired");

        if (board.BackgroundColor is not null)
            board.BackgroundColor = LightValidation.NormalizeHex(board.BackgroundColor); // throws error.common.hexColor
        if (board.BackgroundImage is not null && !ImageFileStorage.IsValidName(board.BackgroundImage))
            throw new ValidationException("error.common.invalidImage");

        // JSON "elements": null overwrites the C# default.
        board.Elements ??= [];
        if (board.Elements.Count > MaxElements)
            throw new ValidationException("error.board.tooManyElements", MaxElements);

        for (var i = 0; i < board.Elements.Count; i++)
        {
            var element = board.Elements[i];
            var pos = i + 1; // 1-based position for the message

            if (!Kinds.Contains(element.Kind))
                throw new ValidationException("error.board.unknownKind", element.Kind, pos, string.Join(", ", Kinds));

            // Percent-of-stage geometry: X/Y anchor the top-left corner (0–100), W/H the size (0.1–100).
            // NaN/Infinity would slip past a bare comparison, so require finiteness explicitly.
            if (!InRange(element.X, 0, 100) || !InRange(element.Y, 0, 100) ||
                !InRange(element.W, 0.1, 100) || !InRange(element.H, 0.1, 100))
                throw new ValidationException("error.board.elementBounds", pos);

            switch (element.Kind)
            {
                case "image":
                    if (string.IsNullOrEmpty(element.Image) || !ImageFileStorage.IsValidName(element.Image))
                        throw new ValidationException("error.board.elementImage", pos);
                    // Normalise away any stray text-only fields (like ScreenValidation clears a command tile's ref).
                    element.Text = null;
                    element.Color = null;
                    element.Size = null;
                    element.Align = null;
                    break;

                case "text":
                    if (string.IsNullOrWhiteSpace(element.Text))
                        throw new ValidationException("error.board.textRequired", pos);
                    if (element.Text.Length > MaxTextLength)
                        throw new ValidationException("error.board.textTooLong", MaxTextLength, pos);
                    if (element.Color is not null)
                        element.Color = LightValidation.NormalizeHex(element.Color); // throws error.common.hexColor
                    if (element.Size is { } size && !InRange(size, 1, 100))
                        throw new ValidationException("error.board.textSize", pos);
                    if (element.Align is not null)
                    {
                        element.Align = element.Align.Trim().ToLowerInvariant();
                        if (!Aligns.Contains(element.Align))
                            throw new ValidationException("error.board.textAlign", pos);
                    }
                    // A text element carries no image.
                    element.Image = null;
                    break;

                case "party":
                case "enemies":
                case "fear":
                    // A party/enemies/fear element is a live placeholder: it carries geometry only (validated
                    // above), and its live table data (the roster, the bestiary, or the fear-keyed table counter)
                    // is fetched at render time — not board state. Null out every content field so nothing stale
                    // is stored — like the image arm clears the text-only fields.
                    element.Image = null;
                    element.Text = null;
                    element.Color = null;
                    element.Size = null;
                    element.Align = null;
                    break;
            }
        }
    }

    // Finite (rejects NaN/Infinity) and within the inclusive range.
    private static bool InRange(double value, double min, double max) =>
        double.IsFinite(value) && value >= min && value <= max;
}
