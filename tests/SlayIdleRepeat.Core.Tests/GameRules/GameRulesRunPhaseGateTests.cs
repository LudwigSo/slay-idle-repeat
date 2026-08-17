using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Tests.Handlers;
using SlayIdleRepeat.Core.Tests.Model;
using Xunit;

namespace SlayIdleRepeat.Core.Tests;

/// <summary>
/// <c>GameRules.Execute</c>'s <c>RunPhase</c> gate: the one place <c>Primitives.RunPhase</c> is
/// actually enforced, over the production dispatch table.
/// </summary>
public sealed class GameRulesRunPhaseGateTests
{
    // ------------------------------------------------------------------ RunPhase.Ended

    /// <summary>
    /// <c>END_RUN</c> and <c>ABANDON_RUN</c> both put a run at <see cref="RunPhase.Ended"/>, and a
    /// run command that arrives afterwards is answered <c>RUN_ALREADY_ENDED</c>.
    /// </summary>
    [Fact]
    public void A_run_command_against_an_Ended_run_is_RUN_ALREADY_ENDED()
    {
        var state = Worlds.InARun(RunSnapshots.With(phase: RunPhase.Ended));

        var result = SlayIdleRepeat.Core.GameRules.Apply(state, new RollDiceCommand(), Worlds.Context);

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.RUN_ALREADY_ENDED);
    }

    /// <summary>
    /// …and it is unconditional except for the one row that opens its own run: even
    /// CONFIRM_BATTLE_RESULT, which the BattlePending gate exempts, is refused here.
    /// <c>START_RUN</c> is the single exemption — see
    /// <see cref="START_RUN_against_an_Ended_run_opens_a_fresh_run"/>.
    /// </summary>
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

    // -------------------------------------------------- START_RUN is the one row that opens its own run

    /// <summary>
    /// 🔒 <b>A player gets more than one run.</b> <c>START_RUN</c> is the single dispatch row marked
    /// <c>OpensRun</c>, and on an ended run the phase gate lets it through to its handler, which
    /// opens a fresh one.
    /// </summary>
    /// <remarks>
    /// The stream positions are the load-bearing half. The ended fixture run carries a non-empty
    /// counter map, and a just-opened run has drawn nothing — so a result whose run inherited those
    /// counters would be the ended run wearing a new phase rather than a new run at all.
    /// </remarks>
    [Fact]
    public void START_RUN_against_an_Ended_run_opens_a_fresh_run()
    {
        var state = EndedRun();
        var ended = state.Run!;

        // 🔒 The premise under the counter assertion below, asserted rather than narrated: against an
        // ended run that had drawn nothing, "the new run has drawn nothing" is equally true of the
        // ended one wearing a new phase, and the sharpest assertion in this case would be vacuous.
        ended.RngStreamPositions.ShouldNotBeEmpty(
            "the ENDED fixture run carries no draw counters. EndedRun() seeds one on purpose — see " +
            "this case's remarks — so a fixture that stopped doing it silences the assertion that " +
            "tells a fresh run apart from a re-phased one.");

        var result = SlayIdleRepeat.Core.GameRules.Apply(
            state, new StartRunCommand(Chapter, DifficultyTier.NORMAL), Worlds.Context);

        result.Accepted.ShouldBeTrue(
            "START_RUN on an ENDED run was refused " + result.Rejection + ". RUN_ALREADY_ENDED means " +
            "the phase gate still refuses every run command including the one whose job is to open a " +
            "new run; ILLEGAL_STATE means the gate let it through but the ended run was still sitting " +
            "in the slice, so StartRun.Handle's already-active-run guard fired instead.");

        var opened = result.NewState.Run;

        opened.ShouldNotBeNull("START_RUN was accepted and attached no run.");

        opened!.Phase.ShouldBe(
            RunPhase.InProgress, "the run that came back is not playable, so nothing was opened.");

        opened.Id.ShouldNotBe(
            ended.Id,
            "the run that came back carries the ENDED run's identity, so the slice was re-phased " +
            "rather than given a new run.");

        opened.RunSeed.ShouldNotBe(
            ended.RunSeed,
            "the new run committed the ended run's seed, so it would replay the board the player has " +
            "already walked.");

        opened.RngStreamPositions.ShouldBeEmpty(
            "the new run inherited the ended run's draw counters, which a run that has drawn nothing " +
            "cannot have — so the ended run was never cleared off the working slice.");
    }

    /// <summary>The lifetime run counter moves by exactly one, since exactly one run was opened.</summary>
    [Fact]
    public void The_run_opened_on_an_Ended_run_spends_one_of_the_lifetime_run_counter()
    {
        var state = EndedRun();
        var before = state.Player.RunsStarted;

        var result = SlayIdleRepeat.Core.GameRules.Apply(
            state, new StartRunCommand(Chapter, DifficultyTier.NORMAL), Worlds.Context);

        result.Accepted.ShouldBeTrue("START_RUN on an ended run was refused " + result.Rejection + ".");

        result.NewState.Player.RunsStarted.ShouldBe(
            before + 1,
            "START_RUN opened a run and the lifetime counter moved by " +
            (result.NewState.Player.RunsStarted - before) + " rather than 1. The counter is what the " +
            "run's seed and its minted id are derived from, so a run opened without spending it " +
            "collides with the one before.");
    }

    /// <summary>
    /// 🔴 <b>The guard that must NOT be widened.</b> A run that is still being played refuses a
    /// second <c>START_RUN</c> — the exemption is for an <em>ended</em> run and nothing else, and a
    /// player who double-taps the button must not lose the run they are in.
    /// </summary>
    /// <remarks>
    /// The refusal comes from <c>StartRun.Handle</c>'s own already-active-run guard, which the phase
    /// gate falls through to. Pinned by identity: the reason AND the run still sitting in the slice
    /// unchanged, since "it was refused" alone would also be satisfied by the gate refusing every
    /// <c>START_RUN</c> outright.
    /// </remarks>
    [Fact]
    public void START_RUN_against_an_InProgress_run_is_refused_and_that_run_is_untouched()
    {
        var state = Worlds.InARun(RunSnapshots.With(phase: RunPhase.InProgress));
        var open = state.Run!;

        var result = SlayIdleRepeat.Core.GameRules.Apply(
            state, new StartRunCommand(Chapter, DifficultyTier.NORMAL), Worlds.Context);

        result.Accepted.ShouldBeFalse(
            "a SECOND run was opened on top of a run the player is still playing. The exemption is " +
            "for RunPhase.Ended only; widened past that it silently discards a live run.");

        result.Rejection.ShouldBe(
            RejectionReason.ILLEGAL_STATE,
            "the refusal came from somewhere other than StartRun.Handle's already-active-run guard. " +
            "RUN_ALREADY_ENDED here would mean the phase gate is refusing an in-progress run as if it " +
            "had ended.");

        result.NewState.Run!.Id.ShouldBe(
            open.Id, "the run the player is playing was replaced by a refused command's run.");

        result.NewState.Run.Phase.ShouldBe(
            RunPhase.InProgress, "the refused START_RUN moved the live run's phase.");

        result.NewState.Player.RunsStarted.ShouldBe(
            state.Player.RunsStarted, "a refused START_RUN must not spend the lifetime run counter.");
    }

    /// <summary>
    /// …and a run with a battle open refuses it too: <c>START_RUN</c> is not
    /// <c>CONFIRM_BATTLE_RESULT</c>, which is the only move a battle leaves.
    /// </summary>
    /// <remarks>
    /// The <c>BattlePending</c> arm is what answers today. Were the exemption widened past
    /// <c>Ended</c>, <c>StartRun.Handle</c>'s already-active-run guard would answer the same
    /// <c>ILLEGAL_STATE</c> instead and no <c>RejectionReason</c> could tell the two apart — so the
    /// assertion that catches the widening a player would feel, the one that also discards the run
    /// they are in, is the refusal itself rather than its reason.
    /// </remarks>
    [Fact]
    public void START_RUN_against_a_BattlePending_run_is_refused_and_that_run_is_untouched()
    {
        var state = Worlds.InARun(RunSnapshots.With(phase: RunPhase.BattlePending));
        var open = state.Run!;

        var result = SlayIdleRepeat.Core.GameRules.Apply(
            state, new StartRunCommand(Chapter, DifficultyTier.NORMAL), Worlds.Context);

        result.Accepted.ShouldBeFalse(
            "a new run was opened while a battle was still open on the current one.");

        result.Rejection.ShouldBe(
            RejectionReason.ILLEGAL_STATE,
            "the BattlePending arm answers ILLEGAL_STATE; RUN_ALREADY_ENDED here would mean the ended " +
            "arm is now firing on a phase it does not own.");

        result.NewState.Run!.Id.ShouldBe(open.Id, "the run with the open battle was replaced.");
        result.NewState.Run.Phase.ShouldBe(RunPhase.BattlePending, "the open battle was cleared.");

        result.NewState.Player.RunsStarted.ShouldBe(
            state.Player.RunsStarted, "a refused START_RUN must not spend the lifetime run counter.");
    }

    /// <summary>
    /// A <c>START_RUN</c> the <em>handler</em> refuses on an ended run leaves the caller's slice
    /// exactly as it was: the ended run still there, the counter unmoved.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Chapter 0 is below the authored floor, so the gate lets the command through and
    /// <c>StartRun.Handle</c> refuses it for a reason of its own — deliberately <b>not</b>
    /// <c>RUN_ALREADY_ENDED</c>, which is what distinguishes "the door opened and the handler said
    /// no" from "the door never opened".
    /// </para>
    /// <para>
    /// ⚠️ <b>That is as far as the reason can pin it, and the limit is stated rather than glossed
    /// (steering S2).</b> <c>StartRun.Handle</c> answers <c>ILLEGAL_STATE</c> from two guards — the
    /// chapter floor this case names, and the already-active-run guard that fires when the gate lets
    /// <c>START_RUN</c> through without the ended run being cleared off the working copy — and a
    /// <c>RejectionReason</c> cannot tell them apart. <see cref="START_RUN_against_an_Ended_run_opens_a_fresh_run"/>
    /// is what rules the second one out; the two are read together.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_refused_START_RUN_on_an_Ended_run_returns_the_callers_own_slice()
    {
        var state = EndedRun();

        var result = SlayIdleRepeat.Core.GameRules.Apply(
            state, new StartRunCommand(BelowTheChapterFloor, DifficultyTier.NORMAL), Worlds.Context);

        result.Accepted.ShouldBeFalse("chapter " + BelowTheChapterFloor + " is below the floor of 1.");

        result.Rejection.ShouldBe(
            RejectionReason.ILLEGAL_STATE,
            "this is deliberately NOT RUN_ALREADY_ENDED: the ended-run gate is supposed to let " +
            "START_RUN through and StartRun.Handle is supposed to refuse this chapter on its own. " +
            "RUN_ALREADY_ENDED means the gate answered first and the handler was never reached. " +
            "⚠️ ILLEGAL_STATE does NOT by itself say which of StartRun.Handle's two guards answered — " +
            "the chapter floor, or the already-active-run guard that fires when the ended run was " +
            "never cleared off the working copy. If START_RUN_against_an_Ended_run_opens_a_fresh_run " +
            "is red too, it is the second one.");

        result.NewState.ShouldBeSameAs(
            state, "a refused command must hand the caller back the very slice it was given.");

        state.Run!.Phase.ShouldBe(
            RunPhase.Ended, "the refused command cleared the ended run off the CALLER's slice.");

        state.Player.RunsStarted.ShouldBe(
            0L, "a refused START_RUN must not spend the lifetime run counter.");
    }

    // ---------------------------------------- the draft arm, which the ended arm now short-circuits

    /// <summary>
    /// 🔒 <b>The second arm the exemption had to cross, and the only one whose behaviour actually
    /// changed.</b> <c>DraftPending</c> is a flag orthogonal to <see cref="RunPhase"/>, so an ended
    /// run can carry one — and the draft arm would otherwise refuse the exempted row a second time,
    /// for a reason that has nothing to do with the run being over.
    /// </summary>
    /// <remarks>
    /// The <c>BattlePending</c> arm needs no companion case: <see cref="RunPhase"/> holds one value,
    /// so a run cannot be <see cref="RunPhase.Ended"/> and <see cref="RunPhase.BattlePending"/> at
    /// once and that arm is unreachable from this one by construction. The draft flag is the arm that
    /// can genuinely be set alongside <see cref="RunPhase.Ended"/>, so it is the arm that needs
    /// proving.
    /// </remarks>
    [Fact]
    public void START_RUN_against_an_Ended_run_with_a_draft_still_open_opens_a_fresh_run()
    {
        var state = EndedRunWithADraftStillOpen();

        state.Run!.DraftPending.ShouldBeTrue(
            "the fixture stopped carrying an open draft, so this case no longer crosses the arm it " +
            "was written for.");

        var result = SlayIdleRepeat.Core.GameRules.Apply(
            state, new StartRunCommand(Chapter, DifficultyTier.NORMAL), Worlds.Context);

        result.Accepted.ShouldBeTrue(
            "START_RUN on an ENDED run carrying an open draft was refused " + result.Rejection +
            ". ILLEGAL_STATE means the draft arm refused the exempted row for a second, unrelated " +
            "reason after the ended arm had already let it through — the arms are chained so that " +
            "cannot happen.");

        result.NewState.Run!.DraftPending.ShouldBeFalse(
            "the run that came back still has the ENDED run's draft open, so the ended run was " +
            "re-phased rather than replaced.");
    }

    /// <summary>
    /// …and the ended arm still answers first for every other row: an ended run with a draft open
    /// refuses <c>PICK_PERK</c> — which the draft arm would otherwise wave through — as
    /// <c>RUN_ALREADY_ENDED</c>.
    /// </summary>
    /// <remarks>
    /// The chain's ordering, pinned by the reason rather than by the refusal: <c>ILLEGAL_STATE</c>
    /// here would mean the draft arm had started answering ahead of the ended one, and acceptance
    /// would mean a finished run could still be drafted into.
    /// </remarks>
    [Fact]
    public void PICK_PERK_against_an_Ended_run_with_a_draft_still_open_is_RUN_ALREADY_ENDED()
    {
        var state = EndedRunWithADraftStillOpen();

        var result = SlayIdleRepeat.Core.GameRules.Apply(
            state, new PickPerkCommand(0), Worlds.Context);

        result.Accepted.ShouldBeFalse("a perk was drafted into a run that is over.");

        result.Rejection.ShouldBe(
            RejectionReason.RUN_ALREADY_ENDED,
            "PICK_PERK was refused " + result.Rejection + ". The ended arm answers first for every " +
            "row that does not open its own run, draft or no draft.");

        result.NewState.ShouldBeSameAs(state, "a rejection hands back the caller's own slice.");
    }

    // ------------------------------------------- every other run command is still refused outright

    /// <summary>
    /// The exemption is one row wide: every run command that is not <c>START_RUN</c> still gets
    /// <c>RUN_ALREADY_ENDED</c> on an ended run.
    /// </summary>
    [Theory]
    [MemberData(nameof(RunCommandsOtherThanStartRun))]
    public void A_run_command_other_than_START_RUN_is_still_refused_on_an_Ended_run(string commandName)
    {
        var state = EndedRun();

        var result = SlayIdleRepeat.Core.GameRules.Apply(state, Named(commandName), Worlds.Context);

        result.Accepted.ShouldBeFalse(
            commandName + " was accepted on an ENDED run. Only the row that opens its own run is " +
            "exempt from the ended-run gate.");

        result.Rejection.ShouldBe(
            RejectionReason.RUN_ALREADY_ENDED,
            commandName + " was refused " + result.Rejection + " rather than RUN_ALREADY_ENDED, so it " +
            "reached past the ended-run gate and was turned away by some later rule instead.");

        result.NewState.ShouldBeSameAs(state, "a rejection hands back the caller's own slice.");
    }

    /// <summary>
    /// The floor under the sweep above (steering S3): the seven names it drives are seven distinct,
    /// registered <c>CommandKind.Run</c> rows, and none of them is the exempt one.
    /// </summary>
    [Fact]
    public void The_swept_set_is_seven_registered_run_commands_and_none_of_them_opens_a_run()
    {
        SweptCommands.Length.ShouldBe(
            7, "a sweep that shrank would report success for the rows it stopped visiting.");

        SweptCommands.Distinct(StringComparer.Ordinal).Count().ShouldBe(
            SweptCommands.Length, "a duplicated name is one fewer command actually swept.");

        foreach (var commandName in SweptCommands)
        {
            var registration = SlayIdleRepeat.Core.GameRules.RegistrationFor(Named(commandName).GetType());

            registration.ShouldNotBeNull(commandName + " names no dispatch row at all.");
            registration!.Kind.ShouldBe(
                CommandKind.Run, commandName + " is not a run command, so the ended-run gate never sees it.");
            registration.OpensRun.ShouldBeFalse(
                commandName + " opens its own run, so it is the exempt row rather than a subject of " +
                "this sweep.");
        }
    }

    /// <summary>
    /// 🔒 <b>The door is one row wide, made mechanical.</b> Exactly one registered command is marked
    /// <c>OpensRun</c>, and it is <c>START_RUN</c>.
    /// </summary>
    /// <remarks>
    /// This is the whole scope of the exemption: a second row marked <c>OpensRun</c> would inherit
    /// the ended-run bypass without anybody deciding it should.
    /// </remarks>
    [Fact]
    public void Exactly_one_dispatch_row_opens_its_own_run()
    {
        var openers = SlayIdleRepeat.Core.GameRules.CommandTypesByWireName
            .Where(row => SlayIdleRepeat.Core.GameRules.RegistrationFor(row.Value)!.OpensRun)
            .OrderBy(row => row.Key, StringComparer.Ordinal)
            .ToArray();

        openers.Length.ShouldBe(
            1,
            "the rows marked OpensRun are [" + string.Join(", ", openers.Select(row => row.Key)) +
            "]. That flag is what exempts a row from BOTH the run-less loading defect and the " +
            "ended-run gate, so every row carrying it may discard an ended run and open a new one.");

        openers[0].Value.ShouldBe(
            typeof(StartRunCommand),
            "the one row that opens its own run is '" + openers[0].Key + "', not START_RUN.");
    }

    // ------------------------------------------------------------------ fixtures

    /// <summary>The chapter every START_RUN below names — the authored floor, and a legal one.</summary>
    private const int Chapter = 1;

    /// <summary>Below the authored floor of 1, which <c>StartRun.Handle</c> refuses on its own.</summary>
    private const int BelowTheChapterFloor = 0;

    /// <summary>An arbitrary but non-zero draw counter, so "the new run inherited it" is visible.</summary>
    private const ulong DrawsTaken = 12UL;

    private static readonly string[] SweptCommands =
    [
        nameof(RollDiceCommand),
        nameof(UseRerollCommand),
        nameof(ResolveTileCommand),
        nameof(StartBattleCommand),
        nameof(EndRunCommand),
        nameof(AbandonRunCommand),
        nameof(SkipDraftCommand),
    ];

    public static TheoryData<string> RunCommandsOtherThanStartRun()
    {
        var data = new TheoryData<string>();

        foreach (var commandName in SweptCommands)
        {
            data.Add(commandName);
        }

        return data;
    }

    /// <summary>
    /// An ended run that has drawn from a stream — see
    /// <see cref="START_RUN_against_an_Ended_run_opens_a_fresh_run"/> for why the counters matter.
    /// </summary>
    private static WorldSlice EndedRun() => Worlds.InARun(RunSnapshots.With(
        phase: RunPhase.Ended,
        rngStreamPositions: RunSnapshots.Streams((RngStreams.Dice, DrawsTaken))));

    /// <summary>
    /// The same ended run with a perk draft never resolved — the one flag that can legitimately still
    /// be set on a finished run, since it is orthogonal to <see cref="RunPhase"/>.
    /// </summary>
    private static WorldSlice EndedRunWithADraftStillOpen() => Worlds.InARun(RunSnapshots.With(
        phase: RunPhase.Ended,
        rngStreamPositions: RunSnapshots.Streams((RngStreams.Dice, DrawsTaken)),
        draftPending: true,
        draftBattleKind: (int)TileKind.Enemy,
        draftBattleStage: 1));

    private static SlayIdleRepeat.Core.Commands.GameCommand Named(string commandName) => commandName switch
    {
        nameof(RollDiceCommand) => new RollDiceCommand(),
        nameof(UseRerollCommand) => new UseRerollCommand(),
        nameof(ResolveTileCommand) => new ResolveTileCommand(),
        nameof(StartBattleCommand) => new StartBattleCommand(),
        nameof(EndRunCommand) => new EndRunCommand(),
        nameof(AbandonRunCommand) => new AbandonRunCommand(),
        nameof(SkipDraftCommand) => new SkipDraftCommand(),
        _ => throw new InvalidOperationException("unreachable"),
    };
}
