using System.Globalization;
using Shouldly;
using SlayIdleRepeat.BalanceHarness.Content;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
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

    /// <summary>
    /// The two stamps pulled apart: the player was here an hour ago and the run was not touched for
    /// the whole window. Without this the fixtures' two anchors sit a second apart and a window
    /// measured off the wrong one answers every other case in this file identically.
    /// </summary>
    [Fact]
    public void The_window_is_measured_from_the_runs_own_last_command_and_not_the_players()
    {
        var at = GearGrantWorlds.NowUtc.AddHours(WindowHours);
        var state = Live(pendingStage: null, playerLastAppliedAtUtc: at.AddHours(-1));

        var result = SlayIdleRepeat.Core.GameRules.Apply(state, new RollDiceCommand(), RunContextAt(at));

        result.Rejection.ShouldBe(
            RejectionReason.RUN_EXPIRED,
            "the run is what expires. Measured off the player instead, a shop visit or a daily "
            + "claim would keep a run nobody is playing alive forever.");
    }

    /// <summary>
    /// The reason a settled run answers with. Both rules can see this slice, and only one of them
    /// describes it.
    /// </summary>
    [Fact]
    public void A_run_that_already_ended_is_refused_as_ended_rather_than_as_expired()
    {
        var state = Live(pendingStage: null, phase: RunPhase.Ended);

        var result = SlayIdleRepeat.Core.GameRules.Apply(
            state, new RollDiceCommand(), RunContextAt(GearGrantWorlds.NowUtc.AddHours(WindowHours)));

        result.Rejection.ShouldBe(
            RejectionReason.RUN_ALREADY_ENDED,
            "the run was closed and paid out; telling the client it expired invites it to wait for "
            + "a settlement that already happened instead of starting the next run.");
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
    public void A_settled_expired_run_keeps_the_gear_it_produced()
    {
        var state = Live(bankedLegendXp: 100, pendingStage: 3, holdingGear: true);

        var result = SlayIdleRepeat.Core.GameRules.Apply(
            state, BeginSessions.Command, MetaContextAt(GearGrantWorlds.NowUtc.AddHours(WindowHours)));

        result.NewState.Run!.Phase.ShouldBe(
            RunPhase.Ended, "the settlement this case is about has to have run at all.");
        result.NewState.Player.Inventory.Stored.Count.ShouldBe(
            1, "the run's drops are the player's the moment they are picked up; expiry settles the "
            + "run, it does not take the session back.");
    }

    /// <summary>🔒 The recorded assumption of D5, stated as a case so it cannot drift silently.</summary>
    [Fact]
    public void A_settled_expired_run_grants_no_session_floor()
    {
        var state = Live(bankedLegendXp: 100, pendingStage: 3, holdingGear: true);

        var result = SlayIdleRepeat.Core.GameRules.Apply(
            state, BeginSessions.Command, MetaContextAt(GearGrantWorlds.NowUtc.AddHours(WindowHours)));

        result.NewState.Run!.Phase.ShouldBe(
            RunPhase.Ended, "the settlement this case is about has to have run at all.");
        result.Events.OfType<GearGranted>().ShouldBeEmpty(
            "the floor is what a run that was genuinely played out is owed, and it needs a draw "
            + "this command has no run scope to draw from — a settlement that granted it would be "
            + "minting items on a command the player did not spend a run making.");
    }

    /// <summary>
    /// 🔒 One command both closes the lapsed run and opens the next one, which is the whole reason
    /// the catch-up runs before the finished run is cleared. Cleared first, the handler is handed a
    /// live run it can only refuse, and the player is left holding one they can neither play (every
    /// run command is refused as expired) nor replace.
    /// </summary>
    [Fact]
    public void START_RUN_settles_the_lapsed_run_and_opens_the_next_one_in_the_same_command()
    {
        var state = Live(bankedLegendXp: 100, pendingStage: 2);
        var before = state.Player.LegendXp;

        var result = SlayIdleRepeat.Core.GameRules.Apply(
            state,
            new StartRunCommand(1, DifficultyTier.NORMAL),
            MetaContextAt(GearGrantWorlds.NowUtc.AddHours(WindowHours)) with { AllocatedRunId = NextRun });

        result.Accepted.ShouldBeTrue(
            "refused here, the lapsed run is a run the player can neither play nor replace — every "
            + "run command answers RUN_EXPIRED and this is the only command that clears one.");
        result.NewState.Run!.Id.ShouldBe(
            NextRun, "the command opened the next run, at the identity the host allocated for it.");
        result.NewState.Run.Phase.ShouldBe(RunPhase.InProgress);
        (result.NewState.Player.LegendXp - before).ShouldBe(
            40,
            "…and the lapsed run was settled on the way, at the stage-2 death rate, rather than "
            + "discarded unpaid by the clear that makes room for the new one.");
    }

    /// <summary>The identity a host allocates for the run <c>START_RUN</c> opens.</summary>
    private static readonly RunId NextRun = new("RUN_4b71e0000000000000000000000000c3");

    /// <summary>
    /// 🔒 The window is the number the content authors, and this is the only case that can tell that
    /// apart from a 48 folded into the rule: every other case here reads identically against a
    /// constant, because the shipped document authors the same 48 they are written around.
    /// </summary>
    [Fact]
    public void The_window_is_the_authored_one_and_not_a_number_in_the_code()
    {
        var at = GearGrantWorlds.NowUtc.AddHours(RetunedWindowHours);

        var shipped = SlayIdleRepeat.Core.GameRules.Apply(
            Live(pendingStage: null), new RollDiceCommand(), RunContextAt(at));

        shipped.Accepted.ShouldBeTrue(
            "the negative control of the pair: at the retuned window the SHIPPED document still has "
            + "hours to run, so a refusal here would mean the two arms are not discriminating "
            + "anything.");

        var retuned = SlayIdleRepeat.Core.GameRules.Apply(
            Live(pendingStage: null),
            new RollDiceCommand(),
            RunContextAt(at) with { Content = RetunedTo(RetunedWindowHours) });

        retuned.Rejection.ShouldBe(
            RejectionReason.RUN_EXPIRED,
            "the same elapsed span against a document authoring a shorter window is over. A rule "
            + "that carried its own 48 would accept this command and no other case in this file "
            + "would notice.");
    }

    /// <summary>A window the shipped document does not author, so the two arms of the pair differ.</summary>
    private const int RetunedWindowHours = 24;

    /// <summary>The shipped content set with the run window authored down to <paramref name="hours"/>.</summary>
    /// <remarks>
    /// Retuned through <c>GameDataLoader.LoadWith</c> over the real tree rather than hand-built, so
    /// the document, the reader and the rule under test are all the shipped ones — a fixture snapshot
    /// would only prove that a hand-written number reaches a hand-written reader.
    /// </remarks>
    private static ContentSnapshot RetunedTo(int hours)
    {
        var authored = File.ReadAllText(
            Path.Combine(GameDataLoader.DataRoot, RunLifetimeTuning.DocumentPath));

        var retuned = authored.Replace(
            "\"expiryHours\": " + WindowHours.ToString(CultureInfo.InvariantCulture),
            "\"expiryHours\": " + hours.ToString(CultureInfo.InvariantCulture),
            StringComparison.Ordinal);

        retuned.ShouldNotBe(
            authored,
            "the retune has to have landed, or both arms read the shipped window and the case "
            + "passes over one document twice.");

        return GameDataLoader.LoadWith(
            GameDataLoader.DataRoot,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [RunLifetimeTuning.DocumentPath] = retuned,
            });
    }

    /// <summary>A live run, last acted on at the fixtures' instant.</summary>
    /// <param name="bankedLegendXp">What the run has banked so far, which the settlement pays out of.</param>
    /// <param name="pendingStage">The stage of the tile the run stopped on, or <c>null</c> for a run standing on nothing — the shape a roll is legal from.</param>
    /// <param name="holdingGear">Whether the player already holds an item the run produced.</param>
    /// <param name="playerLastAppliedAtUtc">When the PLAYER last acted. Defaults to the fixtures' own anchor.</param>
    /// <param name="phase">The run's phase. Defaults to a live run.</param>
    private static WorldSlice Live(
        long bankedLegendXp = 0,
        int? pendingStage = 1,
        bool holdingGear = false,
        DateTimeOffset? playerLastAppliedAtUtc = null,
        RunPhase phase = RunPhase.InProgress) =>
        new(
            Worlds.Rehydrated(PlayerSnapshots.With(
                lastAppliedAtUtc: playerLastAppliedAtUtc,
                inventory: holdingGear ? Inventories.Stock(1) : null)),
            Worlds.NewRun(RunSnapshots.With(
                position: 0,
                currentHp: 100,
                maxHp: 100,
                lastAppliedAtUtc: GearGrantWorlds.NowUtc,
                pendingTileKind: pendingStage is null ? RunSnapshots.NoPendingTile : (int)TileKind.Enemy,
                pendingTileLinearIndex: pendingStage is null ? 0 : 7,
                pendingTileStage: pendingStage ?? 0,
                phase: phase,
                bankedLegendXp: bankedLegendXp)));

    // The whole shipped set, because the settlement reads the completion multipliers and the meta
    // command that carries it reads the login calendar — two documents no partial fixture set holds
    // at once.
    private static GameContext RunContextAt(DateTimeOffset nowUtc) =>
        GearGrantWorlds.Context with { NowUtc = nowUtc };

    private static GameContext MetaContextAt(DateTimeOffset nowUtc) =>
        GearGrantWorlds.Context with { NowUtc = nowUtc, CommandSeed = MetaSeed };
}
