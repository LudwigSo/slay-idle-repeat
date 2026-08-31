using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Stats;
using SlayIdleRepeat.Core.Tests.Rules.Combat.Status;
using SlayIdleRepeat.Core.Tests.Rules.Stats;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat;

/// <summary>
/// Runs a fight through <see cref="CombatSimulator.SimulateDuel"/> — a public entry point — and
/// reads the result out of <see cref="SimulationResult.Log"/>.
/// </summary>
/// <remarks>
/// A duel rather than <see cref="CombatSimulator.Simulate"/> because it takes
/// <c>attackerEffects</c>/<c>defenderEffects</c> and both levels, without a caller touching
/// <c>HeldEffect</c> or any other <c>Rules.Effects</c> type. Constants are varied as content, never
/// as an internal <c>StatCaps</c>/<c>MitigationConstants</c>.
/// <para>
/// <see cref="AttackPipelineBench"/> is the other tool: it reaches through <c>BattlePlan.Seams</c> to
/// the internal pipeline, and is for the few claims a log cannot show.
/// </para>
/// </remarks>
internal static class PublicFightBench
{
    /// <summary>
    /// One tick of the 20 Hz clock. Both sides swing at tick 0 and an ASPD of 1.0 puts the next
    /// swing 20 ticks away, so this isolates a single attack from each actor.
    /// </summary>
    internal const double OneTick = 0.05;

    /// <summary>An arbitrary but fixed battle seed — every case pins its own outcome.</summary>
    internal const ulong Seed = 1UL;

    /// <summary>Runs one duel through the public entry point.</summary>
    internal static SimulationResult Duel(
        ActorStats attacker,
        ActorStats defender,
        int attackerLevel = 1,
        int defenderLevel = 1,
        IReadOnlyList<EffectDefinition>? attackerEffects = null,
        IReadOnlyList<EffectDefinition>? defenderEffects = null,
        IReadOnlyDictionary<StatId, decimal>? capOverrides = null,
        (decimal Flat, decimal PerLevel)? mitigation = null,
        double durationSeconds = OneTick,
        ulong battleSeed = Seed) =>
        CombatSimulator.SimulateDuel(
            battleSeed,
            attacker,
            attackerLevel,
            defender,
            defenderLevel,
            durationSeconds,
            Content(capOverrides, mitigation),
            attackerEffects: attackerEffects,
            defenderEffects: defenderEffects);

    /// <summary>The two documents a public fight reads: combat constants and statuses.</summary>
    internal static ContentSnapshot Content(
        IReadOnlyDictionary<StatId, decimal>? capOverrides = null,
        (decimal Flat, decimal PerLevel)? mitigation = null) =>
        StatusFixtures.With(
            StatFixtures.CombatCapsSnapshot(capOverrides: capOverrides, mitigation: mitigation));

    /// <summary>
    /// A stat block with the named stats set, <c>ASPD</c>/<c>HEAL_PCT</c>/<c>DMG_PCT</c>/<c>DR_PCT</c>
    /// at their identity bases, and every other stat at zero.
    /// </summary>
    internal static ActorStats Stats(double maxHp, params (StatId Stat, double Value)[] rest) =>
        AttackPipelineBench.Stats(maxHp, rest);

    internal static EffectDefinition Effect(string id, EffectOp op, StatId stat, double value) =>
        StatFixtures.Effect(id, op, stat, value);

    /// <summary>
    /// The attacker's aggregated <c>ATK</c>, read back out of a public fight. At <c>DEF = 0</c>
    /// mitigation is 0 and every other step is off, so the <c>Hit</c> carries the aggregated ATK
    /// itself at 4 decimals.
    /// </summary>
    internal static double AggregatedAtk(double baseAtk, params EffectDefinition[] effects) =>
        Duel(
            Stats(500.0, (StatId.ATK, baseAtk)),
            Stats(500_000.0),
            attackerEffects: effects)
        .AttackerHit();

    /// <summary>Every event of one type the named actor produced, in emission order.</summary>
    internal static IReadOnlyList<CombatEvent> EventsBy(
        this SimulationResult result, CombatEventType type, byte sourceId) =>
        result.Log.Where(e => e.Type == type && e.SourceId == sourceId).ToArray();

    /// <summary>The values of the named actor's events of one type, in emission order.</summary>
    internal static IReadOnlyList<double> ValuesBy(
        this SimulationResult result, CombatEventType type, byte sourceId) =>
        result.EventsBy(type, sourceId).Select(e => e.Value).ToArray();

    /// <summary>The single <c>Hit</c> the attacker landed.</summary>
    internal static double AttackerHit(this SimulationResult result) =>
        result.ValuesBy(CombatEventType.Hit, CombatActor.Hero).Single();

    /// <summary>The attacker's own events, frame brackets and the defender's swing dropped.</summary>
    internal static IReadOnlyList<CombatEventType> AttackerSequence(this SimulationResult result) =>
        result.Log
            .Where(e => e.SourceId == CombatActor.Hero)
            .Where(e => e.Type is not (CombatEventType.BattleStart or CombatEventType.BattleEnd))
            .Select(e => e.Type)
            .ToArray();
}
