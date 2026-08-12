using System.Globalization;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Triggers;

namespace SlayIdleRepeat.Core.Rules.Combat.Bosses;

/// <summary>
/// 🔒 `17` §1 / §11 — the three rules a boss mechanic's wind-up has to satisfy, checked at
/// <b>encounter-build time</b> so a failure names the boss, the phase and the mechanic.
/// </summary>
/// <remarks>
/// <para>
/// `17` §1: <em>"Every damaging mechanic has a visible 1.0–1.5 s wind-up … the player cannot act on
/// it — combat is automatic — but they must be able to read what is happening, or the fight feels
/// arbitrary."</em> `17` §11 makes it a deliverable: <em>"Telegraph events emitted 1.0–1.5 s ahead
/// of every damaging mechanic."</em>
/// </para>
/// <list type="number">
///   <item>
///     <b>T1 — the band.</b> The lead is in <c>[1.0, 1.5]</c> s <b>and</b> a whole number of ticks,
///     so 1.00, 1.05, … 1.50 — 20 to 30 ticks. <c>CombatLog.AppendTelegraph</c> enforces the same
///     rule and is the second line of defence; this one fires first and says which mechanic of which
///     phase of which boss is wrong.
///   </item>
///   <item>
///     <b>T2 — the lead is shorter than the period.</b> <c>TriggerSchedule.IntervalTicks &gt;
///     leadTicks</c>. Otherwise firing <c>k+1</c>'s telegraph would be emitted before firing
///     <c>k</c> landed, and two telegraphs would be indistinguishable in the log — which is the
///     replay (`05` §3.1 step 7).
///   </item>
///   <item>
///     <b>T3 — M2-13's mechanical obligation.</b> A <c>PERIODIC</c> mechanic whose op is damaging
///     <b>and</b> whose interval exceeds the longest legal lead <b>must</b> carry one. Two
///     exemptions, both structural rather than discretionary:
///     <list type="bullet">
///       <item><c>ON_PHASE_ENTER</c> damage is exempt — the entry is HP-driven, so it cannot be
///       foreseen 1.2 s out. `17` §9's Dicelord phase-3 entry is the authored case.</item>
///       <item>a continuous aura whose period is at most the longest lead is exempt by T2 — `17`
///       §8's Sporequeen Rot drains once a second, and a 1 s period has nowhere to put a 1.0–1.5 s
///       wind-up.</item>
///     </list>
///   </item>
///   <item>
///     🔴 <b>T4 — a wind-up announces a landing the fight can reach.</b> The firing being announced
///     must fall <b>before</b> <c>CombatRules.MaxTicks</c>, or the log ends with a <c>Telegraph</c>
///     and no hit after it: the replayer (`05` §7/§8) draws the wind-up and nothing ever resolves it.
///     <para>
///     ⚠️ <b>Unlike T1–T3, this one cannot be an authoring rule, and that is why it is stated here
///     rather than checked in <see cref="BossEncounterBuilder"/>.</b> T1, T2 and T3 are decidable
///     from the script alone; whether firing <em>k</em> lands inside the fight depends on the tick
///     the phase was <em>entered</em>, which is HP-driven and therefore unknowable before the fight
///     runs. So T4 is enforced at emission, in
///     <see cref="BossPhaseController.AdvanceTick"/>'s per-instance pass, and it is the only wind-up
///     rule that has no <c>EffectContextException</c> and no marker — there is nothing wrong with the
///     authoring for it to name.
///     </para>
///   </item>
/// </list>
/// </remarks>
internal static class BossTelegraphs
{
    /// <summary>🔒 `17` §1 — the shortest readable wind-up. The same number <c>CombatLog</c> enforces.</summary>
    internal const double MinLeadSeconds = CombatLog.MinTelegraphSeconds;

    /// <summary>🔒 `17` §1 — the longest.</summary>
    internal const double MaxLeadSeconds = CombatLog.MaxTelegraphSeconds;

    /// <summary>
    /// 🔒 `18` §2.2's three damaging ops — the set T3 keys on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>DAMAGE</c>, <c>DAMAGE_TRUE</c> and <c>DAMAGE_MAXHP_PCT</c> are the ops that remove HP
    /// directly. <c>APPLY_STATUS</c> is deliberately <b>not</b> here even though a <c>POISON</c>
    /// eventually removes HP: `17` §1's wind-up announces <em>a mechanic that is about to land</em>,
    /// and a DoT's damage arrives on `05` §3.1 slot 1's cadence rather than at the mechanic's own
    /// firing, so a lead would point at the wrong moment.
    /// </para>
    /// <para>
    /// ⚠️ A <see cref="List{T}"/> initialiser, on <c>EnemyCatalogue.FixedStats</c>' precedent.
    /// </para>
    /// </remarks>
    internal static IReadOnlyList<EffectOp> DamagingOps { get; } = new List<EffectOp>
    {
        EffectOp.DAMAGE, EffectOp.DAMAGE_TRUE, EffectOp.DAMAGE_MAXHP_PCT,
    };

    /// <summary>
    /// 🔒 A lead in whole ticks — the offset <see cref="BossPhaseController.AdvanceTick"/> subtracts
    /// from <c>TriggerInstance.NextFiringTick</c> to find the tick the wind-up is emitted on.
    /// </summary>
    /// <param name="leadSeconds">The mechanic's authored <see cref="BossMechanic.TelegraphSeconds"/>.</param>
    /// <exception cref="EffectContextException">
    /// The lead is not a whole number of ticks. <b>T1</b> has already refused such a lead at
    /// encounter-build time with the boss, the phase and the mechanic named, so this is the
    /// invariant rather than the message — see <see cref="ExactLeadTicks"/>.
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
    /// 🔒 A wind-up in ticks <b>without</b> rounding to a whole one — the single statement of
    /// <em>seconds × <see cref="CombatLog.TicksPerSecond"/></em>, which <b>T1</b>'s refusal quotes so
    /// that a reader sees why 1.03 s is not a legal lead.
    /// </summary>
    /// <param name="leadSeconds">The mechanic's authored <see cref="BossMechanic.TelegraphSeconds"/>.</param>
    /// <returns>The lead in ticks, at `05` §1.1's four decimals.</returns>
    internal static double ExactLeadTicks(double leadSeconds) =>
        DeterminismRounding.Round(leadSeconds * CombatLog.TicksPerSecond);

    /// <summary>
    /// 🔒 <b>T3</b> — whether a mechanic is one `17` §1 <em>requires</em> a wind-up on.
    /// </summary>
    /// <param name="effect">The authored mechanic.</param>
    /// <returns>
    /// <c>true</c> for a <c>PERIODIC</c> whose op is one of <see cref="DamagingOps"/> and whose
    /// period exceeds the longest legal lead. Both exemptions are structural: an
    /// <c>ON_PHASE_ENTER</c> burst is not <c>PERIODIC</c>, and a period at or below
    /// <see cref="MaxLeadSeconds"/> has nowhere to put a wind-up (T2).
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

    private static string Format(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
