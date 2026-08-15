using System.Globalization;
using System.Text;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Events;

/// <summary>A currency movement, with the reason it happened.</summary>
/// <param name="Sequence">The event's ordinal within one <c>Apply</c> call's list — see <see cref="DomainEvent"/>.</param>
/// <param name="Id">Which wallet currency moved.</param>
/// <param name="Delta">How much it moved by, signed: positive is income, negative is a spend. Zero is permitted — see the remarks.</param>
/// <param name="Reason">Why it moved. Never null, empty or whitespace — <see cref="Reason"/> refuses those at construction.</param>
/// <remarks>
/// <para>
/// Every currency movement in the game emits one of these, with a reason — the rule that turns the
/// economy attribution report into a query over events rather than hand-written bookkeeping.
/// </para>
/// <para>
/// One event for all currencies, including run-scoped Gold, even though its storage lives on a
/// different aggregate than the player-scoped ones — splitting the event would force the report to
/// union two tables to answer one question.
/// </para>
/// <para>
/// <c>Reason</c> is free text, deliberately and provisionally: no document fixes a vocabulary yet,
/// so closing it into an enum here would mean inventing one. Prefer a stable lower_snake_case token.
/// </para>
/// <para>
/// A zero <see cref="Delta"/> is permitted: not every emitted event is a movement — a clamp that
/// had nothing left to give may legitimately produce one.
/// </para>
/// </remarks>
public sealed record CurrencyChanged(int Sequence, CurrencyId Id, long Delta, string Reason)
    : DomainEvent(Sequence)
{
    /// <summary>Why the currency moved. Never null, empty or whitespace.</summary>
    /// <remarks>
    /// Get-only rather than the positional <c>init</c> property: an <c>init</c> accessor is
    /// assignable through <c>with</c> without re-running the property initialiser, so
    /// <c>event with { Reason = "" }</c> would otherwise produce an unattributed row through a
    /// validated type.
    /// </remarks>
    public string Reason { get; } = RequireReason(Reason);

    /// <summary>Renders this event with <see cref="CultureInfo.InvariantCulture"/>.</summary>
    /// <param name="builder">The builder the record's <c>ToString()</c> is assembling into.</param>
    /// <returns><see langword="true"/>, so <c>ToString()</c> spaces the closing brace.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is null.</exception>
    protected override bool PrintMembers(StringBuilder builder)
    {
        base.PrintMembers(builder);

        builder.Append(CultureInfo.InvariantCulture, $", {nameof(Id)} = {Id}");
        builder.Append(CultureInfo.InvariantCulture, $", {nameof(Delta)} = {Delta}");
        builder.Append(CultureInfo.InvariantCulture, $", {nameof(Reason)} = {Reason}");

        return true;
    }

    /// <summary>The guard behind <see cref="Reason"/>. Throws rather than substituting a placeholder.</summary>
    /// <param name="reason">The candidate reason.</param>
    /// <returns>The reason, unchanged and untrimmed.</returns>
    /// <exception cref="ArgumentException">The reason is null, empty or whitespace.</exception>
    private static string RequireReason(string reason) =>
        string.IsNullOrWhiteSpace(reason)
            ? throw new ArgumentException(BlankReason, nameof(Reason))
            : reason;

    private const string BlankReason =
        "A CurrencyChanged carries a reason. 30 §7: \"Every currency movement in the game emits " +
        "CurrencyChanged with a reason.\" The reason is the attribution column of 21 §8.3's " +
        "income_attribution.csv — the report that answers risk R10, the compounding of dungeon, event " +
        "and guild income. A row with no reason is a row that report cannot use, and it is refused here " +
        "rather than logged and quietly dropped from the query later. Name the source of the movement " +
        "(a stable lower_snake_case token); do not substitute a placeholder.";
}
