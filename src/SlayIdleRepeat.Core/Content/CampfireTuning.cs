using System.Globalization;

namespace SlayIdleRepeat.Core.Content;

/// <summary>
/// <c>TILE_CAMPFIRE</c>'s rest heal, read out of
/// <c>tuning/currencies.json#/inRunIncome/campfire</c>.
/// </summary>
/// <remarks>
/// Flat across chapters, deliberately: every other <c>inRunIncome</c> row scales with the chapter
/// growth curves, but this one is a share of Max HP, which already grows with the build — scaling
/// it by chapter too would compound the same growth twice.
/// </remarks>
internal sealed class CampfireTuning
{
    /// <summary>The document the campfire block lives in.</summary>
    internal const string DocumentPath = "tuning/currencies.json";

    private const string CampfirePointer = DocumentPath + "#/inRunIncome/campfire";

    /// <summary>The healed share of Max HP. 0.4 as shipped.</summary>
    internal const string HealPctMaxHpReference = CampfirePointer + "/healPctMaxHp";

    private CampfireTuning(double healPctMaxHp) => HealPctMaxHp = healPctMaxHp;

    /// <summary>The share of Max HP a rest heals, in <c>(0,1]</c>.</summary>
    internal double HealPctMaxHp { get; }

    /// <summary>Reads the campfire block. Throws rather than defaulting on anything unusable.</summary>
    /// <param name="content">The version-stamped snapshot the command is reading.</param>
    /// <exception cref="MissingContentException">The document or the pointer is not there.</exception>
    /// <exception cref="UnauthorisedTunableException">The pointer holds a deliberate <c>null</c>.</exception>
    /// <exception cref="ContentTypeMismatchException">The leaf holds the wrong shape.</exception>
    /// <exception cref="InvalidTunableException">The value is authorised but unusable.</exception>
    internal static CampfireTuning Read(ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var healPctMaxHp = content.ReadDouble(HealPctMaxHpReference);

        // Zero is refused (unlike CacheTuning's egg rate, which accepts it): a rest that heals
        // nothing is not a rest. A full heal (1) is accepted; the resolver clamps at Max HP.
        if (!double.IsFinite(healPctMaxHp) || healPctMaxHp is <= 0.0 or > 1.0)
        {
            throw new InvalidTunableException(
                HealPctMaxHpReference,
                "A campfire rest heals a share of Max HP in (0,1]. 03 §2 authors 0.4; this document " +
                "authors " + Text(healPctMaxHp) + ".");
        }

        return new CampfireTuning(healPctMaxHp);
    }

    private static string Text(double value) => value.ToString(CultureInfo.InvariantCulture);
}
