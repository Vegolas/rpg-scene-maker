using AmbientDirector.Api.Errors;
using AmbientDirector.Api.Models;
using AmbientDirector.Api.Services;

namespace AmbientDirector.Api.Validation;

/// <summary>Guards an encounter (issue #122) coming from the editor before it reaches the store; failures map
/// to HTTP 400. Hero/instance problems report a 1-based position so the GM can find the offending row. The
/// shared <see cref="PartyValidation.ValidateCounters"/> guards each instance's live counters (same limits as a
/// member/enemy), and empty scene/event/background refs are normalized to null in place (like
/// <see cref="BoardValidation"/> normalizing hex/alignment).</summary>
public static class EncounterValidation
{
    // An encounter with more instances than this stops being readable on a TV; also bounds the stored JSON.
    private const int MaxEnemies = 50;

    public static void Validate(Encounter encounter)
    {
        if (string.IsNullOrWhiteSpace(encounter.Id))
            throw new ValidationException("error.common.idRequired");
        if (!LightValidation.IsSlug(encounter.Id))
            throw new ValidationException("error.common.idSlug");
        if (string.IsNullOrWhiteSpace(encounter.Name))
            throw new ValidationException("error.common.nameRequired");

        // Heroes: a subset of the party by member id (a slug). An empty list is valid — it means "all players".
        encounter.HeroIds ??= [];
        for (var i = 0; i < encounter.HeroIds.Count; i++)
        {
            var heroId = encounter.HeroIds[i]?.Trim() ?? "";
            if (string.IsNullOrEmpty(heroId) || !LightValidation.IsSlug(heroId))
                throw new ValidationException("error.encounter.heroId", i + 1);
            encounter.HeroIds[i] = heroId; // normalize (trimmed) in place
        }

        // Enemy instances.
        encounter.Enemies ??= [];
        if (encounter.Enemies.Count > MaxEnemies)
            throw new ValidationException("error.encounter.tooManyEnemies", MaxEnemies);

        var seenInstanceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < encounter.Enemies.Count; i++)
        {
            var instance = encounter.Enemies[i];
            var pos = i + 1; // 1-based position for the message

            if (string.IsNullOrWhiteSpace(instance.InstanceId))
                throw new ValidationException("error.encounter.instanceId", pos);
            instance.InstanceId = instance.InstanceId.Trim();
            if (!seenInstanceIds.Add(instance.InstanceId))
                throw new ValidationException("error.encounter.duplicateInstance", pos);

            if (string.IsNullOrWhiteSpace(instance.EnemyId))
                throw new ValidationException("error.encounter.enemyId", pos);
            if (string.IsNullOrWhiteSpace(instance.Name))
                throw new ValidationException("error.encounter.enemyName", pos);
            if (instance.Portrait is not null && !ImageFileStorage.IsValidName(instance.Portrait))
                throw new ValidationException("error.common.invalidImage");

            // JSON "counters": null overwrites the C# default. Shared counter limits (labels unique, pips need a
            // small max, value clamped into range) exactly like a member/enemy.
            PartyValidation.ValidateCounters(instance.Counters ??= []);
        }

        // Owned background image (traversal-guarded stored name).
        if (encounter.BackgroundImage is not null && !ImageFileStorage.IsValidName(encounter.BackgroundImage))
            throw new ValidationException("error.common.invalidImage");

        // Optional scene/event refs: normalize empty → null, then require a slug (they're scene/event ids).
        encounter.ActivateSceneId = NormalizeRef(encounter.ActivateSceneId);
        if (encounter.ActivateSceneId is not null && !LightValidation.IsSlug(encounter.ActivateSceneId))
            throw new ValidationException("error.encounter.sceneId");
        encounter.ActivateEventId = NormalizeRef(encounter.ActivateEventId);
        if (encounter.ActivateEventId is not null && !LightValidation.IsSlug(encounter.ActivateEventId))
            throw new ValidationException("error.encounter.eventId");
    }

    private static string? NormalizeRef(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
