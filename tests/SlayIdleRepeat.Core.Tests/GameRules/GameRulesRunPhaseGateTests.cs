using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Tests.Handlers;
using SlayIdleRepeat.Core.Tests.Model;
using Xunit;

namespace SlayIdleRepeat.Core.Tests;

/// <summary>
/// 🔒 M3-05 — <c>GameRules.Execute</c>'s <c>RunPhase</c> gate: the one place the state-set ruling on
/// <c>Primitives.RunPhase</c> is actually enforced, over the production dispatch table.
/// </summary>
public sealed class GameRulesRunPhaseGateTests
{
    // ------------------------------------------------------------------ RunPhase.Ended

    /// <summary>
    /// 🔒 No command in this milestone ever produces <see cref="RunPhase.Ended"/> — M3-13's
    /// <c>END_RUN</c>/<c>ABANDON_RUN</c> do, once they land — but the gate this task builds must
    /// already answer <c>RUN_ALREADY_ENDED</c> the day a run reaches it, which is exactly what
    /// <c>GapRegister</c>'s discharged <c>RunPhase</c> entry named as the consequence of authoring
    /// the enum at all.
    /// </summary>
    [Fact]
    public void A_run_command_against_an_Ended_run_is_RUN_ALREADY_ENDED()
    {
        var state = Worlds.InARun(RunSnapshots.With(phase: RunPhase.Ended));

        var result = SlayIdleRepeat.Core.GameRules.Apply(state, new RollDiceCommand(), Worlds.Context);

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.RUN_ALREADY_ENDED);
    }

    /// <summary>…and it is unconditional: even CONFIRM_BATTLE_RESULT, which the BattlePending gate exempts, is refused.</summary>
    [Fact]
    public void CONFIRM_BATTLE_RESULT_against_an_Ended_run_is_also_RUN_ALREADY_ENDED()
    {
        var state = Worlds.InARun(RunSnapshots.With(phase: RunPhase.Ended));

        var result = SlayIdleRepeat.Core.GameRules.Apply(
            state, new ConfirmBattleResultCommand("1", Won: true), Worlds.Context);

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.RUN_ALREADY_ENDED);
    }

    /// <summary>A rejection provably changes nothing — the caller's own slice comes back.</summary>
    [Fact]
    public void The_Ended_rejection_returns_the_callers_own_slice()
    {
        var state = Worlds.InARun(RunSnapshots.With(phase: RunPhase.Ended));

        var result = SlayIdleRepeat.Core.GameRules.Apply(state, new RollDiceCommand(), Worlds.Context);

        result.NewState.ShouldBeSameAs(state);
    }

    // ------------------------------------------------------------------ RunPhase.BattlePending

    /// <summary>A battle open blocks every run command except CONFIRM_BATTLE_RESULT.</summary>
    [Theory]
    [InlineData(nameof(RollDiceCommand))]
    [InlineData(nameof(UseRerollCommand))]
    [InlineData(nameof(ResolveTileCommand))]
    [InlineData(nameof(StartBattleCommand))]
    public void A_run_command_other_than_CONFIRM_BATTLE_RESULT_is_rejected_while_a_battle_is_open(
        string commandName)
    {
        var state = TileWorlds.OnTile(TileKind.Enemy, phase: RunPhase.BattlePending);

        SlayIdleRepeat.Core.Commands.GameCommand command = commandName switch
        {
            nameof(RollDiceCommand) => new RollDiceCommand(),
            nameof(UseRerollCommand) => new UseRerollCommand(),
            nameof(ResolveTileCommand) => new ResolveTileCommand(),
            nameof(StartBattleCommand) => new StartBattleCommand(),
            _ => throw new InvalidOperationException("unreachable"),
        };

        var result = SlayIdleRepeat.Core.GameRules.Apply(state, command, TileWorlds.Context);

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    /// <summary>…while CONFIRM_BATTLE_RESULT reaches its own handler and is accepted.</summary>
    [Fact]
    public void CONFIRM_BATTLE_RESULT_reaches_its_handler_while_a_battle_is_open()
    {
        var state = TileWorlds.OnTile(TileKind.Enemy, phase: RunPhase.BattlePending);

        var result = SlayIdleRepeat.Core.GameRules.Apply(
            state, new ConfirmBattleResultCommand("1", Won: true), TileWorlds.Context);

        result.Accepted.ShouldBeTrue();
    }

    // ------------------------------------------------------------------ RunPhase.InProgress is unaffected

    /// <summary>The default phase gates nothing new — every existing legality check still runs.</summary>
    [Fact]
    public void InProgress_gates_nothing_by_itself()
    {
        var state = Worlds.InARun(RunSnapshots.With(phase: RunPhase.InProgress));

        SlayIdleRepeat.Core.GameRules.Apply(state, new RollDiceCommand(), Worlds.Context)
            .Accepted.ShouldBeTrue();
    }
}
