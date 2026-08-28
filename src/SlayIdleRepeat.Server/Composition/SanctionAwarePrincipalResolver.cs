using SlayIdleRepeat.Application.Moderation;
using SlayIdleRepeat.Server.Endpoints;

namespace SlayIdleRepeat.Server.Composition;

/// <summary>
/// The account-standing decorator: a caller who authenticates successfully but carries an active
/// account action is refused with HTTP 403 instead of proceeding.
/// </summary>
/// <remarks>
/// <para>
/// A decorator rather than a check inside the endpoints, because "who is calling" and "may that
/// account be served" are the same question asked at the same moment, and the seam that answers the
/// first already carries the vocabulary for the second — the locked refusal is the principal
/// resolution's own 403. Whatever ends up behind the inner resolver never learns this exists.
/// </para>
/// <para>
/// 🔒 <b>Exactly one sanction kind reaches the wire.</b> An account action locks the account. A
/// ladder exclusion, a rating reset and a name reset are recorded and change nothing here — a
/// shadow exclusion that refused requests would stop being shadow, and the other two are one-off
/// writes owned by the surfaces they act on.
/// </para>
/// <para>
/// The refusal is ordered <b>after</b> authentication on purpose: an unauthenticated caller cannot
/// be told whether an account they are guessing at is sanctioned.
/// </para>
/// </remarks>
public sealed class SanctionAwarePrincipalResolver : IPrincipalResolver
{
    /// <summary>Wraps a resolver with the account-standing check.</summary>
    /// <param name="inner">The resolver that decides who is calling.</param>
    /// <param name="standing">The locked-account set as of the last moderation refresh.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public SanctionAwarePrincipalResolver(IPrincipalResolver inner, IAccountStandingSource standing) =>
        throw new NotImplementedException();

    /// <inheritdoc/>
    public PrincipalResolution Resolve(string? authorizationHeader) => throw new NotImplementedException();
}
