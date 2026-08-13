using System.Diagnostics;
using System.Globalization;
using SlayIdleRepeat.BalanceHarness.Content;
using SlayIdleRepeat.BalanceHarness.Model;
using SlayIdleRepeat.BalanceHarness.Rules;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Rules.Combat;

namespace SlayIdleRepeat.BalanceHarness.Sweep;

/// <summary>
/// 🔒 `05` §9's sweep — every <c>(chapter, tier, buildArchetype)</c> cell, each build placed at
/// <c>ParPower(c, t)</c> by `29` §2.5.3's scaling rule and run against that chapter's boss node.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Cells are independent and every fight is seeded, so the sweep is order-independent.</b> A
/// cell's fights depend only on <c>(chapter, tier, archetype, fightIndex)</c> through
/// <see cref="SweepSeeds"/>; no cell reads another's result and nothing accumulates across them. That
/// is what makes <see cref="Run"/> safe to parallelise, and
/// <c>SweepDeterminismTests</c> pins it by running the same cell at one and at many threads and
/// comparing <c>LogHash</c> fight by fight.
/// </para>
/// <para>
/// 🔒 <b>The hero is built once per cell, not once per fight.</b> <c>ActorStats.From</c> allocates and
/// validates a fourteen-entry dictionary, and the bisection behind
/// <see cref="LoadoutScaling.ToPowerIndex"/> evaluates <c>PowerIndex</c> tens of times. At 10 000
/// fights per cell that work would dominate the measurement it is supposed to be setting up.
/// </para>
/// <para>
/// ⚠️ <b>What "clears (c, t)" means here, and what it does not.</b> `05` §9's guardrail 1 is stated
/// over a player clearing content at par. This harness measures it over the chapter's <b>boss
/// fight</b> at par, because that is the only encounter the repository authorises: the full-run
/// definition needs M3's board layer, <c>game-data/content/chapters/</c> is empty, and node
/// composition — how many enemies of which archetype stand between the player and the boss — is
/// unauthored. Inventing an encounter ladder to walk is exactly the harness-side invention `05` §9
/// forbids. A boss-only clear rate is an <b>upper bound</b> on a true run clear rate, since a real
/// run also has to survive 42 spine nodes to reach the boss, and the report says so.
/// </para>
/// </remarks>
public sealed class SweepRunner
{
    private readonly ContentSnapshot _content;

    /// <summary>Reads every catalogue the sweep needs and derives `29` §2.1's constant.</summary>
    public SweepRunner(ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(content);

        _content = content;
        Calibration = CalibrationBuilds.Read(content);
        ParPower = ParPowerTable.Read(content);
        Enemies = EnemyModel.Read(content);
        Bosses = BossRoster.Read(content);
        KPower = DerivedKPower.Derive(content, Calibration);
    }

    /// <summary>The loaded snapshot the heroes are graded against.</summary>
    public ContentSnapshot Content => _content;

    /// <summary>`29` §2.5's reference build, the five archetypes and the scaling rule.</summary>
    public CalibrationBuilds Calibration { get; }

    /// <summary>`29` §4's twenty-four par cells and `05` §9's clear-rate band.</summary>
    public ParPowerTable ParPower { get; }

    /// <summary>`05` §6's enemy level table and derivation coefficients.</summary>
    public EnemyModel Enemies { get; }

    /// <summary>`17`'s boss scripts, indexed by chapter.</summary>
    public BossRoster Bosses { get; }

    /// <summary>🔒 `29` §2.1's derived calibration constant. Reported, never written back.</summary>
    public DerivedKPower KPower { get; }

    /// <summary>
    /// 🔒 `29` §2.5.3 — places a loadout at <c>ParPower(c, t)</c> at level <c>EnemyLevel(c, t)</c>.
    /// </summary>
    /// <param name="chapter">The chapter, which picks the par cell and the level.</param>
    /// <param name="tier">The tier.</param>
    /// <param name="loadout">The unscaled statline.</param>
    /// <param name="powerMultiple">
    /// ⚠️ A multiple of par. <b>1.0 is the only value `05` §9's guardrails are stated at</b>, and the
    /// sweep never passes anything else. It exists for two things that must not be confused with a
    /// guardrail measurement: the discriminating controls, which deliberately place a build far above
    /// or below par so that the assertions are <em>known</em> to fire; and the shortfall diagnostic,
    /// which searches for the multiple at which a build reaches the 70% target and reports it as
    /// evidence. Neither is an assertion and neither retunes anything.
    /// </param>
    public ScaledLoadout ParHero(int chapter, Tier tier, StatLine loadout, double powerMultiple = 1.0) =>
        LoadoutScaling.ToPowerIndex(
            loadout,
            KPower.TargetPowerIndex(ParPower.Power(chapter, tier) * powerMultiple),
            Enemies.Level(chapter, tier),
            _content,
            Calibration.ScaledStats,
            Calibration.BisectionTolerance,
            Calibration.ScalarDecimalPlaces);

    /// <summary>
    /// Runs one cell: <paramref name="fights"/> seeded boss fights of one build at par.
    /// </summary>
    /// <param name="chapter">The chapter, which picks the boss script and the par cell.</param>
    /// <param name="tier">The tier.</param>
    /// <param name="archetypeId">The build archetype's id, used for the seed and the report.</param>
    /// <param name="loadout">
    /// The unscaled statline. Normally the archetype's own; the discriminating controls and the
    /// guardrail-6 elasticity table pass a perturbed one, which is why this is a parameter rather
    /// than looked up.
    /// </param>
    /// <param name="fights">How many seeded fights to run.</param>
    /// <param name="fightContent">
    /// 🔒 The snapshot the <b>fight</b> reads, which is the shipped one for the sweep and a
    /// <c>GameDataLoader.LoadWith</c> override for an experiment. The hero is always scaled against
    /// the <em>shipped</em> snapshot, so an override that changes a boss cannot silently move par.
    /// </param>
    /// <param name="heroPowerMultiple">
    /// ⚠️ See <see cref="ParHero"/>. 1.0 — par — is the only value the sweep and every guardrail use.
    /// 🔒 The <b>boss</b> power is unaffected: it is always <c>EnemyPower(42)</c> of the authored par
    /// cell, so a control that makes the hero stronger is a stronger hero at the same content and not
    /// a different fight.
    /// </param>
    public CellResult RunCell(
        int chapter,
        Tier tier,
        string archetypeId,
        StatLine loadout,
        int fights,
        ContentSnapshot? fightContent = null,
        double heroPowerMultiple = 1.0)
    {
        ArgumentNullException.ThrowIfNull(archetypeId);
        ArgumentNullException.ThrowIfNull(loadout);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fights);

        var content = fightContent ?? _content;
        var parPower = ParPower.Power(chapter, tier);
        var bossPower = NodePower.BossPower(parPower);
        var enemyLevel = Enemies.Level(chapter, tier);
        var boss = Bosses.ForChapter(chapter);

        var hero = ParHero(chapter, tier, loadout, heroPowerMultiple);
        var heroStats = hero.Stats.ToActorStats();

        var cellSeed = SweepSeeds.CellSeed(chapter, tier, archetypeId);
        var outcomes = new FightOutcome[fights];

        for (var fightIndex = 0; fightIndex < fights; fightIndex++)
        {
            var start = Stopwatch.GetTimestamp();
            var result = CombatSimulator.SimulateBossFight(
                SweepSeeds.FightSeed(cellSeed, fightIndex),
                heroStats,
                hero.Level,
                boss.Id,
                bossPower,
                enemyLevel,
                content);
            var elapsed = Stopwatch.GetTimestamp() - start;

            // 🔴 The highest phase the boss entered, off the log's own PhaseChange events. Phase 1 is
            // entered in the pre-tick and is not logged as a change, so the floor is 1. See
            // FightOutcome's remarks for why this is measured at all.
            var maxPhase = 1;
            foreach (var logEvent in result.Log)
            {
                if (logEvent.Type == CombatEventType.PhaseChange && logEvent.Value > maxPhase)
                {
                    maxPhase = (int)logEvent.Value;
                }
            }

            outcomes[fightIndex] = new FightOutcome(
                result.HeroWon,
                result.DurationTicks,
                result.HeroHpRemaining,
                result.LogHash,
                elapsed * 1_000_000.0 / Stopwatch.Frequency,
                maxPhase);
        }

        return new CellResult(
            chapter, tier, archetypeId, boss.Id, parPower, bossPower, enemyLevel, hero, outcomes);
    }

    /// <summary>
    /// 🔴 Runs one cell, returning the engine fault instead of propagating it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>A balance harness that dies on the first engine fault reports nothing about the other
    /// 119 cells, and this is not hypothetical.</b> M2-16a's first run crashed on Chapter 3 —
    /// <c>BOSS_OSSUARY_KING</c> summons on <c>ON_PHASE_ENTER</c> phase 1 and the pre-tick walked the
    /// roster with a <c>foreach</c> — and its second crashed on Chapter 8, where
    /// <c>BOSS_DICELORD_P3_ALL_IN</c> is a <c>PERIODIC DAMAGE</c> on <c>CURRENT_TARGET</c> and a
    /// periodic's context carries no current target. A fault is a <b>finding</b>: it is caught here,
    /// named in the report, and counted as a non-pass so <c>assert</c> still exits non-zero.
    /// </para>
    /// <para>
    /// ⚠️ <see cref="RunCell"/> itself does NOT catch, and must not: the test suite asserts on real
    /// exceptions, and a swallowed <c>ArgumentException</c> from <c>ActorStats.From</c> would turn a
    /// scaling bug into a silently empty cell.
    /// </para>
    /// </remarks>
    public (CellResult? Cell, string? Fault) TryRunCell(
        int chapter,
        Tier tier,
        string archetypeId,
        StatLine loadout,
        int fights,
        ContentSnapshot? fightContent = null,
        double heroPowerMultiple = 1.0)
    {
        try
        {
            return (
                RunCell(chapter, tier, archetypeId, loadout, fights, fightContent, heroPowerMultiple),
                null);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return (
                null,
                $"C{chapter.ToString(CultureInfo.InvariantCulture)} {tier} {archetypeId} @" +
                $"{heroPowerMultiple.ToString("0.##", CultureInfo.InvariantCulture)}x par — " +
                $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    /// <summary>
    /// 🔒 The whole sweep, parallelised across cells.
    /// </summary>
    /// <remarks>
    /// Results are written into a pre-sized array by cell index, so the returned order is the
    /// scope's order regardless of the order the threads finished in. Nothing is appended to a shared
    /// list and no result depends on scheduling.
    /// </remarks>
    public SweepResult Run(SweepScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var plans = new List<(int Chapter, Tier Tier, BuildArchetype Archetype)>();
        foreach (var chapter in scope.Chapters)
        {
            foreach (var tier in scope.Tiers)
            {
                foreach (var archetypeId in scope.ArchetypeIds)
                {
                    plans.Add((chapter, tier, Calibration.Archetype(archetypeId)));
                }
            }
        }

        var cells = new CellResult?[plans.Count];
        var faults = new string?[plans.Count];
        var stopwatch = Stopwatch.StartNew();

        Parallel.For(
            0,
            plans.Count,
            new ParallelOptions { MaxDegreeOfParallelism = scope.MaxDegreeOfParallelism },
            i =>
            {
                var (chapter, tier, archetype) = plans[i];
                (cells[i], faults[i]) =
                    TryRunCell(chapter, tier, archetype.Id, archetype.Stats, scope.Fights);
            });

        stopwatch.Stop();

        var (sampleCell, sample) = MeasureUncontendedCost(scope);

        return new SweepResult(
            cells.Where(c => c is not null).Select(c => c!).ToArray(),
            faults.Where(f => f is not null).Select(f => f!).ToArray(),
            KPower,
            stopwatch.Elapsed,
            scope.MaxDegreeOfParallelism,
            sample,
            sampleCell);
    }

    /// <summary>
    /// ⚠️ A short SINGLE-THREADED cost sample, because the per-fight timings inside a parallel sweep
    /// are contended and overstate what `05`'s &lt; 5 ms budget is about.
    /// </summary>
    /// <remarks>
    /// The budget is a statement about one simulation on one core — the client running a fight
    /// locally (`14` §2.4). Under <c>n</c> threads on <c>n</c> cores, memory bandwidth, GC and the
    /// timer itself inflate each measurement, so the parallel numbers are reported as what they are
    /// and this uncontended sample is reported beside them.
    /// </remarks>
    private (string Cell, IReadOnlyList<double> Sample) MeasureUncontendedCost(SweepScope scope)
    {
        var chapter = scope.Chapters[^1];
        var tier = scope.Tiers[^1];
        var archetype = Calibration.Archetype(scope.ArchetypeIds[0]);
        var fights = Math.Min(scope.Fights, SweepScope.UncontendedCostSampleFights);

        var (cell, _) = TryRunCell(chapter, tier, archetype.Id, archetype.Stats, fights);

        if (cell is null)
        {
            return ("(the cost sample cell faulted)", []);
        }

        var sample = cell.Fights.Select(f => f.ElapsedMicroseconds).ToArray();
        Array.Sort(sample);

        return (cell.Key, sample);
    }
}

/// <summary>What to sweep.</summary>
/// <param name="Chapters">The chapters, ascending.</param>
/// <param name="Tiers">The tiers.</param>
/// <param name="ArchetypeIds">The build archetype ids.</param>
/// <param name="Fights">🔒 Fights per cell. `05` §9's figure is 10 000.</param>
/// <param name="MaxDegreeOfParallelism">Threads. 1 makes the run strictly sequential.</param>
public sealed record SweepScope(
    IReadOnlyList<int> Chapters,
    IReadOnlyList<Tier> Tiers,
    IReadOnlyList<string> ArchetypeIds,
    int Fights,
    int MaxDegreeOfParallelism)
{
    /// <summary>🔒 `05` §9 — <em>"10 000 seeded fights per (chapter, tier, buildArchetype)"</em>.</summary>
    public const int DocumentedFightsPerCell = 10_000;

    /// <summary>Fights in the single-threaded cost sample. Enough for a p90, cheap enough to always run.</summary>
    public const int UncontendedCostSampleFights = 500;

    /// <summary>How many cells this scope covers.</summary>
    public int CellCount => Chapters.Count * Tiers.Count * ArchetypeIds.Count;

    /// <summary>The scope as one line, for the report header.</summary>
    public override string ToString() =>
        $"{Chapters.Count.ToString(CultureInfo.InvariantCulture)} chapters x " +
        $"{Tiers.Count.ToString(CultureInfo.InvariantCulture)} tiers x " +
        $"{ArchetypeIds.Count.ToString(CultureInfo.InvariantCulture)} archetypes = " +
        $"{CellCount.ToString(CultureInfo.InvariantCulture)} cells x " +
        $"{Fights.ToString(CultureInfo.InvariantCulture)} fights";
}

/// <summary>Everything the sweep measured.</summary>
/// <param name="Cells">One per <c>(chapter, tier, archetype)</c> that ran, in scope order.</param>
/// <param name="Faults">
/// 🔴 One line per cell the engine could not simulate at all. A non-empty list is a FINDING and makes
/// <c>assert</c> exit non-zero — a sweep that quietly covered fewer cells than it was asked for would
/// otherwise report guardrails over a subject set nobody chose.
/// </param>
/// <param name="KPower">`29` §2.1's derived constant.</param>
/// <param name="Elapsed">Wall-clock of the parallel sweep.</param>
/// <param name="DegreeOfParallelism">Threads it ran on.</param>
/// <param name="UncontendedCostSampleMicroseconds">Ascending single-threaded per-fight costs.</param>
/// <param name="UncontendedCostSampleCell">Which cell the sample came from.</param>
public sealed record SweepResult(
    IReadOnlyList<CellResult> Cells,
    IReadOnlyList<string> Faults,
    DerivedKPower KPower,
    TimeSpan Elapsed,
    int DegreeOfParallelism,
    IReadOnlyList<double> UncontendedCostSampleMicroseconds,
    string UncontendedCostSampleCell)
{
    /// <summary>Total fights simulated.</summary>
    public long FightCount => Cells.Sum(c => (long)c.FightCount);

    /// <summary>Median single-threaded per-fight cost, milliseconds.</summary>
    public double UncontendedMedianMilliseconds =>
        CellResult.Percentile(UncontendedCostSampleMicroseconds, 0.50) / 1000.0;

    /// <summary>p90 single-threaded per-fight cost, milliseconds.</summary>
    public double UncontendedP90Milliseconds =>
        CellResult.Percentile(UncontendedCostSampleMicroseconds, 0.90) / 1000.0;

    /// <summary>Worst single-threaded per-fight cost, milliseconds.</summary>
    public double UncontendedMaxMilliseconds =>
        UncontendedCostSampleMicroseconds.Count == 0
            ? double.NaN
            : UncontendedCostSampleMicroseconds[^1] / 1000.0;
}
