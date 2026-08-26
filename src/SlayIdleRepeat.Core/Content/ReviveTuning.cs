using System.Globalization;

namespace SlayIdleRepeat.Core.Content;

/// <summary>
/// The once-per-run <c>AD_REVIVE</c> placement's numbers, read out of
/// <c>tuning/ads.json#/placementRewardValues/AD_REVIVE</c>.
/// </summary>
internal sealed class ReviveTuning
{
    /// <summary>The document the placement values live in.</summary>
    internal const string DocumentPath = "tuning/ads.json";

    /// <summary>The placement id, and the <c>Run.AdUses</c> key the once-per-run gate reads.</summary>
    internal const string PlacementId = "AD_REVIVE";

    private const string ValuesPointer = DocumentPath + "#/placementRewardValues/" + PlacementId;

    /// <summary>The share of Max HP a revive restores. 0.5 as shipped.</summary>
    internal const string HealPctMaxHpReference = ValuesPointer + "/healPctMaxHp";

    /// <summary>The invulnerability window a revive grants, in seconds. 2 as shipped.</summary>
    internal const string InvulnerabilitySecondsReference = ValuesPointer + "/invulnerabilitySeconds";

    private ReviveTuning(double healPctMaxHp, int invulnerabilitySeconds)
    {
        HealPctMaxHp = healPctMaxHp;
        InvulnerabilitySeconds = invulnerabilitySeconds;
    }

    /// <summary>The share of Max HP a revive restores. 0.66 (66%) as shipped.</summary>
    internal double HealPctMaxHp { get; }

    /// <summary>
    /// The invulnerability window a revive grants, in seconds. Recorded here so the fact is not
    /// silently dropped; nothing in <c>Core</c> simulates combat time yet, so no rule consumes it
    /// today.
    /// </summary>
    internal int InvulnerabilitySeconds { get; }

    /// <summary>Reads the AD_REVIVE block. Throws rather than defaulting on anything unusable.</summary>
    /// <exception cref="MissingContentException">The document or a pointer is not there.</exception>
    /// <exception cref="UnauthorisedTunableException">A pointer holds a deliberate <c>null</c>.</exception>
    /// <exception cref="ContentTypeMismatchException">A leaf holds the wrong shape.</exception>
    /// <exception cref="InvalidTunableException">A value is authorised but unusable.</exception>
    internal static ReviveTuning Read(ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var healPctMaxHp = content.ReadDouble(HealPctMaxHpReference);
        if (!double.IsFinite(healPctMaxHp) || healPctMaxHp is <= 0.0 or > 1.0)
        {
            throw new InvalidTunableException(
                HealPctMaxHpReference,
                "A revive heal is a share of Max HP in (0,1]. 02 §6 authors 0.66; this document " +
                "authors " + Text(healPctMaxHp) + ".");
        }

        var invulnerabilitySeconds = content.ReadInt32(InvulnerabilitySecondsReference);
        if (invulnerabilitySeconds < 0)
        {
            throw new InvalidTunableException(
                InvulnerabilitySecondsReference,
                "An invulnerability window cannot be negative. 02 §6 authors 2 seconds; this " +
                "document authors " + Text(invulnerabilitySeconds) + ".");
        }

        return new ReviveTuning(healPctMaxHp, invulnerabilitySeconds);
    }

    private static string Text(double value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);
}
