using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Stats;
using SlayIdleRepeat.Core.Tests.Rules.Combat.Status;
using SlayIdleRepeat.Core.Tests.Rules.Stats;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat;

/// <summary>
/// Runs a fight through <see cref="CombatSimulator.SimulateDuel"/> — a <b>public</b> entry point —
/// and reads the result out of <see cref="SimulationResult.Log"/>.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Why this bench exists, and what it replaces.</b> <see cref="AttackPipelineBench"/> also runs
/// a real fight, but it reaches through <c>BattlePlan.Seams</c> to call the <b>internal</b>
/// <c>AttackPipeline.ResolveAttack</c> directly and asserts on the internal <c>AttackResult</c>. That
/// is the right tool for the handful of claims the log genuinely cannot show — the draw-stream
/// <c>Position</c> counts, the pre-absorption <c>Basis</c>, the ward and <c>DAMAGE_TRUE</c> routes.
/// For everything else, `05` §4's arithmetic is observable from outside <c>Core</c> as the
/// <c>Hit</c>/<c>Crit</c>/<c>Block</c>/<c>Miss</c> events a caller replays (`05` §8, `11` §6), and
/// that is the altitude those cases belong at.
/// </para>
/// <para>
/// 🔒 <b>A duel rather than <see cref="CombatSimulator.Simulate"/>.</b> Both are public, but
/// <see cref="CombatSimulator.SimulateDuel"/> takes <c>attackerEffects</c>/<c>defenderEffects</c>,
/// so `18` §1 effects — and therefore `18` §8's whole aggregation pipeline — are reachable without
/// a caller touching <c>HeldEffect</c> or any other <c>Rules.Effects</c> type. It also takes both
/// levels, which `05` §4's <c>20 × attackerLevel</c> term needs, and both sides are plain
/// <see cref="ActorStats"/> blocks rather than content-derived enemies.
/// </para>
/// <para>
/// ⚠️ <b>The constants are varied as <em>content</em>, never as an internal type.</b>
/// <see cref="StatFixtures.CombatCapsSnapshot"/> authors `05` §1's ceilings and `05` §4's dials into
/// a <c>content/combat_caps.json</c>, which is exactly how the shipped game supplies them.
/// </para>
/// </remarks>
internal static class PublicFightBench
{
    /// <summary>
    /// `05` §3's clock is 20 Hz, so this is one tick — long enough for exactly one swing per side.
    /// </summary>
    /// <remarks>
    /// Both sides swing at tick 0 (`05` §3.1's fixed initiative: hero first, then enemies by index)
    /// and an ASPD of 1.0 puts the next swing 20 ticks away, so a one-tick fight isolates a single
    /// attack from each actor without needing anyone's attacks switched off.
    /// </remarks>
    internal const double OneTick = 0.05;

    /// <summary>An arbitrary but fixed `14` §8.1 battle seed — every case pins its own outcome.</summary>
    internal const ulong Seed = 1UL;

    /// <summary>Runs one duel through the public entry point.</summary>
    /// <param name="attacker">The attacking side's `05` §1 block, before `18` §8.</param>
    /// <param name="defender">The defending side's `05` §1 block, before `18` §8.</param>
    /// <param name="attackerLevel">`05` §4's <c>20 × attackerLevel</c> term.</param>
    /// <param name="attackerEffects">`18` §1 effects the attacker holds — `18` §8 aggregates these.</param>
    /// <param name="defenderEffects">`18` §1 effects the defender holds.</param>
    /// <param name="capOverrides">`05` §1 ceilings authored differently, as content.</param>
    /// <param name="mitigation">`05` §4's two dials, as content.</param>
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

    /// <summary>
    /// The two documents a public fight reads — `05` §1/§4's constants and `05` §5's status
    /// catalogue.
    /// </summary>
    internal static ContentSnapshot Content(
        IReadOnlyDictionary<StatId, decimal>? capOverrides = null,
        (decimal Flat, decimal PerLevel)? mitigation = null) =>
        StatusFixtures.With(
            StatFixtures.CombatCapsSnapshot(capOverrides: capOverrides, mitigation: mitigation));

    /// <summary>
    /// A `05` §1 block with the named stats set, <c>ASPD</c> and <c>HEAL_PCT</c> at their `05` §2
    /// bases, and every other stat at zero.
    /// </summary>
    /// <remarks>
    /// <c>HEAL_PCT</c> is 1.0 rather than 0 for <see cref="AttackPipelineBench.Stats"/>'s reason:
    /// `05` §2 makes it a multiplier on all healing received, so a zero would make every lifesteal
    /// case in the suite pass by healing nothing.
    /// </remarks>
    internal static ActorStats Stats(double maxHp, params (StatId Stat, double Value)[] rest) =>
        AttackPipelineBench.Stats(maxHp, rest);

    /// <summary>
    /// A `18` §8 stat effect — the shape <see cref="Duel"/>'s effect lists carry.
    /// </summary>
    internal static EffectDefinition Effect(string id, EffectOp op, StatId stat, double value) =>
        StatFixtures.Effect(id, op, stat, value);

    /// <summary>
    /// 🔒 The attacker's <b>aggregated</b> <c>ATK</c>, read back out of a public fight: the one
    /// <c>Hit</c> a defender with <c>DEF = 0</c> takes is that number exactly.
    /// </summary>
    /// <param name="baseAtk">The `05` §1 block's <c>ATK</c>, before `18` §8.</param>
    /// <param name="effects">The `18` §1 effects the attacker holds.</param>
    /// <remarks>
    /// <para>
    /// 🔒 <b>Why <c>DEF = 0</c> makes this exact.</b> `05` §4 step 3's mitigation is
    /// <c>effDef / (effDef + flat + perLevel × attackerLevel)</c>, which at <c>effDef = 0</c> is
    /// <c>0 / 140 = 0</c> — so step 3 multiplies by <c>(1 − 0)</c> and step 2's <c>raw</c> survives
    /// to the log untouched. Every other step is off: no crit, no block, no dodge (all three stats
    /// are 0), no <c>DR%</c>, no ward, and step 7's floor is 10% of a raw the hit already exceeds.
    /// The <c>Hit</c> event therefore carries the aggregated <c>ATK</c> itself.
    /// </para>
    /// <para>
    /// 🔒 <b>This is what makes `18` §8 observable from outside <c>Core</c> at full precision.</b>
    /// `05` §1.1 rounds to 4 dp at every accumulation point, and the pipeline's own output is
    /// rounded the same way — so a case that discriminates two readings of the rounding rule in the
    /// fourth decimal (2.247 against 2.2469) survives the trip through the fight and lands in the
    /// log intact. An aggregation bug that moved a stat by one ten-thousandth moves this number by
    /// the same amount.
    /// </para>
    /// <para>
    /// ⚠️ The defender's <c>MAX_HP</c> is large enough to outlive any build a case builds, so the
    /// fight always reaches its single swing rather than ending early on a kill.
    /// </para>
    /// </remarks>
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

    /// <summary>
    /// The single <c>Hit</c> the attacker landed — the number `05` §4's ten steps produced.
    /// </summary>
    internal static double AttackerHit(this SimulationResult result) =>
        result.ValuesBy(CombatEventType.Hit, CombatActor.Hero).Single();

    /// <summary>
    /// The attacker's own events, frame brackets and the defender's swing dropped — the per-attack
    /// emission sequence `05` §7 specifies.
    /// </summary>
    internal static IReadOnlyList<CombatEventType> AttackerSequence(this SimulationResult result) =>
        result.Log
            .Where(e => e.SourceId == CombatActor.Hero)
            .Where(e => e.Type is not (CombatEventType.BattleStart or CombatEventType.BattleEnd))
            .Select(e => e.Type)
            .ToArray();
}
