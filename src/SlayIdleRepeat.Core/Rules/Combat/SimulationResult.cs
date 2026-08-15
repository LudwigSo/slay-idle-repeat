namespace SlayIdleRepeat.Core.Rules.Combat;

/// <summary>Everything one <c>Simulate(seed, heroSnapshot, enemySnapshot)</c> produces.</summary>
/// <param name="HeroWon">Whether the hero side won. On the timeout this is the side with the higher remaining HP fraction, not a draw.</param>
/// <param name="DurationTicks">
/// How many ticks the fight ran, <c>1..1800</c>. The replayer's total length: at ×1 the battle lasts
/// <c>DurationTicks × 0.05 s</c>, at ×3 a third of that.
/// </param>
/// <param name="HeroHpRemaining">The hero's HP at the final tick, rounded to 4 dp.</param>
/// <param name="Log">Every <see cref="CombatEvent"/>, in emission order, which is the same as tick order. This is the replay; nothing else is needed to draw the fight.</param>
/// <param name="LogHash">
/// An FNV-1a hash over the serialised event list, computed by <c>CanonicalStateWriter.HashCombatLog</c>
/// over <see cref="Log"/>. The server compares the client-reported value against its own computed one
/// to detect tampering, and a determinism gate compares it across x64 and ARM64.
/// </param>
/// <remarks>
/// <para>
/// Exactly these five fields — in particular there is no queue field for run effects: the queue is the
/// <see cref="CombatEventType.RunEffectQueued"/> entries of <see cref="Log"/>, read in log order. A
/// sixth field holding them would be a second copy of state the log already carries, and only one of
/// the two would be inside <see cref="LogHash"/>.
/// </para>
/// <para>
/// <see cref="HeroHpRemaining"/> is <see cref="double"/> rather than <see cref="float"/> for the
/// reasons set out on <see cref="CombatEvent"/>, and <see cref="Log"/> is an
/// <see cref="IReadOnlyList{T}"/> rather than a mutable list: a result the consumer could append to
/// would be a replay that can be edited after the outcome was fixed.
/// </para>
/// </remarks>
public sealed record SimulationResult(
    bool HeroWon,
    int DurationTicks,
    double HeroHpRemaining,
    IReadOnlyList<CombatEvent> Log,
    ulong LogHash);
