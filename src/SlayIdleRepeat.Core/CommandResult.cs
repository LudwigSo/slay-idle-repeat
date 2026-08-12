using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core;

/// <summary>
/// 🔒 `30` §2 — everything one <c>GameRules.Apply</c> call produces: whether the command was
/// accepted, why not if it was not, the complete resulting state, and the events it emitted.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>A rejection is a value, not an exception</b> (`30` §2.1, <b>P3</b>). <em>"Every command on
/// every state returns a result. Illegal moves return <c>Rejection</c>, they do not throw."</em> The
/// converse is just as load-bearing and is what the guards below draw: an <em>exception</em> out of
/// <c>Apply</c> means the <b>caller</b> or the <b>domain</b> is wrong (a null argument, a slice
/// missing the run its command needs, a handler that hand-wrote an RNG counter), never that the
/// player asked for something they cannot have.
/// </para>
/// <para>
/// 🔒 <b>Only domain-tier rejections exist here, and that is checked rather than documented</b>
/// (`30` §2, `14` §16.2). Two tiers produce a <see cref="RejectionReason"/>: the transport tier —
/// malformed envelopes, sequence and idempotency conflicts, rate limits, protocol and content
/// version, <c>RUN_NOT_FOUND</c> — rejects before the domain is ever invoked, so those values never
/// appear in a <c>CommandResult</c>. A handler that returned <c>RATE_LIMITED</c> is a <b>defect</b>,
/// not a rejection: it would tell the player the server was busy about a rule that simply said no,
/// and it would make the one wire field ambiguous about which producer wrote it.
/// <see cref="Rejection"/> refuses it at construction, so the value cannot be built anywhere at all
/// rather than only being checked at the one seam somebody remembered.
/// </para>
/// <para>
/// 🔒 <b>Every component is <c>get</c>-only, so there is no <c>with</c> path to bypass.</b> Same
/// idiom, and the same reason, as <c>CurrencyChanged.Reason</c>: an <c>init</c> accessor is
/// assignable through a <c>with</c> expression and that assignment does <b>not</b> re-run the
/// property initialiser, so <c>result with { Rejection = RejectionReason.RATE_LIMITED }</c> would
/// walk straight past the tier guard. Get-only makes it a compile error. Nothing in the game amends
/// a command result — <c>Apply</c> builds it and every consumer reads it — so nothing is lost.
/// </para>
/// <para>
/// ⚠️ <b><c>default(CommandResult)</c> is not a result, and says so.</b> `30` §2 writes this as a
/// <c>readonly record struct</c>, which means the language can hand out an instance that ran no
/// constructor — <c>Accepted = false</c>, <c>Rejection = null</c>, a combination
/// <see cref="CommandResult(bool, RejectionReason?, WorldSlice, IReadOnlyList{DomainEvent})"/>
/// refuses. <c>Result&lt;T&gt;</c> answered the same problem by being a class; the shape here is
/// specified, so instead <see cref="NewState"/> and <see cref="Events"/> throw a described failure
/// rather than answering <c>null</c> three frames from where the uninitialised field was read.
/// </para>
/// </remarks>
/// <param name="Accepted">
/// Whether the command changed the state. Exactly the negation of "there is a
/// <paramref name="Rejection"/>" — the two are checked against each other at construction.
/// </param>
/// <param name="Rejection">
/// 🔒 Why the command was refused, or <c>null</c> when it was accepted. Domain tier only.
/// </param>
/// <param name="NewState">
/// 🔒 The complete resulting state (`30` §2.1, <b>P4</b>). On a rejection this is the slice
/// <c>Apply</c> was handed, unchanged and by construction: a rejected command's handler only ever
/// touched a clone, which is discarded.
/// </param>
/// <param name="Events">
/// The events this one command produced, in order, each stamped with its
/// <c>DomainEvent.Sequence</c> by <c>Apply</c>. Empty on a rejection — a command that changed
/// nothing has nothing to log (`14` §7.1), nothing to animate (`14` §2.4) and nothing to count
/// (`28` D).
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

    /// <summary>
    /// The result of a command the domain refused.
    /// </summary>
    /// <param name="rejection">🔒 A domain-tier reason. A transport-tier value is refused.</param>
    /// <param name="unchangedState">
    /// 🔒 The slice <c>Apply</c> was handed, <b>unchanged</b>. Not a copy and not a rebuilt slice:
    /// `30` §2.1's <b>P4</b> is what lets the Application layer keep the state it loaded when a
    /// command is refused, instead of having to reason about how far a rejected handler got.
    /// </param>
    public static CommandResult Reject(RejectionReason rejection, WorldSlice unchangedState) =>
        new(Accepted: false, rejection, unchangedState, NoEvents);

    /// <summary>
    /// 🔒 Renders the result with <see cref="CultureInfo.InvariantCulture"/>, and without reading
    /// the two throwing accessors — so <c>default(CommandResult).ToString()</c> describes the
    /// default rather than raising out of a debugger's tooltip.
    /// </summary>
    /// <remarks>
    /// The culture half is <c>GameContext.PrintMembers</c>' reasoning: a record's synthesized
    /// <c>PrintMembers</c> appends through <c>StringBuilder.Append(object)</c>, which formats with
    /// the <em>ambient</em> culture and which the IL scan behind
    /// <c>AmbientApiTests.Core_and_Application_contain_no_culture_sensitive_formatting</c> cannot
    /// see through the boxing. `14` §8.2 wants <c>Core</c> reading identically everywhere.
    /// </remarks>
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

    /// <summary>
    /// 🔒 <b>The tier boundary of `30` §2, enforced at construction.</b> <c>Apply</c> returns only
    /// the domain-tier values of `14` §16.2.
    /// </summary>
    /// <remarks>
    /// It leans on <c>RejectionReasons.TierOf</c> rather than restating the two lists, which also
    /// means an <b>undeclared</b> value — <c>default(RejectionReason)</c>, or a number cast in from
    /// the wire — throws there instead of quietly acquiring a tier here.
    /// </remarks>
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

    /// <summary>
    /// 🔒 The three-way agreement between <see cref="Accepted"/>, <see cref="Rejection"/> and
    /// <see cref="Events"/>. Every one of the four illegal combinations is a different defect and
    /// says so (steering <b>S2</b>).
    /// </summary>
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
