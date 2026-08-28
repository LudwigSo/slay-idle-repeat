using SlayIdleRepeat.Application.Wire;

namespace SlayIdleRepeat.Application.Auth;

/// <summary>What the client does next after a request came back unauthorised.</summary>
/// <remarks>
/// Numbered from 1 with no <c>0</c> member, like every other closed vocabulary here, so an
/// uninitialised value cannot read as the first rung and hand a caller a free extra refresh.
/// </remarks>
public enum UnauthorizedRecoveryStep
{
    /// <summary>Exchange the refresh token for a new access token, then retry. Never visible to the player.</summary>
    REFRESH_THE_ACCESS_TOKEN = 1,

    /// <summary>The refresh did not cure it: authenticate again with the device secret, then retry.</summary>
    REAUTHENTICATE_WITH_THE_DEVICE_SECRET = 2,

    /// <summary>Both silent cures failed. Only now does the player see anything.</summary>
    SURFACE_TO_THE_PLAYER = 3,
}

/// <summary>One in-flight command and how far its recovery has already climbed.</summary>
/// <param name="CommandId">
/// The idempotency key of the request being recovered. Carried unchanged through every rung: the
/// retry has to be the same command, or work the server already committed is committed twice.
/// </param>
/// <param name="LastStep">The rung already taken for this attempt, or <see langword="null"/> before any.</param>
public readonly record struct UnauthorizedRecoveryAttempt(
    CommandId CommandId, UnauthorizedRecoveryStep? LastStep);

/// <summary>The rung to take, and the attempt as it stands once it has been taken.</summary>
/// <param name="Step">What the client does now.</param>
/// <param name="Attempt">The attempt to hand back to the policy if the retry is refused again.</param>
public readonly record struct UnauthorizedRecoveryDecision(
    UnauthorizedRecoveryStep Step, UnauthorizedRecoveryAttempt Attempt);

/// <summary>The ladder a client climbs when a request comes back unauthorised: refresh, re-authenticate, tell.</summary>
/// <remarks>
/// A pure function over the attempt record, so the whole ladder is decidable without a session, a
/// socket or a clock. One refresh per attempt and no more — a policy that retried the refresh would
/// spend a mid-run expiry looping instead of curing it.
/// </remarks>
public static class UnauthorizedRecoveryPolicy
{
    /// <summary>A fresh attempt for one command, with no rung taken yet.</summary>
    /// <param name="commandId">The idempotency key the retry must carry.</param>
    public static UnauthorizedRecoveryAttempt Begin(CommandId commandId) => new(commandId, null);

    /// <summary>The rung to take now that this attempt has been refused again.</summary>
    /// <param name="attempt">The attempt as it stands.</param>
    /// <returns>The step, and the attempt advanced past it.</returns>
    public static UnauthorizedRecoveryDecision OnUnauthorized(UnauthorizedRecoveryAttempt attempt)
    {
        // The top rung is terminal rather than throwing: a caller's error path must not depend on how
        // many times it has already asked.
        var step = attempt.LastStep switch
        {
            null => UnauthorizedRecoveryStep.REFRESH_THE_ACCESS_TOKEN,
            UnauthorizedRecoveryStep.REFRESH_THE_ACCESS_TOKEN =>
                UnauthorizedRecoveryStep.REAUTHENTICATE_WITH_THE_DEVICE_SECRET,
            _ => UnauthorizedRecoveryStep.SURFACE_TO_THE_PLAYER,
        };

        return new UnauthorizedRecoveryDecision(step, attempt with { LastStep = step });
    }

    /// <summary>The attempt after a request finally succeeded — the ladder starts from the bottom again.</summary>
    /// <param name="attempt">The attempt as it stands.</param>
    public static UnauthorizedRecoveryAttempt OnAuthorized(UnauthorizedRecoveryAttempt attempt) =>
        attempt with { LastStep = null };
}
