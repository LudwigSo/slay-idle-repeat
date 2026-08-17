using System.Globalization;

namespace SlayIdleRepeat.Core.Content;

/// <summary>
/// The Legend Level <b>curve</b> — the three numbers <see cref="LegendTuning"/> deliberately leaves
/// alone, read out of <c>tuning/progression.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="LegendTuning"/> reads the range and says in as many words that the coefficient, the
/// exponent and the talent-point grant belong to <em>"the level-up rule that computes with them"</em>.
/// This is that reader; the rule is <c>Rules.Hero.LegendLevelCurve</c>.
/// </para>
/// <para>
/// 📐 The exponent is the single dial the whole long-term pacing hangs on, and the document says so
/// itself. It is read here and never restated in code — not as a default, not as a fallback, not in
/// a comment claiming a value. <c>xpExponentSweepRange</c> is deliberately <b>not</b> read: it is the
/// economy simulator's sweep bound, not a runtime number, and reading it here would make a
/// simulator-only key look like something a rule depends on.
/// </para>
/// <para>
/// The two cumulative totals in the document disagree with each other and the formula settles it.
/// The table's level-200 row reads ~3.04M and the prose two lines below reads ~3.07M; summing the
/// authored formula over the 199 level-ups a player actually makes reproduces the table. Neither
/// rounded total is transcribed anywhere in this codebase, and that is the point — the formula is
/// authoritative, the totals are illustrative, and a transcribed total is a number that goes stale
/// the first time the exponent moves.
/// </para>
/// <para>
/// A hole is never a default: every read below goes through <see cref="ContentSnapshot"/>'s typed
/// readers, which throw <see cref="UnauthorisedTunableException"/> rather than answer zero.
/// </para>
/// </remarks>
internal sealed class LegendCurveTuning
{
    /// <summary>The document the curve lives in.</summary>
    internal const string DocumentPath = "tuning/progression.json";

    private const string LegendLevelPointer = DocumentPath + "#/legendLevel";

    /// <summary>The multiplier in front of the power term. 120 as shipped.</summary>
    internal const string CoefficientReference = LegendLevelPointer + "/xpCoefficient";

    /// <summary>📐 The exponent. 1.05 as shipped — the single dial controlling long-term pacing.</summary>
    internal const string ExponentReference = LegendLevelPointer + "/xpExponent";

    /// <summary>Talent Points granted per Legend Level. 1 as shipped.</summary>
    internal const string TalentPointsPerLevelReference = LegendLevelPointer + "/talentPointsPerLevel";

    private LegendCurveTuning(double coefficient, double exponent, int talentPointsPerLevel)
    {
        Coefficient = coefficient;
        Exponent = exponent;
        TalentPointsPerLevel = talentPointsPerLevel;
    }

    /// <summary>The multiplier in front of the power term. 120 as shipped.</summary>
    internal double Coefficient { get; }

    /// <summary>📐 The exponent. 1.05 as shipped.</summary>
    internal double Exponent { get; }

    /// <summary>Talent Points granted by one Legend Level. 1 as shipped.</summary>
    internal int TalentPointsPerLevel { get; }

    /// <summary>
    /// Reads the curve. Throws rather than defaulting on anything missing, unauthorised, mistyped or
    /// nonsensical.
    /// </summary>
    /// <param name="content">The version-stamped snapshot the command is reading.</param>
    /// <returns>The curve numbers.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is null.</exception>
    /// <exception cref="MissingContentException">A pointer is not there.</exception>
    /// <exception cref="UnauthorisedTunableException">A pointer holds a deliberate <c>null</c>.</exception>
    /// <exception cref="ContentTypeMismatchException">A whole-number tunable holds a fraction.</exception>
    /// <exception cref="InvalidTunableException">A value is authorised but unusable.</exception>
    internal static LegendCurveTuning Read(ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var coefficient = content.ReadDouble(CoefficientReference);
        var exponent = content.ReadDouble(ExponentReference);
        var talentPointsPerLevel = content.ReadInt32(TalentPointsPerLevelReference);

        if (!double.IsFinite(coefficient) || coefficient <= 0d)
        {
            throw new InvalidTunableException(
                CoefficientReference,
                "A level-up must cost something. 07 §1.1 authors 120 * L^1.05; a coefficient of " +
                Render(coefficient) + " makes every level either free or negative, and a free level " +
                "is an unbounded loop the moment any XP at all is banked.");
        }

        if (!double.IsFinite(exponent) || exponent <= 0d)
        {
            throw new InvalidTunableException(
                ExponentReference,
                "07 §1.1 calls the exponent the single dial controlling long-term pacing and sweeps " +
                "it over 0.95..1.15. At " + Render(exponent) + " the curve stops rising with the " +
                "level, so every level past the first costs the same or less than the one before it " +
                "and the 200-level ladder is no ladder at all.");
        }

        if (talentPointsPerLevel < 0)
        {
            throw new InvalidTunableException(
                TalentPointsPerLevelReference,
                "07 §1.1 grants +1 Talent Point per Legend Level. A negative grant would take points " +
                "back on the way up, and nothing anywhere in the design set removes a point a player " +
                "has earned; this document authors " + RenderCount(talentPointsPerLevel) + ".");
        }

        return new LegendCurveTuning(coefficient, exponent, talentPointsPerLevel);
    }

    /// <summary>Renders a number with <see cref="CultureInfo.InvariantCulture"/> — a bare interpolation reads differently on a German laptop.</summary>
    private static string Render(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    /// <inheritdoc cref="Render(double)"/>
    private static string RenderCount(int value) => value.ToString(CultureInfo.InvariantCulture);
}
