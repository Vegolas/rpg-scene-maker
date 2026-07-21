using AmbientDirector.Api.Errors;
using AmbientDirector.Api.Models;
using AmbientDirector.Api.Validation;
using Xunit;

namespace AmbientDirector.Tests.Unit;

/// <summary>The board element kind "party" (Phase 3 of #88): a live placeholder that carries geometry only —
/// validation must accept it and strip every content field, since the roster is fetched at render time.</summary>
public class BoardValidationPartyTests
{
    [Fact]
    public void Accepts_a_party_element_and_nulls_its_content_fields()
    {
        var board = new Board
        {
            Id = "roster",
            Name = "Roster",
            Elements =
            [
                new BoardElement
                {
                    Kind = "party", X = 10, Y = 10, W = 40, H = 80,
                    // Junk content fields that a party element must not carry — validation strips them.
                    Image = "x.png", Text = "hi", Color = "#ffffff", Size = 5, Align = "center",
                },
            ],
        };

        BoardValidation.Validate(board); // must not throw

        var element = board.Elements[0];
        Assert.Equal("party", element.Kind);
        Assert.Null(element.Image);
        Assert.Null(element.Text);
        Assert.Null(element.Color);
        Assert.Null(element.Size);
        Assert.Null(element.Align);
    }

    [Fact]
    public void Party_element_still_has_its_geometry_validated()
    {
        var board = new Board
        {
            Id = "roster",
            Name = "Roster",
            // X = 150 is out of the 0–100 stage range — the shared geometry guard runs before the kind switch.
            Elements = [new BoardElement { Kind = "party", X = 150, Y = 10, W = 40, H = 80 }],
        };

        var ex = Assert.ThrowsAny<ValidationException>(() => BoardValidation.Validate(board));
        Assert.Equal("error.board.elementBounds", ex.Code);
    }

    // ---- kind "enemies" (issue #120): the party element's twin — same geometry-only live-placeholder rules ----

    [Fact]
    public void Accepts_an_enemies_element_and_nulls_its_content_fields()
    {
        var board = new Board
        {
            Id = "encounter",
            Name = "Encounter",
            Elements =
            [
                new BoardElement
                {
                    Kind = "enemies", X = 70, Y = 2, W = 28, H = 96,
                    // Junk content fields an enemies element must not carry — validation strips them.
                    Image = "x.png", Text = "hi", Color = "#ffffff", Size = 5, Align = "center",
                },
            ],
        };

        BoardValidation.Validate(board); // must not throw

        var element = board.Elements[0];
        Assert.Equal("enemies", element.Kind);
        Assert.Null(element.Image);
        Assert.Null(element.Text);
        Assert.Null(element.Color);
        Assert.Null(element.Size);
        Assert.Null(element.Align);
    }

    // ---- kind "fear" (issue #144): the party element's other twin — same geometry-only live-placeholder rules,
    // renders the table's fear-keyed counter as a skull track ----

    [Fact]
    public void Accepts_a_fear_element_and_nulls_its_content_fields()
    {
        var board = new Board
        {
            Id = "dread",
            Name = "Dread",
            Elements =
            [
                new BoardElement
                {
                    Kind = "fear", X = 25, Y = 4, W = 50, H = 13,
                    // Junk content fields a fear element must not carry — validation strips them.
                    Image = "x.png", Text = "hi", Color = "#ffffff", Size = 5, Align = "center",
                },
            ],
        };

        BoardValidation.Validate(board); // must not throw

        var element = board.Elements[0];
        Assert.Equal("fear", element.Kind);
        Assert.Null(element.Image);
        Assert.Null(element.Text);
        Assert.Null(element.Color);
        Assert.Null(element.Size);
        Assert.Null(element.Align);
    }

    [Fact]
    public void Still_rejects_an_unknown_kind()
    {
        var board = new Board
        {
            Id = "encounter",
            Name = "Encounter",
            Elements = [new BoardElement { Kind = "monsters", X = 10, Y = 10, W = 40, H = 80 }],
        };

        var ex = Assert.ThrowsAny<ValidationException>(() => BoardValidation.Validate(board));
        Assert.Equal("error.board.unknownKind", ex.Code);
    }
}
