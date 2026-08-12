using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Tests.Content;
using SlayIdleRepeat.TestSupport;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Model;

/// <summary>
/// 🔒 `30` §2.3 — the daily/weekly counter <b>mechanism</b> M1-08's <c>AdvanceTime</c> drives:
/// a period boundary, a key→count map, and the invariants that keep both meaningful.
/// </summary>
/// <remarks>
/// ⚠️ There is no catalogue of counter keys here, and there is not supposed to be. `30` §2.3 names
/// five things the 05:00 UTC reset clears — quest expiry, the wheel's free spin, ad caps, dungeon
/// entries and daily-shop stock — and none of those systems exists yet (M4-09, M10, `12`). What
/// M1-04 ships is the shape they will register into; freezing their vocabulary now would invent it
/// (S6). The keys used below are illustrative and are named as such.
/// </remarks>
public sealed class PlayerCounterTests
{
    private static ContentSnapshot Content => ProgressionDocuments.Shipped;

    private static Core.Model.Player Player() =>
        Core.Model.Player.Rehydrate(PlayerSnapshots.Valid, Content).Value;

    /// <summary>A counter registers itself on first use — nothing declares it in advance.</summary>
    [Fact]
    public void A_counter_comes_into_existence_on_its_first_increment()
    {
        var player = Player();

        player.DailyCount("some_future_system").ShouldBe(0);
        player.DailyCounters.ShouldBeEmpty();

        player.CountDaily("some_future_system", 1);

        player.DailyCount("some_future_system").ShouldBe(1);
        player.DailyCounters.Keys.ShouldBe(new[] { "some_future_system" });
    }

    /// <summary>Increments accumulate within a period.</summary>
    [Fact]
    public void Increments_accumulate_within_a_period()
    {
        var player = Player();

        player.CountDaily("some_future_system", 2);
        player.CountDaily("some_future_system", 3);

        player.DailyCount("some_future_system").ShouldBe(5);
    }

    /// <summary>🔒 Daily and weekly are two independent maps — counting one must not count the other.</summary>
    [Fact]
    public void The_daily_and_weekly_maps_are_independent()
    {
        var player = Player();

        player.CountDaily("shared_key", 4);
        player.CountWeekly("shared_key", 9);

        player.DailyCount("shared_key").ShouldBe(4);
        player.WeeklyCount("shared_key").ShouldBe(9);
    }

    /// <summary>🔒 Keys are compared ordinally, as <c>CanonicalStateWriter</c> orders them.</summary>
    /// <remarks>
    /// A map that compared its keys case-insensitively would round-trip to a different
    /// <c>stateHash</c> than the one it was stored under, because the writer sorts string keys
    /// ordinally and would see one entry where the aggregate saw two — or vice versa.
    /// </remarks>
    [Fact]
    public void Counter_keys_are_ordinal()
    {
        var player = Player();

        player.CountDaily("ad_caps", 1);
        player.CountDaily("AD_CAPS", 1);

        player.DailyCount("ad_caps").ShouldBe(1);
        player.DailyCount("AD_CAPS").ShouldBe(1);
        player.DailyCounters.Count.ShouldBe(2);
    }

    /// <summary>A blank key is refused: a counter key names the system that owns the counter.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_counter_key_is_refused(string key)
    {
        var player = Player();

        Should.Throw<ArgumentException>(() => player.CountDaily(key, 1))
              .Message.ShouldMatchWildcard("*names the system that owns the counter*");

        Should.Throw<ArgumentException>(() => player.DailyCount(key))
              .Message.ShouldMatchWildcard("*names the system that owns the counter*");
    }

    /// <summary>
    /// 🔒 A counter counts upwards. A negative advance is a refund, and a refund belongs to the
    /// rule that granted the thing — not to the counter that recorded the use.
    /// </summary>
    [Fact]
    public void A_negative_advance_is_refused()
    {
        var player = Player();
        player.CountDaily("ad_caps", 4);

        Should.Throw<ArgumentOutOfRangeException>(() => player.CountDaily("ad_caps", -1))
              .Message.ShouldMatchWildcard("*counts upwards*");

        player.DailyCount("ad_caps").ShouldBe(4);
    }

    /// <summary>A count that would overflow is refused rather than wrapping to a negative.</summary>
    [Fact]
    public void An_overflowing_count_is_refused()
    {
        var player = Player();
        player.CountWeekly("guild_boss_damage", long.MaxValue);

        Should.Throw<ArgumentOutOfRangeException>(() => player.CountWeekly("guild_boss_damage", 1))
              .Message.ShouldMatchWildcard("*overflows a 64-bit count*");

        player.WeeklyCount("guild_boss_damage").ShouldBe(long.MaxValue);
    }

    /// <summary>A reset clears the daily counters and records the boundary they were cleared at.</summary>
    [Fact]
    public void A_daily_reset_clears_the_counters_and_records_the_boundary()
    {
        var player = Player();
        player.CountDaily("ad_caps", 4);
        var nextDay = PlayerSnapshots.Wednesday.AddDays(1);

        player.ResetDailyCounters(nextDay);

        player.DailyCounters.ShouldBeEmpty();
        player.DailyCount("ad_caps").ShouldBe(0);
        player.DailyPeriodStartUtc.ShouldBe(nextDay);
    }

    /// <summary>🔒 …and it leaves the weekly counters alone. A day boundary is not a week boundary.</summary>
    [Fact]
    public void A_daily_reset_leaves_the_weekly_counters_alone()
    {
        var player = Player();
        player.CountWeekly("guild_quest_contributions", 3);

        player.ResetDailyCounters(PlayerSnapshots.Wednesday.AddDays(1));

        player.WeeklyCount("guild_quest_contributions").ShouldBe(3);
        player.WeeklyPeriodStartUtc.ShouldBe(PlayerSnapshots.Monday);
    }

    /// <summary>The weekly reset is the mirror image, and leaves the daily counters alone.</summary>
    [Fact]
    public void A_weekly_reset_clears_only_the_weekly_counters()
    {
        var player = Player();
        player.CountDaily("ad_caps", 2);
        player.CountWeekly("guild_quest_contributions", 3);
        var nextWeek = PlayerSnapshots.Monday.AddDays(7);

        player.ResetWeeklyCounters(nextWeek);

        player.WeeklyCounters.ShouldBeEmpty();
        player.WeeklyPeriodStartUtc.ShouldBe(nextWeek);
        player.DailyCount("ad_caps").ShouldBe(2);
    }

    /// <summary>
    /// 🔒 `30` §2.3 — a period boundary is 05:00 UTC. Any other time of day is a period the rest of
    /// the game does not agree exists.
    /// </summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(4, 59)]
    [InlineData(5, 1)]
    [InlineData(12, 0)]
    public void A_daily_boundary_that_is_not_at_0500_UTC_is_refused(int hour, int minute)
    {
        var player = Player();
        var notABoundary = new DateTimeOffset(2026, 8, 13, hour, minute, 0, TimeSpan.Zero);

        Should.Throw<ArgumentOutOfRangeException>(() => player.ResetDailyCounters(notABoundary))
              .Message.ShouldMatchWildcard("*not a game-day boundary*05:00 UTC*");
    }

    /// <summary>…including one that is 05:00 on a clock two hours ahead, which is 03:00 UTC.</summary>
    [Fact]
    public void A_boundary_at_0500_in_another_offset_is_refused_as_an_offset()
    {
        var player = Player();
        var localFive = new DateTimeOffset(2026, 8, 13, 5, 0, 0, TimeSpan.FromHours(2));

        Should.Throw<ArgumentOutOfRangeException>(() => player.ResetDailyCounters(localFive))
              .Message.ShouldMatchWildcard("*offset*Unix milliseconds*");
    }

    /// <summary>
    /// 🔒 Milestone assumption <b>A2</b>, derived from `27` §4: the game <b>week</b> starts on a
    /// <b>Monday</b>. Every other 05:00 UTC boundary is a legal day and an illegal week.
    /// </summary>
    /// <remarks>
    /// The days are all <b>after</b> the fixture's current daily boundary (2026-08-12). A day
    /// before it would be refused by the monotonic clause instead, and the test would pass for the
    /// wrong reason — which is what the first draft of this test did.
    /// </remarks>
    [Theory]
    [InlineData(18)] // Tuesday
    [InlineData(19)] // Wednesday
    [InlineData(20)] // Thursday
    [InlineData(21)] // Friday
    [InlineData(22)] // Saturday
    [InlineData(23)] // Sunday
    public void A_weekly_boundary_that_is_not_a_Monday_is_refused(int dayOfMonth)
    {
        var player = Player();
        var notMonday = new DateTimeOffset(2026, 8, dayOfMonth, 5, 0, 0, TimeSpan.Zero);

        notMonday.DayOfWeek.ShouldNotBe(DayOfWeek.Monday, "the fixture must actually not be a Monday");

        Should.Throw<ArgumentOutOfRangeException>(() => player.ResetWeeklyCounters(notMonday))
              .Message.ShouldMatchWildcard("*game week starts MONDAY*A2*27 §4*");

        // …and the very same instant is a perfectly legal DAY boundary, which is what makes the
        // Monday clause the thing under test rather than the 05:00 clause a second time.
        Should.NotThrow(() => player.ResetDailyCounters(notMonday));
    }

    /// <summary>A Monday at 05:00 UTC is accepted, so the rule above is not "refuse every week".</summary>
    [Fact]
    public void A_Monday_at_0500_UTC_is_accepted_as_a_week_boundary()
    {
        var player = Player();
        var monday = PlayerSnapshots.Monday.AddDays(7);

        monday.DayOfWeek.ShouldBe(DayOfWeek.Monday);

        Should.NotThrow(() => player.ResetWeeklyCounters(monday));
        player.WeeklyPeriodStartUtc.ShouldBe(monday);
    }

    /// <summary>
    /// A reset to an earlier boundary is refused: it would clear a period already counted against,
    /// handing back every cap the player has already spent.
    /// </summary>
    [Fact]
    public void A_reset_to_an_earlier_boundary_is_refused()
    {
        var player = Player();

        Should.Throw<ArgumentOutOfRangeException>(
                  () => player.ResetDailyCounters(PlayerSnapshots.Wednesday.AddDays(-1)))
              .Message.ShouldMatchWildcard("*handing back every cap*");

        Should.Throw<ArgumentOutOfRangeException>(
                  () => player.ResetWeeklyCounters(PlayerSnapshots.Monday.AddDays(-7)))
              .Message.ShouldMatchWildcard("*handing back every cap*");
    }

    /// <summary>
    /// 🔒 Resetting to the boundary <b>already in force</b> is a no-op, not a clear.
    /// </summary>
    /// <remarks>
    /// ⚠️ This is the sharpest correctness case in the mechanism. `30` §2.3 runs lazy catch-up as
    /// the first step of <em>every</em> command, so M1-08 calls this on every command a player
    /// sends. Clearing on equality would wipe the day's ad caps, dungeon entries and quest progress
    /// several times an hour — handing back every cap the player had already spent, which is
    /// exactly what the monotonic guard's own message says must never happen.
    /// </remarks>
    [Fact]
    public void Resetting_to_the_boundary_already_in_force_keeps_the_counts()
    {
        var player = Player();
        player.CountDaily("ad_caps", 2);
        player.CountWeekly("guild_quest_contributions", 3);

        player.ResetDailyCounters(PlayerSnapshots.Wednesday);
        player.ResetWeeklyCounters(PlayerSnapshots.Monday);

        player.DailyCount("ad_caps").ShouldBe(2, "the game day has not turned over");
        player.WeeklyCount("guild_quest_contributions").ShouldBe(3, "the game week has not turned over");
        player.DailyPeriodStartUtc.ShouldBe(PlayerSnapshots.Wednesday);
        player.WeeklyPeriodStartUtc.ShouldBe(PlayerSnapshots.Monday);
    }

    /// <summary>
    /// …and a boundary that <b>has</b> moved does clear, so the no-op above is not "never resets".
    /// </summary>
    [Fact]
    public void A_boundary_that_has_moved_does_clear()
    {
        var player = Player();
        player.CountDaily("ad_caps", 2);

        player.ResetDailyCounters(PlayerSnapshots.Wednesday.AddDays(1));

        player.DailyCount("ad_caps").ShouldBe(0);
    }

    /// <summary>The exposed counter maps are read-only views, not the aggregate's own dictionaries.</summary>
    [Fact]
    public void The_exposed_counter_maps_cannot_be_mutated_through_their_reference()
    {
        var player = Player();
        player.CountDaily("ad_caps", 1);

        Should.Throw<NotSupportedException>(
            () => ((IDictionary<string, long>)player.DailyCounters)["ad_caps"] = 99);

        player.DailyCount("ad_caps").ShouldBe(1);
    }

    /// <summary>A persisted counter map with a blank key or a negative count is a corrupt row.</summary>
    [Fact]
    public void A_persisted_counter_map_with_a_blank_key_or_a_negative_count_is_refused()
    {
        var blank = Core.Model.Player.Rehydrate(
            PlayerSnapshots.With(dailyCounters: PlayerSnapshots.Counters((" ", 1))), Content);
        blank.IsFailure.ShouldBeTrue();
        blank.Error.ShouldContain("DailyCounters carries a blank counter key", Case.Sensitive);

        var negative = Core.Model.Player.Rehydrate(
            PlayerSnapshots.With(weeklyCounters: PlayerSnapshots.Counters(("ad_caps", -1))), Content);
        negative.IsFailure.ShouldBeTrue();
        negative.Error.ShouldContain("WeeklyCounters['ad_caps'] is -1", Case.Sensitive);
    }

    /// <summary>A null counter map is refused; an absent map is not an empty one.</summary>
    [Fact]
    public void A_null_counter_map_is_refused()
    {
        Core.Model.Player.Rehydrate(PlayerSnapshots.WithNull(daily: true), Content)
            .Error.ShouldContain("DailyCounters is null", Case.Sensitive);

        Core.Model.Player.Rehydrate(PlayerSnapshots.WithNull(weekly: true), Content)
            .Error.ShouldContain("WeeklyCounters is null", Case.Sensitive);
    }

    /// <summary>
    /// A persisted daily boundary that is not at 05:00 UTC, or a weekly one that is not a Monday,
    /// is refused at the seam as well as at the mutator.
    /// </summary>
    [Fact]
    public void A_persisted_period_boundary_is_validated_at_the_seam_too()
    {
        var offDay = Core.Model.Player.Rehydrate(
            PlayerSnapshots.With(dailyPeriodStartUtc: PlayerSnapshots.Midmorning), Content);
        offDay.IsFailure.ShouldBeTrue();
        offDay.Error.ShouldContain("not a 05:00 UTC game-day boundary", Case.Sensitive);

        var offWeek = Core.Model.Player.Rehydrate(
            PlayerSnapshots.With(weeklyPeriodStartUtc: PlayerSnapshots.Wednesday), Content);
        offWeek.IsFailure.ShouldBeTrue();
        offWeek.Error.ShouldContain("game week starts MONDAY 05:00 UTC", Case.Sensitive);
    }

    /// <summary>🔒 Every persisted instant must carry a zero offset, named field by field.</summary>
    /// <remarks>
    /// <c>CanonicalStateWriter</c> encodes a <see cref="DateTimeOffset"/> as Unix milliseconds, so
    /// <c>12:00+02:00</c> and <c>10:00Z</c> hash identically while record equality calls them
    /// different — two snapshots the language says are different sharing one <c>stateHash</c>,
    /// which is the same class of defect as the <c>-0.0</c> the writer already refuses. Each field
    /// is asserted by name so a check that covered three of five would fail here rather than pass.
    /// </remarks>
    [Fact]
    public void Every_persisted_instant_must_be_UTC()
    {
        var offset = TimeSpan.FromHours(2);

        Refuses(PlayerSnapshots.With(energyAnchorUtc: new DateTimeOffset(2026, 8, 12, 9, 0, 0, offset)),
            nameof(PlayerSnapshots.Valid.EnergyAnchorUtc));
        Refuses(PlayerSnapshots.With(lastAppliedAtUtc: new DateTimeOffset(2026, 8, 12, 9, 0, 0, offset)),
            nameof(PlayerSnapshots.Valid.LastAppliedAtUtc));
        Refuses(PlayerSnapshots.With(dailyPeriodStartUtc: new DateTimeOffset(2026, 8, 12, 5, 0, 0, offset)),
            nameof(PlayerSnapshots.Valid.DailyPeriodStartUtc));
        Refuses(PlayerSnapshots.With(weeklyPeriodStartUtc: new DateTimeOffset(2026, 8, 10, 5, 0, 0, offset)),
            nameof(PlayerSnapshots.Valid.WeeklyPeriodStartUtc));
        Refuses(
            PlayerSnapshots.With(
                ftueBeatId: SlayIdleRepeat.Core.Primitives.FtueBeat.B10,
                ftueCompletedAtUtc: new DateTimeOffset(2026, 8, 12, 9, 0, 0, offset)),
            nameof(PlayerSnapshots.Valid.FtueCompletedAtUtc));

        static void Refuses(SlayIdleRepeat.Core.Model.Snapshots.PlayerSnapshot snapshot, string field)
        {
            var result = Core.Model.Player.Rehydrate(snapshot, ProgressionDocuments.Shipped);

            result.IsFailure.ShouldBeTrue($"{field} carries a non-zero offset");
            result.Error.ShouldContain(field + " is", Case.Sensitive);
            result.Error.ShouldContain("offset", Case.Sensitive);
        }
    }

    /// <summary>
    /// `30` §2.3 — <c>LastAppliedAtUtc</c> moves forwards, and equal is allowed: two commands can
    /// legitimately share an instant, while an earlier one means a clock moved backwards.
    /// </summary>
    [Fact]
    public void The_last_applied_instant_moves_forwards_and_may_repeat()
    {
        var player = Player();
        var at = player.LastAppliedAtUtc;

        Should.NotThrow(() => player.MarkApplied(at));
        Should.NotThrow(() => player.MarkApplied(at.AddSeconds(1)));
        player.LastAppliedAtUtc.ShouldBe(at.AddSeconds(1));

        Should.Throw<ArgumentOutOfRangeException>(() => player.MarkApplied(at))
              .Message.ShouldMatchWildcard("*30 §2.3 rolls state forward FROM this instant*");
    }

    /// <summary><c>MarkApplied</c> refuses a non-UTC instant for the same reason the seam does.</summary>
    [Fact]
    public void The_last_applied_instant_must_be_UTC()
    {
        Should.Throw<ArgumentOutOfRangeException>(
                  () => Player().MarkApplied(new DateTimeOffset(2026, 8, 13, 9, 0, 0, TimeSpan.FromHours(2))))
              .Message.ShouldMatchWildcard("*Unix milliseconds*");
    }
}
