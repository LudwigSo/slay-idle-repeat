using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Board;
using Shouldly;
using Xunit;
using CoreBoard = SlayIdleRepeat.Core.Rules.Board.BoardGraph;

namespace SlayIdleRepeat.Core.Tests.Rules.Board;

/// <summary>
/// The two mini-boss nodes <see cref="BoardGenerator.GenerateBoard"/> places: where they sit, that
/// no fork branch stands on one, and the two placement constraints they impose on their neighbours.
/// </summary>
public sealed class MiniBossPlacementTests
{
    /// <summary>A spread of seeds, so every claim below is about the arithmetic, not one draw.</summary>
    public static IEnumerable<object[]> Seeds() => new[]
    {
        1UL, 2UL, 3UL, 42UL, 1337UL, 99999UL, 0xC0FFEEUL, 0xDEADBEEFUL, 123456789UL, 987654321UL,
        1111UL, 2222UL, 3333UL, 4444UL, 5555UL, 6666UL, 7777UL, 8888UL, 9999UL, 10101UL,
    }.Select(seed => new object[] { seed });

    [Theory]
    [MemberData(nameof(Seeds))]
    public void GenerateBoard_puts_a_MiniBoss_on_the_last_spine_node_of_stage_1_and_of_stage_2(ulong seed)
    {
        var config = BoardFixtures.ChapterOneConfig();

        var board = Generate(config, seed);

        MiniBossLinearIndices(board, config).ShouldBe(
            new[] { StageEndLinearIndex(config, 0), StageEndLinearIndex(config, 1) },
            "a run carries exactly two mini-bosses, on the last spine node of stage 1 and of stage 2.");
        SpineTile(board, StageEndLinearIndex(config, 2)).ShouldNotBe(
            TileKind.MiniBoss,
            "stage 3's last node keeps whatever the generator drew for it — the boss node is that " +
            "stage's terminus, so a third mini-boss would stand one step in front of the boss.");
    }

    /// <summary>
    /// The positions are read off the chapter's own stage lengths. Chapter 1's 12/14/16 puts them at
    /// 11 and 25, and this chapter's 13/11/15 must put them at 12 and 23 instead — a generator
    /// carrying 11 and 25 as constants fails on both halves of this case.
    /// </summary>
    [Theory]
    [MemberData(nameof(Seeds))]
    public void GenerateBoard_derives_the_MiniBoss_positions_from_the_chapters_own_stage_lengths(ulong seed)
    {
        var config = ChapterBoardConfig.From(
            chapterId: 41,
            stageLengths: new[] { 13, 11, 15 },
            eliteCount: new[] { 1, 1, 1 },
            tileWeights: new[]
            {
                BoardFixtures.DefaultWeights(),
                BoardFixtures.DefaultWeights(),
                BoardFixtures.DefaultWeights(),
            },
            bossId: "BOSS_TEST");

        var board = Generate(config, seed);

        MiniBossLinearIndices(board, config).ShouldBe(new[] { 12, 23 });
        SpineTile(board, 11).ShouldNotBe(TileKind.MiniBoss, "11 is mid-stage-1 for a 13-node first stage.");
        SpineTile(board, 25).ShouldNotBe(TileKind.MiniBoss, "25 is mid-stage-3 here, not a stage end.");
    }

    /// <summary>
    /// No fork branch node carries a mini-boss node's linear index. A branch's last node shares its
    /// index with the spine node it rejoins onto, and fork geometry lets that rejoin land on a
    /// stage's last node (junction at <c>len-4</c>, branch length 3), so this is a real constraint
    /// rather than one the existing arithmetic already delivers.
    /// </summary>
    [Theory]
    [MemberData(nameof(Seeds))]
    public void No_fork_branch_node_stands_on_a_MiniBoss_nodes_linear_index(ulong seed)
    {
        var config = BoardFixtures.ChapterOneConfig();
        var board = Generate(config, seed);
        var miniBossIndices = MiniBossLinearIndices(board, config);

        miniBossIndices.Count.ShouldBe(2, "the claim is vacuous on a board with no mini-boss.");

        BranchNodes(board, config)
            .Where(node => miniBossIndices.Contains(node.LinearIndex))
            .Select(node => node.Id + "@" + node.LinearIndex)
            .ShouldBeEmpty("a branch node at a mini-boss's index is a second tile at that step of " +
                           "the track, so the branch is a way to stand where the mini-boss stands " +
                           "without meeting it.");
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void No_elite_is_placed_next_to_a_MiniBoss_node(ulong seed)
    {
        var config = BoardFixtures.ChapterOneConfig();
        var board = Generate(config, seed);
        var miniBossIndices = MiniBossLinearIndices(board, config);

        miniBossIndices.Count.ShouldBe(2, "the claim is vacuous on a board with no mini-boss.");

        NeighbourTiles(board, config, miniBossIndices)
            .Where(entry => entry.Tile == TileKind.Elite)
            .Select(entry => "index " + entry.LinearIndex)
            .ShouldBeEmpty("an elite beside a mini-boss stacks two elite fights on adjacent steps.");
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void No_curse_is_placed_immediately_before_a_MiniBoss_node(ulong seed)
    {
        var config = BoardFixtures.ChapterOneConfig();
        var board = Generate(config, seed);
        var miniBossIndices = MiniBossLinearIndices(board, config);

        miniBossIndices.Count.ShouldBe(2, "the claim is vacuous on a board with no mini-boss.");

        miniBossIndices
            .Where(index => index >= 1 && SpineTile(board, index - 1) == TileKind.Curse)
            .Select(index => "curse at index " + (index - 1))
            .ShouldBeEmpty("the curse rule that spares an elite and the boss spares a mini-boss too.");
    }

    // ---------------------------------------------------------------- fixtures and readers

    // GenerateBoard takes an already-opened stream, not a seed: DeterministicRng may only be
    // constructed inside Core.Rng, so the stream is built here exactly as BoardGeneratorTests does.
    private static CoreBoard Generate(ChapterBoardConfig config, ulong seed) =>
        BoardGenerator.GenerateBoard(config, new DeterministicRng(seed, RngStreams.Board));

    private static int StageEndLinearIndex(ChapterBoardConfig config, int stageIndex) =>
        config.StageLengths.Take(stageIndex + 1).Sum() - 1;

    private static int SpineLength(ChapterBoardConfig config) => config.StageLengths.Sum();

    private static TileKind SpineTile(CoreBoard board, int linearIndex) =>
        board.Node(board.SpineNode(linearIndex)).Tile;

    private static IReadOnlyList<int> MiniBossLinearIndices(CoreBoard board, ChapterBoardConfig config) =>
        Enumerable.Range(0, SpineLength(config))
                  .Where(index => SpineTile(board, index) == TileKind.MiniBoss)
                  .ToArray();

    /// <summary>The spine tiles immediately before and after each of the given linear indices.</summary>
    private static IReadOnlyList<(int LinearIndex, TileKind Tile)> NeighbourTiles(
        CoreBoard board, ChapterBoardConfig config, IReadOnlyList<int> around)
    {
        var last = SpineLength(config) - 1;

        return around
            .SelectMany(index => new[] { index - 1, index + 1 })
            .Where(index => index >= 0 && index <= last)
            .Select(index => (index, SpineTile(board, index)))
            .ToArray();
    }

    /// <summary>
    /// Every node reachable from the trailhead that is not the spine node at its own linear index —
    /// that is, every fork-branch node. Walked rather than read off the id space, so the claim does
    /// not depend on how the builder numbers nodes.
    /// </summary>
    private static IReadOnlyList<BoardNode> BranchNodes(CoreBoard board, ChapterBoardConfig config)
    {
        var spine = Enumerable.Range(0, SpineLength(config) + 1)
                              .Select(board.SpineNode)
                              .ToHashSet();

        var seen = new HashSet<NodeId>();
        var pending = new Queue<NodeId>();
        var branch = new List<BoardNode>();

        pending.Enqueue(board.FirstNodeId);

        while (pending.Count > 0)
        {
            var id = pending.Dequeue();

            if (!seen.Add(id))
            {
                continue;
            }

            if (!spine.Contains(id))
            {
                branch.Add(board.Node(id));
            }

            foreach (var edge in board.OutgoingEdges(id))
            {
                pending.Enqueue(edge.To);
            }
        }

        return branch;
    }
}
