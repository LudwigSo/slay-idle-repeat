namespace SlayIdleRepeat.Application.Ports.Client;

/// <summary>
/// Showing the player a rewarded ad and reporting what happened.
/// </summary>
/// <remarks>
/// <para>
/// A placement is a plain ordinal string — the same vocabulary the kill-switch list already uses,
/// deliberately not a second identifier type for one concept. Blank or <see langword="null"/> is an
/// <see cref="ArgumentException"/> from every method here.
/// </para>
/// <para>
/// 🔒 <b>No method surfaces a vendor failure as an exception.</b> An SDK that times out, refuses,
/// has no inventory or crashes internally is caught at the adapter edge and reported as
/// <see cref="AdResultKind.NoFill"/> or <see cref="AdResultKind.Error"/>. Callers therefore branch
/// on an outcome rather than wrapping every call in a <c>try</c>, and the branch they write is the
/// same one against the fake and against the real adapter.
/// </para>
/// </remarks>
public interface IRewardedAdPort
{
    /// <summary>
    /// Whether an ad is available for <paramref name="adPlacementId"/> right now.
    /// </summary>
    /// <remarks>
    /// 🔒 A <see langword="true"/> answer is a promise: a <see cref="ShowAsync"/> that follows it
    /// does <b>not</b> come back <see cref="AdResultKind.NoFill"/>. No-fill means no ad was
    /// available, and this method has just said one was — an implementation that can report both is
    /// telling the caller two different things about the same moment, and the UI that offered the
    /// button then has to apologise for it.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="adPlacementId"/> is blank.</exception>
    bool IsReady(string adPlacementId);

    /// <summary>
    /// Shows a rewarded ad for <paramref name="adPlacementId"/> and reports the outcome. Throws only
    /// for a bad argument or an already-cancelled token; every other failure is an
    /// <see cref="AdOutcome"/>.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="adPlacementId"/> is blank.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> is already cancelled.</exception>
    Task<AdOutcome> ShowAsync(string adPlacementId, CancellationToken ct);

    /// <summary>
    /// Asks the implementation to make an ad ready for <paramref name="adPlacementId"/>. Idempotent
    /// and total: preloading a placement that is already loaded, or one that will never fill,
    /// completes normally and changes nothing.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="adPlacementId"/> is blank.</exception>
    Task PreloadAsync(string adPlacementId, CancellationToken ct);
}

/// <summary>What one rewarded-ad attempt produced.</summary>
/// <param name="Kind">How the attempt ended.</param>
/// <param name="VerificationToken">
/// 🔒 Present <b>only</b> when <paramref name="Kind"/> is <see cref="AdResultKind.Completed"/>, and
/// non-blank whenever it is present. It is the thing the server can independently check the grant
/// against. The guarantee runs one way only: a token implies a completed ad, a completed ad does
/// <b>not</b> imply a token.
/// <para>
/// An implementation that grants without a server-verifiable impression — the subscriber
/// auto-grant path, where no ad is shown at all — returns <see cref="AdResultKind.Completed"/> with
/// a <see langword="null"/> token. The absent token is the honest signal that there is nothing for
/// the server to verify, not an oversight: a manufactured token would be a forgery the server would
/// then have to be taught to accept. That is why this is not a biconditional — stating it as one
/// would tell the next implementer to invent a token to satisfy the doc.
/// </para>
/// </param>
public readonly record struct AdOutcome(AdResultKind Kind, string? VerificationToken);

/// <summary>How a rewarded-ad attempt ended.</summary>
public enum AdResultKind
{
    /// <summary>The player watched it through, and the reward is owed.</summary>
    Completed,

    /// <summary>The player closed it early. No reward.</summary>
    Dismissed,

    /// <summary>No ad was available to show.</summary>
    NoFill,

    /// <summary>The attempt failed for any other reason. Vendor faults arrive here, never as an exception.</summary>
    Error,
}
