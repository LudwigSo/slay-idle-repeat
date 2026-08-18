using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Rules.Dice;
using SlayIdleRepeat.Core.Tests.Content;
using SlayIdleRepeat.Core.Tests.Model;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary>
/// When the Stage Gate fires, asserted through the three commands that can bring a run to rest on a
/// node: ROLL_DICE, CHOOSE_FORK and RESOLVE_TILE's Portal jump.
/// </summary>
/// <remarks>
/// <para>
/// The gate is a property of the node the run comes to rest on, not of the step count that got it
/// there — so every case names the landing node it built and asserts the landing before it asserts
/// the gate. All three of the gate's movements are asserted together: the heal alone is also what a
/// campfire, a shrine and a Surge face do, and the dice anchor is the one nothing else moves.
/// </para>
/// <para>
/// Every case is built over the board <c>BoardGenerator</c> actually produces for a fixed seed,
/// recomputed here the way the handler recomputes it, rather than a hand-picked magic position.
/// </para>
/// </remarks>
public sealed class StageGateTriggerTests
{
    /// <summary>The evenly-weighted 12/12/12 chapter, so every stage boundary has a known index.</summary>
    private const int ChapterId = 2;

    private const int StageLength = ChapterDocuments.TinyChapterStageLength;

    /// <summary>Stage 1's last node: its one outgoing edge crosses into stage 2.</summary>
    private const int StageOneLast = StageLength - 1;

    /// <summary>Stage 3's last node: its one outgoing edge leads to the boss, which belongs to no stage.</summary>
    private const int StageThreeLast = (3 * StageLength) - 1;

    private const int BossNode = 3 * StageLength;

    /// <summary>The seed every case but the Portal one runs on. Named so a failure re-runs exactly.</summary>
    private const ulong Seed = 0x00C0FFEE_00C0FFEEUL;

    /// <summary>
    /// The Portal case's own seed, and it is forced rather than preferred.
    /// </summary>
    /// <remarks>
    /// A Portal tile never sits in the last four nodes of a stage, so a jump can only reach a stage's
    /// last node from local index 7 or earlier — and on <see cref="Seed"/>'s board the stage-1
    /// junction sits at local 7, which pauses the jump instead. This board's junction is at local 4,
    /// leaving a clean run from a Portal-legal node to the boundary.
    /// </remarks>
    private const ulong PortalSeed = 17UL;

    /// <summary>The committed <c>board</c> position the Portal case jumps from — it draws a 4 there.</summary>
    private const ulong PortalBoardDraws = 1UL;

    /// <summary>The distance that position draws, asserted by the Portal case rather than trusted.</summary>
    private const int PortalJump = 4;

    /// <summary>Where the Portal case starts: the latest local index a Portal tile may occupy.</summary>
    private const int PortalStart = StageLength - 5;

    private const int WoundedHp = 40;
    private const int MaxHp = 100;

    /// <summary>40 + round(100 × 0.15), the shipped Stage Gate heal.</summary>
    private const int HealedHp = 55;

    private const int ChargesSpent = 3;

    /// <summary>A committed <c>dice</c> position for the cases that take no dice draw of their own.</summary>
    private const ulong DrawnAlready = 6UL;

    private static GameContext Context { get; } = TinyContext();

    private static BoardGraph Board { get; } = BoardFor(Seed);

    private static BoardGraph PortalBoard { get; } = BoardFor(PortalSeed);

    // ═════════════════════════════════════════════════════════ ROLL_DICE

    /// <summary>
    /// 🔒 An <b>exact</b> landing on a stage's last node fires the gate. Nothing about the step count
    /// distinguishes this landing from a mid-stage one; the node does.
    /// </summary>
    [Fact]
    public void An_exact_landing_on_a_stages_last_node_fires_the_stage_gate()
    {
        var dice = DicePositionDrawing(1);

        var result = Roll(RunSnapshots.With(
            chapterId: ChapterId,
            runSeed: Seed,
            position: Board.SpineNode(StageOneLast - 1).Value,
            currentHp: WoundedHp,
            maxHp: MaxHp,
            rerollChargesSpentThisStage: ChargesSpent,
            rngStreamPositions: RunSnapshots.Streams((RngStreams.Dice, dice))));

        result.Accepted.ShouldBeTrue();

        var run = result.NewState.Run!;

        run.Position.ShouldBe(
            Board.SpineNode(StageOneLast).Value,
            "the premise: a one-step roll from one node short must come to rest ON stage 1's last " +
            "node, which is what this case is about.");

        run.CurrentHp.ShouldBe(HealedHp, "the gate heals 15% of Max HP from " + WoundedHp + ".");
        run.RerollChargesSpentThisStage.ShouldBe(0, "the gate refreshes the stage's reroll charges.");
        run.StageGateDiceAnchor.ShouldBe(
            dice + 1,
            "the gate moves the Fair-Dice bag's reset anchor to the dice position it fired at, so " +
            "the next stage's weights replay from this stage rather than from draw 0 of the run.");
    }

    /// <summary>
    /// …and an <b>overshoot</b> clamped onto the same node fires it too. This arm already worked, so
    /// it is the control that says the trigger was widened rather than moved.
    /// </summary>
    [Fact]
    public void An_overshoot_clamped_onto_a_stages_last_node_fires_the_stage_gate()
    {
        var dice = DicePositionDrawing(3);

        var result = Roll(RunSnapshots.With(
            chapterId: ChapterId,
            runSeed: Seed,
            position: Board.SpineNode(StageOneLast - 1).Value,
            currentHp: WoundedHp,
            maxHp: MaxHp,
            rerollChargesSpentThisStage: ChargesSpent,
            rngStreamPositions: RunSnapshots.Streams((RngStreams.Dice, dice))));

        result.Accepted.ShouldBeTrue();

        var run = result.NewState.Run!;

        run.Position.ShouldBe(
            Board.SpineNode(StageOneLast).Value,
            "the premise: a three-step roll from one node short is clamped ON stage 1's last node.");

        run.CurrentHp.ShouldBe(HealedHp);
        run.RerollChargesSpentThisStage.ShouldBe(0);
        run.StageGateDiceAnchor.ShouldBe(dice + 1);
    }

    // ═════════════════════════════════════════════════════════ CHOOSE_FORK

    /// <summary>
    /// 🔒 A movement a junction interrupted, resumed onto a stage's last node, fires the gate — the
    /// resumed half of a roll is the same landing as an unbroken one.
    /// </summary>
    [Fact]
    public void A_fork_resumed_onto_a_stages_last_node_fires_the_stage_gate()
    {
        var junction = FirstJunction(Board);
        var steps = ResumedStepsLandingOn(junction, Board.SpineNode(StageOneLast));

        var result = Choose(RunSnapshots.With(
            chapterId: ChapterId,
            runSeed: Seed,
            position: junction.Value,
            currentHp: WoundedHp,
            maxHp: MaxHp,
            rerollChargesSpentThisStage: ChargesSpent,
            pendingForkJunctionPosition: junction.Value,
            pendingForkRemainingSteps: steps,
            rngStreamPositions: RunSnapshots.Streams((RngStreams.Dice, DrawnAlready))));

        result.Accepted.ShouldBeTrue();

        var run = result.NewState.Run!;

        run.PendingFork.ShouldBeNull("the premise: the fork was answered rather than re-opened.");
        run.Position.ShouldBe(
            Board.SpineNode(StageOneLast).Value,
            "the premise: the resumed movement comes to rest ON stage 1's last node.");

        run.CurrentHp.ShouldBe(HealedHp);
        run.RerollChargesSpentThisStage.ShouldBe(0);
        run.StageGateDiceAnchor.ShouldBe(
            DrawnAlready,
            "CHOOSE_FORK draws no die of its own, so the anchor lands on the run's committed dice " +
            "position — not on 0, which is where it started.");
    }

    // ═════════════════════════════════════════════════════════ the Portal jump

    /// <summary>
    /// 🔒 A Portal jump that comes to rest on a stage's last node fires the gate. This path fires it
    /// zero times today: <c>RESOLVE_TILE</c>'s Portal branch never asks.
    /// </summary>
    [Fact]
    public void A_portal_jump_onto_a_stages_last_node_fires_the_stage_gate()
    {
        MovementEngine.DrawPortalDistance(
            DeterministicRng.OpenAt(PortalSeed, RngStreams.Board, PortalBoardDraws)).ShouldBe(
            PortalJump,
            "the premise: this case is about an EXACT landing, and the landing node below cannot tell " +
            "one apart from an overshoot — a jump of 5 or 6 from local index " + PortalStart +
            " is clamped onto that same node and would quietly prove the clamp arm a second time.");

        var result = SlayIdleRepeat.Core.GameRules.Apply(
            Worlds.InARun(RunSnapshots.With(
                chapterId: ChapterId,
                runSeed: PortalSeed,
                position: PortalBoard.SpineNode(PortalStart).Value,
                currentHp: WoundedHp,
                maxHp: MaxHp,
                rerollChargesSpentThisStage: ChargesSpent,
                pendingTileKind: (int)TileKind.Portal,
                pendingTileLinearIndex: PortalStart,
                pendingTileStage: 1,
                rngStreamPositions: RunSnapshots.Streams(
                    (RngStreams.Board, PortalBoardDraws), (RngStreams.Dice, DrawnAlready)))),
            new ResolveTileCommand(),
            Context);

        result.Accepted.ShouldBeTrue();

        var run = result.NewState.Run!;

        run.PendingFork.ShouldBeNull("the premise: the jump landed rather than pausing at a junction.");
        run.Position.ShouldBe(
            PortalBoard.SpineNode(StageOneLast).Value,
            "the premise: a jump of " + PortalJump + " from local index " + PortalStart +
            " comes to rest ON stage 1's last node.");

        run.CurrentHp.ShouldBe(HealedHp);
        run.RerollChargesSpentThisStage.ShouldBe(0);
        run.StageGateDiceAnchor.ShouldBe(
            DrawnAlready,
            "a Portal jump draws from the board stream, not the dice one, so the anchor lands on the " +
            "run's committed dice position — not on 0, which is where it started.");
    }

    // ═════════════════════════════════════════════════════════ where it must NOT fire

    /// <summary>An ordinary landing inside a stage fires nothing.</summary>
    [Fact]
    public void A_landing_inside_a_stage_fires_no_stage_gate()
    {
        var dice = DicePositionDrawing(1);

        var result = Roll(RunSnapshots.With(
            chapterId: ChapterId,
            runSeed: Seed,
            position: Board.SpineNode(0).Value,
            currentHp: WoundedHp,
            maxHp: MaxHp,
            rerollChargesSpentThisStage: ChargesSpent,
            rngStreamPositions: RunSnapshots.Streams((RngStreams.Dice, dice))));

        result.NewState.Run!.Position.ShouldBe(
            Board.SpineNode(1).Value, "the premise: the run landed one node into stage 1.");

        NoGateFired(result, dice);
    }

    /// <summary>
    /// 🔒 <b>Stage 3's last node fires no gate</b>, because its one outgoing edge leads to the boss —
    /// and the boss belongs to no stage. A run has exactly two gates, not three.
    /// </summary>
    [Fact]
    public void The_last_node_of_the_final_stage_fires_no_stage_gate()
    {
        var dice = DicePositionDrawing(1);

        var result = Roll(RunSnapshots.With(
            chapterId: ChapterId,
            runSeed: Seed,
            position: Board.SpineNode(StageThreeLast - 1).Value,
            currentHp: WoundedHp,
            maxHp: MaxHp,
            rerollChargesSpentThisStage: ChargesSpent,
            rngStreamPositions: RunSnapshots.Streams((RngStreams.Dice, dice))));

        result.NewState.Run!.Position.ShouldBe(
            Board.SpineNode(StageThreeLast).Value,
            "the premise: the run came to rest ON stage 3's last node.");

        NoGateFired(
            result, dice,
            "stage 3's last node gated. The ad cadence is authored against two gates per run, and a " +
            "trigger that only asks whether the next node changes stage counts the boss as a third.");
    }

    /// <summary>A landing exactly on a junction — a legal stop that prompts nothing — fires nothing.</summary>
    /// <remarks>
    /// ⚠️ <b>This does not exercise "a junction is not a stage end because it has two edges."</b> Every
    /// junction candidate sits at a local index no later than <c>spineLength − 4</c>, so a junction is
    /// never a stage's last node on a generated board and the case would pass against a trigger that
    /// counted no edges at all. What it does pin is the one landing that most resembles a stop worth
    /// gating — movement stopped on a node it could not have walked through, and no fork opened — and
    /// that the gate is left alone for it.
    /// </remarks>
    [Fact]
    public void A_landing_exactly_on_a_junction_fires_no_stage_gate()
    {
        var junction = FirstJunction(Board);
        var dice = DicePositionDrawing(1);

        var result = Roll(RunSnapshots.With(
            chapterId: ChapterId,
            runSeed: Seed,
            position: Board.SpineNode(Board.Node(junction).LinearIndex - 1).Value,
            currentHp: WoundedHp,
            maxHp: MaxHp,
            rerollChargesSpentThisStage: ChargesSpent,
            rngStreamPositions: RunSnapshots.Streams((RngStreams.Dice, dice))));

        result.NewState.Run!.Position.ShouldBe(
            junction.Value, "the premise: the run came to rest ON the junction.");
        result.NewState.Run!.PendingFork.ShouldBeNull(
            "the premise: landing on a junction with nothing left to spend does not prompt.");

        NoGateFired(result, dice);
    }

    /// <summary>Reaching the boss fires nothing: the boss node belongs to no stage.</summary>
    [Fact]
    public void Reaching_the_boss_fires_no_stage_gate()
    {
        var dice = DicePositionDrawing(1);

        var result = Roll(RunSnapshots.With(
            chapterId: ChapterId,
            runSeed: Seed,
            position: Board.SpineNode(StageThreeLast).Value,
            currentHp: WoundedHp,
            maxHp: MaxHp,
            rerollChargesSpentThisStage: ChargesSpent,
            rngStreamPositions: RunSnapshots.Streams((RngStreams.Dice, dice))));

        result.NewState.Run!.Position.ShouldBe(
            Board.SpineNode(BossNode).Value, "the premise: the run reached the boss node.");

        NoGateFired(result, dice);
    }

    // ═════════════════════════════════════════════════════════ the chain, and why it is not here

    /// <summary>
    /// 🔒 No face <c>ROLL_DICE</c> can roll today asks to roll again, which is why nothing here
    /// crosses a stage boundary mid-chain.
    /// </summary>
    /// <remarks>
    /// The handler rolls the starting die and nothing else, so a chain cannot be reached through any
    /// command — the case that would assert "an exact landing on a stage's last node ends the chain"
    /// cannot be built. This goes red on the commit that puts a rolling-again face within reach, and
    /// that commit owes the case.
    /// </remarks>
    [Fact]
    public void No_face_the_handler_can_roll_asks_to_roll_again()
    {
        var faces = DieComposer.StartingDie;

        faces.Count.ShouldBe(6, "a die has six faces, and this claim is about all of them.");

        faces.Select(face => FaceEffectResolver.Resolve(face)).ShouldAllBe(
            outcome => !outcome.RollAgain,
            "a face the handler can roll now asks to roll again, so a chain can carry a run over a " +
            "stage boundary without stopping on it. Write the case: an exact landing on a stage's " +
            "last node ends the chain and fires the gate.");
    }

    // ═════════════════════════════════════════════════════════ fixtures

    private static GameContext TinyContext()
    {
        var baseContent = TileWorlds.Context.Content;

        // 🔒 Every OTHER chapter document is dropped, and the tiny one is the only one left that
        // answers to this chapter id. ChapterBoardTuning.FindChapterDocument scans content/chapters/
        // for the document whose `id` matches, so two documents claiming chapter 2 make the board an
        // ordering accident — and the board decides every node number this suite asserts. It became
        // reachable when TileWorlds.Context grew the whole shipped set (a fight needs it), which
        // brought the real chapter 2 in beside this tiny one; before that the fixture set carried
        // chapter 1 alone and appending was safe. Filtering rather than appending is what makes the
        // tiny chapter authoritative regardless of what else the base set happens to hold.
        var documents = baseContent.DocumentPaths
            .Where(path => !path.StartsWith(ChapterDirectory, StringComparison.Ordinal))
            .Select(baseContent.GetDocument)
            .Append(ChapterDocuments.TinyChapterDocument(ChapterId));

        return TileWorlds.Context with
        {
            Content = new ContentSnapshot(baseContent.Version, documents),
        };
    }

    /// <summary>Where chapter documents live, which is the directory the chapter scan reads.</summary>
    private const string ChapterDirectory = "content/chapters/";


    private static BoardGraph BoardFor(ulong seed) => BoardGenerator.GenerateBoard(
        ChapterBoardTuning.Read(Context.Content, ChapterId),
        DeterministicRng.OpenAt(seed, RngStreams.Board, 0));

    private static CommandResult Roll(RunSnapshot snapshot) =>
        SlayIdleRepeat.Core.GameRules.Apply(
            Worlds.InARun(snapshot), new RollDiceCommand(), Context);

    private static CommandResult Choose(RunSnapshot snapshot) =>
        SlayIdleRepeat.Core.GameRules.Apply(
            Worlds.InARun(snapshot), new ChooseForkCommand(BranchIndex: 0), Context);

    /// <summary>None of the three things a gate moves has moved.</summary>
    private static void NoGateFired(CommandResult result, ulong diceBefore, string? because = null)
    {
        var reason = because ?? "a Stage Gate fired on a landing that crosses no stage boundary.";
        var run = result.NewState.Run!;

        run.CurrentHp.ShouldBe(WoundedHp, reason + " The hero was healed.");
        run.RerollChargesSpentThisStage.ShouldBe(
            ChargesSpent, reason + " The stage's reroll charges were refreshed.");
        run.StageGateDiceAnchor.ShouldBe(
            0UL, reason + " The Fair-Dice anchor moved off 0, and only a gate moves it. (The dice " +
            "stream stood at " + diceBefore + " before the command.)");
    }

    /// <summary>The first junction along the spine — every board this size has one in stage 1.</summary>
    private static NodeId FirstJunction(BoardGraph board)
    {
        for (var linearIndex = 0; linearIndex < StageLength; linearIndex++)
        {
            var id = board.SpineNode(linearIndex);

            if (board.IsJunction(id))
            {
                return id;
            }
        }

        throw new InvalidOperationException(
            "this fixture's stage 1 has no junction at all; the seed needs revisiting.");
    }

    /// <summary>
    /// The <c>PendingFork.RemainingSteps</c> whose Continue edge, resumed, comes to rest exactly on
    /// <paramref name="target"/> — computed off the engine rather than counted by hand.
    /// </summary>
    private static int ResumedStepsLandingOn(NodeId junction, NodeId target)
    {
        var continued = Board.OutgoingEdges(junction)[0].To;

        for (var remaining = 1; remaining <= 6; remaining++)
        {
            var landed = MovementEngine.Advance(Board, continued, remaining - 1);

            if (!landed.PausedAtJunction && landed.RemainingSteps == 0 && landed.Node.Equals(target))
            {
                return remaining;
            }
        }

        throw new InvalidOperationException(
            "no fork remainder a roll can produce resumes exactly onto " + target.Value +
            "; this fixture's seed needs revisiting.");
    }

    /// <summary>The <c>dice</c> position whose replayed draw is exactly <paramref name="pip"/>.</summary>
    private static ulong DicePositionDrawing(int pip)
    {
        for (var position = 0UL; position < 200; position++)
        {
            var weights = FairDiceBag.Replay(Seed, resetAtDraw: 0, uptoDraw: position);
            var (face, _) = FairDiceBag.Step(
                DeterministicRng.OpenAt(Seed, RngStreams.Dice, position), weights);

            if (face == pip)
            {
                return position;
            }
        }

        throw new InvalidOperationException(
            "no dice position under 200 draws a " + pip + "; this fixture's seed needs revisiting.");
    }
}
