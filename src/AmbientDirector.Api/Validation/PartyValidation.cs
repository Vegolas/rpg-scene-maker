using AmbientDirector.Api.Errors;
using AmbientDirector.Api.Models;
using AmbientDirector.Api.Services;

namespace AmbientDirector.Api.Validation;

/// <summary>Guards a party member (and the table-level counter list) coming from the editor before it reaches
/// the store; failures map to HTTP 400. Counter problems report a 1-based position so the GM can find the
/// offending row. <see cref="PartyCounter.Value"/> is <em>silently clamped</em> into range — a normalization,
/// not an error — in the same spirit as <see cref="BoardValidation"/> normalizing hex/alignment in place.</summary>
public static class PartyValidation
{
    // More counters than this stops being readable on a TV, and bounds the stored JSON.
    private const int MaxCounters = 8;

    // The longest a single label may be — enough for "Armor"/"Stress", bounded so one row can't balloon the
    // JSON. The label doubles as the adjust key, so it stays short.
    private const int MaxLabelLength = 40;

    // A counter's value/max ceiling: high enough for any sane tracker, low enough that clamping stays meaningful.
    private const int MaxCounterMax = 999;

    // "pips" draws one dot per unit, so its max must stay small enough for a TV to render as dots (999 can't).
    private const int MaxPips = 24;

    private static readonly HashSet<string> Styles = new(StringComparer.Ordinal) { "pips", "number" };

    public static void Validate(PartyMember m)
    {
        if (string.IsNullOrWhiteSpace(m.Id))
            throw new ValidationException("error.common.idRequired");
        if (!LightValidation.IsSlug(m.Id))
            throw new ValidationException("error.common.idSlug");
        if (string.IsNullOrWhiteSpace(m.Name))
            throw new ValidationException("error.common.nameRequired");
        if (m.Portrait is not null && !ImageFileStorage.IsValidName(m.Portrait))
            throw new ValidationException("error.common.invalidImage");

        // JSON "counters": null overwrites the C# default.
        ValidateCounters(m.Counters ??= []);
    }

    /// <summary>Validate + normalize a counter list in place — shared by a member and the table-level counters
    /// endpoint. Labels must be present, short and unique (they are the case-insensitive adjust key); style is
    /// null/"pips"/"number"; max is null or 1–999 (required and ≤24 for pips); value is clamped into
    /// <c>[0, max ?? 999]</c>.</summary>
    public static void ValidateCounters(List<PartyCounter> counters)
    {
        counters ??= [];
        if (counters.Count > MaxCounters)
            throw new ValidationException("error.party.tooManyCounters", MaxCounters);

        for (var i = 0; i < counters.Count; i++)
        {
            var counter = counters[i];
            var pos = i + 1; // 1-based position for the message

            if (string.IsNullOrWhiteSpace(counter.Label))
                throw new ValidationException("error.party.counterLabel", pos);
            counter.Label = counter.Label.Trim();
            if (counter.Label.Length > MaxLabelLength)
                throw new ValidationException("error.party.counterLabelLength", MaxLabelLength, pos);

            // Labels are the adjust key, so they must be unique within the owner (case-insensitive).
            for (var j = 0; j < i; j++)
                if (string.Equals(counters[j].Label, counter.Label, StringComparison.OrdinalIgnoreCase))
                    throw new ValidationException("error.party.duplicateCounter", counter.Label, pos);

            if (counter.Style is not null)
            {
                // Normalize the accepted value back onto the model (like BoardValidation lower-cases align).
                counter.Style = counter.Style.Trim().ToLowerInvariant();
                if (!Styles.Contains(counter.Style))
                    throw new ValidationException("error.party.counterStyle", pos);
            }

            if (counter.Max is { } max && (max < 1 || max > MaxCounterMax))
                throw new ValidationException("error.party.counterMax", pos);

            // "pips" draws a dot per unit, so it NEEDS a bounded, TV-renderable max.
            if (counter.Style == "pips" && (counter.Max is not { } pipsMax || pipsMax > MaxPips))
                throw new ValidationException("error.party.pipsMax", pos);

            // Value is normalized, not rejected: clamp it into the counter's range (mirrors BoardValidation's
            // in-place hex/alignment normalization). An unbounded max defaults to the shared ceiling.
            counter.Value = Math.Clamp(counter.Value, 0, counter.Max ?? MaxCounterMax);
        }
    }
}
