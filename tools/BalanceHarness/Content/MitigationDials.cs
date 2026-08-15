using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.BalanceHarness.Content;

/// <summary>
/// <c>content/combat_caps.json#/mitigation</c>'s two dials, which guardrail 5 is stated entirely in
/// terms of.
/// </summary>
/// <remarks>
/// They are authored twice on purpose — here and in <c>tuning/power_model.json#/mitigation</c>,
/// mirrored by a build rule so the simulator and the power model grading it cannot mitigate
/// differently. The harness reads the <c>combat_caps.json</c> copy since guardrail 5 is about the
/// fight, and <c>MitigationModelTests</c> asserts the two copies agree — if the mirror rule ever
/// stopped running, guardrail 5 would silently be measuring the wrong one.
/// </remarks>
public sealed record MitigationDials(double FlatConstant, double PerLevelConstant)
{
    public const string Document = "content/combat_caps.json";

    /// <summary>The mirrored copy, for the agreement check.</summary>
    public const string PowerModelDocument = "tuning/power_model.json";

    /// <summary>Reads the pair off the combat document.</summary>
    /// <exception cref="MissingContentException">A pointer is absent.</exception>
    public static MitigationDials Read(ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(content);

        return new MitigationDials(
            content.ReadDouble($"{Document}#/mitigation/flatConstant"),
            content.ReadDouble($"{Document}#/mitigation/perLevelConstant"));
    }

    /// <summary>The mirrored copy in the power model document, for the agreement check.</summary>
    /// <exception cref="MissingContentException">A pointer is absent.</exception>
    public static MitigationDials ReadPowerModelCopy(ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(content);

        return new MitigationDials(
            content.ReadDouble($"{PowerModelDocument}#/mitigation/flatConstant"),
            content.ReadDouble($"{PowerModelDocument}#/mitigation/perLevelConstant"));
    }
}
