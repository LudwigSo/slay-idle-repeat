using System.Globalization;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat.Enemies;
using SlayIdleRepeat.Core.Rules.Effects;

namespace SlayIdleRepeat.Core.Rules.Combat.Bosses;

/// <summary>
/// 🔒 Everything <see cref="BossEncounterBuilder"/> resolves out of a <see cref="BossScript"/>: the
/// boss's <see cref="ActorPlan"/>, its two phase boundaries, and the two maps
/// <see cref="BossPhaseController"/> reads.
/// </summary>
/// <remarks>
/// 🔒 <b>The maps are the reason this is a record and not just an <see cref="ActorPlan"/>.</b> A
/// plan is what the simulator needs; a phase is what the controller needs, and
/// <c>BattleSimulation.RegisterHoldings</c> registers plan effects without knowing that some of them
/// belong to phases 2 and 3. Everything the controller has to key on — which instance is in which
/// phase, and which instance carries a wind-up — is decided once, here, at build time.
/// </remarks>
internal sealed record BossEncounter
{
    /// <summary>The boss's actor id — <see cref="BossScript.Id"/>, and <see cref="ActorPlan.Id"/>.</summary>
    public required string BossId { get; init; }

    /// <summary>
    /// 🔒 The boss as it enters the fight: `17` §1.2's statline, every phase block's mechanics and
    /// the three <see cref="BossBuiltIns"/>, all on <see cref="ActorPlan.Effects"/> with explicit
    /// instance ids.
    /// </summary>
    public required ActorPlan Plan { get; init; }

    /// <summary>`17` §1's first-clear flag, as the encounter was built with it.</summary>
    public required bool FirstClear { get; init; }

    /// <summary>
    /// The HP fraction at or below which the boss is in phase 2 —
    /// <see cref="BossPhaseRules.Phase2HpFraction"/> for <see cref="FirstClear"/>.
    /// </summary>
    public required double Phase2HpFraction { get; init; }

    /// <summary>
    /// The HP fraction at or below which the boss is in phase 3. Always
    /// <see cref="BossPhaseRules.Phase3HpFraction"/> — the first-clear extension widens phase 1 only.
    /// </summary>
    public required double Phase3HpFraction { get; init; }

    /// <summary>
    /// 🔒 The phase map: every instance that belongs to a phase block, and which block.
    /// </summary>
    /// <remarks>
    /// 🔒 <b>What is <em>not</em> in it is the load-bearing half.</b> The three
    /// <see cref="BossBuiltIns"/> are absent, so no phase transition can deactivate or reactivate
    /// <c>SYS_ENRAGE</c> — which is the one thing that would re-anchor its R8 clock and move the
    /// enrage from 70 s of battle to 70 s after 66% HP.
    /// </remarks>
    public required IReadOnlyDictionary<EffectInstanceId, int> PhaseOfInstance { get; init; }

    /// <summary>
    /// The wind-up map: every instance whose mechanic authored a
    /// <see cref="BossMechanic.TelegraphSeconds"/>, and the lead in seconds.
    /// </summary>
    public required IReadOnlyDictionary<EffectInstanceId, double> LeadSecondsOfInstance { get; init; }
}

/// <summary>
/// Everything <see cref="BossEncounterBuilder.Build"/> needs that a <see cref="BossScript"/> does
/// not carry — the encounter's power and level, its roster position, and the content the script's
/// effect ids resolve against.
/// </summary>
/// <remarks>
/// 🔒 <b><see cref="Power"/> arrives as a parameter and is never derived here</b>, exactly as
/// <c>EnemyDerivation.Derive</c>'s does and for the same reason: `05` §6.3 and `17` §1 both state
/// that a boss's Power <b>already</b> includes <c>StageMult.Boss = 2.20</c> and must not be
/// multiplied again. Keeping the derivation on this side of the parameter is what makes the
/// double-multiplication impossible to write.
/// </remarks>
internal sealed record BossEncounterRequest
{
    /// <summary>The boss script — M2-13's data.</summary>
    public required BossScript Script { get; init; }

    /// <summary>
    /// 🔒 The effect table the script's ids resolve against (R19: effects are referenced, never
    /// embedded). The caller supplies it, on <c>EnemyCatalogue</c>'s pattern — the boss engine does
    /// not read content.
    /// </summary>
    public required IReadOnlyDictionary<string, EffectDefinition> Effects { get; init; }

    /// <summary>
    /// 🔒 `02` §4.3's <c>EnemyPower(i)</c> for the boss node, <b>with <c>StageMult.Boss</c> already
    /// inside it</b> (`05` §6.3, `17` §1). Never multiplied by 2.20 here.
    /// </summary>
    public required double Power { get; init; }

    /// <summary>`05` §6.0's <c>EnemyLevel(chapter, tier)</c>.</summary>
    public required int Level { get; init; }

    /// <summary>The boss's `05` §3.1 actor index.</summary>
    public required int Index { get; init; }

    /// <summary>The boss's `05` §7 log id.</summary>
    public required byte LogId { get; init; }

    /// <summary>
    /// 🔒 `17` §1.2's <em>"Secondary stats: every boss uses the baseline"</em> row, as authored
    /// content.
    /// </summary>
    /// <remarks>
    /// Only its four secondaries — CRIT, CDMG, DODGE, LIFESTEAL — are read; the four power
    /// coefficients are replaced by <see cref="BossScript.Coefficients"/>. It is handed in rather
    /// than written here because <em>"a number in code is a number nobody can retune without a
    /// build"</em>, and `17` §1.2 puts these in <c>data/bosses.json</c>.
    /// </remarks>
    public required ArchetypeRow Baseline { get; init; }

    /// <summary>`05` §6's derivation constants — <c>EnemyCatalogue.Derivation</c>.</summary>
    public required EnemyDerivationConstants Derivation { get; init; }

    /// <summary>`17` §1's first-clear flag for this player and this boss.</summary>
    public bool FirstClear { get; init; }
}

/// <summary>
/// 🔒 `17` §1 / §1.2 — one <see cref="BossScript"/> plus its encounter, resolved into the
/// <see cref="BossEncounter"/> the simulator and the phase controller run on.
/// </summary>
/// <remarks>
/// <para>
/// ═══ 🔒 <b>WHAT IS CHECKED HERE, AND WHY HERE</b> ═══
/// </para>
/// <para>
/// Every rule below is an <b>authoring</b> rule: it can be decided before a tick runs, and a failure
/// names the boss, the phase and the mechanic. Deferring any of them to the tick loop would surface
/// M2-13's typo as a mid-fight exception in a player's run, or — worse — as a boss whose mechanics
/// silently did nothing, which the balance harness would read as the boss being weak.
/// </para>
/// <list type="number">
///   <item>Exactly <see cref="BossScript.PhaseCount"/> blocks, numbered 1, 2, 3, <b>in order</b>.</item>
///   <item>Every referenced effect id resolves against <see cref="BossEncounterRequest.Effects"/>.</item>
///   <item>🔒 No block at phase 2 or 3 carries an <c>ON_BATTLE_START</c> trigger. `05` §3.1's 0b
///   sweep runs <b>before</b> 0c, so such an effect would fire while the boss is still in phase 1 —
///   a phase-3 mechanic landing at battle start.</item>
///   <item>No script may name one of the three <see cref="BossBuiltIns"/>: they are attached here,
///   once, for every boss.</item>
///   <item><b>T1</b>, <b>T2</b> and <b>T3</b> — see <see cref="BossTelegraphs"/>.</item>
///   <item>A <c>SUMMON</c> mechanic's <c>maxAlive</c> is at most <see cref="BossAdds.MaxAlive"/>.</item>
/// </list>
/// <para>
/// 🔒 <b>Every refusal is an <see cref="EffectContextException"/> naming the boss, the phase and the
/// mechanic, and carrying the rule's own marker</b> — <c>T1</c>, <c>T2</c> or <c>T3</c> for the
/// telegraph rules (steering S2: a failure has to say <em>which</em> rule fired, not merely that
/// something was wrong). <c>CombatLog.AppendTelegraph</c> enforces T1 again at emission time and is
/// the second line of defence; the message here is the better one because it knows the authoring.
/// </para>
/// </remarks>
internal static class BossEncounterBuilder
{
    /// <summary>
    /// 🔒 Builds one boss's encounter — its `17` §1.2 statline, its plan with every phase block and
    /// built-in on it, its two phase boundaries and the controller's two maps.
    /// </summary>
    /// <param name="request">The script and its encounter.</param>
    /// <returns>The resolved encounter.</returns>
    /// <remarks>🔴 <b>PHASE 1b STUB — M2-12's implementation phase owns the body.</b></remarks>
    /// <exception cref="NotSupportedException">Always, until M2-12's implementation phase lands.</exception>
    internal static BossEncounter Build(BossEncounterRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        throw new NotSupportedException(
            $"BossEncounterBuilder.Build('{request.Script.Id}') is declared and not written yet — " +
            "M2-12's IMPLEMENTATION phase owns the body. It must derive the statline through " +
            "EnemyDerivation.Derive(request.Power, StatlineRow(...), request.Derivation) with the " +
            "power AS HANDED IN (05 §6.3 and 17 §1: StageMult.Boss = 2.20 is already inside it), put " +
            "every phase block's mechanics AND the three BossBuiltIns onto ActorPlan.Effects under " +
            "explicit instance ids, and refuse the six authoring rules on this class with a message " +
            "naming boss, phase and effect. Returning a plan with no mechanics would be a boss the " +
            "balance harness reads as weak rather than as unwired (steering S6).");
    }

    /// <summary>
    /// 🔒 `17` §1.2's statline row: <see cref="BossEncounterRequest.Baseline"/>'s secondaries with
    /// the script's four coefficients in place of the archetype's.
    /// </summary>
    /// <param name="coefficients">`17` §1.2's per-boss row.</param>
    /// <param name="baseline">The authored baseline row.</param>
    /// <remarks>🔴 <b>PHASE 1b STUB — M2-12's implementation phase owns the body.</b></remarks>
    /// <exception cref="NotSupportedException">Always, until M2-12's implementation phase lands.</exception>
    internal static ArchetypeRow StatlineRow(BossCoefficients coefficients, ArchetypeRow baseline)
    {
        ArgumentNullException.ThrowIfNull(baseline);

        throw new NotSupportedException(
            "BossEncounterBuilder.StatlineRow is declared and not written yet — M2-12's " +
            "IMPLEMENTATION phase owns the body. It is `baseline with { HpCoef = coefficients.Hp, " +
            "AtkCoef = coefficients.Atk, DefCoef = coefficients.Def, AspdCoef = coefficients.Aspd }` " +
            "— 17 §1.2's 'a per-boss coefficient row instead of a shared archetype', with the " +
            $"baseline's secondaries kept (it was handed hpCoef " +
            $"{coefficients.Hp.ToString("R", CultureInfo.InvariantCulture)}). Keeping the baseline's " +
            "coefficients instead would give every boss the same shape.");
    }
}
