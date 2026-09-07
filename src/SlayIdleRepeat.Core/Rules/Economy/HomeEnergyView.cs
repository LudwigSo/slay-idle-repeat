using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model.Snapshots;

namespace SlayIdleRepeat.Core.Rules.Economy;

/// <summary>
/// The four Energy numbers the Home screen's energy pill draws: what the bar holds, what it holds
/// out of, how long until the next point, and what a run costs.
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
public sealed record HomeEnergyView(int Current, int Max, TimeSpan RefillIn, int RunCost)
{
    /// <summary>Projects the four numbers from a player's row at an instant.</summary>
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

        throw new NotImplementedException(
            "HomeEnergyView.Project is a Phase 1 skeleton: the tests stating what it must answer " +
            "are written and red. Phase 3 implements it over EnergyTuning and EnergyMath.");
    }
}
