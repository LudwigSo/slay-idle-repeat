using System.Globalization;
using SlayIdleRepeat.BalanceHarness.Model;
using SlayIdleRepeat.BalanceHarness.Rules;

namespace SlayIdleRepeat.BalanceHarness.Sweep;

/// <summary>One fight's measured outcome.</summary>
/// <param name="HeroWon">Did the hero clear the boss.</param>
/// <param name="DurationTicks">Ticks the fight ran. 20 ticks is one second; the cap is 1800.</param>
/// <param name="HeroHpRemaining">The hero's HP at the end. Zero on a loss.</param>
/// <param name="LogHash">The replay hash — the determinism handle.</param>
/// <param name="ElapsedMicroseconds">
/// Wall-clock cost of the <c>SimulateBossFight</c> call itself, in microseconds. Under
/// <c>--parallel</c> these are contended measurements and read high; the report states an uncontended
/// sample separately.
/// </param>
/// <param name="MaxBossPhase">
/// The highest boss phase the fight reached, read off the log's <c>PhaseChange</c> events — recorded
/// because boss phases are HP bands, so a hero that dies early never sees a later phase's mechanics,
/// and without this an experiment reading "no effect" cannot tell that apart from "never fired".
/// </param>
public readonly record struct FightOutcome(
    bool HeroWon,
    int DurationTicks,
    double HeroHpRemaining,
    ulong LogHash,
    double ElapsedMicroseconds,
    int MaxBossPhase)
{
    /// <summary>The fight's duration in seconds.</summary>
    public double Seconds => DurationBands.Seconds(DurationTicks);
}

/// <summary>One <c>(chapter, tier, archetype)</c> cell of the sweep and everything measured over it.</summary>
/// <remarks>
/// The duration statistics are taken over cleared fights only (every member that does so says so in
/// its name): a loss's duration is the length of the hero's death or the 90 s cap, a different
/// quantity that would drag every percentile toward the cap the worse a build is.
/// </remarks>
public sealed class CellResult
{
    private readonly double[] _clearedSecondsSorted;
    private readonly double[] _costsSorted;

    /// <summary>Builds a cell result from its fights.</summary>
    public CellResult(
        int chapter,
        Tier tier,
        string archetypeId,
        string bossId,
        double parPower,
        double bossPower,
        int enemyLevel,
        ScaledLoadout hero,
        IReadOnlyList<FightOutcome> fights)
    {
        ArgumentNullException.ThrowIfNull(hero);
        ArgumentNullException.ThrowIfNull(fights);

        Chapter = chapter;
        Tier = tier;
        ArchetypeId = archetypeId;
        BossId = bossId;
        ParPower = parPower;
        BossPower = bossPower;
        EnemyLevel = enemyLevel;
        Hero = hero;
        Fights = fights;

        _clearedSecondsSorted = fights.Where(f => f.HeroWon).Select(f => f.Seconds).ToArray();
        Array.Sort(_clearedSecondsSorted);

        _costsSorted = fights.Select(f => f.ElapsedMicroseconds).ToArray();
        Array.Sort(_costsSorted);
    }

    /// <summary>The chapter.</summary>
    public int Chapter { get; }

    /// <summary>The tier.</summary>
    public Tier Tier { get; }

    /// <summary>The build archetype's id.</summary>
    public string ArchetypeId { get; }

    /// <summary>The boss script fought.</summary>
    public string BossId { get; }

    /// <summary><c>ParPower(c, t)</c> for this cell.</summary>
    public double ParPower { get; }

    /// <summary><c>EnemyPower(42)</c>, with <c>StageMult.Boss</c> already inside it.</summary>
    public double BossPower { get; }

    /// <summary><c>EnemyLevel(c, t)</c> — the hero's level too.</summary>
    public int EnemyLevel { get; }

    /// <summary>The par-scaled hero.</summary>
    public ScaledLoadout Hero { get; }

    /// <summary>Every fight, in fight-index order.</summary>
    public IReadOnlyList<FightOutcome> Fights { get; }

    /// <summary>How many fights ran.</summary>
    public int FightCount => Fights.Count;

    /// <summary>How many the hero won.</summary>
    public int ClearCount => _clearedSecondsSorted.Length;

    /// <summary>Guardrail 1's measurement — cleared ÷ fought.</summary>
    public double ClearRate => FightCount == 0 ? 0.0 : (double)ClearCount / FightCount;

    /// <summary>Median seconds over <b>cleared</b> fights. <c>NaN</c> when nothing cleared.</summary>
    public double MedianClearedSeconds => Percentile(_clearedSecondsSorted, 0.50);

    /// <summary>p10 seconds over <b>cleared</b> fights. <c>NaN</c> when nothing cleared.</summary>
    public double P10ClearedSeconds => Percentile(_clearedSecondsSorted, 0.10);

    /// <summary>p90 seconds over <b>cleared</b> fights. <c>NaN</c> when nothing cleared.</summary>
    public double P90ClearedSeconds => Percentile(_clearedSecondsSorted, 0.90);

    /// <summary>The fastest clear. <c>NaN</c> when nothing cleared.</summary>
    public double MinClearedSeconds =>
        _clearedSecondsSorted.Length == 0 ? double.NaN : _clearedSecondsSorted[0];

    /// <summary>The slowest clear. <c>NaN</c> when nothing cleared.</summary>
    public double MaxClearedSeconds =>
        _clearedSecondsSorted.Length == 0
            ? double.NaN
            : _clearedSecondsSorted[^1];

    /// <summary>Cleared fights faster than the 12 s floor — the raw tail, reported beside the median.</summary>
    public int ClearsUnderHardFloor =>
        _clearedSecondsSorted.Count(s => s < DurationBands.HardFloorSeconds);

    /// <summary>Cleared fights slower than the 70 s ceiling — the raw tail.</summary>
    public int ClearsOverHardCeiling =>
        _clearedSecondsSorted.Count(s => s > DurationBands.HardCeilingSeconds);

    /// <summary>The highest boss phase any fight in this cell reached. 1 means no mechanic ever fired.</summary>
    public int MaxBossPhaseReached => Fights.Count == 0 ? 0 : Fights.Max(f => f.MaxBossPhase);

    /// <summary>The share of fights that reached boss phase 3, where most authored mechanics live.</summary>
    public double Phase3Share =>
        Fights.Count == 0 ? 0.0 : (double)Fights.Count(f => f.MaxBossPhase >= 3) / Fights.Count;

    /// <summary>Median per-fight simulation cost, microseconds.</summary>
    public double MedianCostMicroseconds => Percentile(_costsSorted, 0.50);

    /// <summary>p90 per-fight simulation cost, microseconds.</summary>
    public double P90CostMicroseconds => Percentile(_costsSorted, 0.90);

    /// <summary>Worst per-fight simulation cost, microseconds.</summary>
    public double MaxCostMicroseconds => _costsSorted.Length == 0 ? double.NaN : _costsSorted[^1];

    /// <summary>The cell as a stable, sortable key: <c>C1 NORMAL ARCH_CRIT</c>.</summary>
    public string Key =>
        $"C{Chapter.ToString(CultureInfo.InvariantCulture)} {Tier} {ArchetypeId}";

    /// <summary>
    /// The nearest-rank percentile of an ascending array.
    /// </summary>
    /// <remarks>
    /// Nearest-rank rather than interpolated: it always returns a value that actually occurred, which
    /// for a tick-quantised duration is the honest answer — an interpolated "median" of 23.15 s names
    /// a fight length the 20 Hz clock cannot produce.
    /// </remarks>
    public static double Percentile(IReadOnlyList<double> sortedAscending, double fraction)
    {
        ArgumentNullException.ThrowIfNull(sortedAscending);

        if (sortedAscending.Count == 0)
        {
            return double.NaN;
        }

        var rank = (int)Math.Ceiling(fraction * sortedAscending.Count) - 1;

        return sortedAscending[Math.Clamp(rank, 0, sortedAscending.Count - 1)];
    }
}
