using System.Globalization;
using System.Text;
using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Testing;
using SlayIdleRepeat.Core.Tests.BalanceHarness;
using SlayIdleRepeat.Core.Tests.Model;
using Xunit;
using CoreBoard = SlayIdleRepeat.Core.Rules.Board.BoardGraph;
using RunAggregate = SlayIdleRepeat.Core.Model.Run;

namespace SlayIdleRepeat.Core.Tests.Rules.Board;

/// <summary>
/// <c>BoardView</c> — the narrow public projection of a run's board, and the only thing outside
/// <c>Core</c> that can see a tile track or a fork preview at all.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>The load-bearing claim is not "a board exists" — it is that this is the SAME board the run
/// is played on</b> (steering S2). The board is never persisted: it regenerates from
/// <c>RunSeed</c> on every command, and the view replays it the same way. A view that generated a
/// second, plausible board would satisfy every structural case in this file and would draw a track
/// the run is not standing on, so
/// <see cref="The_view_names_the_tile_the_run_actually_resolved_at_every_landing_of_a_command_driven_run"/>
/// pins the view against what the run's own pending-tile state reports, landing by landing.
/// </para>
/// <para>
/// ⚠️ <b>Collections are never compared by record equality</b> (steering S17). A synthesized record
/// <c>Equals</c> compares <c>IReadOnlyList&lt;TileKind&gt;</c> by REFERENCE, so two entirely
/// unrelated views would compare unequal on their icons and equal on nothing — and a projection that
/// returned the same cached instance twice would compare equal for a reason that says nothing about
/// its contents. <see cref="Canonical"/> is the comparison, and
/// <see cref="Two_different_run_seeds_project_to_different_bytes"/> is the negative control proving
/// it can see a difference at all.
/// </para>
/// <para>
/// ⚠️ Internals are reachable here under `30` §11.3's one <c>InternalsVisibleTo</c> grant, so
/// <see cref="Oracle"/> generates the real <see cref="CoreBoard"/> and is used as the oracle. That
/// is the point: the view must agree with the producer, and the producer is what the handlers move
/// the run across.
/// </para>
/// </remarks>
public sealed class BoardViewTests
{
    /// <summary>The chapter every case runs, and the one the shipped content authors first.</summary>
    private const int Chapter = 1;

    /// <summary>The fixed run seed the single-board cases use. Named so a failure re-runs exactly.</summary>
    private const ulong FixedSeed = 0x00C0FFEE_00C0FFEEUL;

    /// <summary>
    /// The seeds every swept case walks — twenty, fixed, shared with <c>BoardGeneratorTests</c> so a
    /// board that misbehaves here can be looked at there.
    /// </summary>
    private static readonly ulong[] Seeds =
    {
        1UL, 2UL, 3UL, 42UL, 1337UL, 99999UL, 0xC0FFEEUL, 0xDEADBEEFUL, 123456789UL, 987654321UL,
        1111UL, 2222UL, 3333UL, 4444UL, 5555UL, 6666UL, 7777UL, 8888UL, 9999UL, 10101UL,
    };

    /// <summary>The harness seed for the command-driven walk.</summary>
    private const ulong HarnessSeed = 0xB0A2D_0000_2222UL;

    /// <summary>The instant the command-driven walk starts. Fixed: the run seed folds it in.</summary>
    private static readonly DateTimeOffset Start = new(2026, 8, 12, 5, 0, 0, TimeSpan.Zero);

    /// <summary>The most commands the walk may send, so a defect cannot hang the suite.</summary>
    private const int CommandBudget = 600;

    /// <summary>
    /// How many distinct landings the walk must check before it counts as evidence (steering S3).
    /// </summary>
    /// <remarks>
    /// Measured on this checkout: the walk travels from the trailhead to the boss node and checks
    /// <b>13</b> landings. Floored well under that, on
    /// <c>StageBoundaryTraversalTests.MustCross</c>'s precedent — a hero can still die and one board
    /// changing shape is not a failure — and far enough over zero that a run which stopped on its
    /// first tile cannot satisfy it.
    /// </remarks>
    private const int MustCheckLandings = 10;

    /// <summary>
    /// How many distinct stages the checked landings must span, so the agreement is not a claim
    /// about one corner of one stage (steering S3).
    /// </summary>
    /// <remarks>
    /// Measured: the walk spans stages 1, 2, 3 and the boss stage — four. Floored at two, which is
    /// what makes "the walk crossed a Stage Gate" part of the claim without turning a hero's death
    /// in stage 3 into a failure of this case.
    /// </remarks>
    private const int MustSpanStages = 2;

    /// <summary>
    /// How many of the checked landings must be OFF the spine, inside a fork branch (steering S2).
    /// </summary>
    /// <remarks>
    /// 🔒 <b>Without this floor the walk proves the projection only where it cannot be wrong.</b> On
    /// a spine node the node id and the linear index are interchangeable, so a projection that
    /// resolved <c>Position</c> as a track offset agrees with the run at every spine landing and
    /// disagrees at every branch one — which is precisely the mistake
    /// <see cref="A_run_standing_inside_a_branch_resolves_to_the_branch_node_not_the_spine_node"/>
    /// describes and the reason <see cref="NextCommand"/> takes the BRANCH at each fork. Measured on
    /// this checkout: the walk lands on <b>five</b> off-spine nodes, across three different branches.
    /// Floored at two rather than five, so a board whose forks move — or a hero who dies inside the
    /// last branch — is not a failure, while a walk that never left the spine at all still is.
    /// </remarks>
    private const int MustLandOffSpine = 2;

    // ------------------------------------------------------------------------------------------
    // A — the view is the SAME board the run is actually played on.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// 🔒 <b>The identity, not a symptom.</b> A run is driven entirely through <c>GameRules.Apply</c>,
    /// and at every landing the tile kind, linear index and stage the RUN reports for the node it
    /// stands on are the ones <c>BoardView.Project</c> reports for that same node id.
    /// </summary>
    /// <remarks>
    /// The run's <c>PendingTileKind</c>/<c>PendingTileLinearIndex</c>/<c>PendingTileStage</c> are
    /// written by the movement handlers off the board <c>BoardResolution</c> resolved — so agreeing
    /// with them is agreeing with the board the run is on, which is the one thing a second plausible
    /// board could not do. Asserted across consecutive landings rather than at one, because a
    /// projection that drifted after the first Portal jump or the first fork would pass a single-shot
    /// check.
    /// </remarks>
    [Fact]
    public void The_view_names_the_tile_the_run_actually_resolved_at_every_landing_of_a_command_driven_run()
    {
        var game = new InMemoryGame(ShippedHarness.Content, HarnessSeed, new VirtualClock(Start));
        var player = game.CreatePlayer();
        game.Send(player, new BeginSessionCommand("1.0.0", "content"));
        game.Send(player, new StartRunCommand(Chapter, DifficultyTier.NORMAL));

        var checkedLandings = new HashSet<int>();
        var offSpineLandings = new HashSet<int>();
        var stagesSeen = new HashSet<int>();
        var acknowledged = new HashSet<int>();
        var trace = new List<string>();
        var choice = 0;

        for (var issued = 0; issued < CommandBudget; issued++)
        {
            var run = game.State(player).Run;

            if (run is null || run.Phase == RunPhase.Ended)
            {
                break;
            }

            if (run.HasPendingTile && checkedLandings.Add(run.Position))
            {
                if (AssertLandingAgrees(run))
                {
                    offSpineLandings.Add(run.Position);
                }

                stagesSeen.Add(run.PendingTileStage);
            }

            if (run.PendingFork is { } paused)
            {
                var view = BoardView.Project(run.ToSnapshot(), ShippedHarness.Content);

                view.PendingFork.ShouldNotBeNull(
                    "the run is paused at junction " + Text(paused.JunctionPosition) +
                    " waiting for CHOOSE_FORK, and the view offers no fork to choose from.");
                view.PendingFork!.JunctionNodeId.ShouldBe(
                    paused.JunctionPosition,
                    "the view named a different junction than the one the run is waiting at.");
            }

            var next = NextCommand(run, acknowledged, choice);

            if (next is null)
            {
                break;
            }

            var before = run.Position;
            var hadTile = run.HasPendingTile;
            var result = game.Send(player, next);

            trace.Add(next.GetType().Name + " @" + Text(before) +
                (result.Accepted ? " -> accepted" : " -> refused " + result.Rejection));

            if (!result.Accepted)
            {
                // A refused CHOICE is an option this hero cannot afford, not a dead end — the next
                // one is tried. A refusal of anything else ends the walk.
                if (next is EventChooseCommand or CampfireChooseCommand or ChooseForkCommand && choice < 3)
                {
                    choice++;
                    continue;
                }

                break;
            }

            choice = 0;

            var after = game.State(player).Run;

            if (next is ResolveTileCommand && hadTile && after?.HasPendingTile == true && before >= 0)
            {
                acknowledged.Add(before);
            }
        }

        checkedLandings.Count.ShouldBeGreaterThanOrEqualTo(
            MustCheckLandings,
            "the walk checked only " + Text(checkedLandings.Count) + " landings, so the agreement " +
            "above is a claim about too few nodes to be evidence that the view tracks the run's own " +
            "board rather than happening to match its first tile." + Environment.NewLine +
            "commands: " + string.Join(Environment.NewLine + "  ", trace));

        stagesSeen.Count.ShouldBeGreaterThanOrEqualTo(
            MustSpanStages,
            "every checked landing was in the same stage, so the walk never crossed a Stage Gate and " +
            "the agreement is a claim about one corner of one stage." + Environment.NewLine +
            "commands: " + string.Join(Environment.NewLine + "  ", trace));

        offSpineLandings.Count.ShouldBeGreaterThanOrEqualTo(
            MustLandOffSpine,
            "only " + Text(offSpineLandings.Count) + " of the walk's landings were off the spine, so " +
            "the agreement above is a claim about nodes whose id and linear index happen to be the " +
            "same number — and a projection that read Position as a track offset would satisfy every " +
            "one of them. CHOOSE_FORK takes the branch first here precisely so it does not." +
            Environment.NewLine +
            "commands: " + string.Join(Environment.NewLine + "  ", trace));
    }

    /// <returns><c>true</c> when the run landed off the spine, inside a fork branch.</returns>
    private static bool AssertLandingAgrees(RunAggregate run)
    {
        var snapshot = run.ToSnapshot();
        var view = BoardView.Project(snapshot, ShippedHarness.Content);
        var at = "node " + Text(run.Position) + " (linear " + Text(snapshot.PendingTileLinearIndex) + ")";

        view.StandingOn.ShouldNotBeNull(
            "the run is standing on " + at + " with a tile pending, and the view has no node there.");

        view.StandingOn!.NodeId.ShouldBe(run.Position, "the view resolved the wrong node for " + at + ".");

        ((int)view.StandingOn.Tile).ShouldBe(
            snapshot.PendingTileKind,
            "at " + at + " the run resolved tile kind " + Text(snapshot.PendingTileKind) +
            " and the view says " + view.StandingOn.Tile + ". The view is drawing a different board " +
            "from the one the run is being played on.");

        view.StandingOn.LinearIndex.ShouldBe(
            snapshot.PendingTileLinearIndex, "the view's linear index disagrees with the run's at " + at + ".");

        view.StandingOn.Stage.ShouldBe(
            snapshot.PendingTileStage, "the view's stage disagrees with the run's at " + at + ".");

        // Read off the PRODUCER rather than off the view: the off-spine floor this feeds must not be
        // satisfiable by a projection that simply answers OnSpine = false everywhere.
        var onSpine = Oracle(snapshot.RunSeed)
            .SpineNode(snapshot.PendingTileLinearIndex).Value == run.Position;

        view.StandingOn.OnSpine.ShouldBe(
            onSpine,
            "the view says " + at + " is " + (view.StandingOn.OnSpine ? "on" : "off") +
            " the spine and the board the run is being played on says the opposite.");

        return !onSpine;
    }

    /// <summary>
    /// 🔒 <b>A paused <c>CHOOSE_FORK</c>, by identity.</b> The view names the junction the run waits
    /// at, its two edges, and the branch preview `03` §1.1's board actually carries there — and the
    /// real <c>CHOOSE_FORK</c> command lands the run on the very node the view called the branch.
    /// </summary>
    /// <remarks>
    /// The preview is compared against the internal <see cref="CoreBoard"/>'s own Branch edge, which
    /// is the producer the handlers read. Comparing it against the bias table instead would assert
    /// what the label is supposed to encourage rather than what the branch actually holds.
    /// </remarks>
    [Fact]
    public void A_paused_fork_names_the_junction_the_run_waits_at_and_the_preview_the_board_carries()
    {
        var board = Oracle(FixedSeed);
        var junction = FirstJunction(board);
        var edges = board.OutgoingEdges(junction);
        var preview = edges[1].Preview.ShouldNotBeNull(
            "the fixture is only meaningful if the Branch edge really carries a preview.");

        var snapshot = PausedAtFork(FixedSeed, junction);
        var view = BoardView.Project(snapshot, ShippedHarness.Content);

        view.PendingFork.ShouldNotBeNull();
        view.PendingFork!.JunctionNodeId.ShouldBe(junction.Value);
        view.PendingFork.ContinueNodeId.ShouldBe(edges[0].To.Value, "edge 0 is 03 §1.1's spine continuation.");
        view.PendingFork.BranchNodeId.ShouldBe(edges[1].To.Value, "edge 1 is the fork's branch entry.");
        view.PendingFork.BranchLabel.ShouldBe(preview.Label);
        view.PendingFork.BranchIcons.ShouldBe(
            preview.Icons, "the preview must be the branch's own drawn tiles, in walk order.");

        var result = SlayIdleRepeat.Core.GameRules.Apply(
            Worlds.InARun(snapshot), new ChooseForkCommand(BranchIndex: 1), Context());

        result.Accepted.ShouldBeTrue();
        result.NewState.Run!.Position.ShouldBe(
            view.PendingFork.BranchNodeId,
            "CHOOSE_FORK took the branch onto a node the view did not call the branch, so the fork " +
            "the player is shown is not the fork the command resolves.");
    }

    // ------------------------------------------------------------------------------------------
    // B — determinism and replay identity.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// 🔒 `03` §1 — projecting the same run twice yields byte-identical views. The board is a pure
    /// function of the run seed and the chapter's content, and the view must be too.
    /// </summary>
    [Fact]
    public void Two_projections_of_the_same_run_are_byte_identical()
    {
        var snapshot = RunSnapshots.With(chapterId: Chapter, runSeed: FixedSeed);

        var first = Canonical(BoardView.Project(snapshot, ShippedHarness.Content));
        var second = Canonical(BoardView.Project(snapshot, ShippedHarness.Content));

        second.ShouldBe(first);
    }

    /// <summary>
    /// 🔴 `03` §1 — the negative control for the case above: two different run seeds project to
    /// different bytes, so the comparison can actually see a difference.
    /// </summary>
    /// <remarks>
    /// Without this, a <see cref="Canonical"/> that returned a constant would satisfy the identity
    /// case forever — the shape steering S1 exists for.
    /// </remarks>
    [Fact]
    public void Two_different_run_seeds_project_to_different_bytes()
    {
        var a = Canonical(BoardView.Project(RunSnapshots.With(chapterId: Chapter, runSeed: 1UL), ShippedHarness.Content));
        var b = Canonical(BoardView.Project(RunSnapshots.With(chapterId: Chapter, runSeed: 2UL), ShippedHarness.Content));

        a.ShouldNotBe(b);
    }

    // ------------------------------------------------------------------------------------------
    // C — the tile track.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// 🔒 `03` §1.1 — <c>Spine</c> is the walk order: entry <c>i</c> is linear index <c>i</c>, every
    /// entry is on the spine, the last is the Boss, and the count is the chapter's own stage lengths
    /// plus the boss node.
    /// </summary>
    /// <remarks>
    /// The length is read from the chapter's content rather than written as 43, so the case follows
    /// the authored geometry instead of pinning a number the content owns.
    /// </remarks>
    [Fact]
    public void The_spine_is_the_walk_order_and_ends_on_the_boss()
    {
        var expectedCount = ChapterBoardTuning.Read(ShippedHarness.Content, Chapter).StageLengths.Sum() + 1;
        var view = BoardView.Project(RunSnapshots.With(chapterId: Chapter, runSeed: FixedSeed), ShippedHarness.Content);

        view.Spine.Count.ShouldBe(
            expectedCount, "the spine is every stage node in walk order plus the boss node.");

        for (var i = 0; i < view.Spine.Count; i++)
        {
            view.Spine[i].LinearIndex.ShouldBe(i, "Spine is indexed by walk order, so entry i is linear index i.");
            view.Spine[i].OnSpine.ShouldBeTrue("a spine entry that is not on the spine is a contradiction.");
        }

        view.Spine[^1].Tile.ShouldBe(TileKind.Boss);
        view.Spine[^1].Stage.ShouldBe(CoreBoard.BossStage, "the boss belongs to no stage.");
    }

    /// <summary>
    /// 🔒 `03` §1.1 — a branch node is off the spine and carries its junction's forward index: the
    /// k-th node of a branch has the linear index the spine node the same distance ahead has.
    /// </summary>
    /// <remarks>
    /// That shared index is what makes a fork a RISK choice rather than a length discount — the
    /// branch costs the same steps as the spine it shadows — and it is also why the client cannot
    /// address a node by linear index alone.
    /// </remarks>
    [Fact]
    public void Every_branch_node_is_off_the_spine_and_carries_its_junctions_forward_index()
    {
        var checkedNodes = 0;

        foreach (var seed in Seeds)
        {
            var board = Oracle(seed);
            var view = BoardView.Project(RunSnapshots.With(chapterId: Chapter, runSeed: seed), ShippedHarness.Content);

            foreach (var junction in Junctions(board))
            {
                var junctionIndex = board.Node(junction).LinearIndex;
                var branch = BranchNodes(board, junction);

                for (var k = 0; k < branch.Count; k++)
                {
                    var node = view.Node(branch[k].Value).ShouldNotBeNull(
                        "seed " + Text(seed) + ": branch node " + branch[k] + " is on the board and " +
                        "missing from the view.");

                    node.OnSpine.ShouldBeFalse("seed " + Text(seed) + ": " + branch[k] + " is a branch node.");
                    node.LinearIndex.ShouldBe(
                        junctionIndex + k + 1,
                        "seed " + Text(seed) + ": the k-th branch node from a junction must carry the " +
                        "linear index of the spine node the same forward distance away.");

                    checkedNodes++;
                }
            }
        }

        checkedNodes.ShouldBeGreaterThan(
            0, "no seed produced a fork, so this case asserted nothing about branch nodes.");
    }

    /// <summary>
    /// 🔒 `03` §1.1 — <c>Nodes</c> is every reachable node of the run's board, spine and branch
    /// alike, ordered by node id and with no duplicates.
    /// </summary>
    /// <remarks>
    /// The expected set is walked out of the real graph through its public API rather than read off
    /// the view, so agreement is evidence rather than a restatement.
    /// </remarks>
    [Fact]
    public void Nodes_holds_every_reachable_node_once_ordered_by_node_id()
    {
        var board = Oracle(FixedSeed);
        var view = BoardView.Project(RunSnapshots.With(chapterId: Chapter, runSeed: FixedSeed), ShippedHarness.Content);

        var ids = view.Nodes.Select(n => n.NodeId).ToArray();

        ids.ShouldBe(ids.OrderBy(id => id).ToArray(), "Nodes is ordered by node id.");
        ids.Distinct().Count().ShouldBe(ids.Length, "Nodes holds each node once.");
        ids.ShouldBe(
            Reachable(board).Select(id => id.Value).OrderBy(v => v).ToArray(),
            "Nodes must be exactly the nodes the run can reach — spine, branch and boss.");

        view.Nodes.Count(n => n.OnSpine).ShouldBe(
            view.Spine.Count, "every spine entry appears in Nodes, and nothing else claims to be on the spine.");
    }

    // ------------------------------------------------------------------------------------------
    // D — the fork preview and its authored labels.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// 🔒 `03` §3.1 — <b>the honesty rule.</b> A fork's preview lists the branch's REAL first tiles:
    /// <c>min(3, branch length)</c> icons, each equal to the tile of the corresponding branch node in
    /// walk order, read back out of the view itself.
    /// </summary>
    /// <remarks>
    /// Asserted against the branch nodes the view reports, never against the label's bias table: the
    /// bias only tilts a weighted draw, so a preview built from it would be a description of an
    /// intention rather than of the branch the player is about to walk.
    /// </remarks>
    [Fact]
    public void Every_fork_previews_the_real_tiles_of_its_own_branch()
    {
        var checkedForks = 0;

        foreach (var seed in Seeds)
        {
            var board = Oracle(seed);
            var view = BoardView.Project(RunSnapshots.With(chapterId: Chapter, runSeed: seed), ShippedHarness.Content);

            foreach (var fork in view.Forks)
            {
                var branch = BranchNodes(board, new NodeId(fork.JunctionNodeId));

                fork.BranchIcons.Count.ShouldBe(
                    Math.Min(3, branch.Count),
                    "seed " + Text(seed) + ": a preview shows min(3, branch length) icons.");

                for (var k = 0; k < fork.BranchIcons.Count; k++)
                {
                    var node = view.Node(branch[k].Value).ShouldNotBeNull();

                    fork.BranchIcons[k].ShouldBe(
                        node.Tile,
                        "seed " + Text(seed) + ": icon " + Text(k) + " of the fork at node " +
                        Text(fork.JunctionNodeId) + " promises " + fork.BranchIcons[k] + " and the " +
                        "branch node actually holds " + node.Tile + ". The preview must be honest.");
                }

                checkedForks++;
            }
        }

        checkedForks.ShouldBeGreaterThan(
            0, "no seed produced a fork, so the honesty rule asserted nothing.");
    }

    /// <summary>
    /// 🔒 `03` §1.1 — <c>Forks</c> is one entry per junction, ordered by junction node id, with the
    /// junction on the spine and the branch entry off it.
    /// </summary>
    [Fact]
    public void Forks_holds_one_entry_per_junction_ordered_by_junction_node_id()
    {
        var board = Oracle(FixedSeed);
        var view = BoardView.Project(RunSnapshots.With(chapterId: Chapter, runSeed: FixedSeed), ShippedHarness.Content);

        var junctions = Junctions(board).Select(j => j.Value).OrderBy(v => v).ToArray();

        junctions.Length.ShouldBeGreaterThan(0, "the fixture seed must produce a board with forks.");
        view.Forks.Select(f => f.JunctionNodeId).ShouldBe(junctions);

        foreach (var fork in view.Forks)
        {
            view.Node(fork.JunctionNodeId).ShouldNotBeNull().OnSpine.ShouldBeTrue(
                "a junction is a spine node — 03 §1.1's forks leave the spine and rejoin it.");
            view.Node(fork.BranchNodeId).ShouldNotBeNull().OnSpine.ShouldBeFalse(
                "the branch entry is the first node off the spine.");
        }
    }

    /// <summary>
    /// 🔒 `03` §3.1 — every declared fork label is reachable across the twenty fixed seeds. A floor
    /// over the label SET, so the label plumbing cannot silently collapse to one value.
    /// </summary>
    /// <remarks>
    /// Twenty seeds rather than one: a label is drawn uniformly per fork, so any single board says
    /// nothing about the others. Stated over every declared member rather than over a count, so a
    /// fifth label added later is covered without editing a number (steering S3) — and the NAME says
    /// "every declared" rather than "all four" for the same reason, so it cannot promise a quantity
    /// the assertion has stopped delivering. Measured on this checkout: the twenty seeds produce 86
    /// forks carrying Perilous 24, Sheltered 19, Arcane 22, Feral 21.
    /// </remarks>
    [Fact]
    public void Every_declared_fork_label_is_reachable_across_the_fixed_seeds()
    {
        var seen = new HashSet<ForkLabel>();

        foreach (var seed in Seeds)
        {
            var view = BoardView.Project(RunSnapshots.With(chapterId: Chapter, runSeed: seed), ShippedHarness.Content);

            foreach (var fork in view.Forks)
            {
                fork.BranchLabel.ShouldBeOneOf(Enum.GetValues<ForkLabel>());
                seen.Add(fork.BranchLabel);
            }
        }

        foreach (var label in Enum.GetValues<ForkLabel>())
        {
            seen.ShouldContain(
                label,
                "no fork across the " + Text(Seeds.Length) + " fixed seeds carried " + label +
                ". Either the label draw has collapsed, or the view is not carrying the label the " +
                "board drew.");
        }
    }

    // ------------------------------------------------------------------------------------------
    // E — position.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// 🔒 `03` §1.1 — a run at the virtual trailhead (<c>-1</c>) stands on no node, so
    /// <c>StandingOn</c> is <c>null</c> rather than the first node.
    /// </summary>
    [Fact]
    public void A_run_at_the_trailhead_stands_on_no_node()
    {
        var view = BoardView.Project(
            RunSnapshots.With(chapterId: Chapter, runSeed: FixedSeed, position: -1), ShippedHarness.Content);

        view.StandingOn.ShouldBeNull(
            "-1 is the trailhead a started-but-unrolled run legitimately sits at; it is not a board node.");
    }

    /// <summary>
    /// 🔒 `03` §1.1 — <b>the case the client provably cannot do today.</b> A run standing inside a
    /// fork branch resolves to that BRANCH node, because <c>Position</c> is a node id and the linear
    /// index alone is shared with the spine node the same distance ahead.
    /// </summary>
    /// <remarks>
    /// The discriminating assertion is that the resolved node's linear index is NOT its node id: a
    /// consumer that read <c>Position</c> as a linear index would draw the spine tile instead, which
    /// is exactly the mistake this projection exists to make impossible.
    /// </remarks>
    [Fact]
    public void A_run_standing_inside_a_branch_resolves_to_the_branch_node_not_the_spine_node()
    {
        var board = Oracle(FixedSeed);
        var junction = FirstJunction(board);
        var branchEntry = board.OutgoingEdges(junction)[1].To;

        var view = BoardView.Project(
            RunSnapshots.With(chapterId: Chapter, runSeed: FixedSeed, position: branchEntry.Value),
            ShippedHarness.Content);

        var standing = view.StandingOn.ShouldNotBeNull();

        standing.NodeId.ShouldBe(branchEntry.Value);
        standing.OnSpine.ShouldBeFalse("the run is inside the branch, not on the spine.");
        standing.Tile.ShouldBe(board.Node(branchEntry).Tile);
        standing.LinearIndex.ShouldBe(board.Node(junction).LinearIndex + 1);

        standing.LinearIndex.ShouldNotBe(
            standing.NodeId,
            "a branch node's id and its linear index differ, which is the whole reason Position " +
            "cannot be drawn as a track offset — and why this projection resolves the id.");
    }

    /// <summary>
    /// 🔒 `03` §1.1 — <c>Node</c> answers <c>null</c> for an id this board does not hold, rather than
    /// throwing or returning a plausible neighbour.
    /// </summary>
    [Fact]
    public void Looking_up_an_id_this_board_does_not_hold_answers_null()
    {
        var view = BoardView.Project(RunSnapshots.With(chapterId: Chapter, runSeed: FixedSeed), ShippedHarness.Content);

        view.Node(int.MaxValue).ShouldBeNull();
        view.Node(-1).ShouldBeNull("-1 is the trailhead sentinel, not a node id.");
        view.Node(view.Nodes.Count).ShouldBeNull("one past the last id is off this board.");
    }

    /// <summary>
    /// 🔒 `03` §1.1 — <c>PendingFork</c> is <c>null</c> when the run is not waiting at a junction.
    /// </summary>
    [Fact]
    public void A_run_with_no_paused_fork_offers_no_pending_fork()
    {
        var view = BoardView.Project(RunSnapshots.With(chapterId: Chapter, runSeed: FixedSeed), ShippedHarness.Content);

        view.PendingFork.ShouldBeNull();
    }

    /// <summary>
    /// 🔒 `03` §1.1 — a paused-fork position that is not a junction of THIS board yields no pending
    /// fork. The projection reports what the board holds; it never invents a fork to fill the field.
    /// </summary>
    /// <remarks>
    /// The subject is a real spine node rather than a nonsense number, so the case discriminates
    /// "not a junction" from "not a node" — the failure it guards is a projection that answered the
    /// nearest fork, or the first one, for any position at all.
    /// </remarks>
    [Fact]
    public void A_paused_fork_position_that_is_not_a_junction_yields_no_pending_fork()
    {
        var board = Oracle(FixedSeed);
        var notAJunction = board.SpineNode(0);

        board.IsJunction(notAJunction).ShouldBeFalse(
            "the fixture is only meaningful if the node it names really is not a junction.");

        var view = BoardView.Project(
            RunSnapshots.With(
                chapterId: Chapter,
                runSeed: FixedSeed,
                position: notAJunction.Value,
                pendingForkJunctionPosition: notAJunction.Value,
                pendingForkRemainingSteps: 1),
            ShippedHarness.Content);

        view.PendingFork.ShouldBeNull();
    }

    // ------------------------------------------------------------------------------------------
    // F — argument guards.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// 🔒 `03` §1.1 — a null run or a null content set is refused rather than projected into an empty
    /// board that a caller would draw as a legal one.
    /// </summary>
    [Fact]
    public void Projecting_a_null_run_or_a_null_content_set_is_refused()
    {
        Should.Throw<ArgumentNullException>(() => BoardView.Project(null!, ShippedHarness.Content));
        Should.Throw<ArgumentNullException>(
            () => BoardView.Project(RunSnapshots.With(chapterId: Chapter, runSeed: FixedSeed), null!));
    }

    // ------------------------------------------------------------------------------------------
    // Fixtures.
    // ------------------------------------------------------------------------------------------

    /// <summary>The context the fork case applies <c>CHOOSE_FORK</c> in, over the shipped chapters.</summary>
    private static GameContext Context() => Worlds.Context with { Content = ShippedHarness.Content };

    /// <summary>
    /// The board the run is actually played on, generated through the internal producer — the oracle
    /// every structural case is stated against.
    /// </summary>
    private static CoreBoard Oracle(ulong runSeed) => BoardGenerator.GenerateBoard(
        ChapterBoardTuning.Read(ShippedHarness.Content, Chapter),
        DeterministicRng.OpenAt(runSeed, RngStreams.Board, 0));

    /// <summary>A run paused at <paramref name="junction"/> exactly as <c>ROLL_DICE</c> leaves one.</summary>
    private static RunSnapshot PausedAtFork(ulong runSeed, NodeId junction) => RunSnapshots.With(
        chapterId: Chapter,
        runSeed: runSeed,
        position: junction.Value,
        pendingForkJunctionPosition: junction.Value,
        pendingForkRemainingSteps: 1);

    /// <summary>
    /// The chapter's spine length, read from its own content: every stage node plus the boss.
    /// </summary>
    private static readonly Lazy<int> SpineLength = new(
        () => ChapterBoardTuning.Read(ShippedHarness.Content, Chapter).StageLengths.Sum() + 1);

    /// <summary>Every junction of a board, in spine walk order.</summary>
    private static IReadOnlyList<NodeId> Junctions(CoreBoard board)
    {
        var found = new List<NodeId>();

        for (var linearIndex = 0; linearIndex < SpineLength.Value; linearIndex++)
        {
            var id = board.SpineNode(linearIndex);

            if (board.IsJunction(id))
            {
                found.Add(id);
            }
        }

        return found;
    }

    /// <summary>The first junction of a board's spine.</summary>
    private static NodeId FirstJunction(CoreBoard board)
    {
        var junctions = Junctions(board);

        return junctions.Count > 0
            ? junctions[0]
            : throw new InvalidOperationException(
                "this fixture board has no fork at all, so every fork case over it asserts nothing.");
    }

    /// <summary>A junction's branch nodes, in walk order, up to but not including the rejoin.</summary>
    private static IReadOnlyList<NodeId> BranchNodes(CoreBoard board, NodeId junction)
    {
        var branch = new List<NodeId>();
        var cursor = board.OutgoingEdges(junction)[1].To;

        while (true)
        {
            branch.Add(cursor);

            var node = board.Node(cursor);
            var next = board.OutgoingEdges(cursor).Single(e => e.Kind == EdgeKind.Continue).To;

            // The branch's last node shares its linear index with the spine node it rejoins onto.
            if (board.SpineNode(node.LinearIndex) == next)
            {
                return branch;
            }

            cursor = next;
        }
    }

    /// <summary>Every node reachable from the first node, walked through the board's public API.</summary>
    private static IReadOnlyCollection<NodeId> Reachable(CoreBoard board)
    {
        var seen = new HashSet<NodeId> { board.FirstNodeId };
        var pending = new Stack<NodeId>();
        pending.Push(board.FirstNodeId);

        while (pending.Count > 0)
        {
            foreach (var edge in board.OutgoingEdges(pending.Pop()))
            {
                if (seen.Add(edge.To))
                {
                    pending.Push(edge.To);
                }
            }
        }

        return seen;
    }

    /// <summary>
    /// 🔒 The comparison every determinism case uses — an explicit member-by-member walk, never
    /// record <c>Equals</c> (steering S17).
    /// </summary>
    /// <remarks>
    /// A synthesized record equality compares <c>BranchIcons</c> by reference, so it would report two
    /// unrelated boards as different for a reason that has nothing to do with their contents and
    /// could never report two identical ones as equal. Every field of every node and every fork is
    /// appended in order, so a difference anywhere is a difference in the string.
    /// </remarks>
    private static string Canonical(BoardView view)
    {
        var text = new StringBuilder();

        text.Append("spine:");
        foreach (var node in view.Spine)
        {
            Append(text, node);
        }

        text.Append("|nodes:");
        foreach (var node in view.Nodes)
        {
            Append(text, node);
        }

        text.Append("|forks:");
        foreach (var fork in view.Forks)
        {
            text.Append(Text(fork.JunctionNodeId)).Append('>')
                .Append(Text(fork.ContinueNodeId)).Append('/')
                .Append(Text(fork.BranchNodeId)).Append(':')
                .Append(fork.BranchLabel).Append('[');

            foreach (var icon in fork.BranchIcons)
            {
                text.Append(icon).Append(',');
            }

            text.Append("];");
        }

        return text.ToString();

        static void Append(StringBuilder text, BoardTrackNode node) =>
            text.Append(Text(node.NodeId)).Append('=')
                .Append(node.Tile).Append('@')
                .Append(Text(node.LinearIndex)).Append('s')
                .Append(Text(node.Stage))
                .Append(node.OnSpine ? "+" : "-")
                .Append(';');
    }

    /// <summary>The one command that is legal next, or <c>null</c> when the walk cannot go on.</summary>
    private static GameCommand? NextCommand(RunAggregate run, IReadOnlySet<int> acknowledged, int choice)
    {
        if (run.Phase == RunPhase.BattlePending)
        {
            return new ConfirmBattleResultCommand("1", Won: true);
        }

        if (run.DraftPending)
        {
            return new SkipDraftCommand();
        }

        if (run.PendingFork is not null)
        {
            // 🔒 The BRANCH first (edge 1), the spine continuation only if the branch is refused.
            // Continuing every time would keep the whole walk on the spine, where a node's id and its
            // linear index are interchangeable — and the case this projection exists for is the one
            // where they are not. Measured: branch-first lands on five off-spine nodes across three
            // branches and still reaches the boss; continue-first lands on none.
            return new ChooseForkCommand(choice == 0 ? 1 : 0);
        }

        if (run.BossDefeated || run.CurrentHp == 0)
        {
            return null;
        }

        if (!run.HasPendingTile)
        {
            return new RollDiceCommand();
        }

        var next = (TileKind)run.PendingTileKindValue switch
        {
            TileKind.Enemy or TileKind.Elite or TileKind.Boss => (GameCommand)new StartBattleCommand(),
            TileKind.Minigame when !run.HasResolvedMinigameAt(run.Position) =>
                new MinigameSubmitCommand(MinigameCatalogue.ChestPick, 0),
            TileKind.Campfire => new CampfireChooseCommand(choice),
            TileKind.Event when run.PendingEventCardId is { Length: > 0 } => new EventChooseCommand(choice),
            TileKind.Minigame => new RollDiceCommand(),
            _ => new ResolveTileCommand(),
        };

        // A RESOLVE_TILE already accepted here that left the tile pending has no second command in
        // this walk, so the run cannot leave — stop rather than resend it until the budget runs out.
        return next is ResolveTileCommand && acknowledged.Contains(run.Position) ? null : next;
    }

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Text(ulong value) => value.ToString(CultureInfo.InvariantCulture);
}
