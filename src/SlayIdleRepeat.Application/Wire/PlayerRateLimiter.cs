using System.Collections.Concurrent;
using SlayIdleRepeat.Application.Ports.Shared;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Application.Wire;

/// <summary>One rate limit: a sustained refill and a burst the bucket may hold.</summary>
/// <param name="Burst">How many requests an idle caller may make back to back. At least 1.</param>
/// <param name="RefillInterval">How long one permit takes to come back. Positive.</param>
/// <remarks>
/// <para>
/// ⚠️ <b>Every number here is an OPERATIONAL default chosen by whoever runs the service.</b> It is
/// not a game tunable, it does not live in the shared content data, and it is not derived from any
/// design text — no document authors a per-endpoint request rate. Deployments override the two
/// numbers through configuration; the defaults exist so an unconfigured process is limited rather
/// than unlimited.
/// </para>
/// <para>
/// The permit interval is stored rather than a rate per second so every decision is exact integer
/// tick arithmetic. A rate expressed as a <see cref="double"/> makes the boundary cases irreproducible
/// — and the boundary is exactly where the question "would this refuse a real player?" is answered.
/// </para>
/// </remarks>
public sealed record RateLimitPolicy(int Burst, TimeSpan RefillInterval)
{
    /// <summary>
    /// The per-player application-level default: 5 permits per second sustained, 20 in a burst.
    /// ⚠️ An operations choice, not a design number — see the type's own remarks.
    /// </summary>
    /// <remarks>
    /// ⚠️ What this pair actually reaches, since a burst is easy to over-read: a client is serialised
    /// by the wire's sequencing rules, so its fastest legitimate cadence is one round trip, and at
    /// the budgeted round trip this admits 77 consecutive commands against a run that costs about
    /// 30. The margin closes as the round trip shortens, and disappears below about 69 ms — which is
    /// why <see cref="MaxConsecutiveAt"/> exists and why the burst is configurable.
    /// </remarks>
    public static readonly RateLimitPolicy PerPlayerDefault = PerSecond(5, burst: 20);

    /// <summary>How many requests an idle caller may make back to back. At least 1.</summary>
    public int Burst { get; } = Burst >= 1
        ? Burst
        : throw new ArgumentOutOfRangeException(
            nameof(Burst), Burst, "a burst below one permit refuses every request, including the first.");

    /// <summary>How long one permit takes to come back. Positive.</summary>
    public TimeSpan RefillInterval { get; } = RefillInterval > TimeSpan.Zero
        ? RefillInterval
        : throw new ArgumentOutOfRangeException(
            nameof(RefillInterval), RefillInterval, "a bucket that never refills is a one-time allowance.");

    /// <summary>A policy stated as a sustained rate per second.</summary>
    /// <param name="permitsPerSecond">Sustained permits per second. Positive.</param>
    /// <param name="burst">How many an idle caller may make back to back. At least 1.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="permitsPerSecond"/> or <paramref name="burst"/> is not positive.</exception>
    public static RateLimitPolicy PerSecond(double permitsPerSecond, int burst)
    {
        if (permitsPerSecond <= 0 || double.IsNaN(permitsPerSecond))
        {
            throw new ArgumentOutOfRangeException(
                nameof(permitsPerSecond),
                permitsPerSecond,
                "a rate of zero or less never refills the bucket, so the burst becomes a lifetime allowance.");
        }

        var ticks = (long)Math.Round(TimeSpan.TicksPerSecond / permitsPerSecond);

        // The one place a rate touches floating point: everything downstream is ticks.
        return new RateLimitPolicy(burst, TimeSpan.FromTicks(Math.Max(ticks, 1L)));
    }

    /// <summary>
    /// How many requests a caller may make back to back at a fixed cadence before this policy refuses
    /// one — the arithmetic that answers "can this refuse a real player?" without running a clock.
    /// </summary>
    /// <param name="cadence">The interval between consecutive requests.</param>
    /// <returns>The count, or <see cref="int.MaxValue"/> when the cadence is at or below the sustained rate and the policy never refuses.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="cadence"/> is negative.</exception>
    /// <remarks>
    /// Request <c>n</c> is admitted while <c>(n − 1)·(refill − cadence) ≤ (burst − 1)·refill</c>,
    /// which is the same inequality the limiter itself decides by — stated once here so a reader can
    /// check the reach at a cadence without simulating one.
    /// </remarks>
    public int MaxConsecutiveAt(TimeSpan cadence)
    {
        if (cadence < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(cadence), cadence, "requests do not arrive in reverse order.");
        }

        if (cadence >= RefillInterval)
        {
            return int.MaxValue;
        }

        var reach = ((Int128)(Burst - 1) * RefillInterval.Ticks) / (RefillInterval.Ticks - cadence.Ticks) + 1;

        // int.MaxValue is reserved for "never refuses"; a merely astronomical reach must not claim it.
        return reach >= int.MaxValue ? int.MaxValue - 1 : (int)reach;
    }
}

/// <summary>The per-player application-level limiter behind the gateway's <c>RATE_LIMITED</c>.</summary>
/// <remarks>
/// <para>
/// 🔒 <b>This is the application-level limit, and it is a different animal from the infrastructure
/// one.</b> It refuses a single player's own flood, inside the protocol: the gateway turns a refusal
/// here into a 200 rejection envelope carrying <c>RATE_LIMITED</c>, which the client must not
/// blind-retry. The infrastructure limit is per remote address, lives in the host's middleware, and
/// answers HTTP 429 with <c>Retry-After</c> — it never reaches this class, and this class never
/// produces a status code. The two must not be collapsed into one mechanism: one is a decided
/// answer about a player, the other is a transport failure about a connection.
/// </para>
/// <para>
/// This is also the rate limit the client-asserted skill minigames are checked against. Those
/// submissions are a documented exception to server authority, and the legality the server does
/// check is tier validity, one submission per tile — both already enforced in the rules library —
/// and the rate, which is this. No document authors a special rate for them, so they get this one.
/// </para>
/// <para>
/// 🔒 <b>Best-effort by design.</b> The counters live in this process's memory. A restart, a
/// redeploy or a request landing on another instance starts a fresh bucket, and that is a bypass —
/// an accepted one, on the same reasoning that makes the hot cache rebuildable: losing this state
/// costs a moment of unthrottled traffic, never a player's progress. Nothing here is a system of
/// record, and nothing reads it back.
/// </para>
/// <para>
/// The tracked-player map is bounded so a stream of invented identities cannot grow it. Eviction
/// takes the buckets closest to full first, and a completely refilled bucket answers every question
/// identically to an absent one — so the account actually being throttled is the last to go, which
/// is what stops an attacker clearing their own limit by inventing identities.
/// </para>
/// <para>
/// One <see cref="long"/> per tracked player: the instant the bucket next comes back to full minus
/// the tolerance — a leaky-bucket meter rather than a stored token count, so an idle player costs
/// nothing to refill and there is no timer anywhere.
/// </para>
/// </remarks>
public sealed class PlayerRateLimiter : ICommandThrottle
{
    private readonly ConcurrentDictionary<PlayerId, long> _nextPermitTicks = new();
    private readonly IClockPort _clock;
    private readonly RateLimitPolicy _policy;
    private readonly int _capacity;
    private readonly long _toleranceTicks;

    /// <summary>Composes the limiter.</summary>
    /// <param name="clock">The clock every decision is measured against.</param>
    /// <param name="policy">The limit.</param>
    /// <param name="trackedPlayerCapacity">How many players' buckets may be held before idle ones are swept. Positive.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="trackedPlayerCapacity"/> is not positive.</exception>
    public PlayerRateLimiter(IClockPort clock, RateLimitPolicy policy, int trackedPlayerCapacity = 100_000)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(policy);

        if (trackedPlayerCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(trackedPlayerCapacity),
                trackedPlayerCapacity,
                "a map that may hold nothing sheds every bucket on every request, which is no limit at all.");
        }

        _clock = clock;
        _policy = policy;
        _capacity = trackedPlayerCapacity;

        // Widened, then saturated: a burst large enough to overflow would otherwise wrap to a
        // tolerance of nearly zero — the tightest possible limit, from the loosest possible setting.
        var tolerance = (Int128)(policy.Burst - 1) * policy.RefillInterval.Ticks;
        _toleranceTicks = tolerance > long.MaxValue ? long.MaxValue : (long)tolerance;
    }

    /// <summary>How many players currently have a bucket held.</summary>
    public int TrackedPlayers => _nextPermitTicks.Count;

    /// <inheritdoc/>
    public bool ShouldReject(PlayerId player)
    {
        var now = _clock.UtcNow.UtcTicks;
        var interval = _policy.RefillInterval.Ticks;

        while (true)
        {
            if (_nextPermitTicks.TryGetValue(player, out var held))
            {
                var spent = Math.Max(held, now);

                if (spent - now > _toleranceTicks)
                {
                    // Refused: the meter is NOT advanced, so a rejected request costs the player
                    // nothing extra and a client that keeps hammering does not push its own recovery
                    // further away with every attempt.
                    return true;
                }

                if (_nextPermitTicks.TryUpdate(player, spent + interval, held))
                {
                    break;
                }
            }
            else if (_nextPermitTicks.TryAdd(player, now + interval))
            {
                break;
            }
        }

        Sweep(now);

        return false;
    }

    private void Sweep(long now)
    {
        if (_nextPermitTicks.Count <= _capacity)
        {
            return;
        }

        // Down to three quarters, so a map sitting at the cap does not sweep on every single
        // request. Ordered by how full each bucket is: the emptiest survives longest.
        var target = Math.Max(1, _capacity * 3 / 4);

        foreach (var candidate in _nextPermitTicks.ToArray().OrderBy(entry => entry.Value))
        {
            if (_nextPermitTicks.Count <= target)
            {
                return;
            }

            // The pair overload: a bucket that got busy since the snapshot no longer matches and
            // stays, so the sweep can never drop state it has not actually looked at.
            _nextPermitTicks.TryRemove(candidate);
        }
    }
}
