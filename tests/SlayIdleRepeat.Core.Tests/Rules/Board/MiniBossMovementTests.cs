using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Board;
using Shouldly;
using Xunit;
using CoreBoard = SlayIdleRepeat.Core.Rules.Board.BoardGraph;

namespace SlayIdleRepeat.Core.Tests.Rules.Board;

/// <summary>
/// A mini-boss node cannot be skipped or overshot. Stated over <b>generated</b> boards rather than
/// hand-authored ones, because the claim is the conjunction of two halves that live apart: the
/// generator putting the mini-boss on a stage's last node, and the movement engine stopping there.
/// A hand-authored board would restate the clamp cases <see cref="MovementEngineTests"/> already
/// pins and prove nothing about where the mini-boss actually sits.
/// </summary>
public sealed class MiniBossMovementTests
{
    private const ulong Seed = 20260821UL;

    private static readonly ulong[] PortalSeeds =
    {
        1UL, 2UL, 3UL, 42UL, 1337UL, 99999UL, 0xC0FFEEUL, 0xDEADBEEFUL, 123456789UL, 987654321UL,
        1111UL, 2222UL, 3333UL, 4444UL, 5555UL, 6666UL, 7777UL, 8888UL, 9999UL, 10101UL,
    };

    /// <summary>Every roll that overshoots from two tiles out. A roll of 2 lands exactly and is not a clamp.</summary>
    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void A_roll_that_would_carry_past_the_MiniBoss_stops_on_it(int roll)
    {
        var config = BoardFixtures.ChapterOneConfig();
        var board = Generate(config, Seed);
        var miniBossIndex = StageEndLinearIndex(config, 0);

        SpineTile(board, miniBossIndex).ShouldBe(
            TileKind.MiniBoss, "the premise of every case in this file: stage 1 ends on the mini-boss.");

        var result = MovementEngine.Advance(board, board.SpineNode(miniBossIndex - 2), roll);

        result.Node.ShouldBe(
            board.SpineNode(miniBossIndex),
            "two tiles out, a roll of " + roll + " still moves exactly 2 — the mini-boss is a stop.");
        result.RemainingSteps.ShouldBe(roll - 2, "the steps the stop still owes.");
        result.ReachedBoss.ShouldBeFalse("a mini-boss is a fight, not the run's terminus.");
        result.PausedAtJunction.ShouldBeFalse("no fork stands two tiles before a stage's last node.");
    }

    /// <summary>
    /// The stop is a one-time stop, not a wall. This is the case a naive mini-boss clamp fails: a
    /// clamp that fires whenever the mini-boss is anywhere in the move leaves a run standing on it
    /// unable to ever leave, and every case above still passes.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void A_move_that_begins_on_the_MiniBoss_leaves_it(int steps)
    {
        var config = BoardFixtures.ChapterOneConfig();
        var board = Generate(config, Seed);
        var miniBossIndex = StageEndLinearIndex(config, 0);

        SpineTile(board, miniBossIndex).ShouldBe(TileKind.MiniBoss, "the premise.");

        var result = MovementEngine.Advance(board, board.SpineNode(miniBossIndex), steps);

        board.Node(result.Node).LinearIndex.ShouldBe(
            miniBossIndex + steps,
            "the move spends every step it was given; a landing back on the mini-boss is the wall.");
        board.Node(result.Node).Stage.ShouldBe(2, "the identity of the landing, not merely that it moved.");
        result.RemainingSteps.ShouldBe(0);
        result.ReachedBoss.ShouldBeFalse();
    }

    /// <summary>
    /// A Portal jump cannot carry past a mini-boss either. Aggregated over the whole seed spread in
    /// one case rather than one case per seed, because the two floors — that the mini-bosses exist
    /// at all, and that some Portal was actually jumped from — are what stop the claim being
    /// vacuous, and both are properties of the spread.
    /// </summary>
    [Fact]
    public void A_Portal_jump_never_carries_past_a_MiniBoss_node()
    {
        var config = BoardFixtures.ChapterOneConfig();

        var boards = PortalSeeds.Select(seed => Generate(config, seed)).ToArray();

        boards.Select(board => MiniBossCount(board, config)).Distinct().ShouldBe(
            new[] { 2 }, "every board in the spread carries two mini-bosses, or the claim is vacuous.");

        var jumps = boards.Select(board => PortalJumps(board, config)).ToArray();

        jumps.Sum(jump => jump.Examined).ShouldBeGreaterThan(
            0, "no Portal was jumped from anywhere in the spread, so nothing was measured.");
        jumps.SelectMany(jump => jump.Violations).ShouldBeEmpty(
            "a Portal that overshoots a mini-boss is a way to skip it, which is the one thing the " +
            "node may not allow.");
    }

    /// <summary>
    /// The boss-exact rule reads the <em>tile</em> of the node a stage-crossing step would land on,
    /// so a mini-boss placed across a boundary is the shape that rule would misfire on. Not a
    /// generated layout — this is the guard against a mini-boss being added to that rule's arm,
    /// which would end the run on the first mini-boss instead of opening a fight.
    /// </summary>
    [Fact]
    public void Stepping_onto_a_MiniBoss_across_a_stage_boundary_announces_no_boss()
    {
        var stageOneLast = new BoardNode(new NodeId(0), TileKind.Enemy, 0, 1);
        var miniBoss = new BoardNode(new NodeId(1), TileKind.MiniBoss, 1, 2);
        var stageTwo = new BoardNode(new NodeId(2), TileKind.Enemy, 2, 2);
        var boss = new BoardNode(new NodeId(3), TileKind.Boss, 3, CoreBoard.BossStage);

        var board = CoreBoard.FromLayout(
            new[] { stageOneLast, miniBoss, stageTwo, boss },
            new[]
            {
                new BoardEdge(stageOneLast.Id, miniBoss.Id, EdgeKind.Continue),
                new BoardEdge(miniBoss.Id, stageTwo.Id, EdgeKind.Continue),
                new BoardEdge(stageTwo.Id, boss.Id, EdgeKind.Continue),
            },
            new[] { stageOneLast.Id, miniBoss.Id, stageTwo.Id, boss.Id },
            Array.Empty<NodeId>());

        var result = MovementEngine.Advance(board, stageOneLast.Id, 1);

        result.Node.ShouldBe(miniBoss.Id);
        result.ReachedBoss.ShouldBeFalse(
            "the boss-exact rule is the boss's alone. A mini-boss answering it would report the " +
            "run's terminus at a node the run is meant to fight through and carry on from.");
    }

    // ---------------------------------------------------------------- fixtures and readers

    private static CoreBoard Generate(ChapterBoardConfig config, ulong seed) =>
        BoardGenerator.GenerateBoard(config, new DeterministicRng(seed, RngStreams.Board));

    private static int StageEndLinearIndex(ChapterBoardConfig config, int stageIndex) =>
        config.StageLengths.Take(stageIndex + 1).Sum() - 1;

    private static TileKind SpineTile(CoreBoard board, int linearIndex) =>
        board.Node(board.SpineNode(linearIndex)).Tile;

    private static int MiniBossCount(CoreBoard board, ChapterBoardConfig config) =>
        Enumerable.Range(0, config.StageLengths.Sum())
                  .Count(index => SpineTile(board, index) == TileKind.MiniBoss);

    /// <summary>
    /// Every Portal jump a run could take in stage 1 or 2, and the ones that ended past that
    /// stage's mini-boss. Stage 3 is excluded: it holds no mini-boss, and its own pre-boss campfire
    /// clamp is already pinned by <see cref="MovementEngineTests"/>.
    /// </summary>
    private static (int Examined, IReadOnlyList<string> Violations) PortalJumps(
        CoreBoard board, ChapterBoardConfig config)
    {
        var examined = 0;
        var violations = new List<string>();

        for (var stageIndex = 0; stageIndex < 2; stageIndex++)
        {
            var stageStart = config.StageLengths.Take(stageIndex).Sum();
            var miniBossIndex = StageEndLinearIndex(config, stageIndex);

            for (var index = stageStart; index < miniBossIndex; index++)
            {
                if (SpineTile(board, index) != TileKind.Portal)
                {
                    continue;
                }

                for (var distance = 3; distance <= 6; distance++)
                {
                    examined++;

                    var landed = MovementEngine.AdvancePortal(board, board.SpineNode(index), distance);
                    var landedIndex = board.Node(landed.Node).LinearIndex;

                    if (landedIndex > miniBossIndex)
                    {
                        violations.Add(
                            "portal@" + index + " distance " + distance + " landed at " + landedIndex +
                            ", past the mini-boss at " + miniBossIndex);
                    }
                }
            }
        }

        return (examined, violations);
    }
}
