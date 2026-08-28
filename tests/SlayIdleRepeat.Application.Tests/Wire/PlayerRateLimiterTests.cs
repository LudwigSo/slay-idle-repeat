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

    private static (PlayerRateLimiter Limiter, AdjustableClock Clock) Build(RateLimitPolicy? policy = null)
    {
        var clock = new AdjustableClock();

        return (new PlayerRateLimiter(clock, policy ?? RateLimitPolicy.PerPlayerDefault), clock);
    }

    [Fact]
    public void The_shipped_policy_is_five_per_second_with_a_burst_of_twenty()
    {
        var policy = RateLimitPolicy.PerPlayerDefault;

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
        var policy = RateLimitPolicy.PerPlayerDefault;

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
    /// The other end of the same proof, and the honest limit: below a 69 ms round trip a
    /// zero-think-time run would out-run the burst. Stated as a test so the number cannot rot.
    /// </summary>
    [Theory]
    [InlineData(69, true)]
    [InlineData(68, false)]
    [InlineData(150, true)]
    [InlineData(1000, true)]
    public void A_whole_run_back_to_back_is_admitted_down_to_a_sixty_nine_millisecond_round_trip(
        int roundTripMilliseconds, bool admitted)
    {
        var reach = RateLimitPolicy.PerPlayerDefault
            .MaxConsecutiveAt(TimeSpan.FromMilliseconds(roundTripMilliseconds));

        (reach >= CommandsPerRun).ShouldBe(
            admitted,
            $"at a {roundTripMilliseconds} ms round trip the policy reaches {reach} consecutive "
            + $"commands, and a run costs {CommandsPerRun}. 69 ms is the boundary: below it a run "
            + "played with literally zero think time would be refused, which is why the burst is "
            + "configurable and why this boundary is pinned rather than assumed.");
    }

    [Fact]
    public void A_cadence_at_or_slower_than_the_sustained_rate_is_never_refused()
    {
        RateLimitPolicy.PerPlayerDefault.MaxConsecutiveAt(TimeSpan.FromMilliseconds(200)).ShouldBe(
            int.MaxValue,
            "at the sustained rate the bucket never drains, so there is no consecutive count at "
            + "which a refusal happens — reporting a finite number here would understate the policy.");
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
            new AdjustableClock(), RateLimitPolicy.PerPlayerDefault, trackedPlayerCapacity: 32);

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
            new AdjustableClock(), RateLimitPolicy.PerPlayerDefault, trackedPlayerCapacity: 32);

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

    [Fact]
    public void An_evicted_bucket_costs_a_bypass_and_never_a_false_refusal()
    {
        var (limiter, clock) = Build();

        for (var i = 0; i < 20; i++)
        {
            limiter.ShouldReject(Alice);
        }

        limiter.ShouldReject(Alice).ShouldBeTrue("Alice's burst is spent.");

        clock.Advance(TimeSpan.FromMinutes(5));

        limiter.ShouldReject(Alice).ShouldBeFalse(
            "counters are best-effort: whether Alice's bucket was swept as refilled or refilled in "
            + "place, the answer after five idle minutes is the same. Losing this state costs a "
            + "moment of unthrottled traffic, never a player's progress.");
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
    public void A_non_positive_rate_is_refused_rather_than_producing_a_limiter_that_never_refills(
        double permitsPerSecond)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => RateLimitPolicy.PerSecond(permitsPerSecond, 20));
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
