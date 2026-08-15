using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Board;
using Shouldly;
using Xunit;
using CoreBoard = SlayIdleRepeat.Core.Rules.Board.BoardGraph;

namespace SlayIdleRepeat.Core.Tests.Rules.Board;

/// <summary>
/// 🔒 `03` §3 — <see cref="BoardGenerator.GenerateBoard"/>: mandatory placements, the weighted
/// draw, constraints C1-C7 with their redraw/injection fallbacks, and the 1-2 forks per stage.
/// </summary>
public sealed class BoardGeneratorTests
{
    // A spread of seeds so structural checks (C1-C6) are exercised across many independent draws
    // rather than one lucky/unlucky sequence.
    private static readonly ulong[] Seeds =
    {
        1UL, 2UL, 3UL, 42UL, 1337UL, 99999UL, 0xC0FFEEUL, 0xDEADBEEFUL, 123456789UL, 987654321UL,
        1111UL, 2222UL, 3333UL, 4444UL, 5555UL, 6666UL, 7777UL, 8888UL, 9999UL, 10101UL,
    };

    // ------------------------------------------------------------------------------------
    // Determinism
    // ------------------------------------------------------------------------------------

    [Fact]
    public void The_same_seed_produces_a_byte_identical_board()
    {
        var config = BoardFixtures.ChapterOneConfig();

        var first = Describe(Generate(config, 555UL));
        var second = Describe(Generate(config, 555UL));

        second.ShouldBe(first);
    }

    [Fact]
    public void Different_seeds_usually_produce_different_boards()
    {
        var config = BoardFixtures.ChapterOneConfig();

        var a = Describe(Generate(config, 1UL));
        var b = Describe(Generate(config, 2UL));

        a.ShouldNotBe(b);
    }

    [Fact]
    public void A_null_config_is_rejected()
    {
        Should.Throw<ArgumentNullException>(() => Generate(null!, 1UL));
    }

    [Fact]
    public void A_null_rng_is_rejected()
    {
        var config = BoardFixtures.ChapterOneConfig();
        Should.Throw<ArgumentNullException>(() => BoardGenerator.GenerateBoard(config, null!));
    }

    // ------------------------------------------------------------------------------------
    // Linear node index scheme — `03` §1.1, the load-bearing rule for EnemyPower(i).
    // ------------------------------------------------------------------------------------

    [Fact]
    public void Chapter_ones_stage_lengths_produce_linear_indices_0_to_41_and_boss_at_42()
    {
        var config = BoardFixtures.ChapterOneConfig(); // 12 + 14 + 16 = 42
        var board = Generate(config, 42UL);

        board.Node(board.SpineNode(0)).LinearIndex.ShouldBe(0);
        board.Node(board.SpineNode(41)).LinearIndex.ShouldBe(41);
        board.Node(board.BossNodeId).LinearIndex.ShouldBe(42);
        board.Node(board.BossNodeId).Tile.ShouldBe(TileKind.Boss);
        Should.Throw<ArgumentOutOfRangeException>(() => board.SpineNode(43));
    }

    [Fact]
    public void A_branch_nodes_linear_index_equals_the_spine_nodes_index_at_the_same_forward_distance()
    {
        // 03 §1.1: the k-th branch node from junction j has i = i(j) + k, and the branch rejoins
        // at j + branchLen — so the parallel spine node always exists at that same index.
        var config = BoardFixtures.ChapterOneConfig();
        var foundAny = false;

        foreach (var seed in Seeds)
        {
            var board = Generate(config, seed);

            foreach (var linearIndex in Enumerable.Range(0, 42))
            {
                var spineId = board.SpineNode(linearIndex);
                if (!board.IsJunction(spineId))
                {
                    continue;
                }

                var edges = board.OutgoingEdges(spineId);
                edges.Count.ShouldBe(2, "03 §1.1: a junction has exactly two outgoing edges");
                edges[0].Kind.ShouldBe(EdgeKind.Continue);
                edges[1].Kind.ShouldBe(EdgeKind.Branch);

                var junctionIndex = board.Node(spineId).LinearIndex;
                var k = 0;
                var cursor = edges[1].To;

                while (true)
                {
                    var node = board.Node(cursor);
                    k++;
                    node.LinearIndex.ShouldBe(junctionIndex + k,
                        "the k-th branch node from junction j must have i = i(j) + k");

                    var next = board.OutgoingEdges(cursor).Single(e => e.Kind == EdgeKind.Continue);

                    // 03 §1.1: the branch's last node (k = branchLen) has i(j) + branchLen — the
                    // SAME linear index as the spine node it rejoins onto (the rejoin node IS
                    // "j + branchLen"). Detect the rejoin by that shared index rather than by
                    // walking further.
                    if (board.SpineNode(node.LinearIndex) == next.To)
                    {
                        break;
                    }

                    cursor = next.To;
                }

                foundAny = true;
            }
        }

        foundAny.ShouldBeTrue("at least one of the sampled seeds must have produced a junction to check");
    }

    // ------------------------------------------------------------------------------------
    // C6: stage 1's first node is always TILE_ENEMY.
    // ------------------------------------------------------------------------------------

    [Fact]
    public void C6_stage_one_first_node_is_always_enemy()
    {
        var config = BoardFixtures.ChapterOneConfig();

        foreach (var seed in Seeds)
        {
            var board = Generate(config, seed);
            board.Node(board.SpineNode(0)).Tile.ShouldBe(TileKind.Enemy, $"seed {seed}");
        }
    }

    // ------------------------------------------------------------------------------------
    // C1: no 3 identical non-ENEMY tiles in a row.
    // ------------------------------------------------------------------------------------

    [Fact]
    public void C1_no_three_identical_non_enemy_tiles_in_a_row_on_any_stage_spine()
    {
        var config = BoardFixtures.ChapterOneConfig();

        foreach (var seed in Seeds)
        {
            var board = Generate(config, seed);
            foreach (var stage in SpineTilesByStage(board, config))
            {
                for (var i = 2; i < stage.Length; i++)
                {
                    if (stage[i] == TileKind.Enemy)
                    {
                        continue;
                    }

                    (stage[i] == stage[i - 1] && stage[i] == stage[i - 2]).ShouldBeFalse(
                        $"seed {seed}: three identical {stage[i]} in a row at index {i}");
                }
            }
        }
    }

    // ------------------------------------------------------------------------------------
    // C2: no 4 consecutive TILE_ENEMY.
    // ------------------------------------------------------------------------------------

    [Fact]
    public void C2_no_four_consecutive_enemy_tiles_on_any_stage_spine()
    {
        var config = BoardFixtures.ChapterOneConfig();

        foreach (var seed in Seeds)
        {
            var board = Generate(config, seed);
            foreach (var stage in SpineTilesByStage(board, config))
            {
                for (var i = 3; i < stage.Length; i++)
                {
                    var run = stage[i] == TileKind.Enemy && stage[i - 1] == TileKind.Enemy
                              && stage[i - 2] == TileKind.Enemy && stage[i - 3] == TileKind.Enemy;
                    run.ShouldBeFalse($"seed {seed}: four consecutive ENEMY ending at index {i}");
                }
            }
        }
    }

    // ------------------------------------------------------------------------------------
    // C3: at least 1 healing opportunity (SHRINE/CAMPFIRE/SHOP) per 8-node window.
    // ------------------------------------------------------------------------------------

    [Fact]
    public void C3_every_eight_node_window_has_a_healing_opportunity()
    {
        var config = BoardFixtures.ChapterOneConfig();

        foreach (var seed in Seeds)
        {
            var board = Generate(config, seed);
            foreach (var stage in SpineTilesByStage(board, config))
            {
                for (var start = 0; start < stage.Length; start += 8)
                {
                    var end = Math.Min(start + 8, stage.Length);
                    var hasHealing = stage.Skip(start).Take(end - start)
                        .Any(t => t is TileKind.Shrine or TileKind.Campfire or TileKind.Shop);

                    hasHealing.ShouldBeTrue($"seed {seed}: window [{start},{end}) has no healing tile");
                }
            }
        }
    }

    // ------------------------------------------------------------------------------------
    // C4: TILE_PORTAL never in the last 4 nodes of a stage. Deterministic guard proof: a
    // weight table with ONLY Portal available forces the redraw to exhaust and fall back to
    // TILE_ENEMY inside the guarded zone (S1 — the guard must be provably able to fire).
    // ------------------------------------------------------------------------------------

    [Fact]
    public void C4_portal_never_appears_in_the_last_four_nodes_of_a_stage()
    {
        var config = BoardFixtures.ChapterOneConfig();

        foreach (var seed in Seeds)
        {
            var board = Generate(config, seed);
            foreach (var stage in SpineTilesByStage(board, config))
            {
                for (var i = stage.Length - 4; i < stage.Length; i++)
                {
                    stage[i].ShouldNotBe(TileKind.Portal, $"seed {seed}: portal at index {i} (last 4)");
                }
            }
        }
    }

    [Fact]
    public void C4_when_only_portal_is_weighted_the_last_four_nodes_never_end_up_as_portal()
    {
        // A table with ONLY Portal weighted means the weighted draw can never legitimately
        // produce anything else — so a non-Portal tile surviving in the last 4 nodes can only
        // come from the redraw-exhaustion fallback (03 §3: 8 redraws, then TILE_ENEMY), possibly
        // further rewritten by a later constraint's own injection (e.g. C3). Either way, the
        // guard this proves is C4 itself: it holds even when nothing else was ever going to be
        // drawn there.
        var onlyPortal = ChapterBoardConfig.From(
            chapterId: 77,
            stageLengths: new[] { 12, 12, 12 },
            eliteCount: new[] { 0, 0, 0 },
            tileWeights: new[]
            {
                new Dictionary<TileKind, double> { [TileKind.Portal] = 100.0 },
                new Dictionary<TileKind, double> { [TileKind.Portal] = 100.0 },
                new Dictionary<TileKind, double> { [TileKind.Portal] = 100.0 },
            },
            bossId: "BOSS_TEST");

        var board = Generate(onlyPortal, 1UL);

        foreach (var stage in SpineTilesByStage(board, onlyPortal))
        {
            for (var i = stage.Length - 4; i < stage.Length; i++)
            {
                stage[i].ShouldNotBe(TileKind.Portal, "03 §3 C4: portal never in the last 4 nodes of a stage");
            }
        }
    }

    // ------------------------------------------------------------------------------------
    // C5: TILE_CURSE never immediately before TILE_ELITE or TILE_BOSS.
    // ------------------------------------------------------------------------------------

    [Fact]
    public void C5_curse_never_immediately_precedes_elite_or_the_boss()
    {
        var config = BoardFixtures.ChapterOneConfig();

        foreach (var seed in Seeds)
        {
            var board = Generate(config, seed);
            var stages = SpineTilesByStage(board, config);

            for (var s = 0; s < stages.Length; s++)
            {
                var stage = stages[s];
                for (var i = 0; i < stage.Length - 1; i++)
                {
                    if (stage[i] == TileKind.Curse)
                    {
                        stage[i + 1].ShouldNotBe(TileKind.Elite, $"seed {seed} stage {s + 1} index {i}");
                    }
                }

                if (s == 2 && stage[^1] == TileKind.Curse)
                {
                    Assert.Fail($"seed {seed}: stage 3's last node is CURSE, immediately before the boss");
                }
            }
        }
    }

    // ------------------------------------------------------------------------------------
    // C7: >= 2 TILE_TREASURE and >= 1 TILE_CACHE across the whole run.
    // ------------------------------------------------------------------------------------

    [Fact]
    public void C7_every_run_has_at_least_two_treasure_and_one_cache()
    {
        var config = BoardFixtures.ChapterOneConfig();

        foreach (var seed in Seeds)
        {
            var board = Generate(config, seed);
            var stages = SpineTilesByStage(board, config);

            var treasureCount = stages.Sum(s => s.Count(t => t == TileKind.Treasure));
            var cacheCount = stages.Sum(s => s.Count(t => t == TileKind.Cache));

            treasureCount.ShouldBeGreaterThanOrEqualTo(2, $"seed {seed}");
            cacheCount.ShouldBeGreaterThanOrEqualTo(1, $"seed {seed}");
        }
    }

    [Fact]
    public void C7_injects_the_exact_minimum_when_the_weighted_draw_cannot_produce_treasure_or_cache()
    {
        // Zero-weight treasure/cache: the weighted draw can NEVER produce either kind, so any
        // that appear must come from C7's injection fallback, and the count must be exactly the
        // stated floor (S2 — pin the identity: injected, not coincidentally drawn, and exactly 2/1).
        var noTreasureNoCache = ChapterBoardConfig.From(
            chapterId: 78,
            stageLengths: new[] { 12, 12, 12 },
            eliteCount: new[] { 0, 0, 0 },
            tileWeights: new[]
            {
                Zeroed(),
                Zeroed(),
                Zeroed(),
            },
            bossId: "BOSS_TEST");

        var board = Generate(noTreasureNoCache, 1UL);
        var stages = SpineTilesByStage(board, noTreasureNoCache);

        stages.Sum(s => s.Count(t => t == TileKind.Treasure)).ShouldBe(2);
        stages.Sum(s => s.Count(t => t == TileKind.Cache)).ShouldBe(1);

        static Dictionary<TileKind, double> Zeroed() => new()
        {
            [TileKind.Enemy] = 34,
            [TileKind.Empty] = 12,
            [TileKind.Shrine] = 9,
            [TileKind.Treasure] = 0,
            [TileKind.Event] = 8,
            [TileKind.Minigame] = 7,
            [TileKind.Curse] = 6,
            [TileKind.Shop] = 5,
            [TileKind.Elite] = 4,
            [TileKind.Cache] = 0,
            [TileKind.Portal] = 2,
            [TileKind.DiceForge] = 2,
        };
    }

    // ------------------------------------------------------------------------------------
    // Forks.
    // ------------------------------------------------------------------------------------

    [Fact]
    public void Every_stage_has_one_or_two_forks()
    {
        var config = BoardFixtures.ChapterOneConfig();

        foreach (var seed in Seeds)
        {
            var board = Generate(config, seed);
            var stages = SpineTilesByStage(board, config);

            var linear = 0;
            for (var s = 0; s < stages.Length; s++)
            {
                var junctions = 0;
                for (var p = 0; p < stages[s].Length; p++)
                {
                    if (board.IsJunction(board.SpineNode(linear)))
                    {
                        junctions++;
                    }

                    linear++;
                }

                junctions.ShouldBeInRange(1, 2, $"seed {seed} stage {s + 1}");
            }
        }
    }

    [Fact]
    public void A_forks_branch_never_starts_on_the_shop_or_campfire_node()
    {
        var config = BoardFixtures.ChapterOneConfig();

        foreach (var seed in Seeds)
        {
            var board = Generate(config, seed);

            for (var linearIndex = 0; linearIndex < 42; linearIndex++)
            {
                var id = board.SpineNode(linearIndex);
                if (!board.IsJunction(id))
                {
                    continue;
                }

                board.Node(id).Tile.ShouldNotBe(TileKind.Shop, $"seed {seed}");
                board.Node(id).Tile.ShouldNotBe(TileKind.Campfire, $"seed {seed}");
            }
        }
    }

    [Fact]
    public void A_forks_preview_honestly_lists_up_to_three_of_the_branchs_real_tiles()
    {
        var config = BoardFixtures.ChapterOneConfig();
        var checkedAny = false;

        foreach (var seed in Seeds)
        {
            var board = Generate(config, seed);

            for (var linearIndex = 0; linearIndex < 42; linearIndex++)
            {
                var id = board.SpineNode(linearIndex);
                if (!board.IsJunction(id))
                {
                    continue;
                }

                var branchEdge = board.OutgoingEdges(id).Single(e => e.Kind == EdgeKind.Branch);
                branchEdge.Preview.ShouldNotBeNull();

                var actual = new List<TileKind>();
                var cursor = branchEdge.To;
                while (true)
                {
                    actual.Add(board.Node(cursor).Tile);
                    var outgoing = board.OutgoingEdges(cursor);
                    if (outgoing.Count == 0 || board.IsJunction(cursor))
                    {
                        break;
                    }

                    var next = outgoing.Single(e => e.Kind == EdgeKind.Continue);
                    if (board.SpineNode(board.Node(cursor).LinearIndex) == next.To)
                    {
                        break; // rejoined the spine
                    }

                    cursor = next.To;
                }

                branchEdge.Preview!.Icons.ShouldBe(actual.Take(3).ToArray());
                checkedAny = true;
            }
        }

        checkedAny.ShouldBeTrue();
    }

    // ------------------------------------------------------------------------------------
    // Helpers.
    // ------------------------------------------------------------------------------------

    // GenerateBoard takes an already-opened DeterministicRng, not a seed (30 §11's
    // DeterministicRng_is_constructed_only_inside_Core_Rng architecture rule forbids
    // constructing one inside Rules/Board — the sanctioned caller is RunRngScope). Tests
    // construct the stream directly, exactly like ChapterEnemyPoolTests does for RngStreams.Combat.
    private static CoreBoard Generate(ChapterBoardConfig config, ulong seed) =>
        BoardGenerator.GenerateBoard(config, new DeterministicRng(seed, RngStreams.Board));

    private static TileKind[][] SpineTilesByStage(CoreBoard board, ChapterBoardConfig config)
    {
        var result = new TileKind[3][];
        var linear = 0;

        for (var s = 0; s < 3; s++)
        {
            var length = config.StageLengths[s];
            result[s] = new TileKind[length];
            for (var p = 0; p < length; p++)
            {
                result[s][p] = board.Node(board.SpineNode(linear)).Tile;
                linear++;
            }
        }

        return result;
    }

    private static string Describe(CoreBoard board)
    {
        var lines = new List<string>();
        for (var i = 0; i <= 42; i++)
        {
            var id = board.SpineNode(i);
            var node = board.Node(id);
            lines.Add($"{i}:{node.Tile}:{board.IsJunction(id)}");
        }

        foreach (var i in Enumerable.Range(0, 42))
        {
            var id = board.SpineNode(i);
            if (!board.IsJunction(id))
            {
                continue;
            }

            var branch = board.OutgoingEdges(id).Single(e => e.Kind == EdgeKind.Branch);
            lines.Add($"fork@{i}:{branch.Preview!.Label}:{string.Join(",", branch.Preview.Icons)}");
        }

        return string.Join("|", lines);
    }
}
