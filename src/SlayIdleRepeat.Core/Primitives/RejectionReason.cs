namespace SlayIdleRepeat.Core.Primitives;

/// <summary>
/// 🔒 The rejection catalogue of `14` §16.2 — <b>one enum, one wire field, two producers</b>
/// (`30` §2).
/// </summary>
/// <remarks>
/// <para>
/// `14` §16.2 opens with what a rejection <i>is</i>: <i>"A rejection is a successful protocol
/// exchange whose answer is no. It rides HTTP 200 in a rejection envelope. HTTP status codes are
/// reserved for transport failures."</i> So this is not an error type. It is the vocabulary of
/// every "no" the game is allowed to say, and it travels in the <c>reason</c> field of a rejection
/// envelope beside a <c>stateHash</c> of the untouched state.
/// </para>
/// <para>
/// <b>Two tiers produce it, and they are one enum on purpose.</b> The <b>transport</b> tier —
/// malformed envelopes, sequence and idempotency conflicts, rate limits, protocol and content
/// version — is decided by the server host and the <c>Application</c> layer, before the domain is
/// ever invoked (`30` §8: the domain sees each command exactly once, and only well-formed ones), so
/// those values never appear in a <c>CommandResult</c>. The <b>domain</b> tier is what
/// <c>GameRules.Apply</c> returns. Splitting them into two enums would give the client two fields
/// to switch on for one question, and `14` §16.2's table one column it could no longer express.
/// Which tier a value belongs to is answered by <see cref="RejectionReasons.TierOf"/>.
/// </para>
/// <para>
/// 🔒 <b>The numbers are wire values and they are permanent.</b>
/// <c>CanonicalStateWriter</c> encodes an enum as its <i>numeric</i> value (`14` §16.6) — the name
/// never reaches the bytes — and `14` §16.2 is explicit: <i>"values may be appended, never renamed
/// or reused. A client receiving an unknown value treats it as a generic rejection and
/// resyncs."</i> So <b>appending is the only permitted change</b>. Renumbering silently re-labels
/// every rejection already recorded against the old number; reusing a retired number is worse,
/// because the two meanings are indistinguishable after the fact. Take the next free integer and
/// leave every existing one alone. Retiring a value means leaving its number unused, not
/// reclaiming it.
/// </para>
/// <para>
/// The member names are the wire spelling from `14` §16.2's table, not C#'s PascalCase, so the
/// enum member <i>is</i> the wire value and there is no mapping table between the two to drift.
/// </para>
/// <para>
/// ⚠️ There is deliberately no <c>0</c> member. `14` §16.2 authorises no "none" value, and
/// numbering from 1 means an uninitialised field can never read as a real rejection. Absence is
/// modelled by a <c>null</c> <see cref="RejectionReason"/>, which is how `30` §2's
/// <c>CommandResult.Rejection</c> declares it.
/// </para>
/// </remarks>
public enum RejectionReason
{
    // ------------------------------------------------------------------ transport tier
    // Produced by the server host / Application layer. Never returned by GameRules.Apply.

    /// <summary>Envelope parsed, but the command failed schema validation.</summary>
    MALFORMED_COMMAND = 1,

    /// <summary>The <c>type</c> is not in the `14` §2.3 registry.</summary>
    UNKNOWN_COMMAND_TYPE = 2,

    /// <summary>Outside the <c>{N, N−1}</c> protocol-version window (`14` §16.1).</summary>
    PROTOCOL_VERSION_UNSUPPORTED = 3,

    /// <summary>
    /// The client's content hash does not match the <c>ContentSnapshot</c> version pinned for this
    /// run or session (`14` §6).
    /// </summary>
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

    /// <summary>The command's feature is kill-switched (`14` §14).</summary>
    FEATURE_DISABLED = 9,

    /// <summary>
    /// No run state exists for the <c>runId</c>. 🔒 Never expressed as HTTP 404: a 404 would tell
    /// the client the conversation broke, when in fact the server understood and answered.
    /// </summary>
    RUN_NOT_FOUND = 10,

    // -------------------------------------------------------------------- domain tier
    // The values GameRules.Apply may return, and the only ones (30 §2).

    /// <summary>The 48 h run TTL has passed (`14` §16.3).</summary>
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

    /// <summary>
    /// Any currency or material shortfall; <c>detail.currencyId</c> names which one.
    /// </summary>
    INSUFFICIENT_FUNDS = 15,

    /// <summary>A daily, weekly or per-run cap, or an attempt limit (`12` §4.3).</summary>
    CAP_REACHED = 16,

    /// <summary>
    /// A cooldown has not elapsed — e.g. the 12 h Focus cooldown (`24` §5);
    /// <c>detail.availableAtUtc</c> says when.
    /// </summary>
    COOLDOWN_ACTIVE = 17,

    /// <summary>
    /// The referenced item, pet, mount, container or message does not exist on this account.
    /// </summary>
    NOT_OWNED = 18,

    /// <summary>A Plus-gated operation without Plus — e.g. preset slot 4+ (`09` §2.1).</summary>
    NOT_ENTITLED = 19,

    /// <summary>A grant would exceed capacity and cannot be held (`08` §5).</summary>
    INVENTORY_FULL = 20,
}
