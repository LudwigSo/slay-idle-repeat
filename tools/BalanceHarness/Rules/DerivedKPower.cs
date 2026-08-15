using SlayIdleRepeat.BalanceHarness.Content;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Rules.Stats;

namespace SlayIdleRepeat.BalanceHarness.Rules;

/// <summary>
/// The power-model calibration constant, derived from the authored reference par build and never
/// written back to <c>tuning/power_model.json</c>.
/// </summary>
/// <remarks>
/// <c>K_POWER = referenceParBuild.targetPower / PowerIndex(referenceParBuild.stats, referenceParBuild.level)</c>,
/// with both inputs authored in <c>tuning/calibration_builds.json</c>. It is derived and reported, not
/// persisted: <c>power_model.json#/kPower</c> is authored <c>null</c> on purpose, and
/// <c>PowerCalculator.Compute</c> throws on it by design — writing the derived value back would be an
/// edit to <c>game-data/</c> this tool must not make. Nothing in the sweep actually depends on it: it
/// cancels out of every guardrail comparison, and exists so the report can state it as a sanity check
/// against the document's expected magnitude.
/// </remarks>
public sealed record DerivedKPower(double Value, double ReferencePowerIndex, int ReferenceLevel)
{
    /// <summary>The magnitude the document says to expect. A sanity check, not a bound.</summary>
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
    /// The power-index a loadout must reach to sit at an absolute power target — the right-hand side
    /// the scaling bisection solves for. <c>PowerIndex = PlayerPower / K_POWER</c>.
    /// </summary>
    public double TargetPowerIndex(double playerPower) => playerPower / Value;
}
