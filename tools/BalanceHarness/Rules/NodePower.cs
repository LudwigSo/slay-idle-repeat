using SlayIdleRepeat.BalanceHarness.Model;

namespace SlayIdleRepeat.BalanceHarness.Rules;

/// <summary>
/// 🔒 `02` §4.3's <c>EnemyPower(i)</c> — the ONE formula this harness computes itself, and the ONE
/// place its 📐 constants live.
/// </summary>
/// <remarks>
/// <para>
/// `02` §4.3, verbatim:
/// <code>
/// EnemyPower(i) = ChapterPowerTarget(c) * TierMult(t) * (1 + 0.035 * i) * StageMult(s)
/// StageMult:  Stage1 = 1.00, Stage2 = 1.15, Stage3 = 1.35, Boss = 2.20
/// </code>
/// <c>ChapterPowerTarget(c) × TierMult(t)</c> <b>is</b> <c>ParPower(c, t)</c>, read off
/// <c>tuning/par_power.json</c>'s twenty-four authored cells — never recomputed from that file's
/// <c>defaultFill</c>, which the file itself calls <em>"the default fill, not a constraint"</em>.
/// </para>
/// <para>
/// 🔴 <b>ERRATUM — these five numbers have no home in <c>game-data/</c>.</b>
/// <see cref="PerNodePowerGrowth"/>, <see cref="Stage1Multiplier"/>, <see cref="Stage2Multiplier"/>,
/// <see cref="Stage3Multiplier"/> and <see cref="BossStageMultiplier"/> are transcribed from `02`
/// §4.3 and are authored in no tuning or content file. `21` §3.1 fixes <c>tuning/</c> at sixteen
/// documents and a seventeenth is a bug, and M2-16a may not edit <c>game-data/</c> at all, so they
/// could not be added here even if that were the right call. They are stated once, named, cited and
/// greppable rather than inlined at the two call sites — and the gap is reported rather than closed.
/// </para>
/// <para>
/// 🔒 <b><see cref="BossStageMultiplier"/> is applied exactly once, here.</b> `05` §6.3 and `17` §1:
/// <em>"do not multiply by 2.20 again."</em> <c>CombatSimulator.SimulateBossFight</c> takes
/// <c>bossPower</c> as a parameter for precisely this reason — every type under
/// <c>Rules/Combat/Bosses/</c> in <c>Core</c> receives the power rather than deriving it, so that the
/// stage multiplier cannot be applied twice. <see cref="BossPower"/> is the harness's single
/// application of it and nothing else in this tool multiplies by 2.20.
/// </para>
/// <para>
/// 🔒 <b><see cref="BossNodeIndex"/> is 42 and is not a free choice.</b> `03` §1.1: <em>"Spine nodes
/// are numbered 0..41 in walk order across the three stages (12 + 14 + 16); the boss node is 42."</em>
/// 12 + 14 + 16 = 42, so the boss is the forty-third node and the linear index the growth term takes.
/// </para>
/// </remarks>
public static class NodePower
{
    /// <summary>📐 `02` §4.3 — the per-node linear power growth. Authored in no data file. See the remarks.</summary>
    public const double PerNodePowerGrowth = 0.035;

    /// <summary>📐 `02` §4.3 — <c>StageMult.Stage1</c>. Authored in no data file.</summary>
    public const double Stage1Multiplier = 1.00;

    /// <summary>📐 `02` §4.3 — <c>StageMult.Stage2</c>. Authored in no data file.</summary>
    public const double Stage2Multiplier = 1.15;

    /// <summary>📐 `02` §4.3 — <c>StageMult.Stage3</c>. Authored in no data file.</summary>
    public const double Stage3Multiplier = 1.35;

    /// <summary>🔒 📐 `02` §4.3 — <c>StageMult.Boss</c>. Applied exactly once, in <see cref="BossPower"/>.</summary>
    public const double BossStageMultiplier = 2.20;

    /// <summary>🔒 `03` §1.1 — the boss node's linear index. 12 + 14 + 16 spine nodes are 0..41.</summary>
    public const int BossNodeIndex = 42;

    /// <summary>
    /// 🔒 `02` §4.3 — <c>EnemyPower(i)</c> for the boss node of a <c>(chapter, tier)</c> at par.
    /// </summary>
    /// <param name="parPower">
    /// <c>ParPower(c, t)</c>, i.e. `02` §4.3's <c>ChapterPowerTarget(c) × TierMult(t)</c>, read off
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

    /// <summary>
    /// `02` §4.3 — <c>EnemyPower(i)</c> for any node, for completeness of the transcription.
    /// </summary>
    /// <param name="parPower"><c>ParPower(c, t)</c>.</param>
    /// <param name="nodeIndex">`03` §1.1's linear walk index.</param>
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
