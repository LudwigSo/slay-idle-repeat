using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Tests.Content;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Model;

/// <summary>
/// The login calendar on the <c>Player</c> aggregate: the open day, the claimed flag, and the
/// invariants the aggregate holds over them.
/// </summary>
/// <remarks>
/// The <b>behaviour</b> — when the calendar advances during a command — is
/// <c>BeginSessionCalendarTests</c>'. What is here is what the aggregate refuses and what it
/// round-trips, the half a handler cannot get wrong on its own.
/// <para>
/// <c>AdvanceLoginCalendar</c> is <c>internal</c>, reached through <c>InternalsVisibleTo</c>:
/// testing it directly is testing the invariant, not opening a second door.
/// </para>
/// </remarks>
public sealed class PlayerLoginCalendarTests
{
    private static LoginCalendarTuning Tuning { get; } =
        LoginCalendarTuning.Read(TuningDocuments.Shipped);

    /// <summary>A new fixture opens on day 1, unclaimed.</summary>
    [Fact]
    public void A_fresh_row_opens_on_day_one_unclaimed()
    {
        var player = Worlds.NewPlayer();

        player.LoginCalendarDay.ShouldBe(LoginCalendarTuning.FirstDay);
        player.LoginCalendarDayClaimed.ShouldBeFalse();
    }

    /// <summary>The pause: an unclaimed day is a silent no-op, not a refusal.</summary>
    /// <remarks>
    /// It is a no-op rather than a throw because the pause is specified behaviour —
    /// <c>BEGIN_SESSION</c> arriving on a paused calendar is the normal case, and an exception out
    /// of <c>Apply</c> is forbidden for something the player did legitimately.
    /// </remarks>
    [Fact]
    public void An_unclaimed_day_does_not_advance()
    {
        var player = Worlds.Rehydrated(
            PlayerSnapshots.With(loginCalendarDay: 9, loginCalendarDayClaimed: false));

        player.AdvanceLoginCalendar(Tuning);

        player.LoginCalendarDay.ShouldBe(9, "an unclaimed day pauses the calendar.");
        player.LoginCalendarDayClaimed.ShouldBeFalse();
    }

    /// <summary>The advance clears the claim, so the newly opened day pauses the calendar again.</summary>
    /// <remarks>
    /// The clearing is the half that is easy to leave out, and leaving it out is unbounded: the
    /// calendar would then advance on <em>every</em> game day for the rest of the player's life,
    /// paying the whole 28-day table to somebody who claimed once.
    /// </remarks>
    [Fact]
    public void The_advance_opens_the_next_day_unclaimed()
    {
        var player = Worlds.Rehydrated(
            PlayerSnapshots.With(loginCalendarDay: 9, loginCalendarDayClaimed: true));

        player.AdvanceLoginCalendar(Tuning);

        player.LoginCalendarDay.ShouldBe(10);
        player.LoginCalendarDayClaimed.ShouldBeFalse(
            "a claim buys ONE advance. Leaving the flag set would advance the calendar every game day " +
            "forever, paying all 28 rows to a player who tapped once.");

        // …and a second advance without a new claim does nothing, which is the same fact observed
        // from the other side.
        player.AdvanceLoginCalendar(Tuning);
        player.LoginCalendarDay.ShouldBe(10);
    }

    /// <summary>The mutator refuses a null tuning rather than dereferencing it.</summary>
    [Fact]
    public void The_advance_refuses_a_null_tuning()
    {
        var player = Worlds.Rehydrated(
            PlayerSnapshots.With(loginCalendarDay: 1, loginCalendarDayClaimed: true));

        Should.Throw<ArgumentNullException>(() => player.AdvanceLoginCalendar(null!));
    }

    // ------------------------------------------------------------------ the row

    /// <summary>Both fields survive the snapshot round trip <c>Apply</c> clones through.</summary>
    /// <remarks>
    /// The slice rebuilds through <c>ToSnapshot()</c>/<c>Rehydrate()</c> on every command, so a
    /// field the pair dropped would be silently reset on the very next command — the calendar
    /// would stand still whatever the handler wrote.
    /// </remarks>
    [Fact]
    public void The_calendar_survives_the_snapshot_round_trip()
    {
        var player = Worlds.Rehydrated(
            PlayerSnapshots.With(loginCalendarDay: 17, loginCalendarDayClaimed: true));

        var round = Worlds.Rehydrated(player.ToSnapshot());

        round.LoginCalendarDay.ShouldBe(17);
        round.LoginCalendarDayClaimed.ShouldBeTrue();
    }

    /// <summary>A row on day 0 or below is refused: the table numbers from 1.</summary>
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

    /// <summary>A row <b>past</b> the cycle length loads, and that is a ruling rather than a gap.</summary>
    /// <remarks>
    /// The same ruling <c>Player.Rehydrate</c> records for the Energy banks: <c>cycleDays</c> is a
    /// tunable, so refusing to load a player left behind by a shortened cycle would turn a balance
    /// patch into an account outage for every one of them.
    /// <c>LoginCalendarTuning.DayAfter</c> wraps them to day 1 on the next advance instead — asserted
    /// here rather than only in the tuning's own tests, because "it loads" is only safe if "it
    /// recovers" is also true.
    /// </remarks>
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
