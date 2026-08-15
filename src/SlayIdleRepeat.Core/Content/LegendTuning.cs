using System.Globalization;

namespace SlayIdleRepeat.Core.Content;

/// <summary>The Legend Level <b>range</b>, read out of <c>tuning/progression.json</c>.</summary>
/// <remarks>
/// <para>
/// Deliberately two leaves and not the whole block: <c>xpCoefficient</c>, <c>xpExponent</c> and
/// <c>talentPointsPerLevel</c> are the level-up curve, owned by the level-up rule that computes
/// with them — this type only validates the range, so it stays out of a computation it never uses.
/// </para>
/// <para>
/// A hole is never a default: every read goes through <see cref="ContentSnapshot"/>'s typed
/// readers, which throw <see cref="UnauthorisedTunableException"/> on a deliberate <c>null</c>
/// rather than answering zero.
/// </para>
/// </remarks>
internal sealed class LegendTuning
{
    /// <summary>The document the Legend Level block lives in.</summary>
    internal const string DocumentPath = "tuning/progression.json";

    private const string LegendLevelPointer = DocumentPath + "#/legendLevel";

    /// <summary>The Legend Level a player starts at. 1 as shipped.</summary>
    internal const string MinimumReference = LegendLevelPointer + "/min";

    /// <summary>The highest Legend Level v1 reaches. 200 as shipped.</summary>
    internal const string MaximumReference = LegendLevelPointer + "/max";

    private LegendTuning(int minimum, int maximum)
    {
        Minimum = minimum;
        Maximum = maximum;
    }

    /// <summary>The Legend Level a player starts at. 1 as shipped.</summary>
    internal int Minimum { get; }

    /// <summary>The highest Legend Level v1 reaches. 200 as shipped.</summary>
    internal int Maximum { get; }

    /// <summary>
    /// Reads the Legend Level range. Throws rather than defaulting on anything missing,
    /// unauthorised, mistyped or nonsensical.
    /// </summary>
    /// <param name="content">The version-stamped snapshot the command is reading.</param>
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
    /// Renders a number with <see cref="CultureInfo.InvariantCulture"/> — a bare interpolation
    /// reads differently on a German laptop than in the Linux container.
    /// </summary>
    private static string Render(int value) => value.ToString(CultureInfo.InvariantCulture);
}
