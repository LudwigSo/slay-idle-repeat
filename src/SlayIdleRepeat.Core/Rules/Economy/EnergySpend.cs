using System.Globalization;
using System.Text;

using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Rules.Economy;

/// <summary>
/// What one spend did: whether the two banks could cover the cost, what they hold afterwards, and
/// how the cost was split between them.
/// </summary>
/// <remarks>
/// The split is reported rather than chosen by a caller — draw order is always main bar first, then
/// Reserve for the shortfall. Unaffordable is a value, not an exception: when
/// <see cref="IsAffordable"/> is false both banks come back exactly as they went in, since a partial
/// draw would charge a player for a run they never got.
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
