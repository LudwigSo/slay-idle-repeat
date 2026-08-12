using System.Globalization;

namespace SlayIdleRepeat.Core.Rules.Combat;

/// <summary>
/// 🔒 The three things `05` §3.3 changes about a fight without changing a line of the tick loop —
/// how long it may run, whether <c>ON_KILL</c> fires, and whether `18` §4's <c>IS_PVP</c> is set.
/// </summary>
/// <param name="MaxTicks">
/// 🔒 The fight's tick cap. `05` §3: <em>"Max fight duration 90 s = 1800 ticks"</em>; `05` §3.3:
/// <em>"60 s cap (<c>pvpMaxFightSeconds</c>)"</em> — 1200 ticks. On reaching it, the side with the
/// higher <b>remaining HP fraction</b> wins.
/// </param>
/// <param name="OnKillTriggersFire">
/// 🔒 `05` §3.3 — <c>ON_KILL</c> triggers <em>"never fire in duels. The only death in a duel ends
/// the fight."</em> True in PvE.
/// </param>
/// <param name="IsPvp">
/// `18` §4's <c>IS_PVP</c>, and the switch behind `05` §3.3's three condition rules
/// (<c>TARGET_IS_ELITE</c>/<c>TARGET_IS_BOSS</c> always false, <c>ENEMY_COUNT</c> always 1). It
/// travels to <c>EffectEvaluationContext.IsPvp</c> unchanged.
/// </param>
/// <remarks>
/// <para>
/// 🔒 <b>The cap is a parameter, and that is the point of this type.</b> M2-06 recorded that
/// `05` §3.3's 1200-tick duel cap is unenforced anywhere in the repository and that M2-14 inherits
/// it. A tick loop with <c>1800</c> written into its <c>for</c> would make the duel cap a second
/// loop rather than a second value — and <c>CombatLog.MaxTicks</c> stays 1800 because it is the
/// <em>log's</em> addressable range, which no fight may exceed and a short fight simply does not
/// reach.
/// </para>
/// <para>
/// ⚠️ <b>What is deliberately NOT here: initiative.</b> `05` §3.3's <em>"the attacker's side acts
/// first"</em> is a different <em>order</em>, not a different value, so it is an override of
/// <c>BattleSimulation</c>'s initiative sequence rather than a flag on this record. Adding a
/// <c>bool AttackerFirst</c> here would put a duel rule in the PvE loop's <c>if</c> with nothing
/// implementing it. M2-14 owns it; see <see cref="BattleSimulation"/>'s remarks for the seam.
/// </para>
/// </remarks>
internal sealed record CombatRules(int MaxTicks, bool OnKillTriggersFire, bool IsPvp)
{
    /// <summary>🔒 `05` §3 — an ordinary fight: 1800 ticks, <c>ON_KILL</c> live, not a duel.</summary>
    internal static CombatRules PvE { get; } = new(CombatLog.MaxTicks, OnKillTriggersFire: true, IsPvp: false);

    /// <summary>The fight's horizon in seconds — <c>EffectEvaluationContext.FightHorizonSeconds</c>.</summary>
    internal double HorizonSeconds => BattleClock.SecondsAt(MaxTicks);

    /// <summary>Validates the cap against the log's addressable range.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The cap is below 1 or above <see cref="CombatLog.MaxTicks"/>.
    /// </exception>
    internal CombatRules Validated()
    {
        if (MaxTicks < 1 || MaxTicks > CombatLog.MaxTicks)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxTicks), MaxTicks,
                $"A fight capped at {MaxTicks.ToString(CultureInfo.InvariantCulture)} ticks is outside " +
                $"1..{CombatLog.MaxTicks.ToString(CultureInfo.InvariantCulture)}. `05` §3's 90 s is the " +
                "longest fight the log can address, and a fight that runs no ticks has no outcome to " +
                "report. `05` §3.3's duel cap is 1200 and sits inside this range.");
        }

        return this;
    }
}
