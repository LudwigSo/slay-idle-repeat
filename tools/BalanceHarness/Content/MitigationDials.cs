using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.BalanceHarness.Content;

/// <summary>
/// 🔒 <c>content/combat_caps.json#/mitigation</c> — `05` §4's two dials, which guardrail 5 is stated
/// entirely in terms of.
/// </summary>
/// <remarks>
/// `05` §4: <em>"the 120 and 20 constants are the two most important balance dials in the game.
/// Expose them in data."</em> They are authored twice on purpose — here and in
/// <c>tuning/power_model.json#/mitigation</c>, mirrored by a build rule so the simulator and the
/// power model grading it cannot mitigate differently. The harness reads the <c>combat_caps.json</c>
/// copy because guardrail 5 is a statement about the <b>fight</b>, and asserts in
/// <c>MitigationModelTests</c> that the two copies agree — if the mirror rule ever stopped running,
/// guardrail 5 would be measuring the wrong one and would still look green.
/// </remarks>
public sealed record MitigationDials(double FlatConstant, double PerLevelConstant)
{
    /// <summary>`05` §1-4 — the document.</summary>
    public const string Document = "content/combat_caps.json";

    /// <summary>`29` §2.3's copy of the same pair.</summary>
    public const string PowerModelDocument = "tuning/power_model.json";

    /// <summary>Reads `05` §4's pair off the combat document.</summary>
    /// <exception cref="MissingContentException">A pointer is absent.</exception>
    public static MitigationDials Read(ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(content);

        return new MitigationDials(
            content.ReadDouble($"{Document}#/mitigation/flatConstant"),
            content.ReadDouble($"{Document}#/mitigation/perLevelConstant"));
    }

    /// <summary>The mirrored copy in `29` §2.3's document, for the agreement check.</summary>
    /// <exception cref="MissingContentException">A pointer is absent.</exception>
    public static MitigationDials ReadPowerModelCopy(ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(content);

        return new MitigationDials(
            content.ReadDouble($"{PowerModelDocument}#/mitigation/flatConstant"),
            content.ReadDouble($"{PowerModelDocument}#/mitigation/perLevelConstant"));
    }
}
