using SlayIdleRepeat.Core.Rng;

namespace SlayIdleRepeat.Core.Rules.Board;

/// <summary>
/// The procedural producer of a run's <see cref="BoardGraph"/>: mandatory-tile placements, the
/// weighted draw and constraints C1-C7 (with their redraw/injection fallbacks), and the authored
/// number of forks per stage.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>
/// Each fork draws exactly one label, uniformly from the 4-label pool, biasing only its own
/// branch's weighted table — the spine continuation is always the unbiased "safe" path. A label's
/// bias multiplies whatever weight a kind already has in the stage's table, so a kind absent from
/// the table stays undrawable rather than getting a fabricated weight.
/// </item>
/// <item>
/// <see cref="DeterministicRng.Range"/> is exclusive of its upper bound, so every inclusive-looking
/// range below (fork count, branch length) adds 1 to the stated upper bound.
/// </item>
/// <item>
/// C3's "per 8 nodes" healing window is read as non-overlapping windows of 8 from the start of
/// each stage (last window may be shorter), repaired after the weighted fill.
/// </item>
/// <item>
/// A fork never overlaps another fork in the same stage (no shared spine index, branch shadow, or
/// rejoin node) — required to keep the graph a valid DAG.
/// </item>
/// <item>
/// C1/C2/C4/C5 are re-applied branch-locally during branch generation; C3 and C6 don't apply to a
/// short branch. C7's global counts include branches, but injection only ever replaces a spine
/// tile — injecting into a branch would make its preview dishonest after the branch already
/// committed to a label.
/// </item>
/// </list>
/// </remarks>
internal static class BoardGenerator
{
    private const int RedrawCap = 8;
    private const int HealingWindowSize = 8;
    private const int MinTreasureAcrossRun = 2;
    private const int MinCacheAcrossRun = 1;

    /// <summary>No index of a healing window may be overwritten — see <see cref="FindLatestReplaceable"/>.</summary>
    private const int NoReplaceableIndex = -1;

    private static readonly ForkLabel[] AllForkLabels =
    {
        ForkLabel.Perilous, ForkLabel.Sheltered, ForkLabel.Arcane, ForkLabel.Feral,
    };

    private static readonly IReadOnlyDictionary<ForkLabel, (TileKind[] Plus, TileKind? Minus)> ForkBiasTable =
        new Dictionary<ForkLabel, (TileKind[], TileKind?)>
        {
            [ForkLabel.Perilous] = (new[] { TileKind.Elite, TileKind.Enemy, TileKind.Treasure }, TileKind.Shrine),
            [ForkLabel.Sheltered] = (new[] { TileKind.Shrine, TileKind.Campfire, TileKind.Shop }, TileKind.Enemy),
            [ForkLabel.Arcane] = (new[] { TileKind.DiceForge, TileKind.Event, TileKind.Minigame }, null),
            [ForkLabel.Feral] = (new[] { TileKind.Cache, TileKind.Curse, TileKind.Elite }, null),
        };

    /// <summary>Generates a full 3-stage board plus boss node from a chapter's config and a run seed.</summary>
    /// <param name="config">The chapter's board-relevant content.</param>
    /// <param name="rng">
    /// The run's <c>board</c> stream, already opened by the caller via
    /// <c>RunRngScope.Stream(RngStreams.Board)</c> — never construct a <see cref="DeterministicRng"/>
    /// here directly, so a stream's counter has exactly one path it ever moves through.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="config"/> or <paramref name="rng"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// The chapter data cannot be laid out at all (e.g. more elites demanded than the stage has
    /// room for under C5's adjacency rule) — a content-authoring defect, not a runtime draw
    /// failure, so it is never swallowed into a fallback tile.
    /// </exception>
    internal static BoardGraph GenerateBoard(ChapterBoardConfig config, DeterministicRng rng)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(rng);

        var stageTiles = new TileKind[3][];
        for (var stageIndex = 0; stageIndex < 3; stageIndex++)
        {
            stageTiles[stageIndex] = GenerateStageSpine(config, stageIndex, rng);
        }

        ApplyRunWideTreasureAndCacheInjection(stageTiles);

        var builder = new GraphBuilder();

        // Materialize spine nodes across all 3 stages in one continuous linear-index run (0..N-1).
        var linearBase = new int[3];
        var runningBase = 0;
        for (var stageIndex = 0; stageIndex < 3; stageIndex++)
        {
            linearBase[stageIndex] = runningBase;
            runningBase += config.StageLengths[stageIndex];
        }

        var spineIds = new NodeId[runningBase + 1]; // +1 for the boss slot
        for (var stageIndex = 0; stageIndex < 3; stageIndex++)
        {
            var stageNumber = stageIndex + 1;
            for (var position = 0; position < config.StageLengths[stageIndex]; position++)
            {
                var linearIndex = linearBase[stageIndex] + position;
                spineIds[linearIndex] = builder.AddNode(stageTiles[stageIndex][position], linearIndex, stageNumber);
            }
        }

        var bossLinearIndex = runningBase;
        spineIds[bossLinearIndex] = builder.AddNode(TileKind.Boss, bossLinearIndex, BoardGraph.BossStage);

        for (var i = 0; i < bossLinearIndex; i++)
        {
            builder.AddEdge(spineIds[i], spineIds[i + 1], EdgeKind.Continue);
        }

        // Forks — Step 4. One (or two) per stage, each contributing exactly one new branch.
        for (var stageIndex = 0; stageIndex < 3; stageIndex++)
        {
            GenerateForksForStage(config, stageIndex, stageTiles[stageIndex], linearBase[stageIndex], spineIds, builder, rng);
        }

        return builder.Build(spineIds);
    }

    // ----------------------------------------------------------------------------------------
    // Step 1-3: one stage's spine.
    // ----------------------------------------------------------------------------------------

    private static TileKind[] GenerateStageSpine(ChapterBoardConfig config, int stageIndex, DeterministicRng rng)
    {
        var stageNumber = stageIndex + 1;
        var spineLength = config.StageLengths[stageIndex];
        var tiles = new TileKind?[spineLength];

        // C6: stage 1's first node is always TILE_ENEMY.
        if (stageNumber == 1)
        {
            tiles[0] = TileKind.Enemy;
        }

        // Step 1: mandatory tiles.
        var shopIndex = rng.Range(3, spineLength - 2); // [3 .. spineLength-3] inclusive
        tiles[shopIndex] = TileKind.Shop;

        if (stageNumber == 3)
        {
            tiles[spineLength - 2] = TileKind.Campfire; // always just before boss
        }
        else
        {
            // The stage gate's fight. Placed structurally like the shop and the campfire, so no draw
            // of the board stream is spent naming it, and placed on the stage's LAST node so the
            // existing stage-end clamp is what makes it unskippable — no movement rule of its own.
            tiles[spineLength - 1] = TileKind.MiniBoss;
        }

        PlaceElites(config.EliteCount[stageIndex], tiles, rng, config.ChapterId, stageNumber);

        // Step 2 + 3 (C1,C2,C4,C5): weighted fill of every remaining index.
        var table = ToWeightTable(config.TileWeights[stageIndex]);
        var isStage3 = stageNumber == 3;

        // Every stage after the first opens on the node immediately AFTER the previous stage's gate,
        // and the two stand in different stage arrays — so the mini-boss adjacency rule cannot be
        // read off this stage's own tiles at position 0 and is carried in instead.
        var followsAMiniBoss = stageNumber > 1;

        for (var position = 0; position < spineLength; position++)
        {
            if (tiles[position] is not null)
            {
                continue;
            }

            tiles[position] = DrawConstrained(
                rng, table, tiles, position, spineLength, isStage3, followsAMiniBoss);
        }

        var resolved = tiles.Select(t => t!.Value).ToArray();

        // C3: at least 1 healing opportunity (SHRINE/CAMPFIRE/SHOP) per 8 nodes.
        ApplyHealingWindowInjection(resolved, stageNumber);

        return resolved;
    }

    private static void PlaceElites(int eliteCount, TileKind?[] tiles, DeterministicRng rng, int chapterId, int stageNumber)
    {
        var placed = new List<int>(eliteCount);

        for (var n = 0; n < eliteCount; n++)
        {
            var candidates = new List<int>();
            for (var index = 2; index < tiles.Length; index++)
            {
                if (tiles[index] is not null)
                {
                    continue;
                }

                if (placed.Any(p => Math.Abs(p - index) <= 1))
                {
                    continue;
                }

                if (IsBesideAMiniBoss(tiles, index))
                {
                    continue;
                }

                candidates.Add(index);
            }

            if (candidates.Count == 0)
            {
                throw new InvalidOperationException(
                    $"chapter {chapterId} stage {stageNumber}: no index left that satisfies 03 §3's elite " +
                    $"placement rule (not in the first 2 nodes, not adjacent to another elite) for elite " +
                    $"{(n + 1)} of {eliteCount}.");
            }

            var pick = candidates[rng.Range(0, candidates.Count)];
            tiles[pick] = TileKind.Elite;
            placed.Add(pick);
        }
    }

    /// <summary>
    /// Whether placing <paramref name="candidate"/> at <paramref name="position"/> would leave three
    /// of it standing in a row, counting the two neighbours on either side.
    /// </summary>
    private static bool WouldCompleteATriple(TileKind candidate, TileKind?[] tiles, int position)
    {
        for (var start = position - 2; start <= position; start++)
        {
            if (start >= 0 &&
                start + 2 < tiles.Length &&
                IsOrIsAt(candidate, tiles, start, position) &&
                IsOrIsAt(candidate, tiles, start + 1, position) &&
                IsOrIsAt(candidate, tiles, start + 2, position))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether <paramref name="index"/> holds <paramref name="candidate"/>, or is where it is about to go.</summary>
    private static bool IsOrIsAt(TileKind candidate, TileKind?[] tiles, int index, int position) =>
        index == position || tiles[index] == candidate;

    /// <summary>
    /// Whether <paramref name="position"/> stands immediately before or after a mini-boss node.
    /// </summary>
    /// <remarks>
    /// Two elite fights on adjacent steps is the spike the stage gate is meant to be; a mini-boss is
    /// already an elite, so an elite beside it doubles the gate rather than leading up to it.
    /// </remarks>
    private static bool IsBesideAMiniBoss(TileKind?[] tiles, int position) =>
        (position > 0 && tiles[position - 1] == TileKind.MiniBoss) ||
        (position + 1 < tiles.Length && tiles[position + 1] == TileKind.MiniBoss);

    /// <summary>
    /// One redraw-guarded weighted pick for a single spine (or branch) position. Position-local
    /// constraints only: C1, C2, C4, C5. Redraws up to <see cref="RedrawCap"/> times; on
    /// exhaustion falls back to <see cref="TileKind.Enemy"/>.
    /// </summary>
    private static TileKind DrawConstrained(
        DeterministicRng rng,
        IReadOnlyList<(TileKind item, double weight)> table,
        TileKind?[] tiles,
        int position,
        int length,
        bool nextIsBoss,
        bool firstNodeFollowsAMiniBoss = false)
    {
        for (var attempt = 0; attempt < RedrawCap; attempt++)
        {
            var candidate = rng.WeightedPick(table);
            if (!ViolatesPositionalConstraints(
                    candidate, tiles, position, length, nextIsBoss, firstNodeFollowsAMiniBoss))
            {
                return candidate;
            }
        }

        return TileKind.Enemy;
    }

    private static bool ViolatesPositionalConstraints(
        TileKind candidate,
        TileKind?[] tiles,
        int position,
        int length,
        bool nextIsBoss,
        bool firstNodeFollowsAMiniBoss)
    {
        // C1: no 3 identical non-ENEMY tiles in a row. Read in BOTH directions, not only backwards:
        // Step 1's mandatory tiles are already standing ahead of the fill, so a backwards-only check
        // lets a draw slot into the gap between two of them and complete a triple nothing rejected.
        if (candidate != TileKind.Enemy && WouldCompleteATriple(candidate, tiles, position))
        {
            return true;
        }

        // C2: no 4 consecutive TILE_ENEMY.
        if (candidate == TileKind.Enemy
            && position >= 3
            && tiles[position - 1] == TileKind.Enemy
            && tiles[position - 2] == TileKind.Enemy
            && tiles[position - 3] == TileKind.Enemy)
        {
            return true;
        }

        // C4: TILE_PORTAL never in the last 4 nodes of a stage.
        if (candidate == TileKind.Portal && position >= length - 4)
        {
            return true;
        }

        // C5: TILE_CURSE never immediately before TILE_ELITE or TILE_BOSS — and a mini-boss is a
        // boss-tier fight standing where a stage ends, so it is spared for the same reason.
        if (candidate == TileKind.Curse)
        {
            if (position + 1 < length && tiles[position + 1] is TileKind.Elite or TileKind.MiniBoss)
            {
                return true;
            }

            if (nextIsBoss && position == length - 1)
            {
                return true;
            }
        }

        // C5's mirror image: an already-placed CURSE immediately before a just-drawn ELITE.
        if (candidate == TileKind.Elite && position >= 1 && tiles[position - 1] == TileKind.Curse)
        {
            return true;
        }

        // The weighted draw can reach an ELITE too, so the adjacency rule PlaceElites applies to the
        // mandatory elites has to hold here as well or the fill re-creates what Step 1 avoided.
        if (candidate == TileKind.Elite &&
            (IsBesideAMiniBoss(tiles, position) || (firstNodeFollowsAMiniBoss && position == 0)))
        {
            return true;
        }

        return false;
    }

    private static void ApplyHealingWindowInjection(TileKind[] tiles, int stageNumber)
    {
        for (var windowStart = 0; windowStart < tiles.Length; windowStart += HealingWindowSize)
        {
            var windowEnd = Math.Min(windowStart + HealingWindowSize, tiles.Length);

            var hasHealing = false;
            for (var i = windowStart; i < windowEnd; i++)
            {
                if (IsHealing(tiles[i]))
                {
                    hasHealing = true;
                    break;
                }
            }

            if (hasHealing)
            {
                continue;
            }

            var target = FindLatestReplaceable(tiles, windowStart, windowEnd, stageNumber);

            if (target == NoReplaceableIndex)
            {
                // The window holds nothing but the stage's own gate. Leaving one window short of a
                // healing tile costs the run a shrine; taking the gate costs it the stage's
                // structure, so the window is left alone rather than repaired at that price.
                continue;
            }

            tiles[target] = TileKind.Shrine;
        }
    }

    private static bool IsHealing(TileKind tile) =>
        tile is TileKind.Shrine or TileKind.Campfire or TileKind.Shop;

    /// <summary>
    /// Latest Empty in range, else latest Enemy in range, else the range's latest index holding
    /// anything but a mini-boss — never index 0 of stage 1 (C6), and
    /// <see cref="NoReplaceableIndex"/> when the range offers nothing else at all.
    /// </summary>
    /// <remarks>
    /// The last resort overwrites whatever stands there, so it has to step over the mini-boss: a
    /// stage that healed itself by replacing its own gate would end the run's structure, not soften
    /// its difficulty. A window can consist of the gate alone — a stage whose length leaves one node
    /// over after the whole windows — and then there is no replaceable index in it at all, which is
    /// why this answers a sentinel instead of falling back on the range's own floor.
    /// </remarks>
    private static int FindLatestReplaceable(TileKind[] tiles, int start, int end, int stageNumber)
    {
        var floor = Math.Max(start, stageNumber == 1 ? 1 : 0);

        for (var i = end - 1; i >= floor; i--)
        {
            if (tiles[i] == TileKind.Empty)
            {
                return i;
            }
        }

        for (var i = end - 1; i >= floor; i--)
        {
            if (tiles[i] == TileKind.Enemy)
            {
                return i;
            }
        }

        for (var i = end - 1; i >= floor; i--)
        {
            if (tiles[i] != TileKind.MiniBoss)
            {
                return i;
            }
        }

        return NoReplaceableIndex;
    }

    // ----------------------------------------------------------------------------------------
    // C7 — run-wide treasure/cache floor.
    // ----------------------------------------------------------------------------------------

    private static void ApplyRunWideTreasureAndCacheInjection(TileKind[][] stageTiles)
    {
        var treasureCount = stageTiles.Sum(stage => stage.Count(t => t == TileKind.Treasure));
        var cacheCount = stageTiles.Sum(stage => stage.Count(t => t == TileKind.Cache));

        var consumed = new HashSet<(int stage, int index)>();

        for (var i = treasureCount; i < MinTreasureAcrossRun; i++)
        {
            InjectRunWide(stageTiles, consumed, TileKind.Treasure);
        }

        for (var i = cacheCount; i < MinCacheAcrossRun; i++)
        {
            InjectRunWide(stageTiles, consumed, TileKind.Cache);
        }
    }

    /// <summary>Injects one tile at the latest available index across the whole run (stage 3 → 1, last index → first), preferring TILE_EMPTY over TILE_ENEMY, per C7.</summary>
    private static void InjectRunWide(TileKind[][] stageTiles, HashSet<(int stage, int index)> consumed, TileKind inject)
    {
        if (TryInjectByPreferredKind(stageTiles, consumed, inject, TileKind.Empty))
        {
            return;
        }

        if (TryInjectByPreferredKind(stageTiles, consumed, inject, TileKind.Enemy))
        {
            return;
        }

        throw new InvalidOperationException(
            $"03 §3 C7 needs to inject a {TileKindIds.ToId(inject)}, but no TILE_EMPTY or TILE_ENEMY tile " +
            "remains anywhere on the board to replace. This chapter's tileWeights cannot satisfy C7.");
    }

    private static bool TryInjectByPreferredKind(
        TileKind[][] stageTiles, HashSet<(int stage, int index)> consumed, TileKind inject, TileKind replace)
    {
        for (var stage = 2; stage >= 0; stage--)
        {
            var tiles = stageTiles[stage];
            var guardStageOneOpener = stage == 0 ? 1 : 0;

            for (var index = tiles.Length - 1; index >= guardStageOneOpener; index--)
            {
                if (tiles[index] != replace || consumed.Contains((stage, index)))
                {
                    continue;
                }

                tiles[index] = inject;
                consumed.Add((stage, index));
                return true;
            }
        }

        return false;
    }

    // ----------------------------------------------------------------------------------------
    // Step 4 — forks.
    // ----------------------------------------------------------------------------------------

    private static void GenerateForksForStage(
        ChapterBoardConfig config,
        int stageIndex,
        TileKind[] stageSpine,
        int stageLinearBase,
        NodeId[] spineIds,
        GraphBuilder builder,
        DeterministicRng rng)
    {
        var spineLength = stageSpine.Length;
        var lastRejoinLocal = LastRejoinLocalIndex(stageSpine);
        // One draw, in the same stream position the hardcoded 1-2 occupied, so moving the number
        // into content changes what is drawn and not when. Exclusive-max Range needs +1, see type doc.
        var forks = config.ForksPerStage[stageIndex];
        var forkCount = rng.Range(forks.Minimum, forks.Maximum + 1);

        var usedSpan = new List<(int start, int endExclusive)>(); // occupied local-index ranges (junction..rejoin]

        for (var f = 0; f < forkCount; f++)
        {
            var candidates = new List<int>();
            for (var localIndex = 4; localIndex <= spineLength - 4; localIndex++)
            {
                if (stageSpine[localIndex] is TileKind.Shop or TileKind.Campfire)
                {
                    continue;
                }

                if (usedSpan.Any(span => localIndex >= span.start && localIndex < span.endExclusive))
                {
                    continue;
                }

                var maxBranchLen = Math.Min(4, lastRejoinLocal - localIndex);
                if (maxBranchLen < 2)
                {
                    continue; // rejoin would fall off the spine — not a viable junction here.
                }

                candidates.Add(localIndex);
            }

            if (candidates.Count == 0)
            {
                // No room left for another fork this stage — an authored fork count is a range,
                // not a floor this generator can force past what the stage geometry allows. Content
                // can now name a number larger than any stage could hold, and this is where that
                // saturates instead of corrupting the layout.
                break;
            }

            var junctionLocal = candidates[rng.Range(0, candidates.Count)];
            var maxBranchLenAtJunction = Math.Min(4, lastRejoinLocal - junctionLocal);
            var branchLen = rng.Range(2, maxBranchLenAtJunction + 1); // "2-4 nodes" — see type doc.

            usedSpan.Add((junctionLocal, junctionLocal + branchLen + 1));

            var label = AllForkLabels[rng.Range(0, AllForkLabels.Length)];
            var branchTable = ToWeightTable(BuildBranchWeights(config.TileWeights[stageIndex], label, config));

            var branchTiles = new TileKind?[branchLen];
            for (var k = 0; k < branchLen; k++)
            {
                branchTiles[k] = DrawConstrained(rng, branchTable, branchTiles, k, branchLen, nextIsBoss: false);
            }

            var resolvedBranch = branchTiles.Select(t => t!.Value).ToArray();

            var junctionLinearIndex = stageLinearBase + junctionLocal;
            var junctionId = spineIds[junctionLinearIndex];

            var branchIds = new NodeId[branchLen];
            for (var k = 0; k < branchLen; k++)
            {
                branchIds[k] = builder.AddNode(resolvedBranch[k], junctionLinearIndex + k + 1, stageIndex + 1);
            }

            var preview = new ForkPreview(label, resolvedBranch.Take(3).ToArray());
            builder.AddEdge(junctionId, branchIds[0], EdgeKind.Branch, preview);
            builder.MarkJunction(junctionId);

            for (var k = 0; k < branchLen - 1; k++)
            {
                builder.AddEdge(branchIds[k], branchIds[k + 1], EdgeKind.Continue);
            }

            var rejoinLinearIndex = stageLinearBase + junctionLocal + branchLen;
            builder.AddEdge(branchIds[^1], spineIds[rejoinLinearIndex], EdgeKind.Continue);
        }
    }

    /// <summary>
    /// The latest local index a branch may rejoin the spine on.
    /// </summary>
    /// <remarks>
    /// 🔒 A branch's last node is built at the rejoin index, so it stands at the same step of the
    /// track as the spine node it rejoins onto. Where that step holds a mini-boss, the branch is a
    /// way to stand on the gate's step without meeting it — so a stage carrying one keeps its rejoins
    /// one node earlier. Always satisfiable: the earliest junction is local index 4, the latest is
    /// <c>length − 4</c>, and the shortest branch is 2, so <c>length − 2</c> is reachable from every
    /// junction the candidate scan offers.
    /// </remarks>
    private static int LastRejoinLocalIndex(TileKind[] stageSpine) =>
        stageSpine[^1] == TileKind.MiniBoss ? stageSpine.Length - 2 : stageSpine.Length - 1;

    private static IReadOnlyDictionary<TileKind, double> BuildBranchWeights(
        IReadOnlyDictionary<TileKind, double> baseWeights, ForkLabel label, ChapterBoardConfig config)
    {
        var (plus, minus) = ForkBiasTable[label];
        var biased = new Dictionary<TileKind, double>(baseWeights);

        foreach (var kind in plus)
        {
            if (biased.TryGetValue(kind, out var w))
            {
                biased[kind] = w * config.ForkBiasPlusMultiplier;
            }
        }

        if (minus is { } minusKind && biased.TryGetValue(minusKind, out var mw))
        {
            biased[minusKind] = mw * config.ForkBiasMinusMultiplier;
        }

        return biased;
    }

    private static IReadOnlyList<(TileKind item, double weight)> ToWeightTable(IReadOnlyDictionary<TileKind, double> weights) =>
        weights.Select(kv => (kv.Key, kv.Value)).ToArray();

    // ----------------------------------------------------------------------------------------
    // Graph assembly.
    // ----------------------------------------------------------------------------------------

    private sealed class GraphBuilder
    {
        private readonly List<BoardNode> _nodes = new();
        private readonly List<BoardEdge> _edges = new();
        private readonly HashSet<NodeId> _junctions = new();
        private int _nextId;

        public NodeId AddNode(TileKind tile, int linearIndex, int stage)
        {
            var id = new NodeId(_nextId++);
            _nodes.Add(new BoardNode(id, tile, linearIndex, stage));
            return id;
        }

        public void AddEdge(NodeId from, NodeId to, EdgeKind kind, ForkPreview? preview = null) =>
            _edges.Add(new BoardEdge(from, to, kind, preview));

        public void MarkJunction(NodeId id) => _junctions.Add(id);

        public BoardGraph Build(NodeId[] spineIds) =>
            BoardGraph.FromLayout(_nodes, _edges, spineIds, _junctions);
    }
}
