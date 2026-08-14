using System.Globalization;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Ops;

namespace SlayIdleRepeat.Core.Rules.Combat.Bosses;

/// <summary>
/// 🔒 `18` §2.4 / §10.1 E6 — <c>RANDOM_OUTCOME</c>'s winner, resolved: the one effect id the op's
/// single draw picked, found among the boss's own holdings and fired.
/// </summary>
/// <remarks>
/// <para>
/// ═══ 🔒 <b>WHY THE OP DRAWS AND THIS RESOLVES, RATHER THAN ONE DOING BOTH</b> ═══
/// </para>
/// <para>
/// R17 fixes the intra-<c>Rules</c> layering as
/// <c>Rules.Combat ▶ Rules.Stats ▶ Rules.Effects</c>, and <c>IntraRulesLayeringRuleTests</c> fails
/// the build on a violation — so <c>Rules/Effects/Ops/</c> may not name a
/// <c>Rules.Combat.Bosses</c> type. The op therefore does what it can do from the bottom layer
/// (validate the table, take <b>exactly one</b> <c>WeightedPick</c>, name the winner) and hands the
/// id across <see cref="ICombatFlowSink.RandomOutcome"/>; <c>BattleSimulation</c> routes that to
/// <see cref="IBossOutcomes"/>, whose implementation is this class.
/// </para>
/// <para>
/// 🔒 <b>The chosen id is resolved against the holder's own <see cref="ActorPlan.Effects"/></b>, not
/// against a content lookup this class holds. Every boss effect is on the plan already (that is what
/// puts it in the battle's effect table, which <c>CombatLog.AppendTelegraph</c> and `05` §7's
/// <c>RunEffectQueued</c> index into), so the outcome rows are there by construction — and an id
/// that is <em>not</em> there is an authoring error the encounter builder should have refused.
/// </para>
/// <para>
/// ⚠️ <b>An outcome effect carries no trigger of its own.</b> `17` §9's <em>Roll of Fate</em> rows
/// are consequences of the roll, not independently fired effects: the <c>RANDOM_OUTCOME</c>'s own
/// <c>PERIODIC</c> is the cadence. Firing them through the resolver here is what makes them mutually
/// exclusive — one call per roll, one effect per call.
/// </para>
/// </remarks>
internal sealed class BossOutcomes : IBossOutcomes
{
    private readonly BattleServices _services;

    /// <summary>Builds the outcome resolver for one fight.</summary>
    /// <param name="services">The battle — its roster, its clock and its `18` §4/§5 context.</param>
    internal BossOutcomes(BattleServices services)
    {
        ArgumentNullException.ThrowIfNull(services);

        _services = services;
    }

    /// <summary>
    /// Fires the one effect a <c>RANDOM_OUTCOME</c> drew.
    /// </summary>
    /// <param name="holder">The actor whose roll it was — `17` §9's Dicelord.</param>
    /// <param name="chosenEffectId">The `18` §8 id of the single winning row.</param>
    /// <param name="sourceEffectId">The <c>RANDOM_OUTCOME</c> effect's own id.</param>
    /// <exception cref="EffectContextException">
    /// The holder holds no effect under that id — an authoring error <b>O1</b> should have refused at
    /// encounter-build time, refused here rather than dropped.
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
