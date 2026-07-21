using AmbientDirector.Api.Validation;

namespace AmbientDirector.Api.Services.Systems;

/// <summary>
/// The registered <see cref="IGameSystem"/>s, in registration order — resolved once from DI (the
/// <c>IEnumerable&lt;IImageSearchSource&gt;</c> idiom) and validated eagerly so a malformed community system
/// fails the app at startup, not mid-session. Lookup is case-insensitive; the stored sentinel <c>"none"</c>
/// (the GM explicitly chose no system — distinct from null = never chosen, which the startup upgrade may
/// stamp) and unknown ids both resolve to null.
/// </summary>
public class GameSystemRegistry
{
    /// <summary>The stored <c>PartyConfig.SystemId</c> sentinel for "the GM explicitly chose no system".
    /// Never a valid <see cref="IGameSystem.Id"/>; the wire shows it as null.</summary>
    public const string None = "none";

    public GameSystemRegistry(IEnumerable<IGameSystem> systems)
    {
        All = [.. systems];

        // Fail fast on contract violations a registration typo could introduce; the phase-3 contract tests
        // (GameSystemContractTests, #129) extend this to full preset validity per system.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var system in All)
        {
            if (string.IsNullOrWhiteSpace(system.Id) || !LightValidation.IsSlug(system.Id) ||
                system.Id != system.Id.ToLowerInvariant())
                throw new InvalidOperationException(
                    $"Game system '{system.GetType().Name}' has an invalid id '{system.Id}' (lowercase slug required).");
            if (string.Equals(system.Id, None, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Game system '{system.GetType().Name}' uses the reserved id '{None}'.");
            if (!seen.Add(system.Id))
                throw new InvalidOperationException($"Duplicate game system id '{system.Id}'.");
        }
    }

    public IReadOnlyList<IGameSystem> All { get; }

    /// <summary>The system for a stored <c>SystemId</c>, or null for null / <see cref="None"/> / an id no
    /// registered system claims (e.g. a community system removed since it was chosen).</summary>
    public IGameSystem? Find(string? id) =>
        string.IsNullOrWhiteSpace(id) || string.Equals(id, None, StringComparison.OrdinalIgnoreCase)
            ? null
            : All.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
}
