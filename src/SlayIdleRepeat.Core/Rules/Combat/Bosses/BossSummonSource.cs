using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Combat.Enemies;
using SlayIdleRepeat.Core.Rules.Effects;

namespace SlayIdleRepeat.Core.Rules.Combat.Bosses;

/// <summary>
/// <c>SUMMON</c>'s roster half — what a boss's add is: a standard archetype derived at a fraction of
/// boss power.
/// </summary>
/// <remarks>
/// <para>
/// The entry rules are not here: the tick loop and <c>BattleSimulation.AdmitSummon</c> own them (the
/// summon takes the next free index/log id, a full cooldown so it never attacks on its spawn tick,
/// and registration at the current tick). This class returns a plan with no
/// <see cref="ActorPlan.Index"/> and no <see cref="ActorPlan.LogId"/>, because both are the roster's.
/// </para>
/// <para>
/// The power fraction is handed in, not chosen: it arrives as a constructor argument from whoever
/// wires this fight's seams, not from <see cref="BossScript"/>, which carries no field for it.
/// </para>
/// <para>
/// The band is checked in <see cref="Spawn"/>, not the constructor, so the refusal can name the
/// <c>sourceEffectId</c> of the mechanic that asked.
/// </para>
/// </remarks>
internal sealed class BossSummonSource : ISummonSource
{
    private readonly EnemyCatalogue _catalogue;
    private readonly double _bossPower;
    private readonly double _powerFraction;
    private readonly int _level;

    /// <summary>Builds the summon source for one boss.</summary>
    /// <param name="catalogue">The archetype rows and derivation constants.</param>
    /// <param name="bossPower">
    /// The boss node's power, as handed to <see cref="BossEncounterRequest.Power"/> — the boss stage
    /// multiplier already inside it and never applied twice.
    /// </param>
    /// <param name="powerFraction">
    /// The fraction of boss power, inside
    /// <see cref="BossAdds.MinPowerFraction"/>..<see cref="BossAdds.MaxPowerFraction"/>.
    /// </param>
    /// <param name="level">The enemy level for the encounter — the boss's own.</param>
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
    /// <param name="archetype">The archetype name, as the op authors it.</param>
    /// <param name="sourceEffectId">The effect id, for the failure message.</param>
    /// <returns>The add's plan, with <see cref="ActorPlan.Index"/> and <see cref="ActorPlan.LogId"/>
    /// left at <c>0</c> for <c>BattleSimulation.AdmitSummon</c> to assign.</returns>
    /// <exception cref="EffectContextException">
    /// The power fraction is outside the band, or there is no such archetype.
    /// </exception>
    public ActorPlan Spawn(BattleActor summoner, string archetype, string sourceEffectId)
    {
        ArgumentNullException.ThrowIfNull(summoner);

        RequirePowerFractionInBand(sourceEffectId);

        var summoned = ArchetypeOf(archetype, sourceEffectId);
        var row = _catalogue.Archetype(summoned);

        // Rounded at the accumulation point: floating-point multiplication carries binary residue
        // that would otherwise propagate into every derived term.
        var power = DeterminismRounding.Round(_bossPower * _powerFraction);

        return new ActorPlan
        {
            Id = $"{summoner.Id}#ADD#{archetype}",
            Identity = summoned.ToString(),
            Index = 0,
            LogId = 0,
            Side = BattleSide.ENEMY,
            Kind = EffectActorKind.ENEMY,
            BaseStats = EnemyDerivation.Derive(power, row, _catalogue.Derivation),
            Level = _level,
        };
    }

    /// <summary>The adds power fraction is a band, and a value outside it is refused.</summary>
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

    /// <summary>The eight archetype shapes, by the name the <c>archetype</c> key authors.</summary>
    private static EnemyArchetype ArchetypeOf(string archetype, string sourceEffectId) =>
        EnemyArchetypes.TryParse(archetype, out var parsed)
            ? parsed
            : throw new EffectContextException(
                sourceEffectId,
                $"it summons '{archetype}', which is not one of `05` §6.1's eight archetypes " +
                $"({EnemyArchetypes.Names})",
                "`17` §1: 'adds use standard archetypes from `05` §6.1'. Spawning nothing instead " +
                "would delete Thornmaw's phase-3 fight without a single event to show for it, and " +
                "guessing an archetype would spawn a shape nobody authored.");

    private static string Format(double value) => InvariantText.Text(value);
}
