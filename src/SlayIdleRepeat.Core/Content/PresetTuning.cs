using System.Globalization;

namespace SlayIdleRepeat.Core.Content;

/// <summary>
/// The preset numbers: the free allowance out of <c>tuning/ads.json#/plus/freePresets</c>, and the
/// two storage bounds out of <c>tuning/ads.json#/presetStorage</c>.
/// </summary>
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
/// <para>
/// 🔴 <b><see cref="HighestSlot"/> and <see cref="LongestNameTextElements"/> are storage bounds, and
/// no design section authors either.</b> They used to be two <c>const</c>s inside
/// <c>LoadoutPreset</c> — invented there, and invisible to every rule in the repo, because a
/// <c>const</c> is folded to a literal and leaves no trace an IL scan can see. They are read here
/// instead: <c>#/presetStorage</c>'s own <c>_doc</c> states plainly that they bound what an untrusted
/// client can make the persisted row grow to and nothing else, and O11 is the open decision that
/// would replace them with numbers a document actually asked for. Authoring them does not make them
/// design numbers; it makes them <em>greppable</em> ones.
/// </para>
/// </remarks>
internal sealed class PresetTuning
{
    /// <summary>The document the allowance lives in.</summary>
    internal const string DocumentPath = "tuning/ads.json";

    /// <summary>How many preset slots a player without Plus may write. 3 as shipped.</summary>
    internal const string FreePresetsReference = DocumentPath + "#/plus/freePresets";

    /// <summary>The block holding the two storage bounds — neither of them a design number.</summary>
    internal const string PresetStorageReference = DocumentPath + "#/presetStorage";

    /// <summary>The highest slot number a persisted preset row may name. 999 as shipped.</summary>
    internal const string HighestSlotReference = PresetStorageReference + "/highestSlot";

    /// <summary>The longest a persisted preset name may be, in text elements. 64 as shipped.</summary>
    internal const string LongestNameReference = PresetStorageReference + "/longestNameTextElements";

    private PresetTuning(int freeSlots, int highestSlot, int longestNameTextElements)
    {
        FreeSlots = freeSlots;
        HighestSlot = highestSlot;
        LongestNameTextElements = longestNameTextElements;
    }

    /// <summary>
    /// How many preset slots a player without Plus may <b>write</b>. 3 as shipped.
    /// </summary>
    /// <remarks>
    /// Writing, not holding: `12` §2.2 is explicit that presets beyond the free allowance become
    /// <em>read-only</em> rather than deleted when Plus lapses, so this number bounds
    /// <c>SAVE_PRESET</c> and never <c>APPLY_PRESET</c>.
    /// </remarks>
    internal int FreeSlots { get; }

    /// <summary>
    /// Whether a slot is past the free allowance — the only slots Plus is the answer to.
    /// </summary>
    /// <param name="presetSlot">The slot a command named.</param>
    /// <returns><see langword="true"/> when only a subscriber may write it.</returns>
    /// <remarks>
    /// 🔒 It is <b>not</b> "the slot is not free", and the difference is the whole reason this is
    /// the only such predicate here. A slot below <see cref="FirstSlot"/> is also not free, and
    /// answering an entitlement refusal for one would tell a player they need a subscription because
    /// their client sent slot 0 — the worst possible message, and one the player cannot act on.
    /// Below the floor is a malformed payload; above the allowance is an entitlement. An earlier
    /// draft carried both predicates side by side, which is an invitation to reach for the wrong one.
    /// </remarks>
    internal bool IsBeyondFreeAllowance(int presetSlot) => presetSlot > FreeSlots;

    /// <summary>
    /// ⚠️ The highest slot number a persisted preset row may name — a bound on what an untrusted
    /// client can store, <b>not</b> a design limit. `12` §2 grants a subscriber unlimited presets and
    /// this does not take that back.
    /// </summary>
    internal int HighestSlot { get; }

    /// <summary>
    /// ⚠️ The longest a persisted preset name may be, in text elements — the same kind of bound, for
    /// the same reason.
    /// </summary>
    /// <remarks>
    /// Text elements rather than <c>char</c>s, for <c>HeroNameRule.MaximumLength</c>'s reason: an
    /// emoji is two UTF-16 code units, and counting those gives a limit that shortens depending on
    /// what the player types.
    /// </remarks>
    internal int LongestNameTextElements { get; }

    /// <summary>The lowest preset slot number. Presets are counted from 1, like the login calendar's days.</summary>
    /// <remarks>
    /// Not authored anywhere, and stated here once rather than spelled at each call site: `09` §2.1
    /// and `12` §2 both count presets ("3 saved preset slots", "beyond the free 3") without ever
    /// numbering them, so the origin is a representation choice. It is 1 rather than 0 because
    /// `14` §16.2's own example is "preset slot 4+", which only names the fourth slot if the first
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
                "with no ad placement attached — and 09 §2.1 says the allowance may be raised and " +
                "never gated further. A free allowance of " + Render(freeSlots) + " would put the " +
                "whole feature behind Plus, which is the one thing that document forbids.");
        }

        return new PresetTuning(
            freeSlots,
            RequireStorageBound(content, HighestSlotReference, "slot number", freeSlots),
            RequireStorageBound(content, LongestNameReference, "name length", 1));
    }

    /// <summary>
    /// One authored storage bound, refused when it is small enough to constrain a design decision.
    /// </summary>
    /// <remarks>
    /// The whole claim these two numbers make is that they refuse abuse and bound nothing the design
    /// set describes. A bound below the free allowance would break that claim outright — it would put
    /// slots `09` §2.1 gives away for free out of reach — so the floor is checked rather than trusted.
    /// </remarks>
    private static int RequireStorageBound(
        ContentSnapshot content, string reference, string what, int floor)
    {
        var value = content.ReadInt32(reference);

        if (value >= floor && value >= 1)
        {
            return value;
        }

        throw new InvalidTunableException(
            reference,
            "This bounds what an untrusted client can make the persisted preset row grow to and is " +
            "deliberately far above anything the design set describes. A " + what + " bound of " +
            Render(value) + " is at or below " + Render(floor) + ", which would make it a limit on " +
            "the player instead — and no design section authors one.");
    }

    /// <summary>Renders a number with <see cref="CultureInfo.InvariantCulture"/>, so a message reads the same on every host.</summary>
    private static string Render(int value) => value.ToString(CultureInfo.InvariantCulture);
}
