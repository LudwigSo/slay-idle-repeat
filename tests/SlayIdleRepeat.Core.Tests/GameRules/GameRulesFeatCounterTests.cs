using Shouldly;
using SlayIdleRepeat.Core.Content.Dice;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Tests.Model;
using Xunit;

namespace SlayIdleRepeat.Core.Tests;

/// <summary>
/// The lifetime feat counters are advanced <b>inside</b> <c>Apply</c>, off the event list it
/// returns — not by a hook each rule has to remember to call, and not by a projection over a
/// retained log.
/// </summary>
/// <remarks>
/// Driven through <c>GameRules.Execute</c> with fixture handlers that return the events directly,
/// so these assertions are about the projection seam rather than about what any particular real
/// handler happens to emit today.
/// </remarks>
public sealed class GameRulesFeatCounterTests
{
    private static DomainEvent Rolled(DieFaceKind kind) =>
        new DiceRolled(DomainEvent.UnstampedSequence, DieFace.Special(kind));

    [Fact]
    public void An_accepted_commands_events_advance_the_players_lifetime_counters()
    {
        var state = Worlds.InARun();

        var result = Core.GameRules.Execute(
            Worlds.RunTable((_, _) => HandlerResult.Accept(Rolled(DieFaceKind.Star), Rolled(DieFaceKind.Chain))),
            state,
            new Worlds.RunFixtureCommand(),
            Worlds.Context);

        result.Accepted.ShouldBeTrue();
        result.NewState.Player.FeatCount("dice_rolled").ShouldBe(2L);
        result.NewState.Player.FeatCount("dice_rolled_star").ShouldBe(1L);
        result.NewState.Player.FeatCount("dice_rolled_chain").ShouldBe(1L);
    }

    /// <summary>The counters accumulate across commands: this is the point of them being aggregate state.</summary>
    [Fact]
    public void Counts_accumulate_across_commands()
    {
        var table = Worlds.RunTable((_, _) => HandlerResult.Accept(Rolled(DieFaceKind.Star)));
        var state = Worlds.InARun();

        for (var i = 0; i < 3; i++)
        {
            state = Core.GameRules.Execute(table, state, new Worlds.RunFixtureCommand(), Worlds.Context).NewState;
        }

        state.Player.FeatCount("dice_rolled_star").ShouldBe(3L);
    }

    /// <summary>A refused command discards the working copy, and the counters go with it.</summary>
    [Fact]
    public void A_rejected_command_advances_nothing()
    {
        var state = Worlds.InARun();

        var result = Core.GameRules.Execute(
            Worlds.RunTable((_, _) => HandlerResult.Reject(RejectionReason.ILLEGAL_STATE)),
            state,
            new Worlds.RunFixtureCommand(),
            Worlds.Context);

        result.Accepted.ShouldBeFalse();
        result.NewState.Player.FeatCount("dice_rolled").ShouldBe(0L);
        state.Player.FeatCount("dice_rolled").ShouldBe(0L);
    }

    /// <summary>
    /// The caller's own slice is never written — the counters are advanced on the working copy, like
    /// every other piece of state.
    /// </summary>
    [Fact]
    public void The_callers_slice_keeps_its_own_counts()
    {
        var state = Worlds.InARun();

        var result = Core.GameRules.Execute(
            Worlds.RunTable((_, _) => HandlerResult.Accept(Rolled(DieFaceKind.Surge))),
            state,
            new Worlds.RunFixtureCommand(),
            Worlds.Context);

        result.NewState.Player.FeatCount("dice_rolled").ShouldBe(1L);
        state.Player.FeatCount("dice_rolled").ShouldBe(0L, "P4: WorldSlice in, NEW WorldSlice out.");
    }

    /// <summary>
    /// The catch-up's own events count too. They are prepended to the handler's and stamped in the
    /// same list, so a counter taken from that list cannot disagree with what the client replays.
    /// </summary>
    [Fact]
    public void The_catch_ups_own_currency_events_advance_the_counters()
    {
        var state = new WorldSlice(
            Worlds.Rehydrated(PlayerSnapshots.With(
                energy: new EnergyBanks(0, 0),
                energyAnchorUtc: PlayerSnapshots.Midmorning)),
            null);

        var result = Core.GameRules.Execute(
            Worlds.MetaTable((_, _) => HandlerResult.Accept()),
            state,
            new Worlds.MetaFixtureCommand(),
            Worlds.Context with { NowUtc = Worlds.NowUtc.AddDays(1) });

        result.Accepted.ShouldBeTrue();
        result.Events.OfType<CurrencyChanged>().ShouldNotBeEmpty(
            "a day of regeneration on empty banks is what makes this test about anything.");

        result.NewState.Player.FeatCount("currency_earned_energy").ShouldBe(
            result.Events.OfType<CurrencyChanged>().Where(e => e.Delta > 0).Sum(e => e.Delta),
            "the counters are taken from the SAME stamped list the caller receives.");
    }

    /// <summary>A meta command may not write the run, but the player's lifetime counters are the player's.</summary>
    [Fact]
    public void A_meta_command_may_advance_the_players_counters_while_a_run_is_open()
    {
        var state = Worlds.InARun();

        var result = Core.GameRules.Execute(
            Worlds.MetaTable((_, input) =>
                HandlerResult.Accept(input.Player.MoveCurrency(CurrencyId.CROWNS, 25L, "fixture_grant"))),
            state,
            new Worlds.MetaFixtureCommand(),
            Worlds.Context);

        result.Accepted.ShouldBeTrue();
        result.NewState.Player.FeatCount("currency_earned_crowns").ShouldBe(25L);
    }

    /// <summary>
    /// 🔒 A counter survives the game-day boundary the same <c>Apply</c> call crosses. The catch-up
    /// clears the daily counters on the way through; if the lifetime counters were reachable from
    /// that reset, a Feat would lose a day's progress every day.
    /// </summary>
    [Fact]
    public void A_counter_survives_a_boundary_crossed_by_a_later_command()
    {
        var table = Worlds.RunTable((_, _) => HandlerResult.Accept(Rolled(DieFaceKind.Star)));

        var state = Core.GameRules.Execute(
            table, Worlds.InARun(), new Worlds.RunFixtureCommand(), Worlds.Context).NewState;

        state.Player.FeatCount("dice_rolled_star").ShouldBe(1L);

        var later = Core.GameRules.Execute(
            table,
            state,
            new Worlds.RunFixtureCommand(),
            Worlds.Context with { NowUtc = Worlds.NextDay(Worlds.NowUtc) });

        later.NewState.Player.DailyPeriodStartUtc.ShouldBeGreaterThan(
            state.Player.DailyPeriodStartUtc, "the command crossed a game-day boundary.");

        later.NewState.Player.FeatCount("dice_rolled_star").ShouldBe(2L);
    }
}
