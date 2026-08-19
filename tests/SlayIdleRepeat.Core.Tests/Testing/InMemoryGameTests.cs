using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Tests.Content;
using Xunit;

namespace SlayIdleRepeat.Core.Tests;

public sealed class InMemoryGameTests
{
    /// <summary>30 §7 attributes every currency movement — a starting balance would be currency no
    /// <c>CurrencyChanged</c> accounts for.</summary>
    [Fact]
    public void A_created_player_starts_at_the_authored_floor_holding_nothing()
    {
        var (game, player) = Harnesses.WithPlayer();
        var state = game.State(player).Player;

        state.LegendLevel.ShouldBe(ProgressionDocuments.ShippedLegendLevelMin);
        state.LegendXp.ShouldBe(0L);
        state.RunsStarted.ShouldBe(0L);

        foreach (var currency in Player.WalletCurrencies)
        {
            state.BalanceOf(currency).ShouldBe(0L);
        }

        state.Energy.ShouldBe(new EnergyBanks(0, 0));
        state.FtueBeat.ShouldBe(FtueBeat.B0);
        state.IsFtueComplete.ShouldBeFalse();
        state.LoginCalendarDay.ShouldBe(LoginCalendarTuning.FirstDay);
        state.LoginCalendarDayClaimed.ShouldBeFalse();

        game.State(player).Run.ShouldBeNull();
        game.Events.ShouldBeEmpty("creating a player is not a command and produces no events.");
        game.CommandsIssued.ShouldBe(0L);
    }

    /// <summary>Asserted against an unshipped floor so a hard-coded literal could not pass.</summary>
    [Fact]
    public void A_created_player_reads_its_Legend_Level_from_the_content_set()
    {
        const int UnshippedFloor = 5;

        UnshippedFloor.ShouldNotBe(
            ProgressionDocuments.ShippedLegendLevelMin,
            "the whole test is that this floor is NOT the shipped one.");

        var content = TuningDocuments.With(legendLevelMin: ContentValue.Number(UnshippedFloor));
        var game = Harnesses.New(content: content);
        var player = game.CreatePlayer();

        game.State(player).Player.LegendLevel.ShouldBe(UnshippedFloor);

        game.Clock.Advance(TimeSpan.FromDays(1));
        game.Send(player, Harnesses.BeginSession);

        game.State(player).Player.Energy.Energy.ShouldBe(
            EnergyTuning.Read(content).MaxEnergyAt(UnshippedFloor),
            "10 §3 derives Max Energy from the level, so a hard-coded level would hand every " +
            "simulated player the wrong tank.");
    }

    /// <summary>Left at a default, the first command would clear a period the player never played,
    /// and a boundary earlier than the stored one makes <c>Player.RequireNotBefore</c> throw.</summary>
    [Fact]
    public void A_created_player_sits_in_the_game_day_and_week_the_clock_is_in()
    {
        var midweek = new DateTimeOffset(2026, 8, 12, 9, 41, 8, TimeSpan.Zero);
        var (game, player) = Harnesses.WithPlayer(midweek);
        var state = game.State(player).Player;

        state.DailyPeriodStartUtc.ShouldBe(GameCalendar.GameDayStartAt(midweek));
        state.WeeklyPeriodStartUtc.ShouldBe(GameCalendar.GameWeekStartAt(midweek));
        state.EnergyAnchorUtc.ShouldBe(midweek);
        state.LastAppliedAtUtc.ShouldBe(midweek);

        state.WeeklyPeriodStartUtc.DayOfWeek.ShouldBe(
            DayOfWeek.Monday,
            "A2's game week starts Monday 05:00 UTC and the fixture starts on a Wednesday — from a " +
            "Monday the day and week boundaries would coincide.");
    }

    [Fact]
    public void Two_players_have_distinct_identities_and_independent_state()
    {
        var game = Harnesses.New();

        var first = game.CreatePlayer();
        var second = game.CreatePlayer("Ludwig the Unhurried");

        first.ShouldNotBe(second);
        game.State(second).Player.DisplayName.ShouldBe("Ludwig the Unhurried");

        game.Clock.Advance(TimeSpan.FromHours(8));
        game.Send(first, Harnesses.BeginSession);

        game.State(first).Player.Energy.ShouldBe(
            new EnergyBanks(
                Harnesses.Tuning.MaxEnergyAt(ProgressionDocuments.ShippedLegendLevelMin), 0),
            "eight hours is exactly one full bar at the shipped rate.");

        game.State(second).Player.Energy.ShouldBe(
            new EnergyBanks(0, 0),
            "a command sent to one player must not roll another player's state forward — a shared " +
            "slice is what a dictionary keyed wrongly would produce.");
    }

    /// <summary>Several Deferred rows from different milestones, so the claim is about the mechanism
    /// rather than whichever command was picked.</summary>
    /// <remarks>Compared through <c>CanonicalStateWriter.HashMetaCommandState</c>, not record
    /// equality: <c>PlayerSnapshot</c> compares its dictionary components by reference.</remarks>
    [Fact]
    public void A_deferred_command_is_refused_with_ILLEGAL_STATE_and_changes_nothing()
    {
        var (game, player) = Harnesses.WithPlayer();

        Harnesses.Drive(game, player, days: 1, commandsPerDay: 2);

        var settled = game.State(player).Player;
        settled.DailyCount("begin_session").ShouldBe(
            1L, "there is a daily counter set, so a wrongly stored catch-up would have something " +
            "visible to wipe.");

        var before = CanonicalStateWriter.HashMetaCommandState(settled.ToSnapshot());
        var rowsFromTheDrivenDay = game.Events.Count;

        rowsFromTheDrivenDay.ShouldBeGreaterThan(0);

        // 25h so every refused command crosses a day boundary and several regen intervals — things
        // AdvanceTime would move if the working copy survived a rejection.
        game.Clock.Advance(TimeSpan.FromHours(25));

        GameCommand[] deferred =
        [
            new SkipFtueCommand(),
            // A payload that NAMES AN ITEM: a refused command must change nothing whether or not it
            // looked the player's stock up on the way to refusing.
            new ReforgeItemCommand(new GearInstanceId("ITEM_1")),
            new RespecCommand(),
            new ClaimCalendarCommand(),
            new SpinWheelCommand(),
        ];

        foreach (var command in deferred)
        {
            var result = game.Send(player, command);

            result.Accepted.ShouldBeFalse($"{command.GetType().Name} is a Deferred row.");
            result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
            result.Events.ShouldBeEmpty();
        }

        CanonicalStateWriter.HashMetaCommandState(game.State(player).Player.ToSnapshot()).ShouldBe(
            before,
            "a rejected command discards the working copy AND the catch-up (30 §2.1's P4): not the " +
            "banks, not the anchor, not LastAppliedAtUtc, and not the day's counter.");

        game.Events.Count.ShouldBe(rowsFromTheDrivenDay);
        game.CommandsIssued.ShouldBe(7L, "a refused command is still a command that was issued.");
    }

    /// <summary>The <c>Sequence</c> ordinal is a position within one <c>Apply</c> call's list, from
    /// 1 — not a running counter across the simulation.</summary>
    [Fact]
    public void The_event_list_accumulates_in_command_order_and_keeps_each_commands_own_sequence()
    {
        var (game, player) = Harnesses.WithPlayer();

        game.Clock.Advance(TimeSpan.FromHours(8));
        var first = game.Send(player, Harnesses.BeginSession);

        game.Clock.Advance(TimeSpan.FromDays(1));
        var second = game.Send(player, Harnesses.BeginSession);

        game.Events.Count.ShouldBe(first.Events.Count + second.Events.Count);
        game.Events.Take(first.Events.Count).ShouldBe(first.Events);
        game.Events.Skip(first.Events.Count).ShouldBe(second.Events);

        first.Events.Select(e => e.Sequence).ShouldBe(Enumerable.Range(1, first.Events.Count));
        second.Events.Select(e => e.Sequence).ShouldBe(Enumerable.Range(1, second.Events.Count));

        first.Events.ShouldNotBeEmpty("neither range may be empty for this to compare anything.");
        second.Events.ShouldNotBeEmpty();
    }
}
