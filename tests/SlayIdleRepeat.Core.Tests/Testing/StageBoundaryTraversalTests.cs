using Shouldly;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Testing;
using SlayIdleRepeat.Core.Tests.BalanceHarness;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Testing;

/// <summary>
/// 🔒 <b>X-10's regression test — the one that would have caught it.</b> A run is driven across a
/// stage boundary <em>through <c>GameRules.Apply</c>, one command at a time</em>, and is required to
/// keep going afterwards.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>Why the defect survived a full milestone.</b> The stage-end clamp of <c>03</c> §1.1 stops a
/// move that would carry <em>past</em> a stage's last node. <c>MovementEngine.Advance</c> applied it
/// whenever the next node changed stage — which is also true of a run already standing on that node
/// — so the roll after the clamp returned the run unmoved with the whole roll unspent, for ever. The
/// clamp's own unit tests all approached the boundary from behind and every one of them passed.
/// Nothing in the repository asked the question steering <b>S24</b> exists for: <em>can the run leave
/// this state?</em> These cases ask it of a stage boundary, and they ask it of a whole run rather
/// than of the engine in isolation — a defect that only shows on the <em>second</em> command is
/// invisible to a test that sends one.
/// </para>
/// <para>
/// ⚠️ <b>"The run did not move" is not the assertion</b> (steering <b>S2</b>). It is equally true of
/// a junction pause, a boss-exact stop and a roll of nothing, so every case below pins the identity
/// instead: the <em>stage</em> of the tiles the run arrives at, and — for the stall itself — a roll
/// accepted that both left the position alone <em>and</em> opened no fork.
/// </para>
/// <para>
/// ⚠️ This is a unit test in the domain suite, driving the in-memory harness in-process, exactly as
/// <see cref="MetaLoopTests"/> is. There is no integration tier in this repository and this is not
/// one.
/// </para>
/// </remarks>
public sealed class StageBoundaryTraversalTests
{
    /// <summary>The two chapters whose boards and enemies are authored.</summary>
    private static readonly int[] AuthoredChapters = [1, 2];

    /// <summary>The harness seed. Named so a failure re-runs exactly.</summary>
    private const ulong Seed = 0xB0A2D_0000_2222UL;

    /// <summary>
    /// How many of the swept runs must reach stage 2 for the sweep to count as having measured
    /// anything (steering <b>S3</b>: a floor on the subject set).
    /// </summary>
    /// <remarks>
    /// A floor rather than "all of them": a run can still legitimately end inside stage 1 — the hero
    /// dies, or a future tile kind arrives before the command that finishes it. Re-measured on this
    /// checkout now that a Shop and a Dice Forge clear, <b>16</b> of the 16 swept runs reach stage 2
    /// and all 16 go on to beat the boss; the floor is set under that so one board changing shape is
    /// not a failure, and far enough over zero that a sweep which stopped crossing anything cannot
    /// pass. The old floor of 6 was justified by the shop soft-lock, which is gone.
    /// </remarks>
    private const int MustCross = 14;

    /// <summary>How many start instants the sweep walks, per chapter.</summary>
    /// <remarks>
    /// The board is derived from the run's seed, which folds in the instant the run started — so the
    /// clock, not <see cref="Seed"/>, is what selects a board. Eight instants across two chapters is
    /// sixteen distinct boards.
    /// </remarks>
    private const int Instants = 8;

    private static readonly DateTimeOffset Start = new(2026, 8, 12, 5, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// 🔒 <b>The case X-10 would have failed.</b> A run driven entirely through commands resolves
    /// tiles in stage 1, then resolves tiles in stage 2 — it left the boundary node instead of
    /// standing on it for the rest of the run.
    /// </summary>
    /// <remarks>
    /// Asserted on the stage the run's own pending-tile state reports, not on the node number: a
    /// branch node's id is not its position along the spine, so a rising position proves travel but
    /// not a crossing.
    /// </remarks>
    [Fact]
    public void A_command_driven_run_leaves_stage_1_and_resolves_tiles_in_stage_2()
    {
        var driver = Play(1, Start);

        driver.Stages.ShouldNotBeEmpty(
            "the run resolved no tile at all, so it was never in a position to cross anything." +
            Trace(driver));

        driver.Stages[0].ShouldBe(1, "every run starts at the trailhead of stage 1." + Trace(driver));

        driver.Stages.ShouldContain(
            2,
            "the run never resolved a tile in stage 2. 03 §1.1's stage-end clamp is a ONE-TIME stop " +
            "on a stage's last node, not a wall around it: the run stops there once, the Stage Gate " +
            "fires, and the next roll carries on into the next stage. A run that only ever sees " +
            "stage 1 is X-10 — the clamp re-firing on a run already standing on the boundary node." +
            Trace(driver));
    }

    /// <summary>
    /// 🔒 <b>X-10's signature, asserted by identity.</b> No <c>ROLL_DICE</c> this run was accepted
    /// while leaving the run exactly where it stood with no fork opened.
    /// </summary>
    /// <remarks>
    /// The fork clause is the whole point (steering <b>S2</b>). A roll taken from a junction also
    /// leaves the position alone — <c>03</c> §1.1 pauses the instant movement must leave one, and it
    /// is reachable, since landing on a junction with nothing left to spend does not prompt. A case
    /// that checked the position alone would call that legal pause a stall and would pass for the
    /// wrong reason on a board with a junction late in stage 1.
    /// </remarks>
    [Fact]
    public void No_accepted_roll_leaves_the_run_standing_where_it_was_without_opening_a_fork()
    {
        var driver = Play(1, Start);

        driver.StalledAt.ShouldBeNull(
            "a ROLL_DICE was accepted, moved the run nowhere and opened no fork — the run has no way " +
            "to leave that node, and every roll after it does the same. That is X-10: 03 §1.1's " +
            "stage-end clamp re-firing on a run that is already standing on the stage's last node." +
            Trace(driver));
    }

    /// <summary>
    /// 🔒 <b>The run meets no tile it cannot leave, and travels the whole board to the boss.</b> That
    /// is the question steering <b>S24</b> exists for, asked of every tile the run lands on rather
    /// than of one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 This case used to require the opposite, and the requirement was honest at the time: it was
    /// the frontier X-10's repair uncovered, the same defect class one layer up. <c>RESOLVE_TILE</c>
    /// acknowledged <c>TILE_SHOP</c> and <c>TILE_DICE_FORGE</c> and left them pending for "the
    /// command that owns it", and neither had one; a pending tile blocks <c>ROLL_DICE</c>, so the run
    /// stopped there. Both now clear through <c>RESOLVE_TILE</c> itself.
    /// </para>
    /// <para>
    /// The driver still concludes "stuck" from what the commands actually did rather than from a list
    /// of tile kinds, so this stays the case that catches the <em>next</em> tile with no way out.
    /// </para>
    /// <para>
    /// ⚠️ <b>The boss arrival is asserted separately from stage 3</b>, because reaching stage 3 is
    /// not reaching the boss: the boss node belongs to <see cref="BoardGraph.BossStage"/>, not to
    /// stage 3, so a run that entered stage 3 and then stopped on a tile it could not finish would
    /// satisfy the stage assertion alone and leave this case's name overstating it.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_run_leaves_every_tile_it_lands_on_and_reaches_the_boss()
    {
        var driver = Play(1, Start);

        driver.StuckOn.ShouldBeNull(
            "the run stopped on a " + driver.StuckOn + " tile that RESOLVE_TILE accepted and left " +
            "pending, and ROLL_DICE then answered " + driver.StuckRollRejection + ". A tile with no " +
            "command that clears it ends the run wherever it happens to sit." + Trace(driver));

        driver.Stages.ShouldContain(
            3, "the run never resolved a tile in stage 3, so it did not cross the second boundary." +
            Trace(driver));

        driver.Stages.ShouldContain(
            BoardGraph.BossStage,
            "the run crossed into stage 3 and never arrived at the boss node, so it stopped somewhere " +
            "along the last stage rather than travelling the whole board." + Trace(driver));
    }

    /// <summary>
    /// 🔒 <b>No board stalls a roll.</b> One row per board — two authored chapters across eight start
    /// instants each — so a single stalling board reports as itself rather than as one aggregate red.
    /// </summary>
    [Theory]
    [MemberData(nameof(SweptBoards))]
    public void No_swept_board_accepts_a_roll_that_moves_the_run_nowhere(int chapter, int instant)
    {
        var driver = Play(chapter, Start.AddHours(instant * 3));

        driver.StalledAt.ShouldBeNull(
            "chapter " + chapter + " instant " + instant + " accepted a ROLL_DICE that moved the run " +
            "nowhere and opened no fork, which is X-10's signature." + Trace(driver));
    }

    /// <summary>
    /// 🔒 <b>The crossing is a rule, not one lucky board</b> — and this floor is what stops the sweep
    /// above being vacuous.
    /// </summary>
    /// <remarks>
    /// Without it, sixteen runs that all died on their first tile would satisfy "nothing stalled"
    /// perfectly (steering <b>S3</b>). A floor rather than a total: a run can still end inside stage 1
    /// for an honest reason — the hero dies — and demanding all sixteen would turn one board changing
    /// shape into a failure. See <see cref="MustCross"/> for the re-measured number.
    /// </remarks>
    [Fact]
    public void Most_of_the_swept_boards_actually_reach_stage_2()
    {
        var crossed = SweptBoards()
            .Select(row => Play((int)row[0], Start.AddHours((int)row[1] * 3)))
            .Count(driver => driver.Stages.Contains(2));

        crossed.ShouldBeGreaterThanOrEqualTo(
            MustCross,
            "only " + crossed + " of the " + (AuthoredChapters.Length * Instants) + " swept runs " +
            "reached stage 2. The per-board stall rows are satisfied by a sweep in which every run " +
            "ended before it ever approached a boundary, so this floor is what makes the sweep " +
            "evidence of a crossing rather than of an early exit.");
    }

    public static TheoryData<int, int> SweptBoards()
    {
        var rows = new TheoryData<int, int>();

        foreach (var chapter in AuthoredChapters)
        {
            for (var instant = 0; instant < Instants; instant++)
            {
                rows.Add(chapter, instant);
            }
        }

        return rows;
    }

    private static MetaLoopDriver Play(int chapter, DateTimeOffset start)
    {
        var game = new InMemoryGame(ShippedHarness.Content, Seed, new VirtualClock(start));
        var player = game.CreatePlayer();

        game.Send(player, new Core.Commands.BeginSessionCommand("1.0.0", "content"));

        return MetaLoopDriver.Play(game, player, chapter, DifficultyTier.NORMAL);
    }

    private static string Trace(MetaLoopDriver driver) =>
        Environment.NewLine + "ending: " + driver.Ending +
        Environment.NewLine + "stages: " + string.Join(", ", driver.Stages) +
        Environment.NewLine + "commands: " + string.Join(Environment.NewLine + "  ", driver.Log);
}
