namespace SlayIdleRepeat.Core.Rules.Combat;

/// <summary>One actor a fight admitted, as the door that composed it named it.</summary>
/// <param name="ActorId">The combat-log id — the <c>TargetId</c> of the actor's <see cref="CombatEventType.ActorSpawned"/> entry.</param>
/// <param name="Identity">
/// The content identity the composing door named — an enemy archetype name, an elite id, a boss
/// script id, or <c>"HERO"</c> — or <see langword="null"/> when no door named one. A stat-block fight
/// and a duel name nobody, and <see langword="null"/> is that answer; it is never defaulted to a
/// plausible string.
/// </param>
/// <param name="IsElite">Whether the actor was composed as the encounter's Elite slot.</param>
/// <param name="IsBoss">Whether the actor is the boss the phase check watches.</param>
/// <param name="IsSummon">Whether a <c>SUMMON</c> op admitted the actor mid-fight.</param>
public sealed record BattleRosterEntry(byte ActorId, string? Identity, bool IsElite, bool IsBoss, bool IsSummon);
