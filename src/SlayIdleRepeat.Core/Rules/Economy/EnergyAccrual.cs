using System.Globalization;
using System.Text;

using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Rules.Economy;

/// <summary>
/// Result of one accrual: the banks afterwards, and how far the accrual anchor advances.
/// </summary>
/// <remarks>
/// <see cref="AnchorAdvance"/> is <c>wholeUnits × the regen interval</c>, never the elapsed span
/// asked about — the caller advances its stored anchor by exactly this, so the sub-interval
/// remainder stays banked instead of being discarded every call. The anchor still advances even
/// when <see cref="Banks"/> doesn't (both banks full): otherwise time spent idle at full would be
/// lost the instant a single point is spent.
/// </remarks>
/// <param name="Banks">The two banks after the accrual.</param>
/// <param name="AnchorAdvance">
/// How far to move the accrual anchor: a whole multiple of the regeneration interval.
/// </param>
internal readonly record struct EnergyAccrual(EnergyBanks Banks, TimeSpan AnchorAdvance)
{
    /// <summary>
    /// Renders with <see cref="CultureInfo.InvariantCulture"/> so output doesn't vary by locale.
    /// </summary>
    private bool PrintMembers(StringBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Append(CultureInfo.InvariantCulture, $"Banks = {Banks}");
        builder.Append(CultureInfo.InvariantCulture, $", AnchorAdvance = {AnchorAdvance:c}");

        return true;
    }
}
