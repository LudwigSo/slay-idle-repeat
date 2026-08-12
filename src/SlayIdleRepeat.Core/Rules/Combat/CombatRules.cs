using System.Globalization;
using SlayIdleRepeat.Core.Rules.Effects;

namespace SlayIdleRepeat.Core.Rules.Combat;

/// <summary>
/// 🔒 The four things `05` §3.3 and `11` §4.3 change about a fight without changing a line of the
/// tick loop — how long it may run, whether <c>ON_KILL</c> fires, whether `18` §4's <c>IS_PVP</c> is
/// set, and who takes an exact tie at the timeout.
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
/// <param name="ExactTieWinner">
/// 🔒 `11` §4.3 — <em>"On timeout, the side with the higher remaining HP fraction wins. On an exact
/// tie, the <b>lower-rated player wins</b> (a small underdog bias that prevents stagnation at the
/// top)."</em> The side named here takes an exact tie.
/// <para>
/// ⚠️ <b>A side rather than two ratings, deliberately.</b> Rating is `11` §5's, and nothing in `05`
/// gives the simulator a reason to know one: the only question a fight can answer is <em>which side
/// wins</em>, so the caller — which knows both players' ratings — translates §4.3's rule into that
/// one bit. Putting Elo inside the simulator would give `11` §6's server re-run a second input to
/// keep in step with the client's.
/// </para>
/// <para>
/// <c>null</c> everywhere else, and that is the PvE reading rather than an absent one: `05` §3
/// authors <b>no</b> tie rule, and <c>BattleSimulation.Outcome</c> records the errata that an exact
/// tie is therefore a loss for the hero, because `05` §9 defines <c>ParPower</c> by <em>clear
/// rate</em> and a 90 s standoff cleared nothing.
/// </para>
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
/// <c>BattleSimulation</c>'s acting sequence rather than a flag on this record. Adding a
/// <c>bool AttackerFirst</c> here would put a duel rule in the PvE loop's <c>if</c> with nothing
/// implementing it — and would be a <b>second</b> statement of "is this a duel" beside
/// <see cref="IsPvp"/>, which is already the switch the loop reads.
/// </para>
/// <para>
/// ⚠️ <b>And what IS here, against M2-08's handover: <see cref="ExactTieWinner"/>.</b> M2-08 recorded
/// that initiative was all that remained of `05` §3.3. `11` §4.3's exact-tie rule was unimplemented
/// and inexpressible — <c>BattleSimulation.Outcome</c> compared the two fractions with a strict
/// <c>&gt;</c> and had no way to be told who the underdog was — so it is added here rather than
/// discovered later by a player who tied a duel and lost it.
/// </para>
/// </remarks>
internal sealed record CombatRules(
    int MaxTicks, bool OnKillTriggersFire, bool IsPvp, BattleSide? ExactTieWinner = null)
{
    /// <summary>🔒 `05` §3 — an ordinary fight: 1800 ticks, <c>ON_KILL</c> live, not a duel.</summary>
    internal static CombatRules PvE { get; } = new(CombatLog.MaxTicks, OnKillTriggersFire: true, IsPvp: false);

    /// <summary>
    /// 🔒 `05` §3.3 and `11` §4.3 — a Ghost Duel's bounds, derived from the one authored number.
    /// </summary>
    /// <param name="pvpMaxFightSeconds">
    /// 🔒 `11` §4.3's <c>pvpMaxFightSeconds</c>, from <c>content/combat_caps.json</c> — <em>"60 s of
    /// simulated time, a PvP-specific override of the simulator's 90 s default"</em>. Converted to
    /// ticks by <see cref="BattleClock"/> rather than written as <c>1200</c>, so the cap the loop
    /// enforces and the cap the data authors are one number.
    /// </param>
    /// <param name="lowerRatedSide">
    /// 🔒 `11` §4.3 — the side holding the <b>lower-rated</b> player, which takes an exact tie at the
    /// timeout. In a duel the attacker is always <see cref="BattleSide.HERO"/> and the Ghost is
    /// always <see cref="BattleSide.ENEMY"/> (<c>CombatActor</c>), so this is the caller answering
    /// "which of the two players is the underdog" and nothing more.
    /// </param>
    /// <remarks>
    /// ⚠️ <b>Equal ratings are the caller's to break, not this method's.</b> `11` §4.3 says <em>"the
    /// lower-rated player wins"</em> and authors nothing for two players on the same rating — a real
    /// possibility, since `11` §4.4 draws candidates from a ±150 band. Inventing a rule here would be
    /// a decision `11` did not make (steering S6), and it would be made in the one place that cannot
    /// see the ratings. The caller must still name a side; `11` §5.1's <em>"only the attacker's rating
    /// is at stake"</em> is the argument for naming the Ghost.
    /// </remarks>
    internal static CombatRules Duel(double pvpMaxFightSeconds, BattleSide lowerRatedSide) =>
        new(
            BattleClock.TicksFor(pvpMaxFightSeconds),
            OnKillTriggersFire: false,
            IsPvp: true,
            ExactTieWinner: lowerRatedSide);

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
