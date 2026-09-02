using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Tests.Content;
using SlayIdleRepeat.Core.Tests.TestSupport;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Model;

/// <summary>
/// The daily/weekly counter mechanism. Tested on the aggregate because no shipped command consumes
/// a counter key yet — the keys below are illustrative, not a catalogue.
/// </summary>
public sealed class PlayerCounterTests
{
    private static ContentSnapshot Content => ProgressionDocuments.Shipped;

    private static Core.Model.Player Player() =>
        Core.Model.Player.Rehydrate(PlayerSnapshots.Valid, Content).Value;

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

    [Fact]
    public void Increments_accumulate_within_a_period()
    {
        var player = Player();

        player.CountDaily("some_future_system", 2);
        player.CountDaily("some_future_system", 3);

        player.DailyCount("some_future_system").ShouldBe(5);
    }

    [Fact]
    public void The_daily_and_weekly_maps_are_independent()
    {
        var player = Player();

        player.CountDaily("shared_key", 4);
        player.CountWeekly("shared_key", 9);

        player.DailyCount("shared_key").ShouldBe(4);
        player.WeeklyCount("shared_key").ShouldBe(9);
    }

    /// <summary>Ordinal keys: a case-insensitive map would round-trip to a different <c>stateHash</c>.</summary>
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

    [Fact]
    public void A_negative_advance_is_refused()
    {
        var player = Player();
        player.CountDaily("ad_caps", 4);

        Should.Throw<ArgumentOutOfRangeException>(() => player.CountDaily("ad_caps", -1))
              .Message.ShouldMatchWildcard("*counts upwards*");

        player.DailyCount("ad_caps").ShouldBe(4);
    }

    [Fact]
    public void An_overflowing_count_is_refused()
    {
        var player = Player();
        player.CountWeekly("guild_boss_damage", long.MaxValue);

        Should.Throw<ArgumentOutOfRangeException>(() => player.CountWeekly("guild_boss_damage", 1))
              .Message.ShouldMatchWildcard("*overflows a 64-bit count*");

        player.WeeklyCount("guild_boss_damage").ShouldBe(long.MaxValue);
    }

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

    [Fact]
    public void A_daily_reset_leaves_the_weekly_counters_alone()
    {
        var player = Player();
        player.CountWeekly("guild_quest_contributions", 3);

        player.ResetDailyCounters(PlayerSnapshots.Wednesday.AddDays(1));

        player.WeeklyCount("guild_quest_contributions").ShouldBe(3);
        player.WeeklyPeriodStartUtc.ShouldBe(PlayerSnapshots.Monday);
    }

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

    /// <summary>A period boundary is 05:00 UTC (27 §4).</summary>
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

    [Fact]
    public void A_boundary_at_0500_in_another_offset_is_refused_as_an_offset()
    {
        var player = Player();
        var localFive = new DateTimeOffset(2026, 8, 13, 5, 0, 0, TimeSpan.FromHours(2));

        Should.Throw<ArgumentOutOfRangeException>(() => player.ResetDailyCounters(localFive))
              .Message.ShouldMatchWildcard("*offset*Unix milliseconds*");
    }

    /// <summary>
    /// The game week starts Monday (rule A2, 27 §4). Days are all AFTER the fixture's current daily
    /// boundary — a day before it would be refused by the monotonic clause instead.
    /// </summary>
    [Theory]
    [InlineData(18)]
    [InlineData(19)]
    [InlineData(20)]
    [InlineData(21)]
    [InlineData(22)]
    [InlineData(23)]
    public void A_weekly_boundary_that_is_not_a_Monday_is_refused(int dayOfMonth)
    {
        var player = Player();
        var notMonday = new DateTimeOffset(2026, 8, dayOfMonth, 5, 0, 0, TimeSpan.Zero);

        notMonday.DayOfWeek.ShouldNotBe(DayOfWeek.Monday, "the fixture must actually not be a Monday");

        Should.Throw<ArgumentOutOfRangeException>(() => player.ResetWeeklyCounters(notMonday))
              .Message.ShouldMatchWildcard("*game week starts MONDAY*A2*27 §4*");

        Should.NotThrow(() => player.ResetDailyCounters(notMonday));
    }

    [Fact]
    public void A_Monday_at_0500_UTC_is_accepted_as_a_week_boundary()
    {
        var player = Player();
        var monday = PlayerSnapshots.Monday.AddDays(7);

        monday.DayOfWeek.ShouldBe(DayOfWeek.Monday);

        Should.NotThrow(() => player.ResetWeeklyCounters(monday));
        player.WeeklyPeriodStartUtc.ShouldBe(monday);
    }

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
    /// Lazy catch-up runs on every command, so clearing on an equal boundary would wipe the day's
    /// counters several times an hour.
    /// </summary>
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

    [Fact]
    public void A_boundary_that_has_moved_does_clear()
    {
        var player = Player();
        player.CountDaily("ad_caps", 2);

        player.ResetDailyCounters(PlayerSnapshots.Wednesday.AddDays(1));

        player.DailyCount("ad_caps").ShouldBe(0);
    }

    [Fact]
    public void The_exposed_counter_maps_cannot_be_mutated_through_their_reference()
    {
        var player = Player();
        player.CountDaily("ad_caps", 1);

        Should.Throw<NotSupportedException>(
            () => ((IDictionary<string, long>)player.DailyCounters)["ad_caps"] = 99);

        player.DailyCount("ad_caps").ShouldBe(1);
    }

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

    [Fact]
    public void A_null_counter_map_is_refused()
    {
        Core.Model.Player.Rehydrate(PlayerSnapshots.WithNull(daily: true), Content)
            .Error.ShouldContain("DailyCounters is null", Case.Sensitive);

        Core.Model.Player.Rehydrate(PlayerSnapshots.WithNull(weekly: true), Content)
            .Error.ShouldContain("WeeklyCounters is null", Case.Sensitive);
    }

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

    /// <summary>
    /// <c>CanonicalStateWriter</c> encodes an instant as Unix milliseconds, so <c>12:00+02:00</c>
    /// and <c>10:00Z</c> would hash identically while record equality calls them different. Each
    /// field is asserted by name so a check covering three of five fails here rather than passes.
    /// </summary>
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

    /// <summary>Equal is allowed: two commands can share an instant; an earlier one is a clock moving backwards.</summary>
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

    [Fact]
    public void The_last_applied_instant_must_be_UTC()
    {
        Should.Throw<ArgumentOutOfRangeException>(
                  () => Player().MarkApplied(new DateTimeOffset(2026, 8, 13, 9, 0, 0, TimeSpan.FromHours(2))))
              .Message.ShouldMatchWildcard("*Unix milliseconds*");
    }
}
