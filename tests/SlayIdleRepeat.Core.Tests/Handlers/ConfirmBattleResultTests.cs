using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary>
/// 🔒 M3-05, `14` §2.3 — <c>CONFIRM_BATTLE_RESULT</c>, driven through the production dispatch table
/// by <c>GameRules.Apply</c>.
/// </summary>
public sealed class ConfirmBattleResultTests
{
    private static CommandResult Confirm(WorldSlice state, string logHash) =>
        SlayIdleRepeat.Core.GameRules.Apply(
            state, new ConfirmBattleResultCommand(logHash), TileWorlds.Context);

    // ------------------------------------------------------------------ the gate

    /// <summary>No battle is open on a run standing on no tile at all.</summary>
    [Fact]
    public void A_run_with_no_battle_open_is_rejected()
    {
        var result = Confirm(TileWorlds.OnNoTile(), "123");

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    /// <summary>A run standing on a pending fight tile that never opened a battle is still not open.</summary>
    [Fact]
    public void A_pending_fight_tile_with_no_open_battle_is_rejected()
    {
        var result = Confirm(TileWorlds.OnTile(TileKind.Enemy), "123");

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    // ------------------------------------------------------------------ LogHash format

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-number")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("0x1A")]
    public void A_malformed_LogHash_is_rejected(string logHash)
    {
        var opened = TileWorlds.OnTile(TileKind.Enemy, phase: RunPhase.BattlePending);

        var result = Confirm(opened, logHash);

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("18446744073709551615")] // ulong.MaxValue
    [InlineData("123456789")]
    public void A_well_formed_LogHash_is_accepted(string logHash)
    {
        var opened = TileWorlds.OnTile(TileKind.Enemy, phase: RunPhase.BattlePending);

        var result = Confirm(opened, logHash);

        result.Accepted.ShouldBeTrue();
    }

    // ------------------------------------------------------------------ closing the fight

    /// <summary>Closing a battle moves the phase back to InProgress.</summary>
    [Fact]
    public void Closing_a_battle_returns_the_phase_to_InProgress()
    {
        var opened = TileWorlds.OnTile(TileKind.Enemy, phase: RunPhase.BattlePending);

        var result = Confirm(opened, "1");

        result.NewState.Run!.Phase.ShouldBe(RunPhase.InProgress);
    }

    /// <summary>…and clears the pending fight tile.</summary>
    [Fact]
    public void Closing_a_battle_clears_the_pending_tile()
    {
        var opened = TileWorlds.OnTile(TileKind.Boss, phase: RunPhase.BattlePending);

        var result = Confirm(opened, "1");

        result.NewState.Run!.HasPendingTile.ShouldBeFalse();
    }

    /// <summary>🔒 The M3-06 hook: closing a battle marks a draft as pending.</summary>
    [Fact]
    public void Closing_a_battle_marks_a_draft_pending()
    {
        var opened = TileWorlds.OnTile(TileKind.Elite, phase: RunPhase.BattlePending);

        var result = Confirm(opened, "1");

        result.NewState.Run!.ToSnapshot().DraftPending.ShouldBeTrue();
    }

    /// <summary>Closing a battle spends no currency and pays no reward — M3-13's, not this task's.</summary>
    [Fact]
    public void Closing_a_battle_pays_no_reward()
    {
        var opened = TileWorlds.OnTile(TileKind.Enemy, gold: 250, currentHp: 60, phase: RunPhase.BattlePending);

        var result = Confirm(opened, "1");

        result.NewState.Run!.Gold.ShouldBe(250);
        result.NewState.Run!.CurrentHp.ShouldBe(60);
        result.Events.ShouldBeEmpty();
    }

    /// <summary>A second CONFIRM_BATTLE_RESULT after the first closed it is refused — nothing is open.</summary>
    [Fact]
    public void Confirming_twice_is_rejected()
    {
        var opened = TileWorlds.OnTile(TileKind.Enemy, phase: RunPhase.BattlePending);

        var closed = Confirm(opened, "1").NewState;

        Confirm(closed, "2").Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    // ------------------------------------------------------------------ the round trip

    /// <summary>The full loop: RESOLVE_TILE acknowledges a fight tile, START_BATTLE opens it, CONFIRM_BATTLE_RESULT closes it.</summary>
    [Fact]
    public void A_fight_resolves_across_three_commands()
    {
        var arrived = TileWorlds.OnTile(TileKind.Enemy);

        var acknowledged = SlayIdleRepeat.Core.GameRules.Apply(
            arrived, new ResolveTileCommand(), TileWorlds.Context);
        acknowledged.Accepted.ShouldBeTrue();
        acknowledged.NewState.Run!.HasPendingTile.ShouldBeTrue("RESOLVE_TILE must not consume a fight tile");

        var opened = SlayIdleRepeat.Core.GameRules.Apply(
            acknowledged.NewState, new StartBattleCommand(), TileWorlds.Context);
        opened.Accepted.ShouldBeTrue();
        opened.NewState.Run!.Phase.ShouldBe(RunPhase.BattlePending);

        var closed = Confirm(opened.NewState, "1");
        closed.Accepted.ShouldBeTrue();
        closed.NewState.Run!.Phase.ShouldBe(RunPhase.InProgress);
        closed.NewState.Run!.HasPendingTile.ShouldBeFalse();
    }
}
