using System.Net;
using System.Threading.RateLimiting;

namespace SlayIdleRepeat.Server.Composition;

/// <summary>The per-remote-address infrastructure limit's deployment contract.</summary>
/// <remarks>
/// ⚠️ Every number is an OPERATIONAL default chosen by whoever runs the service. None of them is a
/// game tunable, none lives in the shared content data, and none is derived from any design text —
/// no document authors a per-endpoint request rate. They exist so an unconfigured process is limited
/// rather than unlimited; a deployment overrides them with <c>RateLimit__Ip__*</c>.
/// </remarks>
public sealed class PerIpRateLimitOptions
{
    /// <summary>Sustained requests per second from one address. Default 20.</summary>
    public int PermitsPerSecond { get; set; } = 20;

    /// <summary>How many requests one address may make back to back. Default 100.</summary>
    public int Burst { get; set; } = 100;

    /// <summary>What a refused address is told to wait, in seconds. Default 10.</summary>
    public int RetryAfterSeconds { get; set; } = 10;
}

/// <summary>
/// The infrastructure rate limit: per remote address, answered with HTTP 429 and <c>Retry-After</c>.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>A different animal from the per-player limit, and the difference is the whole point.</b>
/// The per-player limit is an application-level decision about one account — it rides a 200
/// rejection envelope carrying <c>RATE_LIMITED</c>, and the client must not blind-retry it. This one
/// is an infrastructure refusal about one connection: HTTP 429, a <c>Retry-After</c>, and the client
/// backs off and retries the same command id. Collapsing the two would tell a client to give up on a
/// command it should retry, or to hammer one it should not.
/// </para>
/// <para>
/// It lives inside the API process because there is no gateway or ingress in front of it to hold it.
/// That placement is what makes "infrastructure" a description of the tier rather than of the
/// hardware: put a real gateway in front later and this becomes redundant, not wrong.
/// </para>
/// <para>
/// 🔒 <b>The partition is the transport's own remote address and never a request header.</b>
/// <c>X-Forwarded-For</c> and its relatives are attacker-supplied text on a service with nothing
/// trusted in front of it: partitioning on one would let a single client mint an unlimited number of
/// identities and defeat this limit entirely. When a real proxy is deployed, the fix is that proxy's
/// known address configured into forwarded-headers handling — never trusting the header here.
/// </para>
/// </remarks>
public static class PerIpRateLimit
{
    /// <summary>The partition every request with no readable remote address shares.</summary>
    /// <remarks>
    /// One shared bucket rather than an exemption: an unreadable address is a unix-socket or
    /// in-process caller today, and an exemption would be the one partition key an attacker would
    /// aim for.
    /// </remarks>
    public const string UnknownAddressPartition = "ip:unknown";

    /// <summary>The partition key for one remote address.</summary>
    /// <param name="remoteAddress">The transport's remote address, or <c>null</c> when it has none.</param>
    public static string PartitionKeyFor(IPAddress? remoteAddress) => throw new NotImplementedException();

    /// <summary>The token bucket one address's partition runs on.</summary>
    /// <param name="options">The deployment's numbers.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A number is not positive.</exception>
    public static TokenBucketRateLimiterOptions BucketFor(PerIpRateLimitOptions options) =>
        throw new NotImplementedException();
}
