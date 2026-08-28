using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Shouldly;
using SlayIdleRepeat.Server.Composition;
using Xunit;

namespace SlayIdleRepeat.Server.Tests.Composition;

/// <summary>
/// The per-address infrastructure limit as a unit: the partition it keys on, the bucket it builds,
/// and the deployment keys it binds from. Driven with a <c>DefaultHttpContext</c> — a plain class
/// needs no host, and this repository stands none up.
/// </summary>
public sealed class PerIpRateLimitTests
{
    [Fact]
    public void The_shipped_numbers_are_twenty_per_second_a_hundred_burst_and_ten_seconds_of_backoff()
    {
        var options = new PerIpRateLimitOptions();

        options.PermitsPerSecond.ShouldBe(20);
        options.Burst.ShouldBe(100);
        options.RetryAfterSeconds.ShouldBe(
            10,
            "what a refused address is told to wait is an operations choice like the other two — no "
            + "design text authors a per-endpoint rate anywhere.");
    }

    [Fact]
    public void The_numbers_bind_from_the_ratelimit_ip_section_by_the_keys_a_deployment_sets()
    {
        var options = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimit:Ip:PermitsPerSecond"] = "7",
                ["RateLimit:Ip:Burst"] = "13",
                ["RateLimit:Ip:RetryAfterSeconds"] = "3",
            })
            .Build()
            .GetSection("RateLimit:Ip")
            .Get<PerIpRateLimitOptions>();

        options.ShouldNotBeNull();
        options.PermitsPerSecond.ShouldBe(7, "RateLimit__Ip__PermitsPerSecond is the env spelling.");
        options.Burst.ShouldBe(13);
        options.RetryAfterSeconds.ShouldBe(3);
    }

    [Fact]
    public void Two_addresses_are_two_partitions_and_one_address_is_one()
    {
        var first = PerIpRateLimit.PartitionKeyForAddress(IPAddress.Parse("203.0.113.7"));
        var second = PerIpRateLimit.PartitionKeyForAddress(IPAddress.Parse("203.0.113.8"));

        first.ShouldNotBe(second);
        PerIpRateLimit.PartitionKeyForAddress(IPAddress.Parse("203.0.113.7")).ShouldBe(
            first, "the same address must land in the same bucket, or the limit counts nothing.");
        first.ShouldContain("203.0.113.7", Case.Sensitive);
    }

    [Fact]
    public void An_ipv6_address_partitions_on_its_canonical_form_rather_than_its_spelling()
    {
        PerIpRateLimit.PartitionKeyForAddress(IPAddress.Parse("2001:db8::1")).ShouldBe(
            PerIpRateLimit.PartitionKeyForAddress(IPAddress.Parse("2001:0db8:0000:0000:0000:0000:0000:0001")),
            "two spellings of one address are one address. Keying on the text as typed would hand "
            + "an IPv6 client an unbounded supply of partitions.");
    }

    /// <summary>
    /// 🔒 The IPv6 arm of the same threat the forwarded-for rule closes: a residential client is
    /// delegated a whole /64 and can source from a fresh address per request at no cost.
    /// </summary>
    [Fact]
    public void Every_address_in_one_ipv6_network_shares_a_partition()
    {
        var first = PerIpRateLimit.PartitionKeyForAddress(IPAddress.Parse("2001:db8:1:2::1"));

        PerIpRateLimit.PartitionKeyForAddress(IPAddress.Parse("2001:db8:1:2::dead:beef")).ShouldBe(
            first,
            "partitioning on the full /128 would let one client mint an unlimited number of "
            + "identities out of the prefix it was handed.");
        PerIpRateLimit.PartitionKeyForAddress(IPAddress.Parse("2001:db8:1:2:ffff:ffff:ffff:ffff")).ShouldBe(first);
    }

    /// <summary>The negative control: the aggregation stops at the network, not above it.</summary>
    [Fact]
    public void Two_different_ipv6_networks_stay_apart()
    {
        PerIpRateLimit.PartitionKeyForAddress(IPAddress.Parse("2001:db8:1:2::1")).ShouldNotBe(
            PerIpRateLimit.PartitionKeyForAddress(IPAddress.Parse("2001:db8:1:3::1")),
            "a /64 apart is two customers; merging them would let one exhaust the other's limit.");
    }

    [Fact]
    public void An_ipv4_client_arriving_over_a_dual_stack_socket_is_the_same_partition_as_a_plain_one()
    {
        PerIpRateLimit.PartitionKeyForAddress(IPAddress.Parse("::ffff:203.0.113.7")).ShouldBe(
            PerIpRateLimit.PartitionKeyForAddress(IPAddress.Parse("203.0.113.7")),
            "which socket accepted the client is not a property of the client — two partitions for "
            + "one address would double its allowance.");
    }

    [Fact]
    public void A_request_with_no_readable_address_shares_one_bucket_rather_than_being_exempt()
    {
        PerIpRateLimit.PartitionKeyForAddress(null).ShouldBe(
            PerIpRateLimit.UnknownAddressPartition,
            "an exemption would be the one partition key an attacker would aim for.");
    }

    private static DefaultHttpContext Request(string? remoteAddress, string? forwardedFor = null)
    {
        var http = new DefaultHttpContext();

        http.Connection.RemoteIpAddress = remoteAddress is null ? null : IPAddress.Parse(remoteAddress);

        if (forwardedFor is not null)
        {
            http.Request.Headers["X-Forwarded-For"] = forwardedFor;
            http.Request.Headers["X-Real-IP"] = forwardedFor;
            http.Request.Headers["Forwarded"] = "for=" + forwardedFor;
        }

        return http;
    }

    /// <summary>
    /// 🔒 The spoofing arm, driven through the selector the limiter is actually registered with.
    /// With nothing trusted in front of this service, a forwarded-for header is attacker-supplied
    /// text; partitioning on one would let a single client mint unlimited identities and defeat the
    /// limit entirely.
    /// </summary>
    [Fact]
    public void Two_requests_from_one_address_share_a_partition_however_they_spell_their_forwarded_for()
    {
        var honest = PerIpRateLimit.PartitionKeyFor(Request("203.0.113.7"));

        PerIpRateLimit.PartitionKeyFor(Request("203.0.113.7", forwardedFor: "198.51.100.99")).ShouldBe(
            honest,
            "the partition is the transport's own remote address and never a request header.");
        PerIpRateLimit.PartitionKeyFor(Request("203.0.113.7", forwardedFor: "192.0.2.5")).ShouldBe(
            honest,
            "and a second spoofed value must not split the bucket either — otherwise one client "
            + "mints a fresh limit per header value it invents.");

        honest.ShouldNotContain("198.51.100.99", Case.Sensitive);
        honest.ShouldBe(
            PerIpRateLimit.PartitionKeyForAddress(IPAddress.Parse("203.0.113.7")),
            "the request selector and the address helper must agree, or the tested one is not the "
            + "registered one.");
    }

    /// <summary>The negative control for the arm above: the header cannot MERGE two addresses either.</summary>
    [Fact]
    public void Two_requests_from_different_addresses_stay_apart_even_sharing_a_forwarded_for()
    {
        PerIpRateLimit.PartitionKeyFor(Request("203.0.113.7", forwardedFor: "198.51.100.99")).ShouldNotBe(
            PerIpRateLimit.PartitionKeyFor(Request("203.0.113.8", forwardedFor: "198.51.100.99")),
            "if the header were trusted, two attackers behind one claimed address would share — and "
            + "exhaust — a single bucket, which is the denial-of-service half of the same hole.");
    }

    [Fact]
    public void A_request_whose_connection_has_no_address_lands_in_the_shared_unknown_partition()
    {
        PerIpRateLimit.PartitionKeyFor(Request(remoteAddress: null, forwardedFor: "198.51.100.99")).ShouldBe(
            PerIpRateLimit.UnknownAddressPartition,
            "an absent connection address is not an invitation to believe the header instead.");
    }

    /// <summary>🔒 The infrastructure refusal, which is HTTP and nothing but HTTP.</summary>
    [Theory]
    [InlineData(10)]
    [InlineData(3)]
    public void A_throttled_address_is_refused_with_429_and_the_configured_backoff(int retryAfterSeconds)
    {
        var http = new DefaultHttpContext();

        PerIpRateLimit.WriteRefusal(
            http.Response, new PerIpRateLimitOptions { RetryAfterSeconds = retryAfterSeconds });

        http.Response.StatusCode.ShouldBe(
            429,
            "the infrastructure limit is a transport failure, not a decided answer — the client "
            + "backs off and retries the SAME command id.");
        http.Response.Headers.RetryAfter.ToString().ShouldBe(
            retryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "the header tracks the configured backoff rather than a constant.");
    }

    /// <summary>🔒 The two limits must never blur: this one carries no rejection envelope at all.</summary>
    [Fact]
    public void The_infrastructure_refusal_carries_no_rejection_envelope()
    {
        var http = new DefaultHttpContext();
        http.Response.Body = new MemoryStream();

        PerIpRateLimit.WriteRefusal(http.Response, new PerIpRateLimitOptions());

        http.Response.Body.Length.ShouldBe(
            0,
            "RATE_LIMITED is the per-PLAYER answer and rides HTTP 200. Handing a client both "
            + "signals at once tells it to back off and to never retry, which are opposite rules.");
        http.Response.ContentType.ShouldBeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_refusal_with_a_non_positive_backoff_is_refused_rather_than_telling_a_client_to_retry_now(
        int retryAfterSeconds)
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => PerIpRateLimit.WriteRefusal(
                new DefaultHttpContext().Response,
                new PerIpRateLimitOptions { RetryAfterSeconds = retryAfterSeconds }));
    }

    [Fact]
    public void The_bucket_is_built_from_the_options_and_refills_the_sustained_rate_every_second()
    {
        var bucket = PerIpRateLimit.BucketFor(new PerIpRateLimitOptions { PermitsPerSecond = 7, Burst = 13 });

        bucket.TokenLimit.ShouldBe(13, "the burst is the bucket's capacity.");
        bucket.TokensPerPeriod.ShouldBe(7);
        bucket.ReplenishmentPeriod.ShouldBe(TimeSpan.FromSeconds(1));
        bucket.QueueLimit.ShouldBe(
            0,
            "a refused request is refused, never queued: queueing would spend the command latency "
            + "budget holding a request the client has already been told to back off from.");
        bucket.AutoReplenishment.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0, 100, nameof(PerIpRateLimitOptions.PermitsPerSecond))]
    [InlineData(-1, 100, nameof(PerIpRateLimitOptions.PermitsPerSecond))]
    [InlineData(20, 0, nameof(PerIpRateLimitOptions.Burst))]
    [InlineData(20, -7, nameof(PerIpRateLimitOptions.Burst))]
    public void A_non_positive_number_is_refused_and_the_refusal_names_which_one(
        int permitsPerSecond, int burst, string blamed)
    {
        var options = new PerIpRateLimitOptions { PermitsPerSecond = permitsPerSecond, Burst = burst };

        Should.Throw<ArgumentOutOfRangeException>(() => PerIpRateLimit.BucketFor(options))
            .Message.ShouldContain(
                blamed,
                Case.Sensitive,
                "two independent rules throw the same exception type here; without naming the "
                + "offending setting an operator cannot tell which of their numbers was refused.");
    }
}
