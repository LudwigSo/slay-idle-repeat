using System.Globalization;

namespace SlayIdleRepeat.Core.Content;

/// <summary>
/// The Stage Gate's heal, read out of <c>tuning/currencies.json#/inRunIncome/stageGate</c>.
/// </summary>
/// <remarks>
/// Flat across chapters, deliberately, for the same reason as <see cref="CampfireTuning"/>: it is a
/// share of Max HP, which already grows with the build, so a chapter scalar on top would compound
/// the same growth twice.
/// </remarks>
internal sealed class StageGateTuning
{
    /// <summary>The document the Stage Gate block lives in.</summary>
    internal const string DocumentPath = "tuning/currencies.json";

    private const string StageGatePointer = DocumentPath + "#/inRunIncome/stageGate";

    /// <summary>The healed share of Max HP. 0.15 as shipped.</summary>
    internal const string HealPctMaxHpReference = StageGatePointer + "/healPctMaxHp";

    private StageGateTuning(double healPctMaxHp) => HealPctMaxHp = healPctMaxHp;

    /// <summary>The share of Max HP a Stage Gate heals, in <c>(0,1]</c>.</summary>
    internal double HealPctMaxHp { get; }

    /// <summary>Reads the Stage Gate block. Throws rather than defaulting on anything unusable.</summary>
    /// <param name="content">The version-stamped snapshot the command is reading.</param>
    /// <exception cref="MissingContentException">The document or the pointer is not there.</exception>
    /// <exception cref="UnauthorisedTunableException">The pointer holds a deliberate <c>null</c>.</exception>
    /// <exception cref="ContentTypeMismatchException">The leaf holds the wrong shape.</exception>
    /// <exception cref="InvalidTunableException">The value is authorised but unusable.</exception>
    internal static StageGateTuning Read(ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var healPctMaxHp = content.ReadDouble(HealPctMaxHpReference);

        // Zero is refused for the same reason CampfireTuning refuses it: a heal of nothing is not a
        // heal, and 1 is accepted because a full heal is a coherent tuning.
        if (!double.IsFinite(healPctMaxHp) || healPctMaxHp is <= 0.0 or > 1.0)
        {
            throw new InvalidTunableException(
                HealPctMaxHpReference,
                "A Stage Gate heals a share of Max HP in (0,1]. 03 §1.1 authors 0.15; this document " +
                "authors " + Text(healPctMaxHp) + ".");
        }

        return new StageGateTuning(healPctMaxHp);
    }

    private static string Text(double value) => value.ToString(CultureInfo.InvariantCulture);
}
