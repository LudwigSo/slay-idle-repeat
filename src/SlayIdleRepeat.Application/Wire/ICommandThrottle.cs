using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Application.Wire;

/// <summary>The per-player application-level limit behind 14 §16.2's <c>RATE_LIMITED</c>.</summary>
/// <remarks>
/// Narrow by design: one question, asked before the ledger so a throttled command consumes no
/// sequence number. The infrastructure per-IP limit is a different animal — it answers HTTP 429
/// with <c>Retry-After</c> and never reaches this seam. M5-14 supplies the real limiter;
/// <see cref="UnlimitedCommandThrottle"/> stands in until it does.
/// </remarks>
public interface ICommandThrottle
{
    /// <summary>Whether this player's command should be refused with <c>RATE_LIMITED</c>.</summary>
    /// <param name="player">Whose limit to consult.</param>
    bool ShouldReject(PlayerId player);
}

/// <summary>⚠️ PLACEHOLDER — no limit at all, until M5-14 lands the per-player limiter this seam exists for.</summary>
/// <remarks>
/// Beside its seam rather than in the composition root because "no limit" is the seam's identity
/// element, not an adapter decision — the same placement argument as <c>VolatileCommandLedger</c>'s,
/// without even state to hold. It dies with M5-14.
/// </remarks>
public sealed class UnlimitedCommandThrottle : ICommandThrottle
{
    /// <inheritdoc/>
    public bool ShouldReject(PlayerId player) => false;
}
