using System.Globalization;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Rules.Board;

/// <summary>
/// The enemy power curve a board node's fight is scaled by:
/// <c>EnemyPower(i) = ChapterPowerTarget(c) · TierMult(t) · (1 + 0.035·i) · StageMult(s)</c>.
/// </summary>
/// <remarks>
/// <para>
/// <paramref name="chapterPowerTarget"/> is a parameter, not a table read: it is not authored
/// anywhere in content today, so this formula accepts it from the caller rather than inventing a
/// chapter table here that would be trusted downstream as the real curve.
/// </para>
/// <para>
/// Nothing is rounded — this is an input to the accumulation that gets rounded, not a result of
/// it. A caller that needs an integer rounds at the point it needs one.
/// </para>
/// <para>
/// The linear index is consumed, never recomputed: <c>BoardNode.LinearIndex</c> is already
/// computed by <see cref="BoardGenerator"/> under the branch-parallel rule. Recomputing it from a
/// node id here would be a second source of truth for the one number the whole curve is a
/// function of.
/// </para>
/// </remarks>
internal static class EnemyPowerFormula
{
    /// <summary>The per-node growth term's coefficient.</summary>
    private const double PerNodeGrowth = 0.035;

    /// <summary><c>StageMult</c> for stage 1.</summary>
    private const double Stage1Multiplier = 1.00;

    /// <inheritdoc cref="Stage1Multiplier"/>
    private const double Stage2Multiplier = 1.15;

    /// <inheritdoc cref="Stage1Multiplier"/>
    private const double Stage3Multiplier = 1.35;

    /// <summary><c>StageMult</c> for the boss, which belongs to no stage.</summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b><c>02</c> §4.3 authors <c>2.20</c> here, and <c>2.20</c> puts the boss beyond the
    /// fight cap.</b> The boss node's linear index is the board's last, so the growth term has
    /// already multiplied the chapter target by <c>1 + 0.035 x 42 = 2.47</c> before this factor is
    /// applied: at <c>2.20</c> a boss is <b>5.43x</b> its chapter's par. Against chapter 1's old par
    /// of 1000 that is a Thornmaw of <c>HP 7825 / DEF 130</c>, which the calibrated par build of
    /// <c>tuning/calibration_builds.json</c> (<c>referenceParBuild</c>, Legend 10, <c>ATK 145</c>)
    /// needs <b>~102 s</b> to kill — past <c>CombatRules</c>' 90 s cap, where a timeout counts as a
    /// loss, and long past <c>SYS_ENRAGE</c>'s 70 s ramp. Every boss in the game was unbeatable at
    /// par, and <c>.claude/retros/M2.md</c> records the balance harness finding it from the other
    /// side: over 1.2 M real boss fights, <em>"no build at par ever takes a boss below 66 % HP"</em>,
    /// so no authored phase-2 or phase-3 mechanic had ever been reached at its intended difficulty.
    /// </para>
    /// <para>
    /// <c>1.35</c> leaves the boss at <c>3.34x</c> its chapter's par, which is the figure that puts
    /// the fight in the band <c>17</c> §1 authors and <c>BossDurationGuardrails</c> restates — 35-60 s
    /// at par. It is the same fight <c>02</c> §4.3's <c>2.20</c> describes if the growth term is NOT
    /// applied to the boss node: the two readings differ by exactly the double count.
    /// </para>
    /// <para>
    /// 🔒 Global rather than per chapter, unlike the elite multiplier on
    /// <c>enemies.json#/chapterPools</c>. What makes one boss harder than another is its own four
    /// coefficients, and the double count above is not a property of any one chapter — a
    /// per-chapter boss multiplier would leave it hidden in seven more places.
    /// </para>
    /// </remarks>
    private const double BossMultiplier = 1.35;

    /// <summary><c>TierMult</c> for the three difficulty tiers.</summary>
    private const double NormalMultiplier = 1.0;

    /// <inheritdoc cref="NormalMultiplier"/>
    private const double HeroicMultiplier = 4.0;

    /// <inheritdoc cref="NormalMultiplier"/>
    private const double MythicMultiplier = 16.0;

    /// <summary>The enemy power for one node of one run.</summary>
    /// <param name="chapterPowerTarget">Supplied by the caller — see this type's remarks.</param>
    /// <param name="tier">The run's difficulty tier.</param>
    /// <param name="linearIndex">
    /// The linear node index — <c>BoardNode.LinearIndex</c>, <c>0..42</c> as the board generator
    /// computes it. Never negative.
    /// </param>
    /// <param name="stage">
    /// The stage — <c>1</c>, <c>2</c>, <c>3</c>, or <see cref="BoardGraph.BossStage"/> for the boss
    /// node.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="tier"/> is not one of the three tiers, <paramref name="linearIndex"/> is
    /// negative, or <paramref name="stage"/> is not one of the four stages.
    /// </exception>
    internal static double Compute(
        double chapterPowerTarget, DifficultyTier tier, int linearIndex, int stage)
    {
        if (linearIndex < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(linearIndex),
                linearIndex,
                "03 §1.1's linear node index runs from 0 (the first spine node) upwards; " +
                Text(linearIndex) + " is below it. ⚠️ There is deliberately no UPPER bound here: " +
                "42 is the shipped board's last index, but the ceiling is a property of the " +
                "specific generated board (BoardGraph.SpineNode already checks it against that " +
                "board's own count), and restating it as a constant would be a partial invariant " +
                "wearing the real one's name — the same posture Run.Position takes.");
        }

        return chapterPowerTarget
               * TierMultiplier(tier)
               * (1.0 + (PerNodeGrowth * linearIndex))
               * StageMultiplier(stage);
    }

    /// <summary><c>TierMult(t)</c>.</summary>
    private static double TierMultiplier(DifficultyTier tier) => tier switch
    {
        DifficultyTier.NORMAL => NormalMultiplier,
        DifficultyTier.HEROIC => HeroicMultiplier,
        DifficultyTier.MYTHIC => MythicMultiplier,
        _ => throw new ArgumentOutOfRangeException(
            nameof(tier),
            tier,
            "10 §7 fixes three difficulty tiers and 02 §4.3 authors a TierMult for each. " +
            "DifficultyTier has no zero member on purpose, so an undefined value here is an " +
            "uninitialised field rather than a difficulty the game offers."),
    };

    /// <summary><c>StageMult(s)</c>.</summary>
    private static double StageMultiplier(int stage) => stage switch
    {
        1 => Stage1Multiplier,
        2 => Stage2Multiplier,
        3 => Stage3Multiplier,
        BoardGraph.BossStage => BossMultiplier,
        _ => throw new ArgumentOutOfRangeException(
            nameof(stage),
            stage,
            "03 §1 gives a board three stages (1, 2, 3) plus a boss node that belongs to none of " +
            "them and is carried as BoardGraph.BossStage. " + Text(stage) + " is not one of the " +
            "four, so 02 §4.3 authors no StageMult for it."),
    };

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);
}
