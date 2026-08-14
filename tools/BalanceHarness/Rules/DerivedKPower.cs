using SlayIdleRepeat.BalanceHarness.Content;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Rules.Stats;

namespace SlayIdleRepeat.BalanceHarness.Rules;

/// <summary>
/// 🔒 `29` §2.1's calibration constant, <b>derived</b> from the authored reference par build and
/// never written back to <c>tuning/power_model.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// `29` §2.1: <em>"K_POWER is a pure calibration constant … fixed once, by solving for
/// <c>PlayerPower = 1000</c> on the reference par build."</em> That definition <b>is</b> the
/// derivation:
/// <code>
/// K_POWER = referenceParBuild.targetPower / PowerIndex(referenceParBuild.stats, referenceParBuild.level)
/// </code>
/// Both inputs are authored in <c>tuning/calibration_builds.json</c>, so no number here was invented.
/// </para>
/// <para>
/// 🔒 <b>The harness derives it and does not persist it.</b> <c>power_model.json#/kPower</c> is
/// authored <c>null</c> and <c>PowerCalculator.Compute</c> throws on it <em>by design</em> (steering
/// S6: a hole is greppable, a hole filled with a plausible number is invisible). Writing the derived
/// value into the file would be an edit to <c>game-data/</c>, which M2-16a may not make, and `21`
/// §3.2 keeps that decision with design rather than with the tool that measured it. The value is
/// <b>reported</b>.
/// </para>
/// <para>
/// 🔒 <b>Nothing in the sweep depends on it.</b> <see cref="TargetPowerIndex"/> divides a par power by
/// it and <c>PowerCalculator.PowerIndex</c> is the same quantity without it, so the constant cancels
/// out of every comparison the guardrails make. It is derived so that the report can state it, and
/// because `29` §2.1's <em>"expected magnitude ≈ 5.3"</em> is then a check on the transcription of
/// every weight <c>PowerIndex</c> reads.
/// </para>
/// </remarks>
public sealed record DerivedKPower(double Value, double ReferencePowerIndex, int ReferenceLevel)
{
    /// <summary>`29` §2.1 — the magnitude the document says to expect. A sanity check, not a bound.</summary>
    public const string ExpectedMagnitudePointer = "tuning/power_model.json#/kPowerExpectedMagnitude";

    /// <summary>Derives the constant from the authored reference par build.</summary>
    /// <exception cref="InvalidOperationException">The reference build scores a non-positive power index.</exception>
    public static DerivedKPower Derive(ContentSnapshot content, CalibrationBuilds calibration)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(calibration);

        var index = PowerCalculator.PowerIndex(
            calibration.ReferenceParBuild.ToActorStats(),
            calibration.ReferenceParBuildLevel,
            content);

        if (!double.IsFinite(index) || index <= 0.0)
        {
            throw new InvalidOperationException(
                "29 §2.1 fixes K_POWER by solving PlayerPower(referenceParBuild) := 1000, which needs a " +
                "positive PowerIndex to divide by. The reference build in " +
                $"{CalibrationBuilds.Document} scored {index}, so either its statline or a " +
                "power_model.json weight is not what 29 §2.3 describes.");
        }

        return new DerivedKPower(
            calibration.ReferenceParBuildTargetPower / index,
            index,
            calibration.ReferenceParBuildLevel);
    }

    /// <summary>
    /// 🔒 The power-index a loadout must reach to sit at an absolute power target — the right-hand
    /// side `29` §2.5.3's bisection solves for.
    /// </summary>
    /// <remarks>
    /// <c>PowerIndex = PlayerPower / K_POWER</c>, which is `29` §2.1 rearranged. Stated as a method so
    /// that the one division lives beside the constant it undoes.
    /// </remarks>
    public double TargetPowerIndex(double playerPower) => playerPower / Value;
}
