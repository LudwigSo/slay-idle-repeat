using System.Globalization;
using System.Text;

namespace SlayIdleRepeat.Core.Events;

/// <summary>The base of the domain event hierarchy. <c>GameRules.Apply</c> returns a list of these.</summary>
/// <param name="Sequence">The event's ordinal within one <c>Apply</c> call's event list — see the remarks.</param>
/// <remarks>
/// <para>
/// Four features are downstream of this list and have no other specified source: analytics
/// (projected from it), the economy event log (the same list, appended to storage), Feats
/// (counters increment off it rather than bespoke hooks), and client replay (the list is the
/// animation script).
/// </para>
/// <para>
/// Not event sourcing: the aggregates are the source of truth and are stored as state; events are
/// an output used for analytics, logging, replay and Feats.
/// </para>
/// <para>
/// <see cref="Sequence"/> is assigned by <c>GameRules.Apply</c> alone, never by a constructor or a
/// caller — a handler building an event doesn't yet know its position in the list. It is not the
/// wire <c>sequence</c> (the per-run/per-player command counter on the request envelope) — two
/// different numbers sharing a word.
/// </para>
/// <para>
/// Public and abstract: a bare <c>DomainEvent</c> in the returned list would be an analytics row
/// with no event name, a log row with no meaning and an animation frame with no instruction.
/// </para>
/// <para>
/// An event may hold primitives and value objects describing what changed — not a slice of content,
/// and not a timestamp (time enters the domain as <c>GameContext.NowUtc</c> only).
/// </para>
/// <para>
/// 🔒 <b>And it may hold a domain value record, narrowly.</b> This paragraph used to end "not an
/// aggregate", which would have made <c>GearGranted</c> — carrying the rolled item whole — a
/// contradiction in the same assembly. The ruling is that an event may name a type under
/// <c>Core/Model/</c> <em>only</em> when that type is an immutable, fully serialisable value record
/// with no mutators, and <em>never</em> an aggregate <b>root</b> nor any model type carrying an
/// <c>internal</c> mutator. The permission is forced by the four consumers above: all of them
/// serialise the list, so an event carrying an id instead of the value would send every one of them
/// back to an aggregate whose state has since moved on. The restriction is what keeps it from being
/// an open door — a root in an event is a mutation path around the single public one, handed to
/// whoever reads the list. <c>AccessibilityBoundaryTests</c> enforces it.
/// </para>
/// </remarks>
public abstract record DomainEvent(int Sequence)
{
    /// <summary>
    /// The <see cref="Sequence"/> a producer stamps on an event it has just built, before
    /// <c>GameRules.Apply</c> knows where in the list it belongs.
    /// </summary>
    internal const int UnstampedSequence = 0;

    /// <summary>
    /// Renders this event's members with <see cref="CultureInfo.InvariantCulture"/>. Every event in
    /// the hierarchy overrides it and appends its own; this base renders <see cref="Sequence"/>.
    /// </summary>
    /// <remarks>
    /// A record's synthesized <c>PrintMembers</c> formats through <c>StringBuilder.Append(object)</c>
    /// using the ambient culture, which automated culture-sensitivity checks can't see through the
    /// boxing — hence the hand-written override, enforced per event by an architecture rule.
    /// </remarks>
    /// <param name="builder">The builder the record's <c>ToString()</c> is assembling into.</param>
    /// <returns><see langword="true"/>, so <c>ToString()</c> spaces the closing brace.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is null.</exception>
    protected virtual bool PrintMembers(StringBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Append(CultureInfo.InvariantCulture, $"{nameof(Sequence)} = {Sequence}");

        return true;
    }
}
