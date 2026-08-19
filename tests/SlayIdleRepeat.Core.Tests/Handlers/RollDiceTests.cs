using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Tests.Model;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary>
/// ROLL_DICE: draws against the Fair-Dice bag, moves the run, and commits exactly one dice stream
/// draw per face rolled.
/// </summary>
public sealed class RollDiceTests
{
    [Fact]
    public void A_roll_moves_the_run_by_the_drawn_Pip_value_and_emits_one_DiceRolled()
    {
        var state = Worlds.InARun(RunSnapshots.With(position: 0));

        var result = SlayIdleRepeat.Core.GameRules.Apply(state, new RollDiceCommand(), Worlds.Context);

        result.Accepted.ShouldBeTrue();
        result.Events.Count.ShouldBe(1);

        var rolled = result.Events[0].ShouldBeOfType<DiceRolled>();
        rolled.Face.Kind.ShouldBe(SlayIdleRepeat.Core.Content.Dice.DieFaceKind.Pip);
        rolled.Face.Value.ShouldBeInRange(1, 6);

        result.NewState.Run!.Position.ShouldBe(0 + rolled.Face.Value);
    }

    [Fact]
    public void A_roll_commits_exactly_one_dice_stream_draw()
    {
        var state = Worlds.InARun(RunSnapshots.With(
            position: 0, rngStreamPositions: RunSnapshots.Streams((RngStreams.Dice, 7UL))));

        var result = SlayIdleRepeat.Core.GameRules.Apply(state, new RollDiceCommand(), Worlds.Context);

        result.NewState.Run!.StreamPosition(RngStreams.Dice).ShouldBe(8UL);
    }

    [Fact]
    public void Two_rolls_from_different_committed_positions_can_draw_different_faces()
    {
        // Not a determinism claim about any one pair — just proof the draw actually depends on the
        // stream position rather than always answering the same face regardless of history.
        var faces = new System.Collections.Generic.HashSet<int>();

        for (var position = 0UL; position < 12; position++)
        {
            var state = Worlds.InARun(RunSnapshots.With(
                rngStreamPositions: RunSnapshots.Streams((RngStreams.Dice, position))));

            var result = SlayIdleRepeat.Core.GameRules.Apply(state, new RollDiceCommand(), Worlds.Context);
            var rolled = (DiceRolled)result.Events[0];

            faces.Add(rolled.Face.Value);
        }

        faces.Count.ShouldBeGreaterThan(1, "twelve different draw positions should not all land on the same face.");
    }
}
