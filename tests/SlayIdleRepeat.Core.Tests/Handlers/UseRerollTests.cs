using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Tests.Model;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary>
/// USE_REROLL: burns one dice stream draw without moving the run, so the next roll draws a
/// different index against updated Fair-Dice weights.
/// </summary>
public sealed class UseRerollTests
{
    [Fact]
    public void A_reroll_advances_the_dice_stream_by_one_and_does_not_move_the_run()
    {
        var state = Worlds.InARun(RunSnapshots.With(
            position: 4, rngStreamPositions: RunSnapshots.Streams((RngStreams.Dice, 2UL))));

        var result = SlayIdleRepeat.Core.GameRules.Apply(state, new UseRerollCommand(), Worlds.Context);

        result.Accepted.ShouldBeTrue();
        result.Events.ShouldBeEmpty("a burned draw is not a roll the animation script needs to show.");
        result.NewState.Run!.StreamPosition(RngStreams.Dice).ShouldBe(3UL);
        result.NewState.Run.Position.ShouldBe(4, "USE_REROLL never moves the run.");
    }

    [Fact]
    public void A_reroll_changes_what_the_next_roll_draws_at_least_sometimes()
    {
        // Compare "roll straight away" against "reroll, then roll" from the SAME starting position —
        // if these always agreed, USE_REROLL would not be doing anything.
        var differed = false;

        for (var position = 0UL; position < 12 && !differed; position++)
        {
            var streams = RunSnapshots.Streams((RngStreams.Dice, position));

            var direct = SlayIdleRepeat.Core.GameRules.Apply(
                Worlds.InARun(RunSnapshots.With(rngStreamPositions: streams)),
                new RollDiceCommand(), Worlds.Context);

            var rerolledFirst = SlayIdleRepeat.Core.GameRules.Apply(
                Worlds.InARun(RunSnapshots.With(rngStreamPositions: streams)),
                new UseRerollCommand(), Worlds.Context);

            var afterReroll = SlayIdleRepeat.Core.GameRules.Apply(
                new SlayIdleRepeat.Core.WorldSlice(rerolledFirst.NewState.Player, rerolledFirst.NewState.Run),
                new RollDiceCommand(), Worlds.Context);

            var directFace = ((SlayIdleRepeat.Core.Events.DiceRolled)direct.Events[0]).Face.Value;
            var rerolledFace = ((SlayIdleRepeat.Core.Events.DiceRolled)afterReroll.Events[0]).Face.Value;

            differed |= directFace != rerolledFace;
        }

        differed.ShouldBeTrue("across twelve starting positions, at least one reroll should change the outcome.");
    }

    [Fact]
    public void Using_a_reroll_on_a_run_less_slice_is_a_defect_not_a_rejection()
    {
        Should.Throw<InvalidOperationException>(() => SlayIdleRepeat.Core.GameRules.Apply(
            Worlds.OutsideARun(), new UseRerollCommand(), Worlds.Context));
    }

    // ------------------------------------------------------------------------------------------
    // The base allotment (1/stage, no bonus sources yet) is enforced against
    // Run.RerollChargesSpentThisStage.
    // ------------------------------------------------------------------------------------------

    [Fact]
    public void The_first_reroll_of_a_stage_is_affordable_and_advances_the_spent_count()
    {
        var state = Worlds.InARun(RunSnapshots.With(rerollChargesSpentThisStage: 0));

        var result = SlayIdleRepeat.Core.GameRules.Apply(state, new UseRerollCommand(), Worlds.Context);

        result.Accepted.ShouldBeTrue();
        result.NewState.Run!.ToSnapshot().RerollChargesSpentThisStage.ShouldBe(1);
    }

    [Fact]
    public void A_second_reroll_in_the_same_stage_is_refused_as_CAP_REACHED()
    {
        // The base allotment is 1/stage with no bonus source yet, so a run that has already spent
        // its one charge this stage cannot afford a second.
        var state = Worlds.InARun(RunSnapshots.With(rerollChargesSpentThisStage: 1));

        var result = SlayIdleRepeat.Core.GameRules.Apply(state, new UseRerollCommand(), Worlds.Context);

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.CAP_REACHED);
    }

    [Fact]
    public void A_refused_reroll_moves_no_stream_position()
    {
        var state = Worlds.InARun(RunSnapshots.With(
            rerollChargesSpentThisStage: 1,
            rngStreamPositions: RunSnapshots.Streams((RngStreams.Dice, 3UL))));

        var result = SlayIdleRepeat.Core.GameRules.Apply(state, new UseRerollCommand(), Worlds.Context);

        result.Accepted.ShouldBeFalse();
        result.NewState.Run!.StreamPosition(RngStreams.Dice).ShouldBe(3UL);
    }

    [Fact]
    public void A_reroll_at_a_fresh_Stage_Gate_anchor_is_affordable_again()
    {
        // ApplyStageGate resets the spent count to 0 — the same effect as this fixture's zero.
        var state = Worlds.InARun(RunSnapshots.With(rerollChargesSpentThisStage: 0));

        SlayIdleRepeat.Core.GameRules.Apply(state, new UseRerollCommand(), Worlds.Context)
            .Accepted.ShouldBeTrue();
    }
}
