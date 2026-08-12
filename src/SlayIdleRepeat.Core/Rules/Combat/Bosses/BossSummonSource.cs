using System.Globalization;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Combat.Enemies;
using SlayIdleRepeat.Core.Rules.Effects;

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
    /// <returns>The add's plan, with <see cref="ActorPlan.Index"/> and <see cref="ActorPlan.LogId"/>
    /// left at <c>0</c> for <c>BattleSimulation.AdmitSummon</c> to assign.</returns>
    /// <exception cref="EffectContextException">
    /// The power fraction is outside `17` §1's band, or `05` §6.1 has no such archetype.
    /// </exception>
    public ActorPlan Spawn(BattleActor summoner, string archetype, string sourceEffectId)
    {
        ArgumentNullException.ThrowIfNull(summoner);

        RequirePowerFractionInBand(sourceEffectId);

        var row = _catalogue.Archetype(ArchetypeOf(archetype, sourceEffectId));

        // 🔒 Rounded at the accumulation point (`05` §1.1). 10 000 × 0.35 is 3499.999999999999 5 in
        // binary, and an unrounded power would carry that residue into every derived term.
        var power = DeterminismRounding.Round(_bossPower * _powerFraction);

        return new ActorPlan
        {
            Id = $"{summoner.Id}#ADD#{archetype}",
            Index = 0,
            LogId = 0,
            Side = BattleSide.ENEMY,
            Kind = EffectActorKind.ENEMY,
            BaseStats = EnemyDerivation.Derive(power, row, _catalogue.Derivation),
            Level = _level,
        };
    }

    /// <summary>🔒 `17` §1 — <em>"25–35% of boss power"</em> is a band, and 0.60 is not inside it.</summary>
    private void RequirePowerFractionInBand(string sourceEffectId)
    {
        if (_powerFraction >= BossAdds.MinPowerFraction && _powerFraction <= BossAdds.MaxPowerFraction)
        {
            return;
        }

        throw new EffectContextException(
            sourceEffectId,
            $"its adds are authored at {Format(_powerFraction)} of boss power, outside `17` §1's " +
            $"{Format(BossAdds.MinPowerFraction)}-{Format(BossAdds.MaxPowerFraction)} band",
            "`17` §1 gives the adds a band and the engine does not pick a number inside it — which " +
            "is why a number outside it has to be refused here. An add at 0.60 of boss power is " +
            "twice the fight `17` intends, and nothing else in the log would say so.");
    }

    /// <summary>`05` §6.1's eight shapes, by the name `18` §2.4's <c>archetype</c> key authors.</summary>
    private static EnemyArchetype ArchetypeOf(string archetype, string sourceEffectId) =>
        Enum.TryParse<EnemyArchetype>(archetype, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new EffectContextException(
                sourceEffectId,
                $"it summons '{archetype}', which is not one of `05` §6.1's archetypes",
                "`17` §1: 'adds use standard archetypes from `05` §6.1'. Spawning nothing instead " +
                "would delete Thornmaw's phase-3 fight without a single event to show for it, and " +
                "guessing an archetype would spawn a shape nobody authored.");

    private static string Format(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
