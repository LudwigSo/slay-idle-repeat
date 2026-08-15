using System.Globalization;

namespace SlayIdleRepeat.Core.Content;

/// <summary>
/// 🔒 M3-05, `03` §1.1 — the Stage Gate's heal, read out of
/// <c>tuning/currencies.json#/inRunIncome/stageGate</c>.
/// </summary>
/// <remarks>
/// Authored the same way M3-03's review authored <see cref="CampfireTuning"/>: `21` §3.1 makes a
/// balance number data, not a bare <c>const double</c> — a designer retuning the Stage Gate heal
/// should not need a rebuild, and this one lives beside the campfire's, which is the same shape of
/// number (a share of Max HP).
/// <para>
/// ⚠️ <b>Flat across chapters, deliberately, for the same reason <see cref="CampfireTuning"/> is.</b>
/// It is a share of Max HP, which is already a run-scoped quantity that grows with the build — an
/// <c>M(c)</c>/<c>G(c)</c> chapter scalar on top would compound the same growth twice.
/// </para>
/// </remarks>
internal sealed class StageGateTuning
{
    /// <summary>The document `03` §1.1's Stage Gate block lives in.</summary>
    internal const string DocumentPath = "tuning/currencies.json";

    private const string StageGatePointer = DocumentPath + "#/inRunIncome/stageGate";

    /// <summary>`03` §1.1 — the healed share of Max HP. 0.15 as shipped.</summary>
    internal const string HealPctMaxHpReference = StageGatePointer + "/healPctMaxHp";

    private StageGateTuning(double healPctMaxHp) => HealPctMaxHp = healPctMaxHp;

    /// <summary>
    /// `03` §1.1 — the share of Max HP a Stage Gate heals, in <c>(0,1]</c>.
    /// </summary>
    internal double HealPctMaxHp { get; }

    /// <summary>Reads the Stage Gate block. Throws rather than defaulting on anything unusable.</summary>
    /// <param name="content">The version-stamped snapshot the command is reading (`30` §3).</param>
    /// <exception cref="MissingContentException">The document or the pointer is not there.</exception>
    /// <exception cref="UnauthorisedTunableException">The pointer holds a deliberate <c>null</c>.</exception>
    /// <exception cref="ContentTypeMismatchException">The leaf holds the wrong shape.</exception>
    /// <exception cref="InvalidTunableException">The value is authorised but unusable.</exception>
    internal static StageGateTuning Read(ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var healPctMaxHp = content.ReadDouble(HealPctMaxHpReference);

        // ⚠️ Zero is refused for the same reason CampfireTuning refuses it: a Stage Gate that heals
        // nothing is not a heal, and 1 is accepted because a full heal is a coherent tuning.
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
