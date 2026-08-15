using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Rules.Economy;

/// <summary>
/// Max Energy, regeneration, grants, refills, overflow routing into the Energy Reserve, and the
/// automatic main-bar-first spend order.
/// </summary>
/// <remarks>
/// Pure static arithmetic: takes no <c>Player</c> aggregate and no clock, only banks and an elapsed
/// span the caller measured, since the caller is the one that owns the accrual anchor. Energy
/// cannot be bought with money, directly or indirectly — there is no purchase entry point here.
/// </remarks>
internal static class EnergyMath
{
    /// <summary>
    /// Max Energy: the base plus the per-Legend-Level increment, stopped at the cap. 120 (+2 per
    /// Legend Level, cap 200) as shipped.
    /// </summary>
    /// <remarks>
    /// The increment counts levels <em>gained</em> — <c>(legendLevel − 1)</c> — since Legend Level 1
    /// is the starting level the authored base of 120 already accounts for. The 200 cap is first
    /// reached at Legend Level 41, not 40. Delegates to <see cref="EnergyTuning"/> rather than
    /// duplicating the formula, since the <c>Player</c> aggregate needs this same number for its own
    /// invariant but may not reference <c>Rules</c>.
    /// </remarks>
    /// <param name="tuning">The energy numbers.</param>
    /// <param name="legendLevel">The player's Legend Level. Must be at least 1.</param>
    /// <exception cref="ArgumentNullException"><paramref name="tuning"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="legendLevel"/> is below 1.</exception>
    internal static int MaxEnergy(EnergyTuning tuning, int legendLevel)
    {
        ArgumentNullException.ThrowIfNull(tuning);

        return tuning.MaxEnergyAt(legendLevel);
    }

    /// <summary>
    /// The Energy Reserve's capacity: 1× Max Energy, so it scales with the player's <em>current</em>
    /// Max Energy rather than the 200 cap.
    /// </summary>
    /// <param name="tuning">The energy numbers.</param>
    /// <param name="legendLevel">The player's Legend Level. Never below 1.</param>
    /// <exception cref="ArgumentNullException"><paramref name="tuning"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="legendLevel"/> is below 1.</exception>
    internal static int ReserveCapacity(EnergyTuning tuning, int legendLevel)
    {
        ArgumentNullException.ThrowIfNull(tuning);

        return tuning.ReserveCapacityAt(legendLevel);
    }

    /// <summary>
    /// Regeneration: one Energy per <c>regenMinutesPerPoint</c>, offline included, overflowing
    /// into the Reserve. Only whole units accrue; the anchor advances by
    /// <c>wholeUnits × interval</c>, never to the instant asked about.
    /// </summary>
    /// <remarks>
    /// The remainder isn't discarded or stored — it stays implicit in the gap between the stored
    /// anchor and now, so a player sending a hundred commands in an hour regenerates the same as one
    /// sending a single command. The anchor still advances even when both banks are already full.
    /// </remarks>
    /// <param name="tuning">The energy numbers.</param>
    /// <param name="legendLevel">The player's Legend Level. Never below 1.</param>
    /// <param name="banks">The two banks before the accrual.</param>
    /// <param name="sinceAnchor">
    /// Wall-clock time from the player's stored accrual anchor to now. Never negative — callers must
    /// clamp clock skew themselves (e.g. <c>Math.Max(TimeSpan.Zero, now - anchor)</c>); throwing
    /// here instead of silently clamping keeps a persisted anchor stuck in the future from going
    /// undetected.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="tuning"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="legendLevel"/> is negative, or <paramref name="sinceAnchor"/> is.
    /// </exception>
    internal static EnergyAccrual Accrue(
        EnergyTuning tuning, int legendLevel, EnergyBanks banks, TimeSpan sinceAnchor)
    {
        ArgumentNullException.ThrowIfNull(tuning);
        RequireLegendLevel(legendLevel);

        if (sinceAnchor < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sinceAnchor),
                sinceAnchor,
                "The accrual anchor is in the future. Energy regeneration is computed server-side " +
                "from wall-clock time (10 §10), so a negative span means the anchor was persisted " +
                "wrong or the clock moved backwards. Clamping it to zero here would hide both.");
        }

        var interval = tuning.RegenInterval.Ticks;
        var wholeUnits = sinceAnchor.Ticks / interval;

        return new EnergyAccrual(
            Deposit(banks, wholeUnits, MaxEnergy(tuning, legendLevel), ReserveCapacity(tuning, legendLevel)),
            TimeSpan.FromTicks(wholeUnits * interval));
    }

    /// <summary>
    /// A fixed grant of Energy: a rewarded ad, a daily quest, a tile event, an inbox attachment.
    /// Fills the main bar, overflows into the Reserve, and discards what neither can hold.
    /// </summary>
    /// <param name="tuning">The energy numbers.</param>
    /// <param name="legendLevel">The player's Legend Level. Never below 1.</param>
    /// <param name="banks">The two banks before the grant.</param>
    /// <param name="amount">
    /// How much to grant. Never negative — taking Energy away is a spend, and a spend can be
    /// refused (see <see cref="Spend"/>), which a negative grant could not be.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="tuning"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="legendLevel"/> is negative, or <paramref name="amount"/> is.
    /// </exception>
    internal static EnergyBanks Grant(
        EnergyTuning tuning, int legendLevel, EnergyBanks banks, int amount)
    {
        ArgumentNullException.ThrowIfNull(tuning);
        RequireLegendLevel(legendLevel);

        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                amount,
                "A grant cannot be negative. Taking Energy away is EnergyMath.Spend, which can be " +
                "refused when the two banks cannot cover it; a negative grant would take it anyway.");
        }

        return Deposit(
            banks, amount, MaxEnergy(tuning, legendLevel), ReserveCapacity(tuning, legendLevel));
    }

    /// <summary>
    /// A refill "to full" — the daily free refill on first login, and the Legend Level-up refill.
    /// </summary>
    /// <remarks>
    /// "To full" of a bar that's already full is zero: such a refill grants nothing and therefore
    /// overflows nothing into the Reserve, even in states where a daily or level-up refill is
    /// otherwise treated as a Reserve source. Routes through <see cref="Grant"/> rather than
    /// assigning the bar directly, so overflow behavior doesn't need a second copy.
    /// </remarks>
    /// <param name="tuning">The energy numbers.</param>
    /// <param name="legendLevel">The player's Legend Level. Never below 1.</param>
    /// <param name="banks">The two banks before the refill.</param>
    /// <exception cref="ArgumentNullException"><paramref name="tuning"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="legendLevel"/> is below 1.</exception>
    internal static EnergyBanks RefillToFull(EnergyTuning tuning, int legendLevel, EnergyBanks banks)
    {
        ArgumentNullException.ThrowIfNull(tuning);

        var deficit = Math.Max(0, MaxEnergy(tuning, legendLevel) - banks.Energy);

        return Grant(tuning, legendLevel, banks, deficit);
    }

    /// <summary>
    /// Spending: draws from the main bar first, then the Reserve for any shortfall, with no choice
    /// of which to draw from. Refused as a value when the two together cannot cover the cost,
    /// leaving both banks untouched.
    /// </summary>
    /// <remarks>
    /// Takes no tuning and no Legend Level: what a thing costs is the caller's business, and the
    /// draw order is the same regardless.
    /// </remarks>
    /// <param name="banks">The two banks before the spend.</param>
    /// <param name="cost">What the action costs. Never negative; zero is a legal no-op.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="cost"/> is negative.</exception>
    internal static EnergySpend Spend(EnergyBanks banks, int cost)
    {
        if (cost < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(cost),
                cost,
                "A cost cannot be negative. Handing Energy back is EnergyMath.Grant, which routes " +
                "the overflow into the Reserve; a negative spend would put it straight into the bar " +
                "past its maximum.");
        }

        if ((long)banks.Energy + banks.Reserve < cost)
        {
            return new EnergySpend(IsAffordable: false, banks, DrawnFromBar: 0, DrawnFromReserve: 0);
        }

        var fromBar = Math.Min(banks.Energy, cost);
        var fromReserve = cost - fromBar;

        return new EnergySpend(
            IsAffordable: true,
            new EnergyBanks(banks.Energy - fromBar, banks.Reserve - fromReserve),
            fromBar,
            fromReserve);
    }

    /// <summary>
    /// The one deposit cascade, used by regeneration and by every grant alike: fill the main bar,
    /// put what it could not hold into the Reserve, discard the rest.
    /// </summary>
    /// <remarks>
    /// Headroom is floored at zero rather than asserted non-negative: banks can exceed the current
    /// maximum after a balance patch lowers it, and this neither confiscates the excess nor adds to
    /// it — there's simply nowhere to deposit until the player drains back under the cap. Amount is
    /// a <see cref="long"/> because regeneration can count units over an unbounded offline span; the
    /// two <c>Math.Min</c> calls bound the result before the casts back to <see cref="int"/>.
    /// </remarks>
    private static EnergyBanks Deposit(EnergyBanks banks, long amount, int max, int reserveCapacity)
    {
        var intoBar = Math.Min(amount, Math.Max(0L, max - (long)banks.Energy));
        var intoReserve = Math.Min(
            amount - intoBar, Math.Max(0L, reserveCapacity - (long)banks.Reserve));

        return new EnergyBanks((int)(banks.Energy + intoBar), (int)(banks.Reserve + intoReserve));
    }

    /// <summary>
    /// Fails fast on a Legend Level the derivations have no meaning for, before the entry point
    /// does anything else.
    /// </summary>
    /// <remarks>
    /// Delegates to <see cref="EnergyTuning.RequireLegendLevel"/> rather than restating the
    /// message: <see cref="EnergyTuning.MaxEnergyAt"/> would raise the same guard a few lines
    /// later, and two copies of one refusal are two copies that drift.
    /// </remarks>
    private static void RequireLegendLevel(int legendLevel) =>
        EnergyTuning.RequireLegendLevel(legendLevel);
}
