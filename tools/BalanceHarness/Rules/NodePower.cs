using SlayIdleRepeat.BalanceHarness.Model;

namespace SlayIdleRepeat.BalanceHarness.Rules;

/// <summary>
/// <c>EnemyPower(i)</c> — the one formula this harness computes itself, and the one place its
/// constants live.
/// </summary>
/// <remarks>
/// <para>
/// <code>
/// EnemyPower(i) = ChapterPowerTarget(c) * TierMult(t) * (1 + 0.035 * i) * StageMult(s)
/// StageMult:  Stage1 = 1.00, Stage2 = 1.15, Stage3 = 1.35, Boss = 2.20
/// </code>
/// <c>ChapterPowerTarget(c) × TierMult(t)</c> is <c>ParPower(c, t)</c>, read off
/// <c>tuning/par_power.json</c>'s twenty-four authored cells — never recomputed from that file's
/// <c>defaultFill</c>, which is explicitly a default, not a constraint.
/// </para>
/// <para>
/// <see cref="PerNodePowerGrowth"/>, the three stage multipliers and <see cref="BossStageMultiplier"/>
/// have no home in <c>game-data/</c> — they are transcribed here rather than added to a tuning file
/// this tool may not edit, and the gap is reported rather than closed.
/// </para>
/// <para>
/// <see cref="BossStageMultiplier"/> is applied exactly once, here: every boss-fight type in
/// <c>Core</c> receives the boss power as a parameter rather than deriving it, precisely so the stage
/// multiplier can't be applied twice. <see cref="BossPower"/> is the harness's single application of
/// it.
/// </para>
/// <para>
/// <see cref="BossNodeIndex"/> is 42 and is not a free choice: spine nodes are numbered 0..41 across
/// the three stages (12 + 14 + 16), so the boss is the forty-third node.
/// </para>
/// </remarks>
public static class NodePower
{
    /// <summary>The per-node linear power growth. Authored in no data file. See the remarks.</summary>
    public const double PerNodePowerGrowth = 0.035;

    /// <summary><c>StageMult.Stage1</c>. Authored in no data file.</summary>
    public const double Stage1Multiplier = 1.00;

    /// <summary><c>StageMult.Stage2</c>. Authored in no data file.</summary>
    public const double Stage2Multiplier = 1.15;

    /// <summary><c>StageMult.Stage3</c>. Authored in no data file.</summary>
    public const double Stage3Multiplier = 1.35;

    /// <summary><c>StageMult.Boss</c>. Applied exactly once, in <see cref="BossPower"/>.</summary>
    public const double BossStageMultiplier = 2.20;

    /// <summary>The boss node's linear index. 12 + 14 + 16 spine nodes are 0..41.</summary>
    public const int BossNodeIndex = 42;

    /// <summary><c>EnemyPower(i)</c> for the boss node of a <c>(chapter, tier)</c> at par.</summary>
    /// <param name="parPower">
    /// <c>ParPower(c, t)</c>, i.e. <c>ChapterPowerTarget(c) × TierMult(t)</c>, read off
    /// <c>tuning/par_power.json</c>.
    /// </param>
    /// <remarks>
    /// The result is what <c>CombatSimulator.SimulateBossFight</c>'s <c>bossPower</c> parameter wants:
    /// the stage multiplier is already inside it.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="parPower"/> is not finite and positive.</exception>
    public static double BossPower(double parPower)
    {
        if (!double.IsFinite(parPower) || parPower <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(parPower),
                parPower,
                "ParPower(c, t) is a positive finite power target read off tuning/par_power.json. A " +
                "zero or negative one means the cell was not read but defaulted.");
        }

        return HarnessRounding.Round(
            parPower * (1.0 + (PerNodePowerGrowth * BossNodeIndex)) * BossStageMultiplier);
    }

    /// <summary><c>EnemyPower(i)</c> for any node, for completeness of the transcription.</summary>
    /// <param name="parPower"><c>ParPower(c, t)</c>.</param>
    /// <param name="nodeIndex">The linear walk index.</param>
    /// <param name="stageMultiplier">One of the four <see cref="Stage1Multiplier"/>-family constants.</param>
    /// <remarks>
    /// The sweep only ever asks for the boss node, so this exists to keep the formula stated in one
    /// piece rather than as a boss-shaped fragment. Used by guardrail 5, which walks every node's
    /// power to find the highest reachable enemy DEF in a chapter.
    /// </remarks>
    public static double NodeEnemyPower(double parPower, int nodeIndex, double stageMultiplier) =>
        HarnessRounding.Round(
            parPower * (1.0 + (PerNodePowerGrowth * nodeIndex)) * stageMultiplier);
}
