using Shouldly;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Board;
using Xunit;
using CoreBoard = SlayIdleRepeat.Core.Rules.Board.BoardGraph;

namespace SlayIdleRepeat.Core.Tests.Rules.Board;

/// <summary>
/// 🔒 `16` D69 — <b>how many forks a stage carries is authored, not a generator constant.</b>
/// <see cref="BoardGenerator"/> used to draw <c>1..2</c> from a literal, so a stage of fifty nodes
/// carried the same one or two forks a stage of twelve did and a long board read as a straight line.
/// </summary>
/// <remarks>
/// The cases below are the ones content can now reach and could not before: a stage with no fork at
/// all, and a stage asked for more forks than its geometry can hold. Neither was expressible while
/// the number lived in the generator, so neither had a test.
/// </remarks>
public sealed class ForkDensityTests
{
    /// <summary>A spread of seeds, so a structural claim rests on many draws rather than one lucky one.</summary>
    private static readonly ulong[] Seeds =
    {
        1UL, 2UL, 3UL, 42UL, 1337UL, 99999UL, 0xC0FFEEUL, 0xDEADBEEFUL, 123456789UL, 987654321UL,
    };

    /// <summary>
    /// A stage authored zero forks generates none, and the board is still a walkable spine — the
    /// defect this catches is a generator that treats the authored minimum as advisory and places
    /// one anyway, which is what a `Math.Max(1, min)` would do.
    /// </summary>
    [Fact]
    public void A_stage_authored_no_forks_generates_none_and_still_walks_end_to_end()
    {
        var config = ConfigWith(new ForkCountRange(0, 0), new ForkCountRange(0, 0), new ForkCountRange(0, 0));

        foreach (var seed in Seeds)
        {
            var board = Generate(config, seed);

            JunctionCount(board).ShouldBe(
                0,
                "seed " + seed + ": every stage authored zero forks, so the board carries none.");

            WalkForward(board).ShouldBe(
                board.SpineLength,
                "seed " + seed + ": with no fork, walking single edges from the first node must " +
                "reach every linear index and stop on the boss.");
        }
    }

    /// <summary>
    /// A stage authored more forks than its spine can hold saturates and stops, rather than
    /// overlapping two branches or running a rejoin off the end of the stage.
    /// </summary>
    /// <remarks>
    /// 🔒 This is the arm the authored range opened. The generator's candidate window is a stage's
    /// local indices 4..length-4, and a placed fork reserves its whole junction-to-rejoin span, so a
    /// twelve-node stage has room for very few. Asking for twenty proves the loop breaks on an empty
    /// candidate list instead of forcing a placement.
    /// </remarks>
    [Fact]
    public void A_stage_authored_more_forks_than_it_can_hold_saturates_instead_of_overlapping()
    {
        var greedy = new ForkCountRange(20, 20);
        var config = ConfigWith(greedy, greedy, greedy);

        foreach (var seed in Seeds)
        {
            var board = Generate(config, seed);
            var junctions = JunctionCount(board);

            junctions.ShouldBeGreaterThan(
                0, "seed " + seed + ": a stage with room for at least one fork must still place one.");

            junctions.ShouldBeLessThan(
                60,
                "seed " + seed + ": three stages asked for twenty forks each; the geometry cannot " +
                "hold sixty, so the generator must have stopped short rather than forced them in.");

            foreach (var junction in Junctions(board))
            {
                var edges = board.OutgoingEdges(junction);

                edges.Count.ShouldBe(2, "seed " + seed + ": 03 §1.1 — a junction has exactly two edges.");
                edges[0].Kind.ShouldBe(EdgeKind.Continue);
                edges[1].Kind.ShouldBe(EdgeKind.Branch);
                edges[1].Preview.ShouldNotBeNull(
                    "seed " + seed + ": a saturating stage must not emit a branch without a preview.");
            }
        }
    }

    /// <summary>
    /// A denser authored range really does produce more forks. Without this, every case above would
    /// pass against a generator that read the field and then ignored it.
    /// </summary>
    /// <remarks>
    /// Stated as a total across the seed spread rather than per seed, because the count is a draw:
    /// an individual seed may hand a wide range the same number a narrow one drew.
    /// </remarks>
    [Fact]
    public void A_denser_authored_range_produces_more_forks_across_the_seeds()
    {
        var sparse = ConfigWith(new ForkCountRange(1, 1), new ForkCountRange(1, 1), new ForkCountRange(1, 1));
        var dense = ConfigWith(new ForkCountRange(3, 3), new ForkCountRange(3, 3), new ForkCountRange(3, 3));

        var sparseTotal = Seeds.Sum(seed => JunctionCount(Generate(sparse, seed)));
        var denseTotal = Seeds.Sum(seed => JunctionCount(Generate(dense, seed)));

        sparseTotal.ShouldBe(
            3 * Seeds.Length,
            "one fork per stage on three stages, on every seed — the exact figure, so a generator " +
            "that quietly clamped the count would not slip through a 'greater than zero'.");

        denseTotal.ShouldBeGreaterThan(
            sparseTotal,
            "the authored range is read, not decoration: asking for three forks a stage must put " +
            "more junctions on the board than asking for one.");
    }

    /// <summary>
    /// 🔒 A range that spans more than one value really spans it: with <c>1..4</c> authored, the
    /// seeds together place more forks than <c>1..1</c> does.
    /// </summary>
    /// <remarks>
    /// The only case here where the authored minimum and maximum differ, and it exists because they
    /// did not: every other case states <c>min == max</c>, so a generator that read the minimum and
    /// ignored the maximum — <c>rng.Range(min, min + 1)</c> — satisfied all of them. Found by
    /// mutation, and this is the case that now goes red on it.
    /// <para>
    /// Totalled across the seeds rather than asserted per seed, because the count is a draw and a
    /// single seed may hand a wide range the same number a narrow one drew. Stage geometry also
    /// caps how many of the drawn forks fit, so the claim is "more", never a figure.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_range_that_spans_several_values_places_more_forks_than_its_minimum_alone()
    {
        var atMinimum = ConfigWith(new ForkCountRange(1, 1), new ForkCountRange(1, 1), new ForkCountRange(1, 1));
        var spanning = ConfigWith(new ForkCountRange(1, 4), new ForkCountRange(1, 4), new ForkCountRange(1, 4));

        var atMinimumTotal = Seeds.Sum(seed => JunctionCount(Generate(atMinimum, seed)));
        var spanningTotal = Seeds.Sum(seed => JunctionCount(Generate(spanning, seed)));

        spanningTotal.ShouldBeGreaterThan(
            atMinimumTotal,
            "a range of 1..4 that placed exactly as many forks as 1..1 means the maximum is never " +
            "read — the draw collapsed onto the minimum and content can only ever author a floor.");
    }

    /// <summary>
    /// The same seed and the same authored range still produce a byte-identical board — moving the
    /// number into content must not have moved the draw out of the board stream.
    /// </summary>
    [Fact]
    public void The_same_seed_and_the_same_authored_range_produce_an_identical_board()
    {
        var config = ConfigWith(new ForkCountRange(1, 3), new ForkCountRange(1, 3), new ForkCountRange(1, 3));

        Describe(Generate(config, 555UL)).ShouldBe(Describe(Generate(config, 555UL)));
    }

    /// <summary>
    /// The discriminating negative for the case above: two configs differing ONLY in
    /// <c>forksPerStage</c> generate different boards from the same seed.
    /// </summary>
    [Fact]
    public void A_config_differing_only_in_its_fork_range_generates_a_different_board()
    {
        var sparse = ConfigWith(new ForkCountRange(1, 1), new ForkCountRange(1, 1), new ForkCountRange(1, 1));
        var dense = ConfigWith(new ForkCountRange(3, 3), new ForkCountRange(3, 3), new ForkCountRange(3, 3));

        Describe(Generate(dense, 555UL)).ShouldNotBe(
            Describe(Generate(sparse, 555UL)),
            "if these matched, every case in this file would be measuring a field nothing reads.");
    }

    [Fact]
    public void A_fork_range_whose_maximum_is_below_its_minimum_is_refused_by_field_name()
    {
        var ex = Should.Throw<ArgumentException>(() => ConfigWith(
            new ForkCountRange(3, 1), new ForkCountRange(1, 2), new ForkCountRange(1, 2)));

        ex.ParamName.ShouldBe("forksPerStage");
        ex.Message.ShouldContain("stage 1", Case.Sensitive, "the refusal has to say WHICH stage is malformed.");
    }

    [Fact]
    public void A_negative_fork_minimum_is_refused_by_field_name()
    {
        var ex = Should.Throw<ArgumentException>(() => ConfigWith(
            new ForkCountRange(1, 2), new ForkCountRange(-1, 2), new ForkCountRange(1, 2)));

        ex.ParamName.ShouldBe("forksPerStage");
        ex.Message.ShouldContain("stage 2", Case.Sensitive);
    }

    [Fact]
    public void A_fork_range_array_of_the_wrong_length_is_refused_by_field_name()
    {
        var ex = Should.Throw<ArgumentException>(() => ChapterBoardConfig.From(
            chapterId: 9,
            stageLengths: new[] { 12, 12, 12 },
            eliteCount: new[] { 1, 1, 1 },
            tileWeights: new[] { BoardFixtures.DefaultWeights(), BoardFixtures.DefaultWeights(), BoardFixtures.DefaultWeights() },
            bossId: "BOSS_TEST",
            forksPerStage: new[] { new ForkCountRange(1, 2), new ForkCountRange(1, 2) }));

        ex.ParamName.ShouldBe("forksPerStage");
        ex.Message.ShouldContain("3 entries", Case.Sensitive);
    }

    /// <summary>
    /// The negative control for the three refusals above: a well-formed range is accepted, so they
    /// cannot be passing against a factory that rejects every fork range it is handed.
    /// </summary>
    [Fact]
    public void A_well_formed_fork_range_is_accepted()
    {
        var config = ConfigWith(new ForkCountRange(0, 0), new ForkCountRange(1, 2), new ForkCountRange(2, 5));

        config.ForksPerStage.ShouldBe(new[]
        {
            new ForkCountRange(0, 0), new ForkCountRange(1, 2), new ForkCountRange(2, 5),
        });
    }

    /// <summary>
    /// A caller that states no fork range gets `03` §1's authored one-or-two, which is what keeps
    /// the fixtures across this suite generating the boards they generated before D69.
    /// </summary>
    [Fact]
    public void A_config_that_states_no_fork_range_carries_the_authored_one_or_two()
    {
        BoardFixtures.TinyConfig().ForksPerStage.ShouldBe(new[]
        {
            ForkCountRange.Authored, ForkCountRange.Authored, ForkCountRange.Authored,
        });

        ForkCountRange.Authored.ShouldBe(new ForkCountRange(1, 2));
    }

    // ----------------------------------------------------------------------------------------
    // Fixtures.
    // ----------------------------------------------------------------------------------------

    /// <summary>The tiny three-stage config, varied only in its fork density.</summary>
    private static ChapterBoardConfig ConfigWith(params ForkCountRange[] forksPerStage) =>
        ChapterBoardConfig.From(
            chapterId: 9,
            stageLengths: new[] { 12, 12, 12 },
            eliteCount: new[] { 1, 1, 1 },
            tileWeights: new[] { BoardFixtures.DefaultWeights(), BoardFixtures.DefaultWeights(), BoardFixtures.DefaultWeights() },
            bossId: "BOSS_TEST",
            forksPerStage: forksPerStage);

    private static CoreBoard Generate(ChapterBoardConfig config, ulong seed) =>
        BoardGenerator.GenerateBoard(config, new DeterministicRng(seed, RngStreams.Board));

    private static IEnumerable<NodeId> Junctions(CoreBoard board)
    {
        for (var linearIndex = 0; linearIndex < board.SpineLength; linearIndex++)
        {
            var id = board.SpineNode(linearIndex);

            if (board.IsJunction(id))
            {
                yield return id;
            }
        }
    }

    private static int JunctionCount(CoreBoard board) => Junctions(board).Count();

    /// <summary>How many nodes a single-edge walk from the first node visits before it dead-ends.</summary>
    private static int WalkForward(CoreBoard board)
    {
        var cursor = board.FirstNodeId;
        var visited = 1;

        while (board.OutgoingEdges(cursor) is { Count: 1 } edges)
        {
            cursor = edges[0].To;
            visited++;
        }

        return visited;
    }

    /// <summary>A board as text, so two boards can be compared as one value.</summary>
    private static string Describe(CoreBoard board)
    {
        var lines = new List<string>();

        for (var linearIndex = 0; linearIndex < board.SpineLength; linearIndex++)
        {
            var id = board.SpineNode(linearIndex);
            lines.Add($"{linearIndex}:{board.Node(id).Tile}:{board.IsJunction(id)}");

            foreach (var edge in board.OutgoingEdges(id))
            {
                lines.Add($"  {edge.Kind}->{edge.To}:{board.Node(edge.To).Tile}");
            }
        }

        return string.Join('\n', lines);
    }
}
