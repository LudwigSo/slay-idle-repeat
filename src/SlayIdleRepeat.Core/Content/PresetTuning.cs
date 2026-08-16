using System.Globalization;

namespace SlayIdleRepeat.Core.Content;

/// <summary>The free loadout-preset allowance, read out of <c>tuning/ads.json#/plus/freePresets</c>.</summary>
/// <remarks>
/// <para>
/// 🔒 <b>The number is read, never invented.</b> M1-02 left <c>SavePresetCommand.PresetSlot</c>
/// deliberately unbounded — <em>"no authored bound on the preset count"</em> — rather than guess one,
/// and this reader is what closes that without guessing either: `12` §2 lists <em>"unlimited talent
/// and loadout presets (free players get 3)"</em> among Plus's grants, and <c>#/plus/freePresets</c>
/// is where that 3 is authored.
/// </para>
/// <para>
/// 🔒 <b>There is deliberately no ceiling for a Plus subscriber.</b> `12` §2 grants unlimited slots,
/// so no number is read for one and none exists to read — <see cref="FreeSlots"/> is the free
/// allowance and nothing else. A "max presets" tunable invented to sit beside it would be a cap the
/// design set does not have.
/// </para>
/// <para>
/// It lives in <c>Content/</c> rather than beside the preset rule for <see cref="EnergyTuning"/>'s
/// reason: the allowance is read by the handler that answers <c>NOT_ENTITLED</c> and by the aggregate
/// that decides which stored presets are read-only, and <c>Model</c> may not reference <c>Rules</c>.
/// </para>
/// </remarks>
internal sealed class PresetTuning
{
    /// <summary>The document the allowance lives in.</summary>
    internal const string DocumentPath = "tuning/ads.json";

    /// <summary>How many preset slots a player without Plus may write. 3 as shipped.</summary>
    internal const string FreePresetsReference = DocumentPath + "#/plus/freePresets";

    private PresetTuning(int freeSlots) => FreeSlots = freeSlots;

    /// <summary>
    /// How many preset slots a player without Plus may <b>write</b>. 3 as shipped.
    /// </summary>
    /// <remarks>
    /// Writing, not holding: `12` §66 is explicit that presets beyond the free allowance become
    /// <em>read-only</em> rather than deleted when Plus lapses, so this number bounds
    /// <c>SAVE_PRESET</c> and never <c>APPLY_PRESET</c>.
    /// </remarks>
    internal int FreeSlots { get; }

    /// <summary>The slot numbers, counted from 1, a player without Plus may write.</summary>
    /// <param name="presetSlot">The slot a command named.</param>
    /// <returns><see langword="true"/> when the slot is inside the free allowance.</returns>
    internal bool IsFreeSlot(int presetSlot) => presetSlot >= FirstSlot && presetSlot <= FreeSlots;

    /// <summary>The lowest preset slot number. Presets are counted from 1, like the login calendar's days.</summary>
    /// <remarks>
    /// Not authored anywhere, and stated here once rather than spelled at each call site: `09` §2.1
    /// and `12` §2 both count presets ("3 saved preset slots", "beyond the free 3") without ever
    /// numbering them, so the origin is a representation choice. It is 1 rather than 0 because
    /// `14` §662's own example is "preset slot 4+", which only names the fourth slot if the first
    /// is 1.
    /// </remarks>
    internal const int FirstSlot = 1;

    /// <summary>
    /// Reads the allowance. Throws rather than defaulting on anything missing, unauthorised, mistyped
    /// or nonsensical.
    /// </summary>
    /// <param name="content">The version-stamped snapshot the command is reading.</param>
    /// <returns>The preset numbers.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is null.</exception>
    /// <exception cref="MissingContentException">The document or the pointer is not there.</exception>
    /// <exception cref="UnauthorisedTunableException">The pointer holds a deliberate <c>null</c>.</exception>
    /// <exception cref="ContentTypeMismatchException">The allowance holds a fraction.</exception>
    /// <exception cref="InvalidTunableException">The allowance is authorised but unusable.</exception>
    internal static PresetTuning Read(ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var freeSlots = content.ReadInt32(FreePresetsReference);

        if (freeSlots < 1)
        {
            throw new InvalidTunableException(
                FreePresetsReference,
                "09 §2.1 makes presets a CORE FREE FEATURE — '3 saved preset slots from the start', " +
                "with no ad placement attached — and 09 §53 says the allowance may be raised and " +
                "never gated further. A free allowance of " + Render(freeSlots) + " would put the " +
                "whole feature behind Plus, which is the one thing that document forbids.");
        }

        return new PresetTuning(freeSlots);
    }

    /// <summary>Renders a number with <see cref="CultureInfo.InvariantCulture"/>, so a message reads the same on every host.</summary>
    private static string Render(int value) => value.ToString(CultureInfo.InvariantCulture);
}
