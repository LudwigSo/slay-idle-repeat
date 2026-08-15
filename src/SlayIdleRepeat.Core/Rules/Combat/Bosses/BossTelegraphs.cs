using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Triggers;

namespace SlayIdleRepeat.Core.Rules.Combat.Bosses;

/// <summary>
/// The three rules a boss mechanic's wind-up has to satisfy, checked at encounter-build time so a
/// failure names the boss, the phase and the mechanic.
/// </summary>
/// <remarks>
/// <para>
/// Every damaging mechanic has a visible 1.0-1.5 s wind-up so a fight the player cannot act on still
/// reads as fair rather than arbitrary.
/// </para>
/// <list type="number">
///   <item>
///     <b>T1 — the band.</b> The lead is in <c>[1.0, 1.5]</c> s and a whole number of ticks.
///     <c>CombatLog.AppendTelegraph</c> enforces the same rule as a second line of defence; this one
///     fires first and says which mechanic of which phase of which boss is wrong.
///   </item>
///   <item>
///     <b>T2 — the lead is shorter than the period.</b> Otherwise firing <c>k+1</c>'s telegraph
///     would be emitted before firing <c>k</c> landed, making two telegraphs indistinguishable in
///     the replay log.
///   </item>
///   <item>
///     <b>T3.</b> A <c>PERIODIC</c> mechanic whose op is damaging and whose interval exceeds the
///     longest legal lead must carry one. Exempt: an <c>ON_PHASE_ENTER</c> burst (HP-driven, so it
///     cannot be foreseen) and a continuous aura whose period is at most the longest lead (exempt by T2).
///   </item>
///   <item>
///     <b>T4 — a wind-up announces a landing the fight can reach.</b> Unlike T1-T3 this cannot be an
///     authoring rule (whether firing <em>k</em> lands inside the fight depends on the HP-driven tick
///     the phase was entered at), so it is enforced at emission instead, in
///     <see cref="BossPhaseController.AdvanceTick"/>.
///   </item>
/// </list>
/// </remarks>
internal static class BossTelegraphs
{
    /// <summary>The shortest readable wind-up. The same number <c>CombatLog</c> enforces.</summary>
    internal const double MinLeadSeconds = CombatLog.MinTelegraphSeconds;

    /// <summary>The longest.</summary>
    internal const double MaxLeadSeconds = CombatLog.MaxTelegraphSeconds;

    /// <summary>
    /// The three damaging ops — the set T3 keys on.
    /// </summary>
    /// <remarks>
    /// <c>APPLY_STATUS</c> is deliberately not here even though a POISON eventually removes HP: a
    /// DoT's damage arrives on the status tick's own cadence rather than at the mechanic's firing,
    /// so a lead would point at the wrong moment.
    /// </remarks>
    internal static IReadOnlyList<EffectOp> DamagingOps { get; } = new List<EffectOp>
    {
        EffectOp.DAMAGE, EffectOp.DAMAGE_TRUE, EffectOp.DAMAGE_MAXHP_PCT,
    };

    /// <summary>
    /// A lead in whole ticks — the offset <see cref="BossPhaseController.AdvanceTick"/> subtracts
    /// from <c>TriggerInstance.NextFiringTick</c> to find the tick the wind-up is emitted on.
    /// </summary>
    /// <param name="leadSeconds">The mechanic's authored <see cref="BossMechanic.TelegraphSeconds"/>.</param>
    /// <exception cref="EffectContextException">
    /// The lead is not a whole number of ticks — T1 should have refused this at encounter-build time.
    /// </exception>
    internal static int LeadTicks(double leadSeconds)
    {
        var exact = ExactLeadTicks(leadSeconds);

        if (exact != Math.Floor(exact))
        {
            throw new EffectContextException(
                nameof(LeadTicks),
                $"a wind-up of {Format(leadSeconds)} s is " +
                $"{Format(exact)} ticks, which is not a whole one",
                "`05` §3's simulation is fixed-tick, so a wind-up that points between two ticks " +
                "points at neither. BossEncounterBuilder refuses this at authoring time and names " +
                "the boss, the phase and the mechanic; reaching it here means a lead bypassed that.");
        }

        return (int)exact;
    }

    /// <summary>
    /// A wind-up in ticks without rounding to a whole one — <em>seconds *
    /// <see cref="CombatLog.TicksPerSecond"/></em>.
    /// </summary>
    /// <param name="leadSeconds">The mechanic's authored <see cref="BossMechanic.TelegraphSeconds"/>.</param>
    /// <returns>The lead in ticks, at four decimals.</returns>
    internal static double ExactLeadTicks(double leadSeconds) =>
        DeterminismRounding.Round(leadSeconds * CombatLog.TicksPerSecond);

    /// <summary>
    /// <b>T3</b> — whether a mechanic is one that requires a wind-up.
    /// </summary>
    /// <param name="effect">The authored mechanic.</param>
    /// <returns>
    /// <c>true</c> for a <c>PERIODIC</c> whose op is one of <see cref="DamagingOps"/> and whose
    /// period exceeds the longest legal lead.
    /// </returns>
    internal static bool RequiresLead(EffectDefinition effect)
    {
        ArgumentNullException.ThrowIfNull(effect);

        if (effect.Trigger is not { Kind: TriggerKind.PERIODIC } trigger)
        {
            return false;
        }

        if (!DamagingOps.Contains(effect.Op))
        {
            return false;
        }

        return TriggerSchedule.IntervalTicks(trigger) > LeadTicks(MaxLeadSeconds);
    }

    private static string Format(double value) => InvariantText.Text(value);
}
