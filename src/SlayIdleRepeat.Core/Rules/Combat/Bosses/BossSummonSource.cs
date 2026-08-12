using System.Globalization;
using SlayIdleRepeat.Core.Rules.Combat.Enemies;

namespace SlayIdleRepeat.Core.Rules.Combat.Bosses;

/// <summary>
/// 🔒 `18` §2.4's <c>SUMMON</c>, the <b>roster</b> half — what a boss's add <em>is</em>: `05` §6.1's
/// archetype derived at `17` §1's <em>"25–35% of boss power"</em>.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>The entry rules are not here and must not be restated here.</b> `05` §3.1 gives them to the
/// tick loop and <c>BattleSimulation.AdmitSummon</c> implements them: the summon takes the end of
/// the enemy index list, the next free log id (<b>never a reused one</b>), a <b>full</b>
/// <c>1.0 / ASPD</c> cooldown so that it never attacks on its spawn tick, and its holdings are
/// registered at the current tick — which is its R8 anchor. This class returns a plan with
/// <b>no</b> <see cref="ActorPlan.Index"/> and <b>no</b> <see cref="ActorPlan.LogId"/>, because both
/// are the roster's.
/// </para>
/// <para>
/// 🔒 <b>The <em>3 alive</em> cap is not here either.</b> `18` §2.4's <c>maxAlive</c> is authored on
/// the <c>SUMMON</c> effect and enforced by <c>BattleSimulation.BattleFlowSink.Summon</c>;
/// <see cref="BossEncounterBuilder"/> checks the authoring against <see cref="BossAdds.MaxAlive"/>.
/// A second count here would be a second answer to the same question.
/// </para>
/// <para>
/// ⚠️ <b>The power fraction is handed in, not chosen.</b> `17` §1 gives a <em>band</em>, 25–35%, and
/// picking a number inside it is authoring rather than engineering (steering S6).
/// </para>
/// </remarks>
internal sealed class BossSummonSource : ISummonSource
{
    private readonly EnemyCatalogue _catalogue;
    private readonly double _bossPower;
    private readonly double _powerFraction;
    private readonly int _level;

    /// <summary>Builds the summon source for one boss.</summary>
    /// <param name="catalogue">`05` §6.1's archetype rows and §6's derivation constants.</param>
    /// <param name="bossPower">
    /// 🔒 `02` §4.3's <c>EnemyPower(i)</c> for the boss node, as handed to
    /// <see cref="BossEncounterRequest.Power"/> — <c>StageMult.Boss</c> already inside it and never
    /// applied twice.
    /// </param>
    /// <param name="powerFraction">
    /// `17` §1's fraction of boss power, inside
    /// <see cref="BossAdds.MinPowerFraction"/>..<see cref="BossAdds.MaxPowerFraction"/>.
    /// </param>
    /// <param name="level">`05` §6.0's <c>EnemyLevel</c> for the encounter — the boss's own.</param>
    internal BossSummonSource(
        EnemyCatalogue catalogue, double bossPower, double powerFraction, int level)
    {
        ArgumentNullException.ThrowIfNull(catalogue);

        _catalogue = catalogue;
        _bossPower = bossPower;
        _powerFraction = powerFraction;
        _level = level;
    }

    /// <summary>
    /// The add a <c>SUMMON</c> of <paramref name="archetype"/> spawns, without an index or a log id.
    /// </summary>
    /// <param name="summoner">The boss whose effect fired — <c>OWNER</c>'s subject.</param>
    /// <param name="archetype">`05` §6.1's archetype name, as the op authors it.</param>
    /// <param name="sourceEffectId">The `18` §8 effect id, for the failure message.</param>
    /// <remarks>🔴 <b>PHASE 1b STUB — M2-12's implementation phase owns the body.</b></remarks>
    /// <exception cref="NotSupportedException">Always, until M2-12's implementation phase lands.</exception>
    public ActorPlan Spawn(BattleActor summoner, string archetype, string sourceEffectId)
    {
        ArgumentNullException.ThrowIfNull(summoner);

        throw new NotSupportedException(
            $"BossSummonSource.Spawn('{archetype}' for '{summoner.Id}', from '{sourceEffectId}') is " +
            "declared and not written yet — M2-12's IMPLEMENTATION phase owns the body. It must " +
            "resolve the archetype against the catalogue's 05 §6.1 rows, derive the statline through " +
            "EnemyDerivation.Derive(bossPower x powerFraction, row, catalogue.Derivation) — " +
            $"{_bossPower.ToString("R", CultureInfo.InvariantCulture)} x " +
            $"{_powerFraction.ToString("R", CultureInfo.InvariantCulture)} at level " +
            $"{_level.ToString(CultureInfo.InvariantCulture)}, against " +
            $"{_catalogue.Archetypes.Count.ToString(CultureInfo.InvariantCulture)} authored rows — " +
            "and return an ActorPlan with NO Index and NO LogId, because 05 §3.1 gives both to " +
            "BattleSimulation.AdmitSummon. Refusing an unknown archetype is part of the body: an add " +
            "that silently did not spawn would delete Thornmaw's phase-3 fight.");
    }
}
