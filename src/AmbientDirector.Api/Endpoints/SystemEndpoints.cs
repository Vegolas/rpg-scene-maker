using AmbientDirector.Api.Contracts;
using AmbientDirector.Api.Errors;
using AmbientDirector.Api.Models;
using AmbientDirector.Api.Services;
using AmbientDirector.Api.Services.Systems;
using AmbientDirector.Api.Validation;

namespace AmbientDirector.Api.Endpoints;

public static class SystemEndpoints
{
    public static void MapSystemEndpoints(this WebApplication app)
    {
        // The game-system setting (issue #127; docs/GAME-SYSTEMS.md): which IGameSystem drives the party/
        // bestiary/encounter layer's presets, quickbar and TV flavour. "/systems" is in IsProtectedPath;
        // like /screens there is deliberately nothing at the bare path and no GET /systems/{id}.
        var systems = app.MapGroup("/systems");

        systems.MapGet("/list", async (GameSystemRegistry registry, PartyStore store) =>
            new GameSystemsDto(
                [.. registry.All.Select(ToDto)],
                registry.Find(await store.GetSystemIdAsync())?.Id));

        // Select the active system (or "none" for explicitly no system — the stored sentinel that keeps the
        // startup upgrade from re-stamping daggerheart). GET+POST so a Stream Deck button can switch systems.
        // Selecting a system seeds its table counters FIRST (adopt-or-append; a validation failure — e.g. the
        // 8-counter cap — aborts before the choice is stored), then persists the id.
        systems.MapMethods("/current", EndpointHelpers.GetOrPost,
            async (string? id, HttpContext http, GameSystemRegistry registry, PartyStore store,
                LocaleService locales, TvState tvState, BoardStore boards) =>
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ValidationException("error.system.idRequired");

            id = id.Trim();
            if (string.Equals(id, GameSystemRegistry.None, StringComparison.OrdinalIgnoreCase))
            {
                // Clearing deletes nothing — the counters (and all game data) stay; only the nav gate reacts.
                await store.SaveSystemIdAsync(GameSystemRegistry.None);
                return Results.Ok(new { current = (string?)null });
            }

            var system = registry.Find(id)
                ?? throw new ValidationException("error.system.unknown", id);

            var lang = http.Request.Headers["X-Ui-Lang"].FirstOrDefault();
            if (await SeedTableCountersAsync(system, store, locales, lang))
                await PartyEndpoints.TouchIfPartyShownAsync(tvState, boards, heroChange: true);
            await store.SaveSystemIdAsync(system.Id);

            return Results.Ok(new { current = (string?)system.Id });
        });
    }

    // Seed the system's table-counter presets into the stored table counters, adopt-or-append (never
    // overwriting a value): same Key → already seeded, skip; same label (the preset's label in the panel's
    // language or in English — pre-#127 counters were created from localized UI presets) → adopt it by
    // stamping the key; otherwise append a fresh counter with the localized label. Returns whether anything
    // changed. Validation runs on the final list so seeding can never store what PUT /party/counters wouldn't.
    private static async Task<bool> SeedTableCountersAsync(
        IGameSystem system, PartyStore store, LocaleService locales, string? lang)
    {
        if (system.TableCounters.Count == 0) return false;

        var counters = await store.GetTableCountersAsync();
        var changed = false;
        foreach (var preset in system.TableCounters)
        {
            if (counters.Any(c => string.Equals(c.Key, preset.Key, StringComparison.OrdinalIgnoreCase)))
                continue;

            var label = locales.Localize(lang, preset.LabelKey);
            var adopt = PartyStore.FindCounter(counters, label)
                ?? PartyStore.FindCounter(counters, locales.Localize("en", preset.LabelKey));
            if (adopt is not null)
            {
                adopt.Key = preset.Key; // keep the GM's value/max/style — the key is all that was missing
            }
            else
            {
                counters.Add(new PartyCounter
                {
                    Key = preset.Key,
                    Label = label,
                    Value = preset.Value,
                    Max = preset.Max,
                    Style = preset.Style,
                });
            }
            changed = true;
        }

        if (!changed) return false;
        PartyValidation.ValidateCounters(counters);
        await store.SaveTableCountersAsync(counters);
        return true;
    }

    private static GameSystemDto ToDto(IGameSystem system) => new(
        system.Id,
        system.NameKey,
        [.. system.MemberCounters.Select(ToDto)],
        [.. system.EnemyCounters.Select(ToDto)],
        [.. system.TableCounters.Select(ToDto)],
        [.. system.Quickbar],
        system.SpotlightLabel);

    private static CounterPresetDto ToDto(CounterPreset preset) => new(
        preset.Key, preset.LabelKey, preset.Value, preset.Max, preset.Style, preset.Glyph, preset.Color);
}
