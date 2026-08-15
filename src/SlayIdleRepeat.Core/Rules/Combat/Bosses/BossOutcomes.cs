using System.Globalization;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Ops;

namespace SlayIdleRepeat.Core.Rules.Combat.Bosses;

/// <summary>
/// <c>RANDOM_OUTCOME</c>'s winner, resolved: the one effect id the op's single draw picked, found
/// among the boss's own holdings and fired.
/// </summary>
/// <remarks>
/// <para>
/// The op itself cannot fire the winner: the intra-<c>Rules</c> layering forbids
/// <c>Rules/Effects/Ops/</c> from naming a <c>Rules.Combat.Bosses</c> type, so it validates the table
/// and names the winner, then hands the id across <see cref="ICombatFlowSink.RandomOutcome"/> for
/// <see cref="IBossOutcomes"/> — this class — to resolve.
/// </para>
/// <para>
/// The chosen id is resolved against the holder's own <see cref="ActorPlan.Effects"/>, not a content
/// lookup this class holds — every boss effect is on the plan already, so the outcome rows are there
/// by construction, and an id that is not there is an authoring error the encounter builder should
/// have refused.
/// </para>
/// </remarks>
internal sealed class BossOutcomes : IBossOutcomes
{
    private readonly BattleServices _services;

    /// <summary>Builds the outcome resolver for one fight.</summary>
    /// <param name="services">The battle — its roster, its clock and its effect context.</param>
    internal BossOutcomes(BattleServices services)
    {
        ArgumentNullException.ThrowIfNull(services);

        _services = services;
    }

    /// <summary>
    /// Fires the one effect a <c>RANDOM_OUTCOME</c> drew.
    /// </summary>
    /// <param name="holder">The actor whose roll it was.</param>
    /// <param name="chosenEffectId">The id of the single winning row.</param>
    /// <param name="sourceEffectId">The <c>RANDOM_OUTCOME</c> effect's own id.</param>
    /// <exception cref="EffectContextException">
    /// The holder holds no effect under that id — an authoring error the encounter builder should
    /// have refused, refused here rather than dropped.
    /// </exception>
    public void Resolve(BattleActor holder, string chosenEffectId, string sourceEffectId)
    {
        ArgumentNullException.ThrowIfNull(holder);

        foreach (var held in holder.Plan.Effects)
        {
            if (string.Equals(held.Effect.Id, chosenEffectId, StringComparison.Ordinal))
            {
                _services.ResolveOutcome(holder, held.Effect);

                return;
            }
        }

        throw new EffectContextException(
            chosenEffectId,
            $"'{holder.Id}' rolled it from '{sourceEffectId}' at tick " +
            $"{_services.Tick.ToString(CultureInfo.InvariantCulture)} and holds no such effect",
            "`18` §10.1 E6 hands this seam ONE effect id per roll, and every boss effect is on " +
            "ActorPlan.Effects by construction — that is what puts it in the battle's effect table. " +
            "An id that is not there is authoring BossEncounterBuilder's O1 should have refused; " +
            "dropping it silently would make `17` §9's Roll of Fate a d6 with no faces.");
    }
}
