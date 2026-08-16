using System.Globalization;
using System.Text;
using SlayIdleRepeat.Core.Model.Gear;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Events;

/// <summary>A gear item was granted, and where it came from.</summary>
/// <param name="Sequence">The event's ordinal within one <c>Apply</c> call's list — see <see cref="DomainEvent"/>.</param>
/// <param name="Item">The rolled item, whole.</param>
/// <param name="Source">The grant source class the item came from, and therefore which counters moved.</param>
/// <param name="FromPity">
/// Whether a guarantee forced the rarity. Reported rather than inferred: a natural draw can land on
/// the rarity a guarantee would have forced, and the two are different events to a player, to the
/// analytics stream and to duplicate protection.
/// </param>
/// <remarks>
/// <para>
/// <b>It carries the item itself, not an id, and that is the one place an event may name a
/// <c>Model/</c> type.</b> All four consumers of the event list need the item's contents rather than
/// a handle to it: analytics projects a row, the economy log appends one, Feats count off it and the
/// client replays it as an animation. An id would send every one of them back to the aggregate for
/// state that has since moved on.
/// </para>
/// <para>
/// The permission is narrow and is enforced rather than promised. A gear instance is an immutable,
/// fully serialisable value record with no mutators — snapshot-shaped — which is exactly the
/// condition the architecture rule tests. An event may never name an aggregate <em>root</em>, nor any
/// <c>Model/</c> type that carries an internal mutator: that would hand the outside world a mutation
/// path around the single public one.
/// </para>
/// <para>
/// The event carries no counter movement. A grant can move several counters at once, so the counters
/// are reported by their own event and this one says only that an item arrived and whether a
/// guarantee produced it.
/// </para>
/// </remarks>
public sealed record GearGranted(int Sequence, GearInstance Item, SourceClass Source, bool FromPity)
    : DomainEvent(Sequence)
{
    /// <summary>The rolled item, whole. Never null.</summary>
    /// <remarks>
    /// Get-only rather than the positional <c>init</c> property, on <c>CurrencyChanged.Reason</c>'s
    /// precedent: an <c>init</c> accessor is assignable through <c>with</c> without re-running the
    /// property initialiser, so <c>event with { Item = null! }</c> would otherwise produce a grant of
    /// nothing through a validated type.
    /// </remarks>
    public GearInstance Item { get; } = RequireItem(Item);

    /// <summary>Renders this event with <see cref="CultureInfo.InvariantCulture"/>.</summary>
    /// <param name="builder">The builder the record's <c>ToString()</c> is assembling into.</param>
    /// <returns><see langword="true"/>, so <c>ToString()</c> spaces the closing brace.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is null.</exception>
    protected override bool PrintMembers(StringBuilder builder)
    {
        base.PrintMembers(builder);

        builder.Append(CultureInfo.InvariantCulture, $", {nameof(Item)} = {Item}");
        builder.Append(CultureInfo.InvariantCulture, $", {nameof(Source)} = {Source}");
        builder.Append(CultureInfo.InvariantCulture, $", {nameof(FromPity)} = {FromPity}");

        return true;
    }

    /// <summary>The guard behind <see cref="Item"/>. Throws rather than emitting an empty grant.</summary>
    /// <param name="item">The candidate item.</param>
    /// <returns>The item, unchanged.</returns>
    /// <exception cref="ArgumentNullException">The item is null.</exception>
    private static GearInstance RequireItem(GearInstance item) =>
        item ?? throw new ArgumentNullException(
            nameof(Item),
            "A GearGranted names the item that was granted. The economy log, the analytics " +
            "projection, the Feat counters and the client's replay all read the item out of this " +
            "event, and an event with no item is a grant none of them can attribute or animate.");
}
