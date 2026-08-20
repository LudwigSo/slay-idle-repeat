using System.Globalization;
using System.Text;

namespace SlayIdleRepeat.Core.Events;

/// <summary>A die roll, with the number it came up.</summary>
/// <param name="Sequence">The event's ordinal within one <c>Apply</c> call's list — see <see cref="DomainEvent"/>.</param>
/// <param name="Pips">The number rolled, 1..6. The movement before the run's curses have their say.</param>
public sealed record DiceRolled(int Sequence, int Pips) : DomainEvent(Sequence)
{
    /// <inheritdoc/>
    protected override bool PrintMembers(StringBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Append(CultureInfo.InvariantCulture, $"{nameof(Sequence)} = {Sequence}, ");
        builder.Append(CultureInfo.InvariantCulture, $"{nameof(Pips)} = {Pips}");

        return true;
    }
}
