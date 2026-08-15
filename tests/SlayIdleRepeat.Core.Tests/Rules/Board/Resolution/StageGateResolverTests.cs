using Shouldly;
using SlayIdleRepeat.Core;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Board.Resolution;
using SlayIdleRepeat.Core.Tests.Content;
using SlayIdleRepeat.Core.Tests.Handlers;
using SlayIdleRepeat.Core.Tests.Model;
using Xunit;
using RunAggregate = SlayIdleRepeat.Core.Model.Run;

namespace SlayIdleRepeat.Core.Tests.Rules.Board.Resolution;

/// <summary>
/// Tests <c>StageGateResolver</c>: the heal, the reroll-charge reset and the Fair-Dice bag anchor,
/// over a hand-built <c>HandlerInput</c> — the same shape <c>Handlers.RollDice</c>/
/// <c>Handlers.ChooseFork</c> hand it, without needing to steer a real board to a stage boundary.
/// </summary>
public sealed class StageGateResolverTests
{
    private static HandlerInput InputOver(RunAggregate run)
    {
        var state = new WorldSlice(Worlds.NewPlayer(), run);
        var rng = new RunRngScope(run.RunSeed, run.RngStreamPositions);
        return new HandlerInput(state, TileWorlds.Context, rng);
    }

    /// <summary>The heal is the shipped 15% of Max HP, read from content rather than hardcoded.</summary>
    [Fact]
    public void Apply_heals_the_shipped_percentage_of_max_hp()
    {
        var run = RunAggregate.Rehydrate(RunSnapshots.With(currentHp: 40, maxHp: 100)).Value;
        var input = InputOver(run);

        var healed = StageGateResolver.Apply(input, currentHpBeforeGate: 40);

        healed.ShouldBe(55, "40 + round(100 * 0.15) = 55");
        run.CurrentHp.ShouldBe(55);
    }

    [Fact]
    public void Apply_clamps_the_heal_at_max_hp()
    {
        var run = RunAggregate.Rehydrate(RunSnapshots.With(currentHp: 95, maxHp: 100)).Value;
        var input = InputOver(run);

        var healed = StageGateResolver.Apply(input, currentHpBeforeGate: 95);

        healed.ShouldBe(100);
        run.CurrentHp.ShouldBe(100);
    }

    [Fact]
    public void Apply_resets_reroll_charges_spent_this_stage()
    {
        var run = RunAggregate.Rehydrate(RunSnapshots.With(currentHp: 50, maxHp: 100)).Value;
        run.SpendReroll();
        run.SpendReroll();
        run.SpendReroll();
        var input = InputOver(run);

        StageGateResolver.Apply(input, currentHpBeforeGate: 50);

        run.RerollChargesSpentThisStage.ShouldBe(0);
    }

    [Fact]
    public void Apply_advances_the_dice_reset_anchor_to_the_open_stream_position()
    {
        var run = RunAggregate.Rehydrate(RunSnapshots.With(
            currentHp: 50, maxHp: 100,
            rngStreamPositions: RunSnapshots.Streams((RngStreams.Dice, 9UL)))).Value;
        var input = InputOver(run);

        // Draw three more dice positions on the open scope before the gate fires — the same shape
        // Handlers.RollDice's own chain leaves behind: draws this command took but has not yet
        // folded back into Run.
        var stream = input.Rng.Stream(RngStreams.Dice);
        stream.NextUInt();
        stream.NextUInt();
        stream.NextUInt();

        StageGateResolver.Apply(input, currentHpBeforeGate: 50);

        run.StageGateDiceAnchor.ShouldBe(12UL, "9 committed + 3 drawn this command = 12");
    }

    /// <summary>The heal takes the CALLER'S running HP total, not <c>Run.CurrentHp</c> — RollDice may
    /// have already applied an unwritten Surge heal earlier in the same chain.</summary>
    [Fact]
    public void Apply_heals_from_the_callers_running_total_not_from_Run_CurrentHp()
    {
        var run = RunAggregate.Rehydrate(RunSnapshots.With(currentHp: 40, maxHp: 100)).Value;
        var input = InputOver(run);

        // The caller's own running total (60) differs from Run.CurrentHp (40, still unwritten).
        var healed = StageGateResolver.Apply(input, currentHpBeforeGate: 60);

        healed.ShouldBe(75, "60 + round(100 * 0.15) = 75, not 40 + 15 = 55");
    }
}
