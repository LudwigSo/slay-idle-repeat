using System.Globalization;
using SlayIdleRepeat.Core.Content.Effects;

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
    /// <remarks>🔴 <b>PHASE 1b STUB — M2-12's implementation phase owns the body.</b></remarks>
    /// <exception cref="NotSupportedException">Always, until M2-12's implementation phase lands.</exception>
    internal static int LeadTicks(double leadSeconds) =>
        throw new NotSupportedException(
            $"BossTelegraphs.LeadTicks({leadSeconds.ToString("R", CultureInfo.InvariantCulture)}) is " +
            "declared and not written yet — M2-12's IMPLEMENTATION phase owns the body. It is " +
            "leadSeconds x CombatLog.TicksPerSecond, refused under T1 unless it is inside " +
            "[1.0, 1.5] s AND a whole tick. Rounding a fractional lead silently would announce a " +
            "landing between two ticks, which is a landing at neither (steering S6).");

    /// <summary>
    /// 🔒 <b>T3</b> — whether a mechanic is one `17` §1 <em>requires</em> a wind-up on.
    /// </summary>
    /// <param name="effect">The authored mechanic.</param>
    /// <remarks>🔴 <b>PHASE 1b STUB — M2-12's implementation phase owns the body.</b></remarks>
    /// <exception cref="NotSupportedException">Always, until M2-12's implementation phase lands.</exception>
    internal static bool RequiresLead(EffectDefinition effect)
    {
        ArgumentNullException.ThrowIfNull(effect);

        throw new NotSupportedException(
            $"BossTelegraphs.RequiresLead('{effect.Id}') is declared and not written yet — M2-12's " +
            "IMPLEMENTATION phase owns the body. It is true for a PERIODIC whose op is one of " +
            "DamagingOps and whose interval exceeds MaxLeadSeconds; ON_PHASE_ENTER damage is exempt " +
            "(the entry is HP-driven and cannot be foreseen 1.2 s out) and a period at or below " +
            "MaxLeadSeconds is exempt by T2. Answering false for everything would let M2-13 ship an " +
            "untelegraphed 300% ATK All In and no test would notice (steering S6).");
    }
}
