using System.Globalization;
using System.Net;
using System.Net.Sockets;
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

    /// <summary>How many bytes an IPv6 address is.</summary>
    private const int Ipv6AddressBytes = 16;

    /// <summary>How much of an IPv6 address identifies the client's network rather than one of its hosts — a /64.</summary>
    private const int Ipv6RoutingPrefixBytes = 8;

    /// <summary>The partition key for one remote address.</summary>
    /// <param name="remoteAddress">The transport's remote address, or <c>null</c> when it has none.</param>
    /// <remarks>
    /// The address is normalised, not spelled: an IPv4-mapped IPv6 address (what a dual-stack socket
    /// hands you for an IPv4 client) collapses to its IPv4 form, so one client is one partition
    /// whichever socket accepted it.
    /// </remarks>
    public static string PartitionKeyForAddress(IPAddress? remoteAddress)
    {
        if (remoteAddress is null)
        {
            return UnknownAddressPartition;
        }

        var normalised = remoteAddress.IsIPv4MappedToIPv6
            ? remoteAddress.MapToIPv4()
            : remoteAddress;

        if (normalised.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return "ip:" + normalised;
        }

        // 🔒 One bucket per /64, not per address. A residential IPv6 client is routinely delegated a
        // /56 or /64 and can source from a fresh address per request at no cost — partitioning on
        // the full /128 would hand it the same unlimited supply of identities that trusting a
        // forwarded-for header would.
        Span<byte> prefix = stackalloc byte[Ipv6AddressBytes];
        normalised.TryWriteBytes(prefix, out _);
        prefix[Ipv6RoutingPrefixBytes..].Clear();

        return "ip6:" + new IPAddress(prefix);
    }

    /// <summary>
    /// The partition key for one request — the selector the limiter is actually registered with.
    /// </summary>
    /// <param name="http">The request.</param>
    /// <exception cref="ArgumentNullException"><paramref name="http"/> is null.</exception>
    /// <remarks>
    /// 🔒 It reads the connection and nothing else. Every forwarded-address header on the request is
    /// ignored, deliberately and by omission: see the type's remarks.
    /// </remarks>
    public static string PartitionKeyFor(HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);

        return PartitionKeyForAddress(http.Connection.RemoteIpAddress);
    }

    /// <summary>The token bucket one address's partition runs on.</summary>
    /// <param name="options">The deployment's numbers.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A number is not positive.</exception>
    public static TokenBucketRateLimiterOptions BucketFor(PerIpRateLimitOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        RequirePositive(options.PermitsPerSecond, nameof(PerIpRateLimitOptions.PermitsPerSecond));
        RequirePositive(options.Burst, nameof(PerIpRateLimitOptions.Burst));

        return new TokenBucketRateLimiterOptions
        {
            TokenLimit = options.Burst,
            TokensPerPeriod = options.PermitsPerSecond,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            AutoReplenishment = true,

            // Refused, never queued: the client has already been told to back off, and holding its
            // request open would spend the command latency budget on an answer it is not waiting for.
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        };
    }

    /// <summary>Writes the refusal a throttled address gets: HTTP 429, a <c>Retry-After</c>, no body.</summary>
    /// <param name="response">The response being written.</param>
    /// <param name="options">The deployment's numbers — the backoff comes from here, never from a constant.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The backoff is not positive.</exception>
    /// <remarks>
    /// 🔒 <b>No body.</b> A rejection envelope here would be the application-level answer wearing the
    /// infrastructure answer's status: the client's rule is to back off and retry the SAME command
    /// id after a 429, and to never blind-retry a 200 rejection. Handing it both signals at once is
    /// exactly the blur the two limits are kept apart to prevent.
    /// </remarks>
    public static void WriteRefusal(HttpResponse response, PerIpRateLimitOptions options)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(options);

        RequireBackoff(options);

        response.StatusCode = StatusCodes.Status429TooManyRequests;
        response.Headers.RetryAfter =
            options.RetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Checks the backoff a refusal will quote, without writing one.</summary>
    /// <param name="options">The deployment's numbers.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The backoff is not positive.</exception>
    /// <remarks>
    /// Separated from <see cref="WriteRefusal"/> so the composition root can refuse a bad number at
    /// STARTUP. Validated only on use, a zero backoff would start a healthy-looking process that
    /// throws out of the rate limiter the first time an address is refused — which is the moment
    /// least able to absorb it.
    /// </remarks>
    public static void RequireBackoff(PerIpRateLimitOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        RequirePositive(options.RetryAfterSeconds, nameof(PerIpRateLimitOptions.RetryAfterSeconds));
    }

    /// <summary>The parameter every settings refusal blames — both public entry points name it the same.</summary>
    private const string OptionsParameterName = "options";

    private static void RequirePositive(int value, string setting)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(
                OptionsParameterName,
                value,
                $"{setting} is {value}. Every one of these settings is a positive count; a zero or "
                + "negative one would refuse every request from every address, which is a deployment "
                + "typo turning into an outage.");
        }
    }
}
