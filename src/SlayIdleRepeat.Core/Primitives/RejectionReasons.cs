using System.Collections.ObjectModel;

namespace SlayIdleRepeat.Core.Primitives;

/// <summary>
/// The tier column of `14` §16.2, in code: which producer each <see cref="RejectionReason"/>
/// belongs to, and the two sets that follow from it.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Why a switch and not an attribute.</b> An attribute would be read back by reflection at
/// the one call site that matters most — M1-06 checking, per command, that a handler did not
/// return a transport-tier value. A <c>switch</c> expression costs nothing there, and it fails in
/// a more useful way: the arm list is exhaustive over the catalogue with a throwing default, so a
/// value <b>appended without being classified</b> throws the moment anything asks its tier,
/// rather than silently acquiring whichever tier a missing attribute defaults to.
/// </para>
/// <para>
/// The tests pin both tiers to the literal names `14` §16.2 and `30` §2 write out, in both
/// directions. That is deliberate friction: appending a value to the catalogue is a wire-contract
/// change, and it is meant to cost a deliberate edit in three places — the enum, the arm list
/// here, and the pinned list in the test.
/// </para>
/// </remarks>
public static class RejectionReasons
{
    /// <summary>
    /// The whole catalogue, in ascending wire-number order.
    /// </summary>
    /// <remarks>
    /// 🔒 A <see cref="ReadOnlyCollection{T}"/>, not the array <see cref="Enum.GetValues{TEnum}"/>
    /// hands back. An <see cref="IReadOnlyList{T}"/> over a bare array states an intention it
    /// cannot enforce: the runtime type is still <c>RejectionReason[]</c>, so one cast and one
    /// indexer write re-label a row of `14` §16.2 — permanently, process-wide, for every reader of
    /// this static, with no allocation and no failure anywhere to notice it. These three sets are
    /// the in-memory copy of a wire contract; the wrapper makes the write throw instead.
    /// </remarks>
    public static IReadOnlyList<RejectionReason> All { get; } =
        Array.AsReadOnly(Enum.GetValues<RejectionReason>());

    /// <summary>
    /// 🔒 The values <c>GameRules.Apply</c> may return, and the only ones (`30` §2).
    /// </summary>
    /// <remarks>
    /// This is the set M1-06 polices a handler result against, which is what makes the
    /// <see cref="All"/> wrapper more than tidiness here: a writable <c>DomainTier</c> lets a
    /// caller put a <see cref="RejectionReasonTier.Transport"/> value into the list that decides
    /// whether a transport value reaching <c>Apply</c> is allowed.
    /// </remarks>
    public static IReadOnlyList<RejectionReason> DomainTier { get; } = Of(RejectionReasonTier.Domain);

    /// <summary>
    /// The values decided before the domain is invoked. `30` §8: these never reach <c>Apply</c>,
    /// and <c>Apply</c> must never return one.
    /// </summary>
    public static IReadOnlyList<RejectionReason> TransportTier { get; } = Of(RejectionReasonTier.Transport);

    /// <summary>
    /// The tier `14` §16.2 puts a value in.
    /// </summary>
    /// <param name="reason">A declared <see cref="RejectionReason"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The value is not in the catalogue — an undeclared number, or a value appended to the enum
    /// without being classified here.
    /// </exception>
    public static RejectionReasonTier TierOf(RejectionReason reason) => reason switch
    {
        RejectionReason.MALFORMED_COMMAND or
        RejectionReason.UNKNOWN_COMMAND_TYPE or
        RejectionReason.PROTOCOL_VERSION_UNSUPPORTED or
        RejectionReason.CONTENT_VERSION_MISMATCH or
        RejectionReason.SEQUENCE_GAP or
        RejectionReason.SEQUENCE_STALE or
        RejectionReason.IDEMPOTENCY_CONFLICT or
        RejectionReason.RATE_LIMITED or
        RejectionReason.FEATURE_DISABLED or
        RejectionReason.RUN_NOT_FOUND => RejectionReasonTier.Transport,

        RejectionReason.RUN_EXPIRED or
        RejectionReason.RUN_ALREADY_ENDED or
        RejectionReason.ILLEGAL_STATE or
        RejectionReason.INSUFFICIENT_ENERGY or
        RejectionReason.INSUFFICIENT_FUNDS or
        RejectionReason.CAP_REACHED or
        RejectionReason.COOLDOWN_ACTIVE or
        RejectionReason.NOT_OWNED or
        RejectionReason.NOT_ENTITLED or
        RejectionReason.INVENTORY_FULL => RejectionReasonTier.Domain,

        _ => throw new ArgumentOutOfRangeException(
            nameof(reason),
            reason,
            "This is not a rejection reason: the 14 §16.2 table has no row for it, so no producer " +
            "is allowed to send it and no client knows how to read it. Either it is an " +
            "uninitialised value — there is deliberately no 0 member — or a value was appended to " +
            "RejectionReason without being given a tier here. Appending is the only permitted " +
            "change to that enum, and it is not finished until this switch classifies the new value."),
    };

    /// <summary>
    /// Whether <c>GameRules.Apply</c> is allowed to return this value (`30` §2).
    /// </summary>
    /// <param name="reason">A declared <see cref="RejectionReason"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">The value is not in the catalogue.</exception>
    public static bool IsDomainTier(RejectionReason reason) =>
        TierOf(reason) == RejectionReasonTier.Domain;

    /// <summary>
    /// Every catalogue value in one tier, in ascending wire-number order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reads <see cref="Enum.GetValues{TEnum}"/> directly rather than <see cref="All"/>: static
    /// property initialisers run in declaration order, and a set that silently came out empty
    /// because it initialised first would make every rule stated over it pass over nothing.
    /// </para>
    /// <para>
    /// Wrapped for the reason <see cref="All"/> is wrapped, and the <c>ToArray</c> is what makes
    /// that necessary — the query is materialised once here rather than re-run per read, so the
    /// array it produces is the one every caller shares.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<RejectionReason> Of(RejectionReasonTier tier) =>
        Array.AsReadOnly(Enum.GetValues<RejectionReason>().Where(value => TierOf(value) == tier).ToArray());
}
