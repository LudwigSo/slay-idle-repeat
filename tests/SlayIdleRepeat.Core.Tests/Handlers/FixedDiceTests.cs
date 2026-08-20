using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Tests.Model;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary>
/// The fixed dice: <c>CHOOSE_FIXED_DIE</c> turns an owed grant into a die showing a number the
/// player named, and <c>USE_FIXED_DIE</c> spends one to move exactly that far instead of rolling.
/// </summary>
/// <remarks>
/// 🔒 The two commands are tested together because neither is reachable without the other: a die can
/// only exist by being chosen, and a choice is only worth owing because a die can be spent.
/// </remarks>
public sealed class FixedDiceTests
{
    private static WorldSlice Owed(int choices) =>
        Worlds.InARun(RunSnapshots.With(position: 0, pendingFixedDieChoices: choices));

    private static WorldSlice Holding(params (int Pips, int Count)[] held) =>
        Worlds.InARun(RunSnapshots.With(position: 0, fixedDice: RunSnapshots.FixedDice(held)));

    private static CommandResult Apply(WorldSlice state, GameCommand command) =>
        SlayIdleRepeat.Core.GameRules.Apply(state, command, Worlds.Context);

    // ═════════════════════════════════════════════════════════ CHOOSE_FIXED_DIE

    /// <summary>The whole point of the mechanic: the number is the player's.</summary>
    [Fact]
    public void Choosing_turns_one_owed_grant_into_a_die_showing_the_number_named()
    {
        var result = Apply(Owed(1), new ChooseFixedDieCommand(3));

        result.Accepted.ShouldBeTrue();

        var run = result.NewState.Run!;

        run.FixedDice.ShouldBe(new Dictionary<int, int> { [3] = 1 });
        run.PendingFixedDieChoices.ShouldBe(0, "the debt was paid by the choice.");
    }

    /// <summary>Two grants are two independent choices, and they need not be the same number.</summary>
    [Fact]
    public void Two_owed_grants_can_be_answered_with_two_different_numbers()
    {
        var after = Apply(Owed(2), new ChooseFixedDieCommand(6)).NewState;

        var result = Apply(after, new ChooseFixedDieCommand(1));
        var run = result.NewState.Run!;

        run.FixedDice.ShouldBe(new Dictionary<int, int> { [6] = 1, [1] = 1 });
        run.PendingFixedDieChoices.ShouldBe(0);
    }

    /// <summary>Two dice showing the same number are the same holding twice, counted rather than listed.</summary>
    [Fact]
    public void The_same_number_chosen_twice_is_a_count_of_two()
    {
        var after = Apply(Owed(2), new ChooseFixedDieCommand(4)).NewState;

        Apply(after, new ChooseFixedDieCommand(4)).NewState.Run!.FixedDice
            .ShouldBe(new Dictionary<int, int> { [4] = 2 });
    }

    /// <summary>A choice nothing owes is refused, so the command cannot mint dice.</summary>
    [Fact]
    public void Choosing_with_nothing_owed_is_refused()
    {
        var result = Apply(Owed(0), new ChooseFixedDieCommand(3));

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
        result.NewState.Run!.FixedDice.ShouldBeEmpty();
    }

    /// <summary>
    /// 🔒 A number the die cannot show is refused and the grant is NOT spent — the player keeps the
    /// choice rather than losing it to a malformed request.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(-1)]
    [InlineData(1000)]
    public void A_number_outside_the_dies_range_is_refused_and_costs_the_grant_nothing(int pips)
    {
        var result = Apply(Owed(1), new ChooseFixedDieCommand(pips));

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
        result.NewState.Run!.PendingFixedDieChoices.ShouldBe(
            1, "a refused choice must leave the debt standing, or a mis-typed number burns a reward.");
    }

    /// <summary>
    /// 🔒 An owed choice blocks nothing — unlike a pending draft, which refuses every other run
    /// command. A grant can land mid-shop-visit, and interrupting the player would be the worse trade.
    /// </summary>
    [Fact]
    public void An_owed_choice_does_not_block_rolling()
    {
        var result = Apply(Owed(1), new RollDiceCommand());

        result.Accepted.ShouldBeTrue(
            "ROLL_DICE was refused " + result.Rejection + " while a fixed-die choice was owed.");
        result.NewState.Run!.PendingFixedDieChoices.ShouldBe(1, "and the debt survives the roll.");
    }

    // ═════════════════════════════════════════════════════════ USE_FIXED_DIE

    /// <summary>Exactly that many steps, and the die is gone.</summary>
    [Fact]
    public void Using_a_die_moves_exactly_its_number_and_consumes_it()
    {
        var result = Apply(Holding((3, 1)), new UseFixedDieCommand(3));

        result.Accepted.ShouldBeTrue();

        var run = result.NewState.Run!;

        run.Position.ShouldBe(3, "a fixed die's only promise is this many steps.");
        run.FixedDice.ShouldBeEmpty("the die is spent, and the last one leaves no zero behind.");
        result.Events.Single().ShouldBeOfType<FixedDieUsed>().Pips.ShouldBe(3);
    }

    /// <summary>
    /// 🔒 <b>No dice-stream draw.</b> A deterministic move that consumed randomness would shift every
    /// later roll of the same seed for no reason, which is a replay divergence with no cause.
    /// </summary>
    [Fact]
    public void Using_a_die_takes_no_draw_from_the_dice_stream()
    {
        var state = Worlds.InARun(RunSnapshots.With(
            position: 0,
            fixedDice: RunSnapshots.FixedDice((2, 1)),
            rngStreamPositions: RunSnapshots.Streams((RngStreams.Dice, 7UL))));

        Apply(state, new UseFixedDieCommand(2)).NewState.Run!
            .StreamPosition(RngStreams.Dice).ShouldBe(7UL);
    }

    /// <summary>One of a pair spends one, not both.</summary>
    [Fact]
    public void Using_one_of_a_pair_leaves_the_other()
    {
        Apply(Holding((5, 2)), new UseFixedDieCommand(5)).NewState.Run!.FixedDice
            .ShouldBe(new Dictionary<int, int> { [5] = 1 });
    }

    /// <summary>A number the run does not hold is refused, and the run does not move.</summary>
    [Fact]
    public void Using_a_number_the_run_does_not_hold_is_refused_and_moves_nothing()
    {
        var result = Apply(Holding((3, 1)), new UseFixedDieCommand(4));

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
        result.NewState.Run!.Position.ShouldBe(0, "a refused move must not have moved the run.");
        result.NewState.Run.FixedDice.ShouldBe(
            new Dictionary<int, int> { [3] = 1 }, "and must not have spent anything either.");
    }

    /// <summary>Holding none at all is the same refusal.</summary>
    [Fact]
    public void Using_a_die_with_an_empty_holding_is_refused()
    {
        Apply(Holding(), new UseFixedDieCommand(3)).Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    /// <summary>A number the die cannot show names no holding, so it can never be one the run has.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(-1)]
    public void Using_a_number_outside_the_dies_range_is_refused(int pips)
    {
        Apply(Holding((3, 1)), new UseFixedDieCommand(pips))
            .Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    /// <summary>
    /// 🔒 The three states that refuse a roll refuse a fixed die too, and the die is NOT spent on the
    /// way out. A guard one movement command honoured and the other did not would be a way to move
    /// out of a state the game says you cannot move out of.
    /// </summary>
    [Fact]
    public void An_unresolved_tile_refuses_a_fixed_die_without_spending_it()
    {
        var state = TileWorlds.OnTile(Core.Rules.Board.TileKind.Enemy);

        var owed = Apply(state, new ChooseFixedDieCommand(2));
        owed.Accepted.ShouldBeFalse("the premise: this state owes no choice, so seed the die instead.");

        var holding = Worlds.InARun(RunSnapshots.With(
            pendingTileKind: (int)Core.Rules.Board.TileKind.Enemy,
            pendingTileLinearIndex: 0,
            pendingTileStage: 1,
            fixedDice: RunSnapshots.FixedDice((2, 1))));

        var result = Apply(holding, new UseFixedDieCommand(2));

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
        result.NewState.Run!.FixedDice.ShouldBe(
            new Dictionary<int, int> { [2] = 1 },
            "a refused move must not have spent the die — the run would have paid for nothing.");
    }

    /// <summary>A dead hero does not move, by either route.</summary>
    [Fact]
    public void A_dead_hero_cannot_spend_a_fixed_die()
    {
        var state = Worlds.InARun(RunSnapshots.With(
            currentHp: 0, fixedDice: RunSnapshots.FixedDice((2, 1))));

        Apply(state, new UseFixedDieCommand(2)).Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    /// <summary>
    /// 🔒 <b>A curse does not shrink a fixed die.</b> <c>CUR_SLIPPERY</c> takes 1 off a ROLL, and
    /// deliberately not off this: the die's only promise is <em>this many steps</em>, and a curse that
    /// silently broke it would make the one deterministic tool in the game undependable.
    /// </summary>
    [Fact]
    public void A_pip_penalty_curse_does_not_shorten_a_fixed_die()
    {
        var state = Worlds.InARun(RunSnapshots.With(
            position: 0,
            curses: RunSnapshots.Ids("CUR_SLIPPERY"),
            fixedDice: RunSnapshots.FixedDice((4, 1))));

        Apply(state, new UseFixedDieCommand(4)).NewState.Run!.Position.ShouldBe(
            4, "the die said four, so the hero moves four — cursed or not.");
    }

    /// <summary>
    /// The whole loop, through the production dispatch table: be owed a choice, name a number, spend
    /// it, land exactly there.
    /// </summary>
    [Fact]
    public void A_granted_die_is_chosen_then_spent_and_lands_exactly_where_it_said()
    {
        var chosen = Apply(Owed(1), new ChooseFixedDieCommand(5)).NewState;

        chosen.Run!.FixedDice.ShouldBe(new Dictionary<int, int> { [5] = 1 });

        var moved = Apply(chosen, new UseFixedDieCommand(5));

        moved.Accepted.ShouldBeTrue("USE_FIXED_DIE was refused " + moved.Rejection);
        moved.NewState.Run!.Position.ShouldBe(5);
        moved.NewState.Run.FixedDice.ShouldBeEmpty();
        moved.NewState.Run.PendingFixedDieChoices.ShouldBe(0);
    }
}
