using System.Globalization;
using System.Text;
using SlayIdleRepeat.Core.Content.Dice;

namespace SlayIdleRepeat.Core.Events;

/// <summary>
/// 🔒 `30` §7 — a die roll, with the face it landed on. Declared by M3-04, the task the (now-removed)
/// <c>GapRegister.Deferred</c> row for <c>DiceRolled</c> named as its owner: <see cref="Content.Dice.DieFace"/>
/// exists, so this event can be written.
/// </summary>
/// <param name="Sequence">The event's ordinal within one <c>Apply</c> call's list — see <see cref="DomainEvent"/>.</param>
/// <param name="Face">The face the roll resolved to, after every upgrade source `04` §2 lists has already applied.</param>
public sealed record DiceRolled(int Sequence, DieFace Face) : DomainEvent(Sequence)
{
    /// <inheritdoc/>
    protected override bool PrintMembers(StringBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Append(CultureInfo.InvariantCulture, $"{nameof(Sequence)} = {Sequence}, ");
        builder.Append(CultureInfo.InvariantCulture, $"{nameof(Face)} = {Face.Kind}");

        if (Face.Kind == DieFaceKind.Pip)
        {
            builder.Append(CultureInfo.InvariantCulture, $"({Face.Value})");
        }

        return true;
    }
}
