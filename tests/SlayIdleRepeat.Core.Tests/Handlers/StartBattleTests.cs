using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Board;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary>
/// 🔒 M3-05, `14` §2.3 — <c>START_BATTLE</c>, driven through the production dispatch table by
/// <c>GameRules.Apply</c>.
/// </summary>
public sealed class StartBattleTests
{
    private static CommandResult Start(WorldSlice state) =>
        SlayIdleRepeat.Core.GameRules.Apply(state, new StartBattleCommand(), TileWorlds.Context);

    // ------------------------------------------------------------------ the gate

    /// <summary>A run standing on no tile has no fight to open.</summary>
    [Fact]
    public void A_run_with_no_pending_tile_is_rejected()
    {
        var result = Start(TileWorlds.OnNoTile());

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    /// <summary>A pending tile of a non-fight kind cannot open a battle either.</summary>
    [Theory]
    [InlineData((int)TileKind.Empty)]
    [InlineData((int)TileKind.Shop)]
    [InlineData((int)TileKind.Event)]
    [InlineData((int)TileKind.Campfire)]
    [InlineData((int)TileKind.Shrine)]
    [InlineData((int)TileKind.Treasure)]
    [InlineData((int)TileKind.Cache)]
    [InlineData((int)TileKind.Curse)]
    [InlineData((int)TileKind.Minigame)]
    [InlineData((int)TileKind.DiceForge)]
    [InlineData((int)TileKind.Portal)]
    public void A_pending_tile_of_a_non_fight_kind_is_rejected(int kind)
    {
        var result = Start(TileWorlds.OnTile((TileKind)kind));

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    // ------------------------------------------------------------------ opening the fight

    /// <summary>🔒 Each of the three fight kinds opens a battle: Phase moves to BattlePending.</summary>
    [Theory]
    [InlineData((int)TileKind.Enemy)]
    [InlineData((int)TileKind.Elite)]
    [InlineData((int)TileKind.Boss)]
    public void A_pending_fight_tile_opens_the_battle(int kind)
    {
        var result = Start(TileWorlds.OnTile((TileKind)kind));

        result.Accepted.ShouldBeTrue();
        result.NewState.Run!.Phase.ShouldBe(RunPhase.BattlePending);
    }

    /// <summary>Opening a battle does not clear the pending tile — the fight still needs to close over it.</summary>
    [Fact]
    public void Opening_a_battle_does_not_clear_the_pending_tile()
    {
        var result = Start(TileWorlds.OnTile(TileKind.Enemy));

        result.NewState.Run!.ToSnapshot().PendingTileKind.ShouldBe((int)TileKind.Enemy);
    }

    /// <summary>🔒 `14` §8.1 — opening a battle advances the run's <c>combat</c> stream by exactly one.</summary>
    [Fact]
    public void Opening_a_battle_advances_the_combat_stream_by_one()
    {
        var result = Start(TileWorlds.OnTile(TileKind.Enemy));

        result.NewState.Run!.StreamPosition(RngStreams.Combat).ShouldBe(1UL);
    }

    /// <summary>A second battle opened later in the same run advances the stream again, from where it stood.</summary>
    [Fact]
    public void A_second_battle_continues_the_combat_stream()
    {
        var firstBattle = Start(TileWorlds.OnTile(TileKind.Enemy)).NewState;

        var closed = SlayIdleRepeat.Core.GameRules.Apply(
            firstBattle, new ConfirmBattleResultCommand("1", Won: true), TileWorlds.Context).NewState;

        closed.Run!.HasPendingTile.ShouldBeFalse("CONFIRM_BATTLE_RESULT must clear the pending tile");

        // 🔒 M3-06 — CONFIRM_BATTLE_RESULT also marks a draft pending, and GameRules.Execute's
        // DraftPending gate refuses every run command but PICK_PERK/REROLL_DRAFT/SKIP_DRAFT while
        // one is open. This test is about the combat stream's monotonicity, not the draft, so it
        // resolves the draft with SKIP_DRAFT before splicing a second fight tile onto the run.
        var draftResolved = SlayIdleRepeat.Core.GameRules.Apply(
            closed, new SkipDraftCommand(), TileWorlds.Context).NewState;

        // Splice a second pending fight tile directly onto the closed run's snapshot — this test is
        // about the combat stream's monotonicity, not about how a second tile is arrived at.
        var secondTileSnapshot = draftResolved.Run!.ToSnapshot() with
        {
            PendingTileKind = (int)TileKind.Boss,
            PendingTileLinearIndex = 41,
            PendingTileStage = 0,
        };
        var secondTileRun = SlayIdleRepeat.Core.Model.Run.Rehydrate(secondTileSnapshot).Value;

        var secondBattle = Start(closed with { Run = secondTileRun });

        secondBattle.Accepted.ShouldBeTrue();
        secondBattle.NewState.Run!.StreamPosition(RngStreams.Combat).ShouldBe(2UL);
    }

    // ------------------------------------------------------------------ the phase gate

    /// <summary>A second START_BATTLE while one is already open is refused — GameRules.Execute's phase gate.</summary>
    [Fact]
    public void A_second_START_BATTLE_while_one_is_open_is_rejected()
    {
        var opened = TileWorlds.OnTile(TileKind.Enemy, phase: RunPhase.BattlePending);

        var result = Start(opened);

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    /// <summary>…and ROLL_DICE is refused too, while a battle is open.</summary>
    [Fact]
    public void Rolling_while_a_battle_is_open_is_rejected()
    {
        var opened = TileWorlds.OnTile(TileKind.Enemy, phase: RunPhase.BattlePending);

        var result = SlayIdleRepeat.Core.GameRules.Apply(
            opened, new RollDiceCommand(), TileWorlds.Context);

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }
}
