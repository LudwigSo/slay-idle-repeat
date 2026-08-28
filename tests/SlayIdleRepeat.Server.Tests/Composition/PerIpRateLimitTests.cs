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
        var first = PerIpRateLimit.PartitionKeyFor(IPAddress.Parse("203.0.113.7"));
        var second = PerIpRateLimit.PartitionKeyFor(IPAddress.Parse("203.0.113.8"));

        first.ShouldNotBe(second);
        PerIpRateLimit.PartitionKeyFor(IPAddress.Parse("203.0.113.7")).ShouldBe(
            first, "the same address must land in the same bucket, or the limit counts nothing.");
        first.ShouldContain("203.0.113.7", Case.Sensitive);
    }

    [Fact]
    public void An_ipv6_address_partitions_on_its_canonical_form_rather_than_its_spelling()
    {
        PerIpRateLimit.PartitionKeyFor(IPAddress.Parse("2001:db8::1")).ShouldBe(
            PerIpRateLimit.PartitionKeyFor(IPAddress.Parse("2001:0db8:0000:0000:0000:0000:0000:0001")),
            "two spellings of one address are one address. Keying on the text as typed would hand "
            + "an IPv6 client an unbounded supply of partitions.");
    }

    [Fact]
    public void A_request_with_no_readable_address_shares_one_bucket_rather_than_being_exempt()
    {
        PerIpRateLimit.PartitionKeyFor(null).ShouldBe(
            PerIpRateLimit.UnknownAddressPartition,
            "an exemption would be the one partition key an attacker would aim for.");
    }

    /// <summary>
    /// 🔒 The spoofing arm. With nothing trusted in front of this service, a forwarded-for header is
    /// attacker-supplied text; partitioning on one would let a single client mint unlimited
    /// identities and defeat the limit entirely.
    /// </summary>
    [Fact]
    public void A_forwarded_for_header_does_not_change_which_partition_a_request_lands_in()
    {
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");

        var honest = PerIpRateLimit.PartitionKeyFor(http.Connection.RemoteIpAddress);

        http.Request.Headers["X-Forwarded-For"] = "198.51.100.99";
        http.Request.Headers["X-Real-IP"] = "198.51.100.99";
        http.Request.Headers["Forwarded"] = "for=198.51.100.99";

        PerIpRateLimit.PartitionKeyFor(http.Connection.RemoteIpAddress).ShouldBe(
            honest,
            "the partition is the transport's own remote address and never a request header.");

        honest.ShouldNotContain("198.51.100.99", Case.Sensitive);
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
    [InlineData(0, 100, 10)]
    [InlineData(-1, 100, 10)]
    [InlineData(20, 0, 10)]
    [InlineData(20, 100, 0)]
    [InlineData(20, 100, -5)]
    public void A_non_positive_number_is_refused_rather_than_producing_a_limiter_that_refuses_everything(
        int permitsPerSecond, int burst, int retryAfterSeconds)
    {
        var options = new PerIpRateLimitOptions
        {
            PermitsPerSecond = permitsPerSecond,
            Burst = burst,
            RetryAfterSeconds = retryAfterSeconds,
        };

        Should.Throw<ArgumentOutOfRangeException>(() => PerIpRateLimit.BucketFor(options));
    }
}
