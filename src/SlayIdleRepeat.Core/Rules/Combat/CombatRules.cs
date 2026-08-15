using System.Globalization;
using SlayIdleRepeat.Core.Rules.Effects;

namespace SlayIdleRepeat.Core.Rules.Combat;

/// <summary>The four things a duel changes about a fight without changing a line of the tick loop — how long it may run, whether <c>ON_KILL</c> fires, whether <c>IS_PVP</c> is set, and who takes an exact tie at the timeout.</summary>
/// <param name="MaxTicks">The fight's tick cap: 1800 (90 s) in PvE, 1200 (60 s) in a duel. On reaching it, the side with the higher remaining HP fraction wins.</param>
/// <param name="OnKillTriggersFire"><c>ON_KILL</c> triggers never fire in duels — the only death in a duel ends the fight. True in PvE.</param>
/// <param name="ExactTieWinner">
/// The side that takes an exact tie at the timeout — the lower-rated player wins, a small underdog
/// bias that prevents stagnation at the top. Naming one is what makes the fight a duel (see
/// <see cref="IsPvp"/>).
/// <para>
/// A side rather than two ratings, deliberately: rating comparison happens upstream, and the caller
/// translates it into this one bit. Putting Elo inside the simulator would give a server re-run a
/// second input to keep in step with the client's.
/// </para>
/// <para><c>null</c> is the PvE reading rather than an absent one — it authors no tie rule. See <c>BattleSimulation.Outcome</c>'s remarks for the errata this follows.</para>
/// </param>
/// <remarks>
/// <para>The cap is a parameter, and that is the point of this type: a tick loop with <c>1800</c> written into its <c>for</c> would make the duel cap a second loop rather than a second value. <c>CombatLog.MaxTicks</c> stays 1800 because it is the log's addressable range, which no fight may exceed and a short fight simply does not reach.</para>
/// <para>What is deliberately not here: initiative. The attacker's side acting first is a different order, not a different value, so it is an override of <c>BattleSimulation</c>'s acting sequence rather than a flag on this record.</para>
/// <para>And what is here: <see cref="ExactTieWinner"/>. The exact-tie rule was previously unimplemented and inexpressible — the outcome check compared the two fractions with a strict <c>&gt;</c> and had no way to be told who the underdog was.</para>
/// <para><b><see cref="IsPvp"/> is derived, and that is the whole reason it is not stored.</b> A duel is exactly a fight with an underdog named: every duel requires one and PvE gives none — so a stored <c>bool IsPvp</c> beside <see cref="ExactTieWinner"/> would be two spellings of one bit, agreeing on the day they are written and diverging the day one caller sets one of them without the other.</para>
/// </remarks>
internal sealed record CombatRules(
    int MaxTicks, bool OnKillTriggersFire, BattleSide? ExactTieWinner = null)
{
    /// <summary>An ordinary fight: 1800 ticks, <c>ON_KILL</c> live, not a duel.</summary>
    internal static CombatRules PvE { get; } = new(CombatLog.MaxTicks, OnKillTriggersFire: true);

    /// <summary>
    /// <c>IS_PVP</c> — the switch behind a duel's condition rules (elite/boss checks always false,
    /// enemy count always 1), the discarded run queue and the acting order. It travels to the
    /// evaluation context and trigger occurrences unchanged.
    /// </summary>
    /// <remarks>Computed from <see cref="ExactTieWinner"/> rather than stored — see the type remarks.</remarks>
    internal bool IsPvp => ExactTieWinner is not null;

    /// <summary>A Ghost Duel's bounds, derived from the one authored number.</summary>
    /// <param name="pvpMaxFightSeconds">
    /// The duel cap in seconds, from content — 60 s of simulated time, a PvP-specific override of the
    /// simulator's 90 s default. Converted to ticks by <see cref="BattleClock"/> rather than written as
    /// <c>1200</c>, so the cap the loop enforces and the cap the data authors are one number.
    /// </param>
    /// <param name="lowerRatedSide">
    /// The side holding the lower-rated player, which takes an exact tie at the timeout. In a duel the
    /// attacker is always <see cref="BattleSide.HERO"/> and the Ghost is always <see cref="BattleSide.ENEMY"/>,
    /// so this is the caller answering "which of the two players is the underdog" and nothing more.
    /// </param>
    /// <remarks>
    /// <para>
    /// Equal ratings are the caller's to break, not this method's: two players on the same rating are a
    /// real possibility, and inventing a rule here would be a decision made in the one place that
    /// cannot see the ratings.
    /// </para>
    /// <para>
    /// This bit is a <c>LogHash</c> input, since it reaches <c>ON_BATTLE_END</c>'s <c>HeroWon</c> and
    /// affects win-only grants that append to the log before it is hashed. So it must be fixed by the
    /// server and travel with the seed, exactly as the duel seed does — a rating that moved between the
    /// client's fetch and the server's re-run would otherwise discard an honest duel.
    /// </para>
    /// <para>
    /// <see cref="BattleClock.TicksFor"/> rounds to the nearest tick, not down. At the authored 60 the
    /// arithmetic is exact; a future non-integral duration would snap to the nearest tick, so the cap
    /// the loop runs could exceed the one the data authors by up to half a tick.
    /// </para>
    /// </remarks>
    internal static CombatRules Duel(double pvpMaxFightSeconds, BattleSide lowerRatedSide) =>
        new(
            BattleClock.TicksFor(pvpMaxFightSeconds),
            OnKillTriggersFire: false,
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
