using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Tests.Model;
using SlayIdleRepeat.Core.Tests.Model.Gear;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary>
/// A run left alone for longer than its sliding window is over: run commands addressed to it are
/// refused, and the next command the player is allowed to make settles it as a death at the stage
/// it reached.
/// </summary>
/// <remarks>
/// The window slides from the run's own last accepted command, not the player's — a shop visit
/// mid-run must not keep a run alive.
/// </remarks>
public sealed class RunExpiryTests
{
    /// <summary>The authored window, in hours.</summary>
    private const int WindowHours = 48;

    /// <summary>An hour inside the window, for the control that must stay ordinary.</summary>
    private const int InsideWindowHours = 47;

    private const ulong MetaSeed = 0x5EED_0000_0000_00E1UL;

    [Theory]
    [InlineData(WindowHours)]
    [InlineData(WindowHours * 2)]
    public void A_run_command_is_refused_as_expired_once_the_window_has_passed(int hours)
    {
        var state = Live(pendingStage: null);

        var result = SlayIdleRepeat.Core.GameRules.Apply(
            state, new RollDiceCommand(), RunContextAt(GearGrantWorlds.NowUtc.AddHours(hours)));

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(
            RejectionReason.RUN_EXPIRED,
            "the reason names what actually happened. RUN_NOT_FOUND would tell a client its run "
            + "never existed, and ILLEGAL_STATE would tell it to try something else in a run it can "
            + "no longer act in.");
    }

    /// <summary>The control: one hour short of the window is an ordinary turn.</summary>
    [Fact]
    public void A_run_command_inside_the_window_is_applied_as_usual()
    {
        var state = Live(pendingStage: null);

        var result = SlayIdleRepeat.Core.GameRules.Apply(
            state, new RollDiceCommand(), RunContextAt(GearGrantWorlds.NowUtc.AddHours(InsideWindowHours)));

        result.Accepted.ShouldBeTrue(
            "an expiry that fired early would end runs players are still in the middle of.");
        result.NewState.Run!.Phase.ShouldBe(RunPhase.InProgress);
    }

    [Fact]
    public void The_next_accepted_command_settles_an_expired_run_as_a_death_at_the_stage_reached()
    {
        var state = Live(bankedLegendXp: 100, pendingStage: 2);
        var before = state.Player.LegendXp;

        var result = SlayIdleRepeat.Core.GameRules.Apply(
            state, BeginSessions.Command, MetaContextAt(GearGrantWorlds.NowUtc.AddHours(WindowHours)));

        result.Accepted.ShouldBeTrue();
        result.NewState.Run!.Phase.ShouldBe(
            RunPhase.Ended,
            "a run that is over has to be closed by something, or the player's next START_RUN is "
            + "refused for an active run nobody can play.");
        (result.NewState.Player.LegendXp - before).ShouldBe(
            40,
            "100 banked, paid at the stage-2 death rate of 0.4. Paying the abandon rate would "
            + "charge the player for a disconnection, and paying the full rate would make walking "
            + "away the cheapest way to bank a run.");
    }

    [Fact]
    public void A_settled_expired_run_keeps_the_gear_it_produced_and_grants_no_session_floor()
    {
        var state = Live(bankedLegendXp: 100, pendingStage: 3, holdingGear: true);

        var result = SlayIdleRepeat.Core.GameRules.Apply(
            state, BeginSessions.Command, MetaContextAt(GearGrantWorlds.NowUtc.AddHours(WindowHours)));

        result.NewState.Run!.Phase.ShouldBe(
            RunPhase.Ended, "the settlement this case is about has to have run at all.");

        result.NewState.Player.Inventory.Stored.Count.ShouldBe(
            1, "the run's drops are the player's the moment they are picked up; expiry settles the "
            + "run, it does not take the session back.");

        result.Events.OfType<GearGranted>().ShouldBeEmpty(
            "the floor is what a run that was genuinely played out is owed, and it needs a draw "
            + "this command has no run scope to draw from — a settlement that granted it would be "
            + "minting items on a command the player did not spend a run making.");
    }

    /// <summary>A live run, last acted on at the fixtures' instant.</summary>
    /// <param name="bankedLegendXp">What the run has banked so far, which the settlement pays out of.</param>
    /// <param name="pendingStage">The stage of the tile the run stopped on, or <c>null</c> for a run standing on nothing — the shape a roll is legal from.</param>
    /// <param name="holdingGear">Whether the player already holds an item the run produced.</param>
    private static WorldSlice Live(
        long bankedLegendXp = 0, int? pendingStage = 1, bool holdingGear = false) =>
        new(
            Worlds.Rehydrated(PlayerSnapshots.With(
                inventory: holdingGear ? Inventories.Stock(1) : null)),
            Worlds.NewRun(RunSnapshots.With(
                position: 0,
                currentHp: 100,
                maxHp: 100,
                lastAppliedAtUtc: GearGrantWorlds.NowUtc,
                pendingTileKind: pendingStage is null ? RunSnapshots.NoPendingTile : (int)TileKind.Enemy,
                pendingTileLinearIndex: pendingStage is null ? 0 : 7,
                pendingTileStage: pendingStage ?? 0,
                phase: RunPhase.InProgress,
                bankedLegendXp: bankedLegendXp)));

    // The whole shipped set, because the settlement reads the completion multipliers and the meta
    // command that carries it reads the login calendar — two documents no partial fixture set holds
    // at once.
    private static GameContext RunContextAt(DateTimeOffset nowUtc) =>
        GearGrantWorlds.Context with { NowUtc = nowUtc };

    private static GameContext MetaContextAt(DateTimeOffset nowUtc) =>
        GearGrantWorlds.Context with { NowUtc = nowUtc, CommandSeed = MetaSeed };
}
