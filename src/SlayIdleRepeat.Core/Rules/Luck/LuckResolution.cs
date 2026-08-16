using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Rules.Luck;

/// <summary>One weighted row of a rarity table.</summary>
/// <param name="Rarity">The rarity this row draws.</param>
/// <param name="Weight">
/// Its share of the draw. Finite and never negative; zero is legal and is what a floor leaves behind
/// for every rarity below it.
/// </param>
internal readonly record struct RarityWeight(Rarity Rarity, double Weight);

/// <summary>One counter's value after a resolution.</summary>
/// <param name="Key">The counter id, as the tuning reader forms it.</param>
/// <param name="Value">The counter's value after the draw — not the delta.</param>
/// <remarks>
/// A resulting value rather than a delta, for the same reason the domain event carries one: the two
/// movements that matter are an advance and a reset, and a reset is badly described as a negative.
/// </remarks>
internal readonly record struct PityCounterChange(string Key, int Value);

/// <summary>
/// What one draw resolved to: the rarity, whether a guarantee forced it, and every counter the draw
/// moved.
/// </summary>
/// <param name="Outcome">The rarity drawn.</param>
/// <param name="FromPity">
/// Whether a hard guarantee forced this draw. Reported rather than inferred from the rarity: a
/// natural draw can land on the same rarity a guarantee would have forced, and the two are different
/// events to a player, to the analytics stream and to duplicate protection.
/// </param>
/// <param name="Changes">
/// Every counter this draw moved, advanced and reset alike. The caller applies them; the resolution
/// itself writes nothing.
/// </param>
/// <remarks>
/// Deltas rather than a mutated counter map: the luck service is stateless, so the aggregate that
/// owns the counters is the one that decides whether the draw is kept.
/// </remarks>
internal sealed record LuckResolution(
    Rarity Outcome, bool FromPity, IReadOnlyList<PityCounterChange> Changes);
