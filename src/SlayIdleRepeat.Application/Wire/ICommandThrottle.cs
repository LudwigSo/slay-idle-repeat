using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Application.Wire;

/// <summary>The per-player application-level limit behind the wire's <c>RATE_LIMITED</c>.</summary>
/// <remarks>
/// Narrow by design: one question, asked before the ledger so a throttled command consumes no
/// sequence number. The infrastructure per-IP limit is a different animal — it answers HTTP 429
/// with <c>Retry-After</c> and never reaches this seam. <see cref="PlayerRateLimiter"/> is the
/// implementation this seam was cut for.
/// </remarks>
public interface ICommandThrottle
{
    /// <summary>Whether this player's command should be refused with <c>RATE_LIMITED</c>.</summary>
    /// <param name="player">Whose limit to consult.</param>
    bool ShouldReject(PlayerId player);
}
