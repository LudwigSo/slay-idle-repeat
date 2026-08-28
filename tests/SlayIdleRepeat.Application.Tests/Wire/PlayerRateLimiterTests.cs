using Shouldly;
using SlayIdleRepeat.Adapters.InMemory;
using SlayIdleRepeat.Application.Wire;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Wire;

/// <summary>
/// The per-player application-level limit: the burst it admits, the rate it refills at, the
/// isolation between players, the bound on what it holds — and the arithmetic that answers whether
/// it can refuse a real player mid-run.
/// </summary>
public sealed class PlayerRateLimiterTests
{
    private static readonly PlayerId Alice = new("PLAYER_alice");
    private static readonly PlayerId Bob = new("PLAYER_bob");

    /// <summary>The command round trip the latency budget targets at p95.</summary>
    private static readonly TimeSpan BudgetedRoundTrip = TimeSpan.FromMilliseconds(150);

    /// <summary>How many commands a run costs, from the authored cost shape (~30 commands/run).</summary>
    private const int CommandsPerRun = 30;

    /// <summary>
    /// The shipped per-player policy, built the one way production builds it — from the two
    /// constants the host's settings also default from, never from numbers re-typed here.
    /// </summary>
    private static RateLimitPolicy Shipped { get; } =
        RateLimitPolicy.PerSecond(RateLimitPolicy.PerPlayerPermitsPerSecond, RateLimitPolicy.PerPlayerBurst);

    private static (PlayerRateLimiter Limiter, AdjustableClock Clock) Build(RateLimitPolicy? policy = null)
    {
        var clock = new AdjustableClock();

        return (new PlayerRateLimiter(clock, policy ?? Shipped), clock);
    }

    [Fact]
    public void The_shipped_policy_is_five_per_second_with_a_burst_of_twenty()
    {
        var policy = Shipped;

        policy.Burst.ShouldBe(20);
        policy.RefillInterval.ShouldBe(
            TimeSpan.FromMilliseconds(200),
            "5 permits per second is one permit every 200 ms; the interval is what the limiter "
            + "actually measures with, so it is the half worth pinning.");
    }

    [Fact]
    public void An_idle_player_may_spend_the_whole_burst_back_to_back_and_is_refused_on_the_next()
    {
        var (limiter, _) = Build();

        for (var i = 1; i <= 20; i++)
        {
            limiter.ShouldReject(Alice).ShouldBeFalse(
                $"request {i} of an instantaneous burst is within the burst of 20.");
        }

        limiter.ShouldReject(Alice).ShouldBeTrue(
            "the 21st instantaneous request is one past the burst, and this is the refusal the "
            + "gateway turns into RATE_LIMITED.");
    }

    [Fact]
    public void One_refill_interval_of_silence_buys_back_exactly_one_permit()
    {
        var (limiter, clock) = Build();

        for (var i = 0; i < 20; i++)
        {
            limiter.ShouldReject(Alice);
        }

        limiter.ShouldReject(Alice).ShouldBeTrue("the burst is spent.");

        clock.Advance(TimeSpan.FromMilliseconds(200));

        limiter.ShouldReject(Alice).ShouldBeFalse("200 ms is exactly one permit at 5 per second.");
        limiter.ShouldReject(Alice).ShouldBeTrue("and exactly one — the second is refused again.");
    }

    [Fact]
    public void Idle_time_never_banks_more_than_the_burst()
    {
        var (limiter, clock) = Build();

        clock.Advance(TimeSpan.FromHours(1));

        for (var i = 1; i <= 20; i++)
        {
            limiter.ShouldReject(Alice).ShouldBeFalse($"request {i} is inside the burst.");
        }

        limiter.ShouldReject(Alice).ShouldBeTrue(
            "an hour idle is 18,000 permits' worth of refill and must still cap at the burst of 20 "
            + "— otherwise a player who put the phone down for lunch comes back able to flood.");
    }

    [Fact]
    public void One_players_flood_never_refuses_another_player()
    {
        var (limiter, _) = Build();

        for (var i = 0; i < 200; i++)
        {
            limiter.ShouldReject(Alice);
        }

        limiter.ShouldReject(Bob).ShouldBeFalse(
            "the limit is per player. A shared or stripe-collided bucket would let one account's "
            + "flood refuse an unrelated player's legitimate command.");
    }

    /// <summary>
    /// 🔒 The headroom proof, as arithmetic rather than as a claim: at the budgeted round trip the
    /// shipped policy admits far more consecutive commands than a whole run costs.
    /// </summary>
    [Fact]
    public void At_the_budgeted_round_trip_the_policy_admits_far_more_than_a_run_costs()
    {
        var policy = Shipped;

        policy.MaxConsecutiveAt(BudgetedRoundTrip).ShouldBe(
            77,
            "a client is serialised by the sequencing rules — one command in flight per scope — so "
            + "its fastest legitimate cadence is one round trip. At the budgeted 150 ms that is 77 "
            + "consecutive commands before a refusal, against a run that costs 30.");

        policy.MaxConsecutiveAt(BudgetedRoundTrip).ShouldBeGreaterThan(
            CommandsPerRun * 2,
            "and the margin is more than double a whole run, not a hair's breadth.");
    }

    /// <summary>
    /// The exact reach at five cadences, and with it the honest limit. ⚠️ The real boundary is
    /// 68.966 ms, so 69 ms clears a whole run by 0.005 of a permit — this is a cliff, not a slope,
    /// and any change to how elapsed permits are counted moves it.
    /// </summary>
    [Theory]
    [InlineData(0, 20)]
    [InlineData(68, 29)]
    [InlineData(69, 30)]
    [InlineData(150, 77)]
    [InlineData(199, 3801)]
    public void The_reach_at_a_fixed_cadence_is_exact_and_the_run_boundary_sits_at_sixty_nine_milliseconds(
        int roundTripMilliseconds, int expectedReach)
    {
        var reach = Shipped
            .MaxConsecutiveAt(TimeSpan.FromMilliseconds(roundTripMilliseconds));

        reach.ShouldBe(
            expectedReach,
            $"at a {roundTripMilliseconds} ms cadence the shipped policy reaches exactly "
            + $"{expectedReach} consecutive commands. A boolean 'enough?' would pass for any "
            + "number above the run cost and hide a change of an order of magnitude.");

        (reach >= CommandsPerRun).ShouldBe(
            roundTripMilliseconds >= 69,
            $"a run costs {CommandsPerRun}. Below 69 ms a run played with literally zero think "
            + "time would be refused, which is why the burst is configurable and why this boundary "
            + "is pinned rather than assumed.");
    }

    [Fact]
    public void A_zero_cadence_reaches_exactly_the_burst()
    {
        Shipped.MaxConsecutiveAt(TimeSpan.Zero).ShouldBe(
            20,
            "an infinitely fast client can spend the burst and nothing more — not one command, and "
            + "not unlimited, which are the two degenerate answers the formula can produce.");
    }

    [Theory]
    [InlineData(200)]
    [InlineData(1_000)]
    public void A_cadence_at_or_slower_than_the_sustained_rate_is_never_refused(int roundTripMilliseconds)
    {
        Shipped
            .MaxConsecutiveAt(TimeSpan.FromMilliseconds(roundTripMilliseconds))
            .ShouldBe(
                int.MaxValue,
                "at or below the sustained rate the bucket never drains, so there is no consecutive "
                + "count at which a refusal happens — a finite number here would understate it.");
    }

    [Fact]
    public void A_negative_cadence_is_refused_rather_than_answered()
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => Shipped.MaxConsecutiveAt(TimeSpan.FromMilliseconds(-1)));
    }

    /// <summary>The proof driven through the real limiter and the real clock, not only the arithmetic.</summary>
    [Fact]
    public void A_whole_run_at_the_budgeted_round_trip_is_never_refused()
    {
        var (limiter, clock) = Build();

        for (var command = 1; command <= CommandsPerRun; command++)
        {
            limiter.ShouldReject(Alice).ShouldBeFalse(
                $"command {command} of a {CommandsPerRun}-command run, issued back to back at the "
                + "budgeted round trip with no think time at all, must never be refused.");

            clock.Advance(BudgetedRoundTrip);
        }
    }

    [Fact]
    public void A_stream_of_invented_identities_cannot_grow_the_tracked_map_past_its_capacity()
    {
        var limiter = new PlayerRateLimiter(
            new AdjustableClock(), Shipped, trackedPlayerCapacity: 32);

        for (var i = 0; i < 500; i++)
        {
            limiter.ShouldReject(new PlayerId("PLAYER_invented_" + i));
        }

        limiter.TrackedPlayers.ShouldBeLessThanOrEqualTo(
            32,
            "an unauthenticated flood cannot be allowed to allocate memory per invented identity — "
            + "the gateway's own striped gate pool is fixed for the same reason.");
    }

    /// <summary>
    /// The discriminating half of the bound: eviction sheds the buckets closest to full, so the
    /// account actually being throttled keeps its bucket while the idle ones go.
    /// </summary>
    [Fact]
    public void Eviction_sheds_the_most_refilled_buckets_and_keeps_the_one_being_throttled()
    {
        var limiter = new PlayerRateLimiter(
            new AdjustableClock(), Shipped, trackedPlayerCapacity: 32);

        for (var i = 0; i < 20; i++)
        {
            limiter.ShouldReject(Alice);
        }

        limiter.ShouldReject(Alice).ShouldBeTrue("Alice's burst is spent.");

        for (var i = 0; i < 500; i++)
        {
            limiter.ShouldReject(new PlayerId("PLAYER_invented_" + i));
        }

        limiter.ShouldReject(Alice).ShouldBeTrue(
            "a flood of invented identities must not evict the one bucket that is doing work. If it "
            + "did, an attacker could clear their own throttle by inventing identities.");
    }

    /// <summary>
    /// 🔒 The best-effort claim, with an eviction that really happens: a bucket that has refilled
    /// completely answers every question identically to an absent one, so shedding it costs nothing.
    /// </summary>
    [Fact]
    public void Shedding_a_refilled_bucket_changes_no_answer_the_limiter_would_have_given()
    {
        var clock = new AdjustableClock();
        var limiter = new PlayerRateLimiter(
            clock, Shipped, trackedPlayerCapacity: 32);

        for (var i = 0; i < 20; i++)
        {
            limiter.ShouldReject(Alice);
        }

        limiter.ShouldReject(Alice).ShouldBeTrue("Alice's burst is spent.");

        // Five idle minutes refills Alice completely, so a sweep may now legitimately take her.
        clock.Advance(TimeSpan.FromMinutes(5));

        for (var i = 0; i < 500; i++)
        {
            limiter.ShouldReject(new PlayerId("PLAYER_invented_" + i));
        }

        limiter.TrackedPlayers.ShouldBeLessThanOrEqualTo(32);

        for (var i = 1; i <= 20; i++)
        {
            limiter.ShouldReject(Alice).ShouldBeFalse(
                $"command {i} of a fresh burst. Whether Alice's bucket was swept or refilled in "
                + "place, a refilled bucket and an absent one are the same bucket — which is why "
                + "losing this state costs a moment of unthrottled traffic and never progress.");
        }

        limiter.ShouldReject(Alice).ShouldBeTrue(
            "and the burst still ends where it should, so the sweep restored a real bucket rather "
            + "than an unlimited one.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_burst_below_one_is_refused_rather_than_silently_treated_as_one(int burst)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => RateLimitPolicy.PerSecond(5, burst));
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-5d)]
    [InlineData(double.NaN)]
    public void A_non_positive_rate_is_refused_rather_than_producing_a_limiter_that_never_refills(
        double permitsPerSecond)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => RateLimitPolicy.PerSecond(permitsPerSecond, 20));
    }

    /// <summary>
    /// 🔒 The overflow arm, in both directions. A rate so slow that its interval leaves the range
    /// the meter can hold must be REFUSED — the unchecked conversion would wrap, clamp to one tick,
    /// and turn the slowest conceivable setting into ten million permits a second.
    /// </summary>
    [Theory]
    [InlineData(1e-9)]
    [InlineData(1e-30)]
    [InlineData(double.Epsilon)]
    public void A_rate_slower_than_one_permit_a_day_is_refused_rather_than_wrapping(double permitsPerSecond)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => RateLimitPolicy.PerSecond(permitsPerSecond, 20))
            .ParamName.ShouldBe("permitsPerSecond");
    }

    [Fact]
    public void A_refill_interval_longer_than_a_day_is_refused_rather_than_overflowing_the_meter()
    {
        Should.Throw<ArgumentOutOfRangeException>(
                () => new RateLimitPolicy(Burst: 2, TimeSpan.MaxValue))
            .ParamName.ShouldBe("RefillInterval");

        Should.NotThrow(() => new RateLimitPolicy(Burst: 2, RateLimitPolicy.LongestRefillInterval));
    }

    /// <summary>
    /// The negative control for the bound above: a policy at the very edge still LIMITS. Without
    /// this, "refused past a day" could be satisfied by a policy that refused everything.
    /// </summary>
    [Fact]
    public void A_policy_at_the_slowest_permitted_refill_still_limits()
    {
        var clock = new AdjustableClock();
        var limiter = new PlayerRateLimiter(
            clock, new RateLimitPolicy(Burst: 2, RateLimitPolicy.LongestRefillInterval));

        limiter.ShouldReject(Alice).ShouldBeFalse();
        limiter.ShouldReject(Alice).ShouldBeFalse();
        limiter.ShouldReject(Alice).ShouldBeTrue("the burst of two is spent.");

        clock.Advance(RateLimitPolicy.LongestRefillInterval);

        limiter.ShouldReject(Alice).ShouldBeFalse("and a day buys exactly one permit back.");
    }

    [Fact]
    public void A_tracked_capacity_below_one_is_refused_rather_than_shedding_every_bucket()
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => new PlayerRateLimiter(new AdjustableClock(), Shipped, 0));
    }

    /// <summary>
    /// A capacity large enough to overflow the int product the sweep computes its target from. The
    /// wrapped product would collapse the largest map anybody asked for into a single bucket.
    /// </summary>
    [Fact]
    public void A_very_large_tracked_capacity_does_not_collapse_the_map_to_one_bucket()
    {
        var limiter = new PlayerRateLimiter(
            new AdjustableClock(), Shipped, trackedPlayerCapacity: int.MaxValue);

        for (var i = 0; i < 200; i++)
        {
            limiter.ShouldReject(new PlayerId("PLAYER_invented_" + i));
        }

        limiter.TrackedPlayers.ShouldBe(
            200, "nothing may be swept while the map is nowhere near a capacity of int.MaxValue.");
    }

    /// <summary>The negative control: a policy is not the shipped one just because it exists.</summary>
    [Fact]
    public void A_tighter_policy_refuses_where_the_shipped_one_does_not()
    {
        var (tight, _) = Build(RateLimitPolicy.PerSecond(1, burst: 2));

        tight.ShouldReject(Alice).ShouldBeFalse();
        tight.ShouldReject(Alice).ShouldBeFalse();
        tight.ShouldReject(Alice).ShouldBeTrue(
            "a burst of 2 refuses the third — the same code path the shipped burst of 20 admits, so "
            + "these tests are measuring the policy and not a constant.");

        RateLimitPolicy.PerSecond(1, burst: 2).MaxConsecutiveAt(BudgetedRoundTrip).ShouldBe(
            2, "and the arithmetic tracks the policy too, rather than reporting the shipped number.");
    }
}
