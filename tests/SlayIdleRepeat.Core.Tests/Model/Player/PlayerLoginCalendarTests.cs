using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Tests.Content;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Model;

/// <summary>
/// The login calendar's persisted half: what the aggregate refuses and round-trips. The advance
/// behaviour is covered at the <c>Apply</c> seam by <c>BeginSessionCalendarTests</c>.
/// </summary>
public sealed class PlayerLoginCalendarTests
{
    private static LoginCalendarTuning Tuning { get; } =
        LoginCalendarTuning.Read(TuningDocuments.Shipped);

    /// <summary>The slice rebuilds through <c>ToSnapshot()</c>/<c>Rehydrate()</c> on every command, so a dropped field resets silently.</summary>
    [Fact]
    public void The_calendar_survives_the_snapshot_round_trip()
    {
        var player = Worlds.Rehydrated(
            PlayerSnapshots.With(loginCalendarDay: 17, loginCalendarDayClaimed: true));

        var round = Worlds.Rehydrated(player.ToSnapshot());

        round.LoginCalendarDay.ShouldBe(17);
        round.LoginCalendarDayClaimed.ShouldBeTrue();
    }

    /// <summary>The table numbers from 1 (19 G).</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void A_row_below_day_one_is_refused(int loginCalendarDay)
    {
        var result = Core.Model.Player.Rehydrate(
            PlayerSnapshots.With(loginCalendarDay: loginCalendarDay),
            TuningDocuments.Shipped);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain(nameof(Core.Model.Snapshots.PlayerSnapshot.LoginCalendarDay), Case.Sensitive);
        result.Error.ShouldContain("19 G", Case.Sensitive);
    }

    /// <summary>
    /// <c>cycleDays</c> is a tunable, so refusing a player stranded past a shortened cycle would
    /// turn a balance patch into an account outage. "It loads" is only safe if "it recovers" is
    /// also true, so both halves are asserted here.
    /// </summary>
    [Fact]
    public void A_row_past_the_cycle_length_loads_and_recovers_on_the_next_advance()
    {
        var beyond = TuningDocuments.ShippedCycleDays + 5;

        var result = Core.Model.Player.Rehydrate(
            PlayerSnapshots.With(loginCalendarDay: beyond, loginCalendarDayClaimed: true),
            TuningDocuments.Shipped);

        result.IsSuccess.ShouldBeTrue(
            "a player stranded past the cycle by a balance patch must still load — refusing them " +
            "would make a tuning change an outage.");
        result.Value.LoginCalendarDay.ShouldBe(beyond, "…unchanged, rather than silently clamped.");

        result.Value.AdvanceLoginCalendar(Tuning);
        result.Value.LoginCalendarDay.ShouldBe(
            LoginCalendarTuning.FirstDay, "…and they rejoin the cycle at day 1 on the next advance.");
    }
}
