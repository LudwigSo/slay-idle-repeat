using Shouldly;
using SlayIdleRepeat.Core;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Rules.Board.Resolution;
using SlayIdleRepeat.Core.Tests.Handlers;
using SlayIdleRepeat.Core.Tests.Model;
using Xunit;
using RunAggregate = SlayIdleRepeat.Core.Model.Run;

namespace SlayIdleRepeat.Core.Tests.Rules.Board.Resolution;

/// <summary>
/// The Stage Gate's two edges the command-level suite (<c>Handlers.StageGateTriggerTests</c>, which
/// pins the heal, the reroll reset and the dice anchor through <c>GameRules.Apply</c>) does not
/// cover: the heal clamp at Max HP, and healing from the caller's running total.
/// </summary>
public sealed class StageGateResolverTests
{
    /// <summary>Chapter 1's stage-1 last spine index: its stage lengths are 12/14/16.</summary>
    private const int StageOneLast = 11;

    /// <summary>The gate's heal is clamped at Max HP: 95 + round(100 × 0.15) stops at 100, not 110.</summary>
    /// <remarks>
    /// Any pip from one node short of stage 1's last node comes to rest ON it — a 1 lands exactly,
    /// anything larger is clamped by the stage-end rule — so the gate fires whatever the seed draws.
    /// </remarks>
    [Fact]
    public void The_stage_gate_heal_is_clamped_at_max_hp()
    {
        var board = BoardGenerator.GenerateBoard(
            ChapterBoardTuning.Read(TileWorlds.Context.Content, chapterId: 1),
            DeterministicRng.OpenAt(TileWorlds.Seed, RngStreams.Board, 0));

        var result = SlayIdleRepeat.Core.GameRules.Apply(
            Worlds.InARun(RunSnapshots.With(
                chapterId: 1,
                runSeed: TileWorlds.Seed,
                position: board.SpineNode(StageOneLast - 1).Value,
                currentHp: 95,
                maxHp: 100)),
            new RollDiceCommand(),
            TileWorlds.Context);

        result.Accepted.ShouldBeTrue();

        var run = result.NewState.Run!;

        run.Position.ShouldBe(
            board.SpineNode(StageOneLast).Value,
            "the premise: any roll from one node short comes to rest on stage 1's last node.");
        run.CurrentHp.ShouldBe(
            100, "the premise: the gate fired — 95 + 15 healed is clamped at Max HP.");
    }

    /// <summary>The heal takes the CALLER'S running HP total, not <c>Run.CurrentHp</c>.</summary>
    /// <remarks>
    /// Internal seam by necessity: the divergence exists only mid-command, when
    /// a caller has computed a heal it has not yet written to <c>Run</c> — a state no snapshot
    /// handed to <c>Apply</c> can express.
    /// </remarks>
    [Fact]
    public void Apply_heals_from_the_callers_running_total_not_from_Run_CurrentHp()
    {
        var run = RunAggregate.Rehydrate(RunSnapshots.With(currentHp: 40, maxHp: 100)).Value;
        var state = new WorldSlice(Worlds.NewPlayer(), run);
        var input = new HandlerInput(
            state, TileWorlds.Context, new RunRngScope(run.RunSeed, run.RngStreamPositions));

        var healed = StageGateResolver.Apply(input, currentHpBeforeGate: 60);

        healed.ShouldBe(75, "60 + round(100 * 0.15) = 75, not 40 + 15 = 55");
    }
}
