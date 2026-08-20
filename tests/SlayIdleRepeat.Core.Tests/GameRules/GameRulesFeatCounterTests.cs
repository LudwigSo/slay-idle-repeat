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
    private static DomainEvent Rolled(int pips) =>
        new DiceRolled(DomainEvent.UnstampedSequence, pips);

    /// <summary>A slice whose player has drained banks, so a day of catch-up regenerates something.</summary>
    private static WorldSlice EmptyBanks() => new(
        Worlds.Rehydrated(PlayerSnapshots.With(
            energy: new EnergyBanks(0, 0),
            energyAnchorUtc: PlayerSnapshots.Midmorning)),
        null);

    /// <summary>A context one game day past the fixtures' anchors, so the catch-up has ground to cover.</summary>
    private static GameContext ADayLater => Worlds.Context with { NowUtc = Worlds.NowUtc.AddDays(1) };

    [Fact]
    public void An_accepted_commands_events_advance_the_players_lifetime_counters()
    {
        var state = Worlds.InARun();

        var result = Core.GameRules.Execute(
            Worlds.RunTable((_, _) => HandlerResult.Accept(Rolled(3), Rolled(5))),
            state,
            new Worlds.RunFixtureCommand(),
            Worlds.Context);

        result.Accepted.ShouldBeTrue();
        result.NewState.Player.FeatCount("dice_rolled").ShouldBe(2L);
        result.NewState.Player.FeatCount("dice_rolled_pips_3").ShouldBe(1L);
        result.NewState.Player.FeatCount("dice_rolled_pips_5").ShouldBe(1L);
    }

    /// <summary>
    /// Two events of the SAME shape in one list count twice. The test above uses two different
    /// numbers, which an implementation folding one advance per distinct counter would also satisfy.
    /// </summary>
    [Fact]
    public void Two_identical_events_in_one_list_count_twice()
    {
        var result = Core.GameRules.Execute(
            Worlds.RunTable((_, _) => HandlerResult.Accept(Rolled(3), Rolled(3))),
            Worlds.InARun(),
            new Worlds.RunFixtureCommand(),
            Worlds.Context);

        result.NewState.Player.FeatCount("dice_rolled_pips_3").ShouldBe(2L);
        result.NewState.Player.FeatCount("dice_rolled").ShouldBe(2L);
    }

    /// <summary>
    /// An event carrying a value no counter can be named for is a DEFECT out of <c>Apply</c>, not a
    /// rejection: a handler that built a <c>DiceRolled</c> around a number the die cannot show has
    /// produced an animation frame and a log row that mean nothing either, and answering the player
    /// a polite "no" would leave that row in the stream.
    /// </summary>
    [Fact]
    public void An_event_carrying_an_uncountable_value_is_a_defect_out_of_Apply()
    {
        var thrown = Should.Throw<InvalidOperationException>(() => Core.GameRules.Execute(
            Worlds.RunTable((_, _) => HandlerResult.Accept(new DiceRolled(DomainEvent.UnstampedSequence, default))),
            Worlds.InARun(),
            new Worlds.RunFixtureCommand(),
            Worlds.Context));

        thrown.Message.ShouldContain("04 §1's die shows 1..6 pips", Case.Sensitive);
    }

    /// <summary>The counters accumulate across commands: this is the point of them being aggregate state.</summary>
    [Fact]
    public void Counts_accumulate_across_commands()
    {
        var table = Worlds.RunTable((_, _) => HandlerResult.Accept(Rolled(3)));
        var state = Worlds.InARun();

        for (var i = 0; i < 3; i++)
        {
            state = Core.GameRules.Execute(table, state, new Worlds.RunFixtureCommand(), Worlds.Context).NewState;
        }

        state.Player.FeatCount("dice_rolled_pips_3").ShouldBe(3L);
    }

    /// <summary>
    /// A refused command discards the working copy, and the counters go with it.
    /// </summary>
    /// <remarks>
    /// ⚠️ Arranged so the CATCH-UP produces real events before the handler refuses — empty banks,
    /// a day of elapsed time. A handler that simply rejects emits nothing at all, so the assertion
    /// would hold even if the counting were hoisted above the acceptance gate: there would be
    /// nothing to count either way.
    /// </remarks>
    [Fact]
    public void A_rejected_command_advances_nothing_even_when_the_catch_up_produced_events()
    {
        var state = EmptyBanks();

        var accepted = Core.GameRules.Execute(
            Worlds.MetaTable((_, _) => HandlerResult.Accept()),
            state,
            new Worlds.MetaFixtureCommand(),
            ADayLater);

        accepted.Events.OfType<CurrencyChanged>().ShouldNotBeEmpty(
            "the same arrangement must produce countable events when the command IS accepted, or " +
            "the refusal below proves nothing.");

        var refused = Core.GameRules.Execute(
            Worlds.MetaTable((_, _) => HandlerResult.Reject(RejectionReason.ILLEGAL_STATE)),
            state,
            new Worlds.MetaFixtureCommand(),
            ADayLater);

        refused.Accepted.ShouldBeFalse();
        refused.NewState.ShouldBeSameAs(state, "P4: a rejection returns the caller's own slice.");
        state.Player.FeatCount("currency_earned_energy").ShouldBe(0L);
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
            Worlds.RunTable((_, _) => HandlerResult.Accept(Rolled(4))),
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
        var result = Core.GameRules.Execute(
            Worlds.MetaTable((_, _) => HandlerResult.Accept()),
            EmptyBanks(),
            new Worlds.MetaFixtureCommand(),
            ADayLater);

        result.Accepted.ShouldBeTrue();

        var regenerated = result.Events.OfType<CurrencyChanged>()
            .Where(e => e.Id == CurrencyId.ENERGY && e.Delta > 0)
            .Sum(e => e.Delta);

        regenerated.ShouldBeGreaterThan(
            0L, "a day of regeneration on empty banks is what makes this test about anything.");

        result.NewState.Player.FeatCount("currency_earned_energy").ShouldBe(
            regenerated,
            "the counters are taken from the SAME stamped list the caller receives.");

        result.NewState.Player.FeatCount("currency_spent_energy").ShouldBe(
            0L, "an accrual is income; nothing was spent.");

        result.NewState.Player.FeatCount("currency_earned_crowns").ShouldBe(
            0L,
            "the counter is per currency — an implementation that lumped every movement into one " +
            "row would still satisfy the equality above.");
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
        var table = Worlds.RunTable((_, _) => HandlerResult.Accept(Rolled(3)));

        var state = Core.GameRules.Execute(
            table, Worlds.InARun(), new Worlds.RunFixtureCommand(), Worlds.Context).NewState;

        state.Player.FeatCount("dice_rolled_pips_3").ShouldBe(1L);

        var later = Core.GameRules.Execute(
            table,
            state,
            new Worlds.RunFixtureCommand(),
            Worlds.Context with { NowUtc = Worlds.NextDay(Worlds.NowUtc) });

        later.NewState.Player.DailyPeriodStartUtc.ShouldBeGreaterThan(
            state.Player.DailyPeriodStartUtc, "the command crossed a game-day boundary.");

        later.NewState.Player.FeatCount("dice_rolled_pips_3").ShouldBe(2L);
    }
}
