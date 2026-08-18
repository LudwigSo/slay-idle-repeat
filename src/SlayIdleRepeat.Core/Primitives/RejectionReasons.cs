using System.Collections.ObjectModel;

namespace SlayIdleRepeat.Core.Primitives;

/// <summary>Which producer each <see cref="RejectionReason"/> belongs to, and the two sets that follow from it.</summary>
/// <remarks>
/// A <c>switch</c> rather than an attribute: the arm list is exhaustive over the catalogue with a
/// throwing default, so a value appended without being classified throws the moment anything asks
/// its tier, instead of silently acquiring whichever tier a missing attribute would default to.
/// </remarks>
public static class RejectionReasons
{
    /// <summary>The whole catalogue, in ascending wire-number order.</summary>
    /// <remarks>
    /// A <see cref="ReadOnlyCollection{T}"/>, not the array <see cref="Enum.GetValues{TEnum}"/> hands
    /// back: an <see cref="IReadOnlyList{T}"/> over a bare array doesn't stop a cast reaching the
    /// underlying <c>RejectionReason[]</c> and mutating it in place for every reader of this static.
    /// </remarks>
    public static IReadOnlyList<RejectionReason> All { get; } =
        Array.AsReadOnly(Enum.GetValues<RejectionReason>());

    /// <summary>The values <c>GameRules.Apply</c> may return, and the only ones.</summary>
    public static IReadOnlyList<RejectionReason> DomainTier { get; } = Of(RejectionReasonTier.Domain);

    /// <summary>The values decided before the domain is invoked. These never reach <c>Apply</c>, and <c>Apply</c> must never return one.</summary>
    public static IReadOnlyList<RejectionReason> TransportTier { get; } = Of(RejectionReasonTier.Transport);

    /// <summary>The tier a value belongs to.</summary>
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
        RejectionReason.INVENTORY_FULL or
        RejectionReason.PREREQUISITE_NOT_CLEARED or
        RejectionReason.LEGEND_LEVEL_TOO_LOW => RejectionReasonTier.Domain,

        _ => throw new ArgumentOutOfRangeException(
            nameof(reason),
            reason,
            "This is not a rejection reason: the 14 §16.2 table has no row for it, so no producer " +
            "is allowed to send it and no client knows how to read it. Either it is an " +
            "uninitialised value — there is deliberately no 0 member — or a value was appended to " +
            "RejectionReason without being given a tier here."),
    };

    /// <summary>Whether <c>GameRules.Apply</c> is allowed to return this value.</summary>
    /// <param name="reason">A declared <see cref="RejectionReason"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">The value is not in the catalogue.</exception>
    public static bool IsDomainTier(RejectionReason reason) =>
        TierOf(reason) == RejectionReasonTier.Domain;

    /// <summary>Every catalogue value in one tier, in ascending wire-number order.</summary>
    /// <remarks>
    /// Reads <see cref="Enum.GetValues{TEnum}"/> directly rather than <see cref="All"/>, since static
    /// property initialisers run in declaration order and <see cref="All"/> may not have run yet.
    /// </remarks>
    private static IReadOnlyList<RejectionReason> Of(RejectionReasonTier tier) =>
        Array.AsReadOnly(Enum.GetValues<RejectionReason>().Where(value => TierOf(value) == tier).ToArray());
}
