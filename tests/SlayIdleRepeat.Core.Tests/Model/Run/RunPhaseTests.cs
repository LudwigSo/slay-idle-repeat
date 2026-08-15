using Shouldly;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;
using Xunit;
using RunAggregate = SlayIdleRepeat.Core.Model.Run;

namespace SlayIdleRepeat.Core.Tests.Model;

/// <summary>
/// 🔒 M3-05 — <c>Run</c>'s new phase/battle/draft/reroll/stage-gate seams, exercised directly on the
/// aggregate (the handler-level legality checks that guard each seam are
/// <c>StartBattleTests</c>/<c>ConfirmBattleResultTests</c>'s).
/// </summary>
public sealed class RunPhaseTests
{
    private static RunAggregate NewRun(RunPhase phase = RunPhase.InProgress, int? pendingTileKind = null) =>
        RunAggregate.Rehydrate(RunSnapshots.With(
            phase: phase,
            pendingTileKind: pendingTileKind,
            pendingTileLinearIndex: pendingTileKind is null ? null : 5,
            pendingTileStage: pendingTileKind is null ? null : 1)).Value;

    // ------------------------------------------------------------------ default phase

    [Fact]
    public void A_rehydrated_run_defaults_to_InProgress()
    {
        NewRun().Phase.ShouldBe(RunPhase.InProgress);
    }

    // ------------------------------------------------------------------ EnterBattle / ExitBattle

    [Fact]
    public void EnterBattle_moves_the_phase_to_BattlePending()
    {
        var run = NewRun(pendingTileKind: (int)TileKind.Enemy);

        run.EnterBattle();

        run.Phase.ShouldBe(RunPhase.BattlePending);
    }

    [Fact]
    public void EnterBattle_on_an_already_pending_battle_is_a_defect()
    {
        var run = NewRun(phase: RunPhase.BattlePending, pendingTileKind: (int)TileKind.Enemy);

        Should.Throw<InvalidOperationException>(() => run.EnterBattle());
    }

    [Fact]
    public void EnterBattle_with_no_pending_tile_is_a_defect()
    {
        var run = NewRun();

        Should.Throw<InvalidOperationException>(() => run.EnterBattle());
    }

    [Fact]
    public void ExitBattle_moves_the_phase_back_to_InProgress()
    {
        var run = NewRun(phase: RunPhase.BattlePending, pendingTileKind: (int)TileKind.Enemy);

        run.ExitBattle();

        run.Phase.ShouldBe(RunPhase.InProgress);
    }

    [Fact]
    public void ExitBattle_with_no_battle_open_is_a_defect()
    {
        var run = NewRun();

        Should.Throw<InvalidOperationException>(() => run.ExitBattle());
    }

    // ------------------------------------------------------------------ MarkDraftPending / ClearDraftPending

    [Fact]
    public void MarkDraftPending_sets_the_flag()
    {
        var run = NewRun();

        run.MarkDraftPending();

        run.DraftPending.ShouldBeTrue();
    }

    [Fact]
    public void MarkDraftPending_twice_is_a_defect()
    {
        var run = NewRun();
        run.MarkDraftPending();

        Should.Throw<InvalidOperationException>(() => run.MarkDraftPending());
    }

    [Fact]
    public void ClearDraftPending_is_idempotent()
    {
        var run = NewRun();

        Should.NotThrow(run.ClearDraftPending);
        run.DraftPending.ShouldBeFalse();

        run.MarkDraftPending();
        run.ClearDraftPending();
        run.DraftPending.ShouldBeFalse();

        Should.NotThrow(run.ClearDraftPending);
    }

    // ------------------------------------------------------------------ SpendReroll

    [Fact]
    public void SpendReroll_advances_the_spent_count()
    {
        var run = NewRun();

        run.SpendReroll();
        run.SpendReroll();

        run.RerollChargesSpentThisStage.ShouldBe(2);
    }

    // ------------------------------------------------------------------ ApplyStageGate

    [Fact]
    public void ApplyStageGate_writes_the_healed_hp_the_reset_charges_and_the_dice_anchor()
    {
        var run = RunAggregate.Rehydrate(RunSnapshots.With(currentHp: 40, maxHp: 100)).Value;
        run.SpendReroll();
        run.SpendReroll();

        run.ApplyStageGate(healedCurrentHp: 55, diceStreamPositionAtGate: 12UL);

        run.CurrentHp.ShouldBe(55);
        run.RerollChargesSpentThisStage.ShouldBe(0, "a Stage Gate refreshes reroll charges to the stage's base allotment");
        run.StageGateDiceAnchor.ShouldBe(12UL);
    }

    [Fact]
    public void ApplyStageGate_refuses_a_healed_hp_above_max()
    {
        var run = RunAggregate.Rehydrate(RunSnapshots.With(currentHp: 40, maxHp: 100)).Value;

        Should.Throw<ArgumentOutOfRangeException>(() => run.ApplyStageGate(101, 0UL));
    }

    // ------------------------------------------------------------------ Rehydrate validation

    [Fact]
    public void Rehydrate_refuses_an_out_of_vocabulary_phase()
    {
        var snapshot = RunSnapshots.Valid with { Phase = (RunPhase)99 };

        var result = RunAggregate.Rehydrate(snapshot);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain(nameof(SlayIdleRepeat.Core.Model.Snapshots.RunSnapshot.Phase));
    }

    [Fact]
    public void Rehydrate_refuses_a_negative_reroll_spent_count()
    {
        var snapshot = RunSnapshots.Valid with { RerollChargesSpentThisStage = -1 };

        var result = RunAggregate.Rehydrate(snapshot);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain(nameof(SlayIdleRepeat.Core.Model.Snapshots.RunSnapshot.RerollChargesSpentThisStage));
    }

    [Fact]
    public void ToSnapshot_round_trips_all_four_fields()
    {
        var run = RunAggregate.Rehydrate(RunSnapshots.With(
            pendingTileKind: (int)TileKind.Enemy, pendingTileLinearIndex: 3, pendingTileStage: 1)).Value;
        run.EnterBattle();
        run.SpendReroll();
        run.ApplyStageGate(50, 7UL);
        run.ExitBattle();
        run.MarkDraftPending();

        var snapshot = run.ToSnapshot();

        snapshot.Phase.ShouldBe(RunPhase.InProgress);
        snapshot.DraftPending.ShouldBeTrue();
        snapshot.RerollChargesSpentThisStage.ShouldBe(0);
        snapshot.StageGateDiceAnchor.ShouldBe(7UL);

        var rehydrated = RunAggregate.Rehydrate(snapshot).Value;
        rehydrated.Phase.ShouldBe(RunPhase.InProgress);
        rehydrated.DraftPending.ShouldBeTrue();
        rehydrated.RerollChargesSpentThisStage.ShouldBe(0);
        rehydrated.StageGateDiceAnchor.ShouldBe(7UL);
    }
}
