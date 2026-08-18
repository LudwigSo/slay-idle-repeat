namespace SlayIdleRepeat.Core.Primitives;

/// <summary>The rejection catalogue: one enum, one wire field, two producers.</summary>
/// <remarks>
/// A rejection is a successful protocol exchange whose answer is no — it rides HTTP 200 in a
/// rejection envelope, alongside a <c>stateHash</c> of the untouched state. HTTP status codes are
/// reserved for transport failures, so this is not an error type.
/// <para>
/// Two tiers produce it, deliberately as one enum: the <b>transport</b> tier (malformed envelopes,
/// sequence/idempotency conflicts, rate limits, version mismatches) is decided by the server host
/// before the domain is invoked and never appears in a <c>CommandResult</c>; the <b>domain</b> tier
/// is what <c>GameRules.Apply</c> returns. Splitting them into two enums would give the client two
/// fields to switch on for one question. <see cref="RejectionReasons.TierOf"/> answers which tier a
/// value belongs to.
/// </para>
/// <para>
/// The numeric values are wire values and permanent: <c>CanonicalStateWriter</c> encodes an enum by
/// its number, never its name, so values may only be appended — never renamed, renumbered or reused.
/// A client receiving an unknown value treats it as a generic rejection and resyncs.
/// </para>
/// <para>
/// Member names are the wire spelling verbatim, not C# PascalCase, so there is no mapping table
/// between the two to drift. There is no <c>0</c> member; absence is modelled by a null
/// <see cref="RejectionReason"/> instead.
/// </para>
/// </remarks>
public enum RejectionReason
{
    // Transport tier — produced by the server host / Application layer, never by GameRules.Apply.

    /// <summary>Envelope parsed, but the command failed schema validation.</summary>
    MALFORMED_COMMAND = 1,

    /// <summary>The <c>type</c> is not in the command registry.</summary>
    UNKNOWN_COMMAND_TYPE = 2,

    /// <summary>Outside the supported protocol-version window.</summary>
    PROTOCOL_VERSION_UNSUPPORTED = 3,

    /// <summary>The client's content hash does not match the <c>ContentSnapshot</c> version pinned for this run or session.</summary>
    CONTENT_VERSION_MISMATCH = 4,

    /// <summary>The <c>sequence</c> is ahead of expected — the client must resync.</summary>
    SEQUENCE_GAP = 5,

    /// <summary>The <c>sequence</c> was already processed, under a different <c>commandId</c>.</summary>
    SEQUENCE_STALE = 6,

    /// <summary>A known <c>commandId</c> arriving with a different payload or sequence.</summary>
    IDEMPOTENCY_CONFLICT = 7,

    /// <summary>
    /// A per-player application-level limit. Infrastructure limits use HTTP 429 instead — a 429 is
    /// a transport failure to retry, this is a decision to surface.
    /// </summary>
    RATE_LIMITED = 8,

    /// <summary>The command's feature is kill-switched.</summary>
    FEATURE_DISABLED = 9,

    /// <summary>
    /// No run state exists for the <c>runId</c>. Never expressed as HTTP 404: a 404 would tell the
    /// client the conversation broke, when in fact the server understood and answered.
    /// </summary>
    RUN_NOT_FOUND = 10,

    // Domain tier — the only values GameRules.Apply may return.

    /// <summary>The run's TTL has passed.</summary>
    RUN_EXPIRED = 11,

    /// <summary>A run command on a finished run.</summary>
    RUN_ALREADY_ENDED = 12,

    /// <summary>
    /// The action is not legal at this point of the state machine — wrong phase, revive already
    /// used, illegal merge inputs, fork choice with no fork pending, and so on.
    /// </summary>
    ILLEGAL_STATE = 13,

    /// <summary>Not enough Energy.</summary>
    INSUFFICIENT_ENERGY = 14,

    /// <summary>Any currency or material shortfall; <c>detail.currencyId</c> names which one.</summary>
    INSUFFICIENT_FUNDS = 15,

    /// <summary>A daily, weekly or per-run cap, or an attempt limit.</summary>
    CAP_REACHED = 16,

    /// <summary>A cooldown has not elapsed; <c>detail.availableAtUtc</c> says when.</summary>
    COOLDOWN_ACTIVE = 17,

    /// <summary>The referenced item, pet, mount, container or message does not exist on this account.</summary>
    NOT_OWNED = 18,

    /// <summary>A Plus-gated operation without Plus.</summary>
    NOT_ENTITLED = 19,

    /// <summary>A grant would exceed capacity and cannot be held.</summary>
    INVENTORY_FULL = 20,

    /// <summary>
    /// The clear the chapter/tier ladder demands has not happened — a chapter or tier the player has
    /// not opened yet.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="NOT_ENTITLED"/> on purpose: that one is a Plus paywall, and a client
    /// that read them as one thing would offer a purchase to a player whose only problem is that they
    /// have not finished the previous chapter.
    /// </remarks>
    PREREQUISITE_NOT_CLEARED = 21,

    /// <summary>The Legend Level the chapter/tier ladder demands has not been reached.</summary>
    /// <remarks>
    /// Its own value rather than a second use of <see cref="PREREQUISITE_NOT_CLEARED"/>: the two are
    /// answered by different actions — one by playing the tier below, one by levelling — and a
    /// rejection carries no detail payload to tell them apart afterwards.
    /// </remarks>
    LEGEND_LEVEL_TOO_LOW = 22,
}
