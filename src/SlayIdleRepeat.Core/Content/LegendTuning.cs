using System.Globalization;

namespace SlayIdleRepeat.Core.Content;

/// <summary>
/// 🔒 The `07` §1.1 Legend Level <b>range</b>, read out of <c>tuning/progression.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// `30` §11.5 puts <em>invariants</em> on the aggregate, and `07` §1.1 runs Legend Level 1..200.
/// The <c>Player</c> aggregate therefore has to know those two numbers — and `21` §3.1 is explicit
/// that <em>"a 📐 TUNABLE number that is not in this directory is a bug"</em>, so
/// <c>private const int MaxLegendLevel = 200</c> on the aggregate would be the bug rather than the
/// fix. They are authored at <c>#/legendLevel/min</c> and <c>#/legendLevel/max</c> and reach the
/// aggregate through this type, exactly as the energy numbers reach it through
/// <see cref="EnergyTuning"/>.
/// </para>
/// <para>
/// ⚠️ <b>Deliberately two leaves and not the whole block.</b> <c>xpCoefficient</c>,
/// <c>xpExponent</c> and <c>talentPointsPerLevel</c> are the level-up <em>curve</em>, and `30`
/// §11.5 keeps computation off the aggregate: the curve is <b>M4-10</b>'s (`07` §1, "Legend Level
/// curve + unlock-gate table, level-up grants"). Reading them here would put a number the
/// aggregate never uses into a type whose only caller is an invariant check. `21` §12 also calls
/// <c>xpExponent</c> "the highest-suspicion number in the whole economy"; it belongs where it is
/// swept, not where a range is validated.
/// </para>
/// <para>
/// A hole is never a default: every read goes through <see cref="ContentSnapshot"/>'s typed
/// readers, which throw <see cref="UnauthorisedTunableException"/> on a deliberate <c>null</c>
/// rather than answering zero (S6).
/// </para>
/// </remarks>
internal sealed class LegendTuning
{
    /// <summary>The document `07` §1.1's Legend Level block lives in.</summary>
    internal const string DocumentPath = "tuning/progression.json";

    private const string LegendLevelPointer = DocumentPath + "#/legendLevel";

    /// <summary>`07` §1.1 — the Legend Level a player starts at. 1 as shipped.</summary>
    internal const string MinimumReference = LegendLevelPointer + "/min";

    /// <summary>`07` §1.1 — the highest Legend Level v1 reaches. 200 as shipped.</summary>
    internal const string MaximumReference = LegendLevelPointer + "/max";

    private LegendTuning(int minimum, int maximum)
    {
        Minimum = minimum;
        Maximum = maximum;
    }

    /// <summary>`07` §1.1 — the Legend Level a player starts at. 1 as shipped.</summary>
    internal int Minimum { get; }

    /// <summary>`07` §1.1 — the highest Legend Level v1 reaches. 200 as shipped.</summary>
    internal int Maximum { get; }

    /// <summary>
    /// Reads the Legend Level range. Throws rather than defaulting on anything missing,
    /// unauthorised, mistyped or nonsensical.
    /// </summary>
    /// <param name="content">The version-stamped snapshot the command is reading (`30` §3).</param>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is null.</exception>
    /// <exception cref="MissingContentException">The document or a pointer is not there.</exception>
    /// <exception cref="UnauthorisedTunableException">A pointer holds a deliberate <c>null</c>.</exception>
    /// <exception cref="ContentTypeMismatchException">A whole-number tunable holds a fraction.</exception>
    /// <exception cref="InvalidTunableException">A value is authorised but unusable.</exception>
    internal static LegendTuning Read(ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var minimum = content.ReadInt32(MinimumReference);
        if (minimum < 1)
        {
            throw new InvalidTunableException(
                MinimumReference,
                "The starting Legend Level must be at least 1. 07 §1.1 runs Legend Level from 1 to " +
                "200, and EnergyMath.MaxEnergy counts levels GAINED — anything below 1 subtracts " +
                "from the base Max Energy that 10 §3 authors for a starting player. This document " +
                "authors " + Render(minimum) + ".");
        }

        var maximum = content.ReadInt32(MaximumReference);
        if (maximum < minimum)
        {
            throw new InvalidTunableException(
                MaximumReference,
                "The highest Legend Level of " + Render(maximum) + " is below the starting Legend " +
                "Level of " + Render(minimum) + ", so no level a player could hold would be legal. " +
                "07 §1.1 authors 1..200.");
        }

        return new LegendTuning(minimum, maximum);
    }

    /// <summary>
    /// 🔒 Renders a number with <see cref="CultureInfo.InvariantCulture"/>, for the same reason
    /// <see cref="EnergyTuning"/> does: a bare interpolation reads differently on a German laptop
    /// than in the Linux container, which is two diagnostics for one data defect.
    /// </summary>
    private static string Render(int value) => value.ToString(CultureInfo.InvariantCulture);
}
