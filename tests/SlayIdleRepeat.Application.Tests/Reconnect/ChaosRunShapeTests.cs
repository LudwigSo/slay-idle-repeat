using System.Diagnostics;
using System.Globalization;
using Shouldly;
using SlayIdleRepeat.Core.Rules.Board;
using Xunit;
using Xunit.Abstractions;

namespace SlayIdleRepeat.Application.Tests.Reconnect;

/// <summary>
/// The fault-free reference run, pinned. Everything the chaos cases claim is a comparison against
/// this run, so if it were a run that collapsed at the trailhead every one of those comparisons
/// would be trivially true — this file is what makes them claims about a real run.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>The board is chosen, not accepted.</b> A run's seed folds the player id and the frozen
/// instant together and does NOT depend on the allocated run id, so the pair the fixture names is
/// what selects the board. It was swept across eight identities and six start hours each; the pair
/// below is the one this file's floors are stated against, and every other pair in that sweep also
/// beat the boss — so the floors are the reference run's shape rather than the one board that
/// happened to clear them.
/// </para>
/// <para>
/// The coverage is asserted rather than claimed. What this board actually resolves is travel,
/// enemies, elites, mini-bosses, shrines, an event, a minigame, a shop, an empty node and the boss;
/// the floors below are stated over what the driver recorded, not over a list written here.
/// </para>
/// </remarks>
public sealed class ChaosRunShapeTests
{
    /// <summary>The fewest boundaries a run that genuinely crossed the board can take.</summary>
    /// <remarks>
    /// A floor rather than the measured count, so a content retune that lengthens or shortens the
    /// board does not fail this file — but a run that parked after a handful of commands does. The
    /// measured count on this board is reported by <see cref="The_reference_run_costs_what_a_unit_test_can_afford"/>.
    /// </remarks>
    private const int BoundaryFloor = 40;

    /// <summary>The fewest distinct tile kinds a run has to meet before "a full run" means anything.</summary>
    private const int DistinctTileKindFloor = 6;

    /// <summary>The fewest distinct board positions a travelling run stands on.</summary>
    private const int DistinctPositionFloor = 10;

    /// <summary>The fewest battles a run that reaches a boss has to have fought.</summary>
    private const int BattleFloor = 5;

    /// <summary>The fewest perk drafts such a run opens.</summary>
    private const int DraftFloor = 5;

    /// <summary>
    /// How many stage boundaries a whole chapter-1 run crosses: out of stage 1 and out of stage 2,
    /// never a third — the boss belongs to no stage and is reached by the boss-exact rule.
    /// </summary>
    private const int ExpectedStageGates = 2;

    /// <summary>
    /// The wall-clock bound on one fault-free run, in milliseconds. Roughly 20x the cost measured
    /// when this landed, which the test itself reports.
    /// </summary>
    private const double RunBudgetMs = 2000;

    private readonly ITestOutputHelper _output;

    /// <summary>Builds the case with the sink the measured figures are reported through.</summary>
    /// <param name="output">xunit's output sink.</param>
    public ChaosRunShapeTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// 🔒 The reference run reaches the boss and is ended by an accepted <c>END_RUN</c> — the
    /// ordinary victory, driven entirely through the gateway.
    /// </summary>
    [Fact]
    public async Task The_reference_run_beats_the_boss_and_is_ended_by_an_accepted_END_RUN()
    {
        var world = await ChaosWorld.GearedAsync();
        var driver = await ChaosRunDriver.PlayAsync(world, FaultSchedules.None);
        var (_, run) = await world.RowsAsync();

        driver.BudgetExhausted.ShouldBeFalse(
            "the reference run ran out of command budget, so it never reached an ending at all: " +
            Trace(driver));

        run.ShouldNotBeNull("the reference run left no run row behind: " + Trace(driver));
        run.BossDefeated.ShouldBeTrue(
            "the reference run did not beat the boss, so every chaos case comparing against it would " +
            "be comparing against a run that died on the way: " + Trace(driver));

        driver.Ending.ShouldBe(
            ChaosRunDriver.VictoryEnding,
            "the reference run ended some other way than a victory: " + Trace(driver));

        driver.Boundaries[^1].WireName.ShouldBe(
            "END_RUN", "the last command of the reference run was not END_RUN: " + Trace(driver));

        driver.StuckOn.ShouldBeNull("the reference run met a tile it could not leave: " + Trace(driver));
        driver.StalledAt.ShouldBeNull("a ROLL_DICE moved the reference run nowhere: " + Trace(driver));
    }

    /// <summary>
    /// 🔒 The run's coverage, taken rather than claimed: how far it travelled, how many kinds of
    /// tile it resolved, and how many battles and drafts it actually took.
    /// </summary>
    /// <remarks>
    /// The negative control rides here too: a fault-free run must show ZERO replays and one landed
    /// commit per boundary. A fixture where the schedule silently injected something, or where a
    /// boundary quietly replayed instead of executing, cannot satisfy both halves.
    /// </remarks>
    [Fact]
    public async Task The_reference_run_covers_real_ground_and_replays_nothing()
    {
        var world = await ChaosWorld.GearedAsync();
        var driver = await ChaosRunDriver.PlayAsync(world, FaultSchedules.None);

        var kinds = driver.Tiles.Distinct().ToArray();

        driver.Boundaries.Count.ShouldBeGreaterThanOrEqualTo(
            BoundaryFloor,
            "the reference run took too few command boundaries to be a full run: " + Trace(driver));

        kinds.Length.ShouldBeGreaterThanOrEqualTo(
            DistinctTileKindFloor,
            "the reference run resolved too few kinds of tile to be worth calling a run: " +
            string.Join(", ", kinds) + " — " + Trace(driver));

        driver.Tiles.ShouldContain(
            TileKind.Boss, "the reference run never arrived at the boss node: " + Trace(driver));

        driver.Visited.Distinct().Count().ShouldBeGreaterThanOrEqualTo(
            DistinctPositionFloor, "the reference run barely travelled: " + Trace(driver));

        driver.BattlesFought.ShouldBeGreaterThanOrEqualTo(
            BattleFloor, "the reference run fought too little: " + Trace(driver));

        driver.BattlesWon.ShouldBe(
            driver.BattlesFought,
            "the reference hero lost a fight, which means it is not geared the way the fixture " +
            "believes: " + Trace(driver));

        driver.DraftsSkipped.ShouldBeGreaterThanOrEqualTo(
            DraftFloor, "the reference run opened too few drafts: " + Trace(driver));

        driver.StageGatesCrossed.ShouldBe(
            ExpectedStageGates,
            "the reference run did not cross both stage boundaries: " + Trace(driver));

        driver.GatewayCalls.ShouldBe(
            driver.Boundaries.Count,
            "a fault-free run must take exactly one gateway call per boundary: " + Trace(driver));

        driver.ExecutionsCommitted.ShouldBe(
            driver.Boundaries.Count,
            "a fault-free run must land exactly one commit per boundary: " + Trace(driver));

        driver.ExecutionsLost.ShouldBe(0, "a fault-free run lost a transaction: " + Trace(driver));
        driver.ReplaysServed.ShouldBe(0, "a fault-free run replayed something: " + Trace(driver));
        driver.Resyncs.ShouldBe(0, "a fault-free run reconnected: " + Trace(driver));
        driver.Substitutions.ShouldBeEmpty("a fault-free run substituted a fault class.");

        driver.Bodies.Count.ShouldBe(
            driver.Boundaries.Count,
            "the client did not end up holding one body per boundary: " + Trace(driver));
    }

    /// <summary>
    /// What one fault-free run costs, measured and reported — the number every chaos schedule's own
    /// budget is a multiple of.
    /// </summary>
    [Fact]
    public async Task The_reference_run_costs_what_a_unit_test_can_afford()
    {
        // Built once outside the measurement: the first world in a process pays for loading the
        // whole shipped content set, which is not what one run costs.
        var warm = await ChaosWorld.GearedAsync();
        _ = await ChaosRunDriver.PlayAsync(warm, FaultSchedules.None);

        var built = Stopwatch.StartNew();
        var world = await ChaosWorld.GearedAsync();
        built.Stop();

        var drove = Stopwatch.StartNew();
        var driver = await ChaosRunDriver.PlayAsync(world, FaultSchedules.None);
        drove.Stop();

        _output.WriteLine(
            "one fault-free run: " + Ms(drove.Elapsed.TotalMilliseconds) + " ms over " +
            driver.Boundaries.Count.ToString(CultureInfo.InvariantCulture) + " boundaries, on a world " +
            "built in " + Ms(built.Elapsed.TotalMilliseconds) + " ms.");
        _output.WriteLine("tiles: " + string.Join(", ", driver.Tiles));
        _output.WriteLine(
            "battles " + driver.BattlesWon.ToString(CultureInfo.InvariantCulture) + "/" +
            driver.BattlesFought.ToString(CultureInfo.InvariantCulture) +
            ", drafts " + driver.DraftsSkipped.ToString(CultureInfo.InvariantCulture) +
            ", stage gates " + driver.StageGatesCrossed.ToString(CultureInfo.InvariantCulture) +
            ", run " + driver.Run);

        drove.Elapsed.TotalMilliseconds.ShouldBeLessThan(
            RunBudgetMs,
            "one fault-free run costs more than the chaos schedules can afford to pay dozens of " +
            "times over: " + Trace(driver));
    }

    private static string Ms(double value) =>
        value.ToString("F1", CultureInfo.InvariantCulture);

    private static string Trace(ChaosRunDriver driver) =>
        "ending '" + driver.Ending + "', " +
        driver.Boundaries.Count.ToString(CultureInfo.InvariantCulture) + " boundaries, " +
        driver.GatewayCalls.ToString(CultureInfo.InvariantCulture) + " gateway calls (" +
        driver.ExecutionsCommitted.ToString(CultureInfo.InvariantCulture) + " committed, " +
        driver.ExecutionsLost.ToString(CultureInfo.InvariantCulture) + " lost, " +
        driver.ReplaysServed.ToString(CultureInfo.InvariantCulture) + " replayed). Log:" +
        Environment.NewLine + string.Join(Environment.NewLine, driver.Log);
}
