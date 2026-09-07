using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Rules.Economy;

/// <summary>
/// The five Energy numbers the Home screen draws: what the bar holds, what it holds out of, how
/// long until the next point, what a run costs, and how much of that cost the two banks cannot
/// cover.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>The sanctioned route out of <c>Core</c> for a derived Energy number.</b>
/// <see cref="EnergyTuning"/> and <see cref="EnergyMath"/> are <c>internal</c> and stay so, so a
/// screen that wanted a maximum or a countdown had two options: go without one — which is what
/// <c>HomePresenter</c> did, and said so — or copy the formula, which parts company with the rules
/// the first time either is tuned. This is the third: a public projection, in the layer that owns
/// the arithmetic. Same construction, and the same reason, as <see cref="RunEndView"/> and
/// <see cref="ShopView"/>.
/// </para>
/// <para>
/// 🔒 <b>Takes <c>now</c> as a value, never a clock.</b> <c>Core</c> has no clock and may not have
/// one; a rule that read the time would not be a function of its inputs, and the whole of this
/// projection is time-dependent.
/// </para>
/// </remarks>
/// <param name="Current">The main bar after accrual to the instant asked about.</param>
/// <param name="Max">What the main bar holds at the player's Legend Level.</param>
/// <param name="RefillIn">
/// How long until the next whole point accrues, or <see cref="TimeSpan.Zero"/> when both banks are
/// full and nothing is accruing anywhere. Zero is the pill's instruction to hide its caption, and it
/// is answered here rather than derived on the screen from <paramref name="Current"/> against
/// <paramref name="Max"/>: a full main bar does not mean regeneration has stopped, because the
/// overflow still fills the Reserve, and a caption hidden at a full BAR would hide a countdown that
/// is still running.
/// </param>
/// <param name="RunCost">What one run costs.</param>
/// <param name="Shortfall">
/// How much more Energy a run needs than the two banks hold together, or zero when they cover it.
/// <para>
/// 🔒 <b>Over the bar AND the Reserve, because that is what a run is paid from.</b>
/// <c>EnergyMath.Spend</c> draws the main bar first and the Reserve for the remainder, so a screen
/// deciding affordability from <see cref="Current"/> against <see cref="RunCost"/> refuses a tap the
/// rules would have accepted — a player holding 17 in the bar and 100 in the Reserve can start a
/// 20-cost run, and would be shown a refill offer instead. The <em>pill</em> still reads
/// <see cref="Current"/> out of <see cref="Max"/>: the Reserve is a separate bank, not part of the
/// bar's denominator.
/// </para>
/// </param>
public sealed record HomeEnergyView(
    int Current, int Max, TimeSpan RefillIn, int RunCost, int Shortfall)
{
    /// <summary>Projects the five numbers from a player's row at an instant.</summary>
    /// <param name="player">The player's row — the banks, the anchor and the Legend Level.</param>
    /// <param name="content">The loaded content set the energy block is read from.</param>
    /// <param name="now">
    /// The instant to accrue to. A value, never an ambient reading, and never optional: a default
    /// would be a clock inside <c>Core</c> under another name.
    /// </param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="MissingContentException">The energy block is not authored.</exception>
    public static HomeEnergyView Project(
        PlayerSnapshot player, ContentSnapshot content, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(content);

        var tuning = EnergyTuning.Read(content);
        var level = player.LegendLevel;

        // 🔒 Clamped HERE rather than in EnergyMath.Accrue, which throws on a negative span by
        // design so a persisted anchor stuck in the future cannot go undetected in a command. This
        // is a read, and a screen is the wrong place to surface that fault: the pill would take the
        // whole Home screen down with it. GameRules.AdvanceTime clamps at its own seam for the same
        // reason.
        var sinceAnchor = now - player.EnergyAnchorUtc;
        var elapsed = TimeSpan.FromTicks(Math.Max(0L, sinceAnchor.Ticks));

        var banks = EnergyMath.Accrue(tuning, level, player.Energy, elapsed).Banks;

        return new HomeEnergyView(
            banks.Energy,
            EnergyMath.MaxEnergy(tuning, level),
            TimeToNextPoint(tuning, level, banks, elapsed),
            tuning.RunCost,
            UncoveredCost(banks, tuning.RunCost));
    }

    /// <summary>
    /// How long until the next whole point lands anywhere, or <see cref="TimeSpan.Zero"/> when both
    /// banks are at capacity and no elapsed time can add one.
    /// </summary>
    /// <remarks>
    /// Counted to the NEXT interval boundary, never from the last one: the remainder an accrual left
    /// behind is <c>elapsed mod interval</c>, so what is left to wait is the interval less that
    /// remainder. An elapsed span landing exactly on a boundary has a remainder of nothing and is
    /// therefore a whole interval away from the point after it — subtracting the elapsed span from
    /// the interval instead would answer zero (the caption hidden while it is still counting) or a
    /// negative span (a caption counting backwards) for every anchor more than one interval old.
    /// </remarks>
    private static TimeSpan TimeToNextPoint(
        EnergyTuning tuning, int legendLevel, EnergyBanks banks, TimeSpan elapsed)
    {
        var full = banks.Energy >= EnergyMath.MaxEnergy(tuning, legendLevel) &&
            banks.Reserve >= EnergyMath.ReserveCapacity(tuning, legendLevel);

        if (full)
        {
            return TimeSpan.Zero;
        }

        var interval = tuning.RegenInterval.Ticks;

        return TimeSpan.FromTicks(interval - (elapsed.Ticks % interval));
    }

    /// <summary>
    /// How much of a run's price the two banks cannot cover together, or zero when they can.
    /// </summary>
    /// <remarks>
    /// 🔒 <b>Whether the price is covered is <see cref="EnergyMath.Spend"/>'s verdict, not this
    /// type's.</b> A run draws the main bar first and the Reserve for the remainder, so asking the
    /// rule that performs the draw is the only way this projection and the command that charges it
    /// can be guaranteed to agree — a screen with its own affordability test refuses a tap the rules
    /// would have accepted the day either changes. Only the size of the gap is arithmetic here, and
    /// only on the branch the rule has already declared unaffordable.
    /// </remarks>
    private static int UncoveredCost(EnergyBanks banks, int runCost) =>
        EnergyMath.Spend(banks, runCost).IsAffordable
            ? 0
            : (int)(runCost - ((long)banks.Energy + banks.Reserve));
}
