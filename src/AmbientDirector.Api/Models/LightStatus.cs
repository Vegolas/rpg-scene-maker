namespace AmbientDirector.Api.Models;

/// <summary>
/// A normalized snapshot of the light provider's live state (best-effort — a field is null when the
/// provider doesn't report it), plus the raw provider payload for diagnostics. Both Tuya and Hue map
/// their very different native state onto this shape so the panel can reflect the real bulb state.
/// </summary>
public sealed record LightStatus
{
    /// <summary>Whether the light is currently on (null when unknown).</summary>
    public bool? On { get; init; }

    /// <summary>Normalized mode: "colour" or "white" (null when unknown).</summary>
    public string? Mode { get; init; }

    /// <summary>Brightness percent 0-100 (null when unknown).</summary>
    public int? Brightness { get; init; }

    /// <summary>Current colour as RRGGBB hex (no leading '#') when in colour mode, else null. Reported at
    /// full value — the intensity lives in <see cref="Brightness"/> — so it matches the mood swatches.</summary>
    public string? Color { get; init; }

    /// <summary>White temperature percent, 0 (warm) - 100 (cold), when in white mode, else null.</summary>
    public int? Temperature { get; init; }

    /// <summary>The provider's raw current state (Tuya DP map / Hue lights JSON), for diagnostics.</summary>
    public object? Raw { get; init; }
}
