namespace SlayIdleRepeat.Core.Rules.Economy;

/// <summary>
/// 🔒 `10` §3 and `28` Part C — Max Energy, regeneration, grants, refills, overflow routing into
/// the Energy Reserve, and the automatic main-bar-first spend order.
/// </summary>
/// <remarks>
/// <para>
/// Pure static arithmetic over values (`30` §11.4: <c>Rules/</c> is "internal, static, stateless
/// calculators"). Nothing here takes the <c>Player</c> aggregate — `30` §11.5 keeps <c>Model</c>
/// beneath <c>Rules</c>, aggregates hold state and invariants while rules compute — and nothing
/// here takes a <see cref="GameContext"/> or asks what time it is. Time arrives as an elapsed span
/// the caller measured, because the caller is the one that owns the accrual anchor.
/// </para>
/// <para>
/// 🔒 <b>Energy is not a Plus benefit.</b> Nothing in this file names <c>Entitlements</c>, and
/// <c>IsolationTests.Entitlements_are_unreachable_from_the_rules_and_the_power_computation</c>
/// enforces that structurally. `28` C2 is explicit that <c>AD_ENERGY</c> is "capped, and identical
/// for Plus", and `10` §3 that there is <b>no</b> mechanism to buy Energy with money, directly or
/// indirectly. There is no purchase entry point here for the same reason there is no
/// <c>if (hasPlus)</c>: the surface simply does not exist.
/// </para>
/// <para>
/// ⚠️ <b>What is not here.</b> The Resource Dungeon entry cost is M10's (`25` §5), and the
/// escalating Soul Shard refill price of `10` §3.1 / §5.1 is not authored in
/// <c>game-data/tuning/</c> at all — see <see cref="EnergyTuning"/>. Both are absent rather than
/// guessed at (S6).
/// </para>
/// </remarks>
internal static class EnergyMath
{
    /// <summary>
    /// `10` §3 — Max Energy: the base plus the per-Legend-Level increment, stopped at the cap.
    /// 120 (+2 per Legend Level, cap 200) as shipped.
    /// </summary>
    /// <param name="tuning">The energy numbers, read from <c>tuning/progression.json</c>.</param>
    /// <param name="legendLevel">The player's Legend Level (`07` §1.1). Never negative.</param>
    /// <exception cref="ArgumentNullException"><paramref name="tuning"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="legendLevel"/> is negative.</exception>
    internal static int MaxEnergy(EnergyTuning tuning, int legendLevel)
    {
        ArgumentNullException.ThrowIfNull(tuning);
        RequireLegendLevel(legendLevel);

        // 64-bit, because both operands are authored numbers: a per-level increment of a few
        // million at Legend Level 200 would silently wrap in 32-bit and hand back a small or
        // negative Max Energy, which every rule below would then treat as the truth.
        var grown = tuning.BaseMax + ((long)tuning.PerLegendLevel * legendLevel);

        return (int)Math.Min(grown, tuning.MaxCap);
    }

    /// <summary>
    /// 🔒 `28` C2 — the Energy Reserve's capacity: <b>1× Max Energy</b> as shipped, and therefore
    /// a function of the player's <em>current</em> Max Energy rather than of the 200 cap. At Legend
    /// Level 0 it is 120, not 200.
    /// </summary>
    /// <param name="tuning">The energy numbers, read from <c>tuning/progression.json</c>.</param>
    /// <param name="legendLevel">The player's Legend Level. Never negative.</param>
    internal static int ReserveCapacity(EnergyTuning tuning, int legendLevel)
    {
        var capacity = (long)MaxEnergy(tuning, legendLevel) * tuning.ReserveMultipleOfMax;

        return (int)Math.Min(capacity, int.MaxValue);
    }

    /// <summary>
    /// 🔒 `10` §3 — regeneration: one Energy per <c>regenMinutesPerPoint</c>, offline included,
    /// overflowing into the Reserve. <b>Recorded assumption A1</b>: whole units only, and the
    /// anchor advances by <c>wholeUnits × interval</c>, never to the instant asked about.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The remainder is <b>not</b> discarded and <b>not</b> stored: it stays implicit in the gap
    /// between the anchor the caller keeps and the instant it just asked about. `10` §3 specifies
    /// no rounding, and the obvious reading — floor the elapsed time, then set the anchor to now —
    /// throws that remainder away once per call, so a player who sends a hundred commands in an
    /// hour regenerates far less than one who sends a single command. That is a frequency-dependent
    /// economy bug which punishes exactly the players `10` §3.2 wants never to be stopped by
    /// Energy, and no test that advances the clock once can see it.
    /// </para>
    /// <para>
    /// The anchor advances whether or not the units could be stored. Both banks full is not a
    /// reason to stop the clock — see <see cref="EnergyAccrual"/>.
    /// </para>
    /// </remarks>
    /// <param name="tuning">The energy numbers, read from <c>tuning/progression.json</c>.</param>
    /// <param name="legendLevel">The player's Legend Level. Never negative.</param>
    /// <param name="banks">The two banks before the accrual.</param>
    /// <param name="sinceAnchor">
    /// Wall-clock time from the player's stored accrual anchor to now. Never negative — an anchor
    /// in the future is a clock or a persistence defect, not a rule's business to smooth over.
    /// </param>
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
    /// `10` §3.1 / `28` C2 — a fixed grant: the `AD_ENERGY` rewarded ad's +40, a daily quest's
    /// +20, a `TILE_EVENT`'s +10, an inbox attachment. Fills the main bar, overflows into the
    /// Reserve, and discards what neither can hold.
    /// </summary>
    /// <param name="tuning">The energy numbers, read from <c>tuning/progression.json</c>.</param>
    /// <param name="legendLevel">The player's Legend Level. Never negative.</param>
    /// <param name="banks">The two banks before the grant.</param>
    /// <param name="amount">
    /// How much to grant. Never negative — taking Energy away is a spend, and a spend can be
    /// refused (see <see cref="Spend"/>), which a negative grant could not be.
    /// </param>
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
    /// 🔒 `10` §3.1 — a refill "to full": the daily free refill on first login, and the Legend
    /// Level-up refill.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The ruling on "to full, overflowing".</b> `28` C2 lists daily refills and level-up
    /// refills among the sources that overflow into the Reserve, and `10` §3.1 authors their amount
    /// as "to full". "To full" of a bar that is already full is <b>zero</b>: such a refill grants
    /// nothing, and therefore overflows nothing.
    /// </para>
    /// <para>
    /// The alternative reading — that a refill grants a whole Max Energy regardless of what the bar
    /// holds, so a player at maximum banks an entire second tank — would require inventing an
    /// amount (<c>+max</c>) that no document authors, and S6 forbids filling a hole with a
    /// plausible value. `28` C2's list is naming the sources that route through the overflow
    /// cascade rather than claiming each one always produces overflow; regeneration, quest grants
    /// and ad grants demonstrably do, which is enough to make the sentence true.
    /// </para>
    /// <para>
    /// It routes through <see cref="Grant"/> rather than assigning the bar directly, so a future
    /// source whose amount happens to exceed the deficit overflows correctly without a second
    /// cascade being written.
    /// </para>
    /// </remarks>
    /// <param name="tuning">The energy numbers, read from <c>tuning/progression.json</c>.</param>
    /// <param name="legendLevel">The player's Legend Level. Never negative.</param>
    /// <param name="banks">The two banks before the refill.</param>
    internal static EnergyBanks RefillToFull(EnergyTuning tuning, int legendLevel, EnergyBanks banks)
    {
        ArgumentNullException.ThrowIfNull(tuning);

        var deficit = Math.Max(0, MaxEnergy(tuning, legendLevel) - banks.Energy);

        return Grant(tuning, legendLevel, banks, deficit);
    }

    /// <summary>
    /// 🔒 `28` C2 — spending: "a run or dungeon draws from the main bar first, then from the
    /// Reserve for any shortfall. There is no button and no decision." Refused as a value when the
    /// two together cannot cover the cost, leaving both banks untouched.
    /// </summary>
    /// <remarks>
    /// Takes no tuning and no Legend Level: what a thing costs is the caller's business —
    /// <c>EnergyTuning.RunCost</c> for a run, M10's dungeon cost for a dungeon — and the draw order
    /// is the same either way.
    /// </remarks>
    /// <param name="banks">The two banks before the spend.</param>
    /// <param name="cost">What the action costs. Never negative; zero is a legal no-op.</param>
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
    /// 🔒 `28` C2's one deposit cascade, used by regeneration and by every grant alike: fill the
    /// main bar, put what it could not hold into the Reserve, discard the rest.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Written once, on purpose. `28` C2 says "Regeneration, daily refills, quest grants, ad
    /// grants, level-up refills and inbox attachments all overflow into it" — six sources with one
    /// routing rule, and a second copy of that rule is where the seventh source starts behaving
    /// differently.
    /// </para>
    /// <para>
    /// ⚠️ The headroom is floored at zero rather than asserted non-negative. Banks above the
    /// current maximum are possible after a balance patch that lowered <c>baseMax</c>, and this
    /// neither confiscates the excess nor adds to it: there is simply nowhere to put anything, and
    /// the player drains back under the cap by playing. `30` §11.5 puts the invariant on the
    /// aggregate.
    /// </para>
    /// <para>
    /// <paramref name="amount"/> is a <see cref="long"/> because regeneration counts units over an
    /// arbitrary offline span — three weeks at one per four minutes is 7,560, and an unbounded
    /// anchor is a persistence bug away from far more. The two <c>Math.Min</c> calls bound the
    /// result to the banks' capacities long before the casts back to <see cref="int"/>.
    /// </para>
    /// </remarks>
    private static EnergyBanks Deposit(EnergyBanks banks, long amount, int max, int reserveCapacity)
    {
        var intoBar = Math.Min(amount, Math.Max(0L, max - (long)banks.Energy));
        var intoReserve = Math.Min(
            amount - intoBar, Math.Max(0L, reserveCapacity - (long)banks.Reserve));

        return new EnergyBanks((int)(banks.Energy + intoBar), (int)(banks.Reserve + intoReserve));
    }

    private static void RequireLegendLevel(int legendLevel)
    {
        if (legendLevel < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(legendLevel),
                legendLevel,
                "A Legend Level is never negative — 07 §1.1 runs it from 1 to 200, and the range " +
                "itself is the Player aggregate's invariant to hold (30 §11.5). A negative one here " +
                "would shrink Max Energy below the base 120 that 10 §3 authors.");
        }
    }
}
