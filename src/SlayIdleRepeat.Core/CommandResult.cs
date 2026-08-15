using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core;

/// <summary>
/// Everything one <c>GameRules.Apply</c> call produces: whether the command was accepted, why not
/// if it was not, the complete resulting state, and the events it emitted.
/// </summary>
/// <remarks>
/// <para>
/// A rejection is a value, not an exception: every command on every state returns a result, and
/// illegal moves return <see cref="Rejection"/> rather than throwing. An exception out of
/// <c>Apply</c> means the caller or the domain is wrong, never that the player asked for something
/// they cannot have.
/// </para>
/// <para>
/// Only domain-tier rejections may appear here — transport-tier ones (malformed envelopes,
/// sequence/idempotency conflicts, rate limits, protocol version) reject before the domain is ever
/// invoked. <see cref="Rejection"/> refuses a transport-tier value at construction, so a handler
/// can't accidentally tell the player "rate limited" about a rule that simply said no.
/// </para>
/// <para>
/// Every component is <c>get</c>-only rather than <c>init</c>, so a <c>with</c> expression cannot
/// bypass the tier guard above (an <c>init</c> setter skips the property initialiser).
/// </para>
/// <para>
/// Because this is a <c>readonly record struct</c>, the runtime can hand out a
/// <c>default(CommandResult)</c> that ran no constructor. <see cref="NewState"/> and
/// <see cref="Events"/> throw a descriptive failure for that case instead of returning <c>null</c>.
/// </para>
/// </remarks>
/// <param name="Accepted">
/// Whether the command changed the state. Exactly the negation of "there is a
/// <paramref name="Rejection"/>" — the two are checked against each other at construction.
/// </param>
/// <param name="Rejection">Why the command was refused, or <c>null</c> when it was accepted. Domain tier only.</param>
/// <param name="NewState">
/// The complete resulting state. On a rejection this is the slice <c>Apply</c> was handed,
/// unchanged — a rejected command's handler only ever touched a clone, which is discarded.
/// </param>
/// <param name="Events">
/// The events this command produced, in order, each stamped with its sequence by <c>Apply</c>.
/// Empty on a rejection.
/// </param>
public readonly record struct CommandResult(
    bool Accepted,
    RejectionReason? Rejection,
    WorldSlice NewState,
    IReadOnlyList<DomainEvent> Events)
{
    /// <summary>The shared empty event list, so an accepted no-op allocates nothing.</summary>
    private static readonly ReadOnlyCollection<DomainEvent> NoEvents =
        Array.AsReadOnly(Array.Empty<DomainEvent>());

    private readonly WorldSlice? _newState = RequireState(NewState);
    private readonly IReadOnlyList<DomainEvent>? _events = RequireEvents(Events);

    /// <inheritdoc cref="CommandResult(bool, RejectionReason?, WorldSlice, IReadOnlyList{DomainEvent})"
    ///     path="/param[@name='Accepted']"/>
    public bool Accepted { get; } = RequireAgreement(Accepted, Rejection, Events);

    /// <inheritdoc cref="CommandResult(bool, RejectionReason?, WorldSlice, IReadOnlyList{DomainEvent})"
    ///     path="/param[@name='Rejection']"/>
    public RejectionReason? Rejection { get; } = RequireDomainTier(Rejection);

    /// <inheritdoc cref="CommandResult(bool, RejectionReason?, WorldSlice, IReadOnlyList{DomainEvent})"
    ///     path="/param[@name='NewState']"/>
    /// <exception cref="InvalidOperationException">This is <c>default(CommandResult)</c>.</exception>
    public WorldSlice NewState => _newState ?? throw Uninitialised(nameof(NewState));

    /// <inheritdoc cref="CommandResult(bool, RejectionReason?, WorldSlice, IReadOnlyList{DomainEvent})"
    ///     path="/param[@name='Events']"/>
    /// <exception cref="InvalidOperationException">This is <c>default(CommandResult)</c>.</exception>
    public IReadOnlyList<DomainEvent> Events => _events ?? throw Uninitialised(nameof(Events));

    /// <summary>The result of a command that changed the state.</summary>
    /// <param name="newState">The resulting slice. Never null.</param>
    /// <param name="events">The events, already stamped. Never null; may be empty.</param>
    public static CommandResult Accept(WorldSlice newState, IReadOnlyList<DomainEvent> events) =>
        new(Accepted: true, Rejection: null, newState, events);

    /// <summary>The result of a command the domain refused.</summary>
    /// <param name="rejection">A domain-tier reason. A transport-tier value is refused.</param>
    /// <param name="unchangedState">
    /// The slice <c>Apply</c> was handed, unchanged — not a copy, not a rebuilt slice, so the
    /// Application layer can keep the state it loaded rather than reasoning about how far a
    /// rejected handler got.
    /// </param>
    public static CommandResult Reject(RejectionReason rejection, WorldSlice unchangedState) =>
        new(Accepted: false, rejection, unchangedState, NoEvents);

    /// <summary>
    /// Renders the result without reading the two throwing accessors, so
    /// <c>default(CommandResult).ToString()</c> describes the default instead of raising.
    /// </summary>
    private bool PrintMembers(StringBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Append(CultureInfo.InvariantCulture, $"Accepted = {Accepted}");
        builder.Append(CultureInfo.InvariantCulture, $", Rejection = {Describe(Rejection)}");
        builder.Append(CultureInfo.InvariantCulture, $", NewState = {(_newState is null ? "default" : "present")}");
        builder.Append(CultureInfo.InvariantCulture, $", Events = {(_events?.Count.ToString(CultureInfo.InvariantCulture) ?? "default")}");

        return true;
    }

    private static string Describe(RejectionReason? rejection) => rejection?.ToString() ?? "none";

    /// <summary>Enforces that only domain-tier rejections can be constructed here.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is transport tier, or is not a declared reason.</exception>
    private static RejectionReason? RequireDomainTier(RejectionReason? rejection)
    {
        if (rejection is not { } reason || RejectionReasons.IsDomainTier(reason))
        {
            return rejection;
        }

        throw new ArgumentOutOfRangeException(
            nameof(Rejection),
            reason,
            "TRANSPORT-TIER REJECTION IN A CommandResult. 14 §16.2 splits the catalogue between two " +
            "producers and 30 §2 is explicit that the transport tier 'rejects before the domain is " +
            "ever invoked, so those values never appear in a CommandResult; Apply returns only the " +
            "domain-tier values'. Returning " + reason + " from a handler is a DEFECT, not a " +
            "rejection: the client would be told the envelope, the sequence or the rate limit was at " +
            "fault about a rule that simply said no, and the one wire field would stop saying which " +
            "producer wrote it. Pick the domain-tier value that describes the rule (ILLEGAL_STATE is " +
            "the catch-all), or let the transport reject it before Apply is called.");
    }

    /// <summary>Enforces agreement between <see cref="Accepted"/>, <see cref="Rejection"/> and <see cref="Events"/>.</summary>
    /// <exception cref="ArgumentException">The three do not describe one outcome.</exception>
    private static bool RequireAgreement(
        bool accepted, RejectionReason? rejection, IReadOnlyList<DomainEvent>? events)
    {
        if (accepted && rejection is not null)
        {
            throw new ArgumentException(
                "An ACCEPTED result carries a Rejection (" + rejection + "). 30 §2 makes the two " +
                "halves of one answer: Accepted is exactly 'there is no Rejection'. A result that " +
                "says both would be persisted as a state change and reported to the player as a " +
                "refusal.",
                nameof(Accepted));
        }

        if (!accepted && rejection is null)
        {
            throw new ArgumentException(
                "A REFUSED result carries no Rejection. 30 §2.1's P3 makes an illegal move data: the " +
                "reason IS the answer, and 14 §16.2's wire field has no row for 'refused, cause " +
                "unstated'. If the command was in fact accepted, say so; if a rule refused it, name " +
                "the rule's domain-tier reason.",
                nameof(Rejection));
        }

        if (!accepted && events is { Count: > 0 })
        {
            throw new ArgumentException(
                "A REFUSED result carries " + events.Count.ToString(CultureInfo.InvariantCulture) +
                " event(s). A refused command changed nothing, so there is nothing to append to " +
                "14 §7.1's economy log, nothing for 14 §2.4's client to animate and nothing for " +
                "28 D's Feat counters to count. Events produced before a rule refused belong to a " +
                "state that was discarded with the rejection.",
                nameof(Events));
        }

        return accepted;
    }

    private static WorldSlice RequireState(WorldSlice newState) =>
        newState ?? throw new ArgumentNullException(
            nameof(NewState),
            "A CommandResult always carries the resulting state — 30 §2 calls it 'the complete " +
            "resulting state', and on a rejection it is the slice Apply was handed, unchanged. A " +
            "null would make the Application layer choose between persisting nothing and persisting " +
            "the state it happened to still be holding.");

    private static IReadOnlyList<DomainEvent> RequireEvents(IReadOnlyList<DomainEvent> events) =>
        events ?? throw new ArgumentNullException(
            nameof(Events),
            "A CommandResult always carries an event list; an EMPTY list is how a command that " +
            "produced none says so. A null is not an empty list: 14 §10.1's analytics, 14 §7.1's " +
            "economy log, 28 D's Feats and 14 §2.4's replay all read this list, and each would have " +
            "to invent its own answer for what a null means.");

    private static InvalidOperationException Uninitialised(string member) =>
        new(
            "This is default(CommandResult), so it has no " + member + ". 30 §2 writes CommandResult " +
            "as a readonly record struct, which means the language can produce one that ran no " +
            "constructor — from an uninitialised field or an unfilled array element — and such a " +
            "value carries Accepted = false with no Rejection, a combination the constructor refuses. " +
            "Build results with GameRules.Apply, CommandResult.Accept or CommandResult.Reject.");
}
