using System.Globalization;
using System.Text;

namespace SlayIdleRepeat.Core.Rules.Economy;

/// <summary>
/// 🔒 What one spend did: whether the two banks could cover the cost, what they hold afterwards,
/// and how the cost was split between them.
/// </summary>
/// <remarks>
/// <para>
/// `28` C2: <em>"A run or dungeon draws from the main bar first, then from the Reserve for any
/// shortfall. There is no button and no decision."</em> The split is reported rather than chosen by
/// a caller, and it is reported because callers need it — `30` §7's economy log records where a
/// cost was paid from, and `28` C2's UI draws the Reserve as a second segment.
/// </para>
/// <para>
/// 🔒 Unaffordable is a <b>value</b>, not an exception (`30` §2.1 — an illegal move is data). The
/// handler turns <see cref="IsAffordable"/> being false into
/// <c>RejectionReason.INSUFFICIENT_ENERGY</c>; this rule knows nothing about rejection reasons and
/// does not need to. When it is false both banks come back exactly as they went in — a partial
/// draw would charge a player for a run they never got.
/// </para>
/// </remarks>
/// <param name="IsAffordable">Whether the main bar and the Reserve together covered the cost.</param>
/// <param name="Banks">The two banks afterwards — unchanged when the cost was not affordable.</param>
/// <param name="DrawnFromBar">How much came out of the main bar. Zero when unaffordable.</param>
/// <param name="DrawnFromReserve">How much came out of the Reserve. Zero when unaffordable.</param>
internal readonly record struct EnergySpend(
    bool IsAffordable,
    EnergyBanks Banks,
    int DrawnFromBar,
    int DrawnFromReserve)
{
    /// <summary>
    /// Renders the split as a sentence, replacing the synthesized <c>PrintMembers</c>.
    /// </summary>
    /// <remarks>
    /// ⚠️ This one is for <b>readability</b>, not for `14` §8.2: a <c>bool</c> and two non-negative
    /// <c>int</c>s render identically under every culture, so there is no determinism argument here
    /// — unlike <see cref="EnergyAccrual"/>, whose <see cref="TimeSpan"/> genuinely varies. The
    /// provider is passed anyway so the three values of <c>Rules/Economy/</c> read the same way and
    /// nobody has to work out which of them needed it.
    /// </remarks>
    private bool PrintMembers(StringBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (!IsAffordable)
        {
            builder.Append(CultureInfo.InvariantCulture, $"unaffordable from {Banks}");

            return true;
        }

        builder.Append(
            CultureInfo.InvariantCulture,
            $"paid {DrawnFromBar} from the bar and {DrawnFromReserve} from the reserve, leaving {Banks}");

        return true;
    }
}
