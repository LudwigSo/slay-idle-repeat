using System.Globalization;
using System.Text;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Events;

/// <summary>
/// 🔒 A currency movement, with the reason it happened (`30` §7).
/// </summary>
/// <param name="Sequence">The event's ordinal within one <c>Apply</c> call's list — see <see cref="DomainEvent"/>.</param>
/// <param name="Id">Which of `10` §1's eight wallet currencies moved.</param>
/// <param name="Delta">
/// How much it moved by, signed: positive is income, negative is a spend. Not clamped, not
/// absolute-valued, and zero is permitted — see the remarks.
/// </param>
/// <param name="Reason">
/// 🔒 Why it moved. Never null, empty or whitespace — <see cref="Reason"/> refuses those at
/// construction.
/// </param>
/// <remarks>
/// <para>
/// 🔒 <b>Every currency movement in the game emits one of these, with a reason.</b> That single
/// rule is what makes `21` §8.3's <c>income_attribution.csv</c> — the report that answers risk
/// <b>R10</b>, the compounding of dungeon, event and guild income — a query over events rather
/// than thirty pieces of hand-written bookkeeping that will disagree with each other.
/// </para>
/// <para>
/// The mandatory half of that rule (<i>a currency mutation must emit</i>) is enforced by
/// <c>DomainPurityTests.Every_currency_mutation_emits_CurrencyChanged</c>, an IL scan that has
/// existed since M0-08. ⚠️ It is still <b>vacuous</b> as of this type's arrival: it recognises its
/// subjects by looking for a field a currency is <i>held</i> in, and the first of those comes with
/// the <c>Player</c> aggregate in M1-04. Declaring this type gives that rule a real event name to
/// look for; it does not wake it up.
/// </para>
/// <para>
/// <b>One event for all eight currencies, including <c>GOLD</c>.</b> Milestone assumption
/// <b>A3</b> makes <c>GOLD</c> run-scoped while the other seven are player-scoped, which splits
/// the <i>storage</i> across the <c>Run</c> and <c>Player</c> aggregates in M1-04/M1-05. It does
/// not split this event — if it did, `21` §8.3 would have to union two tables to answer one
/// question.
/// </para>
/// <para>
/// ⚠️ <b><c>Reason</c> is free text, deliberately and provisionally.</b> `30` §7 writes it as a
/// <c>string</c> and no document fixes a vocabulary, so closing it into an enum here would be
/// inventing one. That has a real cost — <c>income_attribution.csv</c> groups by this column, and
/// two spellings of the same reason are two rows — and the milestone that first emits at volume
/// (M1-10's energy math, then M4's grants) is the one placed to close it. Until then: prefer a
/// stable lower_snake_case token, and treat a new spelling as a schema change.
/// </para>
/// <para>
/// ⚠️ <b>A zero <see cref="Delta"/> is permitted, and that is a decision rather than an
/// oversight.</b> `30` §7 says every movement emits an event; it does not say every event is a
/// movement, and a clamp that had nothing left to give (M1-10's energy reserve) may legitimately
/// produce one. If a later milestone rules that a zero-delta row is noise in the economy log, that
/// is a design change and it should cost an edit here and in
/// <c>CurrencyChangedTests.A_zero_delta_is_permitted</c>.
/// </para>
/// </remarks>
public sealed record CurrencyChanged(int Sequence, CurrencyId Id, long Delta, string Reason)
    : DomainEvent(Sequence)
{
    /// <summary>
    /// 🔒 Why the currency moved. Never null, empty or whitespace.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Redeclared as <b>get-only</b> rather than left as the positional <c>init</c> property, and
    /// the difference is the guard: an <c>init</c> accessor is assignable through a <c>with</c>
    /// expression, and that assignment does not re-run the property initialiser. So
    /// <c>event with { Reason = "" }</c> would produce an unattributed row through a validated
    /// type. Get-only makes that a compile error instead. Every other component stays <c>init</c>,
    /// which is what lets <c>GameRules.Apply</c> (M1-06) stamp <see cref="DomainEvent.Sequence"/>
    /// with a <c>with</c> expression once it knows the event's position in the list.
    /// </para>
    /// <para>
    /// Same idiom as <c>PlayerId.Value</c>: validate in the property initialiser, so the primitive
    /// cannot exist in an invalid state rather than being checked wherever someone remembers to.
    /// </para>
    /// </remarks>
    public string Reason { get; } = RequireReason(Reason);

    /// <summary>
    /// 🔒 Renders this event with <see cref="CultureInfo.InvariantCulture"/> — see
    /// <see cref="DomainEvent.PrintMembers"/> for why the hierarchy declares these by hand.
    /// </summary>
    /// <remarks>
    /// <see cref="Delta"/> is the member that makes it matter: a spend of −10 renders through
    /// <c>StringBuilder.Append(object)</c> as <c>−10</c> (U+2212) under <c>sv-SE</c> and as
    /// <c>-10</c> (U+002D) in the CI container, so the same movement reads as two different strings
    /// in two logs.
    /// </remarks>
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

    /// <summary>
    /// The 🔒 guard behind <see cref="Reason"/>. Throws rather than substituting a placeholder:
    /// steering <b>S6</b> — an <c>"unknown"</c> that looks like a real attribution is worse in the
    /// R10 report than a movement that never shipped.
    /// </summary>
    /// <param name="reason">The candidate reason.</param>
    /// <returns>The reason, unchanged and untrimmed.</returns>
    /// <exception cref="ArgumentException">The reason is null, empty or whitespace.</exception>
    private static string RequireReason(string reason) =>
        string.IsNullOrWhiteSpace(reason)
            ? throw new ArgumentException(BlankReason, nameof(Reason))
            : reason;

    /// <summary>
    /// The failure message. It names the report that stops working, not just the null-ness — a
    /// reader who hits this needs to know that "pick any string" is the wrong fix.
    /// </summary>
    private const string BlankReason =
        "A CurrencyChanged carries a reason. 30 §7: \"Every currency movement in the game emits " +
        "CurrencyChanged with a reason.\" The reason is the attribution column of 21 §8.3's " +
        "income_attribution.csv — the report that answers risk R10, the compounding of dungeon, event " +
        "and guild income. A row with no reason is a row that report cannot use, and it is refused here " +
        "rather than logged and quietly dropped from the query later. Name the source of the movement " +
        "(a stable lower_snake_case token); do not substitute a placeholder.";
}
