using System.Globalization;
using System.Text;

namespace SlayIdleRepeat.Core.Events;

/// <summary>A held fixed die spent instead of a roll.</summary>
/// <param name="Sequence">The event's ordinal within one <c>Apply</c> call's list — see <see cref="DomainEvent"/>.</param>
/// <param name="Pips">The number the die showed, 1..6. The movement, exactly.</param>
/// <remarks>
/// 🔒 <b>Its own event rather than a <see cref="DiceRolled"/> with a flag.</b> Nothing was rolled: no
/// dice-stream draw was taken, the number was chosen when the die was granted, and the two are
/// counted apart in the lifetime Feat counters. A shared event would make "how many times have you
/// rolled" unanswerable without reading a discriminator, which is the collapse the log exists to
/// avoid.
/// </remarks>
public sealed record FixedDieUsed(int Sequence, int Pips) : DomainEvent(Sequence)
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
