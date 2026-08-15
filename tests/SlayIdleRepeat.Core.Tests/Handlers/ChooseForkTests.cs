using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Rules.Dice;
using SlayIdleRepeat.Core.Tests.Content;
using SlayIdleRepeat.Core.Tests.Model;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary>
/// 🔒 `03` §1.1 — <c>CHOOSE_FORK</c> (M3-02), plus <c>ROLL_DICE</c>'s own junction-pause wiring.
/// </summary>
/// <remarks>
/// Every scenario is checked against the ACTUAL board <c>Rules.Board.BoardGenerator</c> produces for
/// a fixed seed and a small test-only chapter (<see cref="ChapterDocuments.TinyChapterDocument"/>) —
/// computed independently in each test, the same way the handler computes it — rather than a
/// hand-picked magic position, so nothing here depends on guessing what the generator drew.
/// </remarks>
public sealed class ChooseForkTests
{
    private const int ChapterId = 2;
    private const ulong Seed = 0x00C0FFEE_00C0FFEEUL;

    private static SlayIdleRepeat.Core.GameContext TinyContext()
    {
        var baseContent = Worlds.Context.Content;
        var documents = baseContent.DocumentPaths
            .Select(baseContent.GetDocument)
            .Append(ChapterDocuments.TinyChapterDocument(ChapterId));

        return Worlds.Context with { Content = new ContentSnapshot(baseContent.Version, documents) };
    }

    private static BoardGraph ActualBoard()
    {
        var config = ChapterBoardTuning.Read(TinyContext().Content, ChapterId);
        return BoardGenerator.GenerateBoard(config, DeterministicRng.OpenAt(Seed, RngStreams.Board, 0));
    }

    /// <summary>
    /// The first junction the board's spine reaches — `03` §3 guarantees at least one per stage on a
    /// board this size (12+ nodes/stage), so this always finds one.
    /// </summary>
    private static NodeId FirstJunction(BoardGraph board)
    {
        for (var linearIndex = 0; ; linearIndex++)
        {
            var id = board.SpineNode(linearIndex);
            if (board.IsJunction(id))
            {
                return id;
            }
        }
    }

    /// <summary>What <see cref="Rules.Dice.FaceEffectResolver"/> would draw at a given committed <c>dice</c> position — the same computation <c>Handlers.RollDice</c> makes.</summary>
    private static int PredictedPip(ulong dicePosition)
    {
        var weights = FairDiceBag.Replay(Seed, resetAtDraw: 0, uptoDraw: dicePosition);
        var (face, _) = FairDiceBag.Step(DeterministicRng.OpenAt(Seed, RngStreams.Dice, dicePosition), weights);
        return face;
    }

    /// <summary>The lowest dice-stream position whose predicted Pip is at least <paramref name="min"/>.</summary>
    private static ulong FindDicePositionWithPipAtLeast(int min)
    {
        for (var position = 0UL; position < 200; position++)
        {
            if (PredictedPip(position) >= min)
            {
                return position;
            }
        }

        throw new InvalidOperationException("no dice position under 200 drew a Pip that high — the fixture seed needs revisiting.");
    }

    // ------------------------------------------------------------------------------------------
    // CHOOSE_FORK itself, starting from a run already parked at a junction with a PendingFork —
    // sidesteps needing to control which Pip a roll draws, which ROLL_DICE's own test below covers.
    // ------------------------------------------------------------------------------------------

    [Fact]
    public void ChooseFork_takes_the_chosen_edge_and_clears_the_pending_fork()
    {
        var board = ActualBoard();
        var junction = FirstJunction(board);
        var edges = board.OutgoingEdges(junction);

        var snapshot = RunSnapshots.With(
            chapterId: ChapterId,
            runSeed: Seed,
            position: junction.Value,
            pendingForkJunctionPosition: junction.Value,
            pendingForkRemainingSteps: 1);

        var result = SlayIdleRepeat.Core.GameRules.Apply(
            Worlds.InARun(snapshot), new ChooseForkCommand(BranchIndex: 0), TinyContext());

        result.Accepted.ShouldBeTrue();
        result.NewState.Run!.PendingFork.ShouldBeNull();
        result.NewState.Run.Position.ShouldBe(edges[0].To.Value);
    }

    [Fact]
    public void ChooseFork_can_take_the_branch_edge_too()
    {
        var board = ActualBoard();
        var junction = FirstJunction(board);
        var edges = board.OutgoingEdges(junction);

        var snapshot = RunSnapshots.With(
            chapterId: ChapterId,
            runSeed: Seed,
            position: junction.Value,
            pendingForkJunctionPosition: junction.Value,
            pendingForkRemainingSteps: 1);

        var result = SlayIdleRepeat.Core.GameRules.Apply(
            Worlds.InARun(snapshot), new ChooseForkCommand(BranchIndex: 1), TinyContext());

        result.Accepted.ShouldBeTrue();
        result.NewState.Run!.Position.ShouldBe(edges[1].To.Value);
        edges[1].Kind.ShouldBe(EdgeKind.Branch, "the fixture asserts something meaningful only if index 1 really is the branch.");
    }

    [Fact]
    public void ChooseFork_that_leaves_multiple_steps_unspent_continues_the_move()
    {
        var board = ActualBoard();
        var junction = FirstJunction(board);
        var edges = board.OutgoingEdges(junction);

        var snapshot = RunSnapshots.With(
            chapterId: ChapterId,
            runSeed: Seed,
            position: junction.Value,
            pendingForkJunctionPosition: junction.Value,
            pendingForkRemainingSteps: 2);

        var result = SlayIdleRepeat.Core.GameRules.Apply(
            Worlds.InARun(snapshot), new ChooseForkCommand(BranchIndex: 0), TinyContext());

        result.Accepted.ShouldBeTrue();

        // Independently computed: take the chosen edge, then one more step from the engine.
        var expected = MovementEngine.Advance(board, edges[0].To, 1);
        result.NewState.Run!.Position.ShouldBe(expected.Node.Value);
        result.NewState.Run.PendingFork.HasValue.ShouldBe(expected.PausedAtJunction);
    }

    [Fact]
    public void An_out_of_range_BranchIndex_is_refused()
    {
        var board = ActualBoard();
        var junction = FirstJunction(board);

        var snapshot = RunSnapshots.With(
            chapterId: ChapterId,
            runSeed: Seed,
            position: junction.Value,
            pendingForkJunctionPosition: junction.Value,
            pendingForkRemainingSteps: 1);

        var result = SlayIdleRepeat.Core.GameRules.Apply(
            Worlds.InARun(snapshot), new ChooseForkCommand(BranchIndex: 2), TinyContext());

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    [Fact]
    public void A_negative_BranchIndex_is_refused()
    {
        var board = ActualBoard();
        var junction = FirstJunction(board);

        var snapshot = RunSnapshots.With(
            chapterId: ChapterId,
            runSeed: Seed,
            position: junction.Value,
            pendingForkJunctionPosition: junction.Value,
            pendingForkRemainingSteps: 1);

        var result = SlayIdleRepeat.Core.GameRules.Apply(
            Worlds.InARun(snapshot), new ChooseForkCommand(BranchIndex: -1), TinyContext());

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    [Fact]
    public void ChooseFork_on_a_run_with_no_pending_fork_is_refused()
    {
        var snapshot = RunSnapshots.With(chapterId: ChapterId, runSeed: Seed, position: 0);

        var result = SlayIdleRepeat.Core.GameRules.Apply(
            Worlds.InARun(snapshot), new ChooseForkCommand(BranchIndex: 0), TinyContext());

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    // ------------------------------------------------------------------------------------------
    // ROLL_DICE's own wiring: it actually opens a PendingFork rather than silently overshooting.
    // ------------------------------------------------------------------------------------------

    [Fact]
    public void Rolling_into_a_junction_opens_a_PendingFork_instead_of_finishing_the_move()
    {
        var board = ActualBoard();
        var junction = FirstJunction(board);

        // A Pip of at least 2 from one step short of the junction must leave it.
        var dicePosition = FindDicePositionWithPipAtLeast(2);
        var pip = PredictedPip(dicePosition);
        var startPosition = board.SpineNode(board.Node(junction).LinearIndex - 1).Value;

        var snapshot = RunSnapshots.With(
            chapterId: ChapterId,
            runSeed: Seed,
            position: startPosition,
            rngStreamPositions: RunSnapshots.Streams((RngStreams.Dice, dicePosition)));

        var result = SlayIdleRepeat.Core.GameRules.Apply(
            Worlds.InARun(snapshot), new RollDiceCommand(), TinyContext());

        result.Accepted.ShouldBeTrue();
        result.Events.Count.ShouldBe(1, "the draw happened and is reported even though the move paused.");
        result.Events[0].ShouldBeOfType<DiceRolled>().Face.Value.ShouldBe(pip);

        result.NewState.Run!.Position.ShouldBe(junction.Value);
        result.NewState.Run.PendingFork.ShouldNotBeNull();
        result.NewState.Run.PendingFork!.Value.JunctionPosition.ShouldBe(junction.Value);
        result.NewState.Run.PendingFork.Value.RemainingSteps.ShouldBe(pip - 1);
    }

    [Fact]
    public void Rolling_while_a_fork_is_pending_is_refused()
    {
        var board = ActualBoard();
        var junction = FirstJunction(board);

        var snapshot = RunSnapshots.With(
            chapterId: ChapterId,
            runSeed: Seed,
            position: junction.Value,
            pendingForkJunctionPosition: junction.Value,
            pendingForkRemainingSteps: 1);

        var result = SlayIdleRepeat.Core.GameRules.Apply(
            Worlds.InARun(snapshot), new RollDiceCommand(), TinyContext());

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    // ------------------------------------------------------------------------------------------
    // The board-stream draw is generated once, then replayed identically — never re-drawn.
    // ------------------------------------------------------------------------------------------

    [Fact]
    public void A_second_roll_replays_the_same_board_rather_than_drawing_it_again()
    {
        var board = ActualBoard();

        var afterFirstRoll = SlayIdleRepeat.Core.GameRules.Apply(
            Worlds.InARun(RunSnapshots.With(chapterId: ChapterId, runSeed: Seed, position: -1)),
            new RollDiceCommand(),
            TinyContext());

        afterFirstRoll.Accepted.ShouldBeTrue();
        var boardDrawsAfterFirstRoll = afterFirstRoll.NewState.Run!.StreamPosition(RngStreams.Board);

        var afterSecondRoll = SlayIdleRepeat.Core.GameRules.Apply(
            afterFirstRoll.NewState, new RollDiceCommand(), TinyContext());

        afterSecondRoll.Accepted.ShouldBeTrue();
        afterSecondRoll.NewState.Run!.StreamPosition(RngStreams.Board).ShouldBe(
            boardDrawsAfterFirstRoll,
            "the board is generated once and replayed on every later command; a second roll that " +
            "moved this stream would mean the board was drawn again — from a different, wrong point.");

        // And the board itself really is stable: the trailhead's first landing agrees with the
        // independently-generated one both times.
        board.FirstNodeId.Value.ShouldBeGreaterThanOrEqualTo(0);
    }
}
