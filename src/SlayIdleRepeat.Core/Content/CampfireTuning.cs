using System.Globalization;

namespace SlayIdleRepeat.Core.Content;

/// <summary>
/// 🔒 `03` §2 — <c>TILE_CAMPFIRE</c>'s rest heal, read out of
/// <c>tuning/currencies.json#/inRunIncome/campfire</c>.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>This block was authored by M3-03's review, and the reason is worth keeping.</b> The rest
/// heal shipped as a bare <c>const double</c> on <c>CampfireResolver</c> because
/// <c>currencies.json</c> had no campfire block to read — `03` §2's tile table describes the rest in
/// prose and nowhere else. `21` §3.1 makes a balance number data, so the block was added rather than
/// the constant kept: a designer retuning the rest should not need a rebuild, and the one number
/// that says how much a campfire heals should live beside the shop's and the shrine's, which are
/// both already tunables.
/// </para>
/// <para>
/// ⚠️ <b>Flat across chapters, deliberately.</b> Every other <c>inRunIncome</c> row is a multiple of
/// `03` §7a's <c>M(c)</c> or <c>G(c)</c>; this one is not, and that is not an omission. It is a
/// <em>share of Max HP</em>, and Max HP is already a run-scoped quantity that grows with the build —
/// scaling the share by chapter too would compound the same growth twice.
/// </para>
/// </remarks>
internal sealed class CampfireTuning
{
    /// <summary>The document `03` §2's campfire block lives in.</summary>
    internal const string DocumentPath = "tuning/currencies.json";

    private const string CampfirePointer = DocumentPath + "#/inRunIncome/campfire";

    /// <summary>`03` §2 — the healed share of Max HP. 0.4 as shipped.</summary>
    internal const string HealPctMaxHpReference = CampfirePointer + "/healPctMaxHp";

    private CampfireTuning(double healPctMaxHp) => HealPctMaxHp = healPctMaxHp;

    /// <summary>
    /// `03` §2 — the share of Max HP a rest heals, in <c>(0,1]</c>.
    /// </summary>
    internal double HealPctMaxHp { get; }

    /// <summary>Reads the campfire block. Throws rather than defaulting on anything unusable.</summary>
    /// <param name="content">The version-stamped snapshot the command is reading (`30` §3).</param>
    /// <exception cref="MissingContentException">The document or the pointer is not there.</exception>
    /// <exception cref="UnauthorisedTunableException">The pointer holds a deliberate <c>null</c>.</exception>
    /// <exception cref="ContentTypeMismatchException">The leaf holds the wrong shape.</exception>
    /// <exception cref="InvalidTunableException">The value is authorised but unusable.</exception>
    internal static CampfireTuning Read(ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var healPctMaxHp = content.ReadDouble(HealPctMaxHpReference);

        // ⚠️ Zero is refused where CacheTuning's egg rate accepts it, and the asymmetry is the
        // meaning rather than a stricter mood: an egg rate of 0 is how content switches a branch
        // off, whereas a rest that heals nothing is not a rest — CampfireChoose would accept the
        // command, clear the tile and tell the player they had rested. 1 IS accepted: a full heal is
        // a coherent tuning, and the resolver clamps at Max HP either way.
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
