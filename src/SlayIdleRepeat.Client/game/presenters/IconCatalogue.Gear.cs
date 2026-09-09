using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Client.Game.Presenters;

/// <summary>The marks a bag cell or a slot tile can wear beside its glyph.</summary>
public enum GearMark
{
    /// <summary>The item is locked out of every merge and salvage.</summary>
    Lock,

    /// <summary>The item is the one worn in its slot.</summary>
    Worn,

    /// <summary>The bag holds something that out-powers what the slot wears.</summary>
    Upgrade,

    /// <summary>The item is in the salvage batch.</summary>
    Selected,
}

/// <summary>The Gear screen's glyphs: one per slot, one per mark.</summary>
/// <remarks>
/// 🔒 A slot's glyph stands in for the item's own picture, because no per-item art ships in this
/// build — <c>asset_manifest_art.json</c> lists a 120-icon atlas nothing has drawn. The glyph is
/// neutral on purpose: what says which BAND an item is is the tint and the corner shape the renderer
/// writes onto the face behind it, so the glyph must not carry a colour of its own that could be read
/// as one.
/// </remarks>
public static partial class IconCatalogue
{
    private const string SlotStem = "gear_slot_";

    private const string MarkStem = "gear_mark_";

    /// <summary>The resource path of a slot's glyph.</summary>
    /// <param name="slot">The slot.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="slot"/> is no slot the vocabulary declares.</exception>
    public static string PathOf(GearSlot slot) => Directory + SlotStem + SlotFileStem(slot) + SvgExtension;

    /// <summary>The resource path of a mark's glyph.</summary>
    /// <param name="mark">The mark.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mark"/> is no mark this catalogue names.</exception>
    public static string PathOf(GearMark mark) => Directory + MarkStem + MarkFileStem(mark) + SvgExtension;

    private static IEnumerable<string> GearPaths() =>
        Enum.GetValues<GearSlot>().Select(PathOf).Concat(Enum.GetValues<GearMark>().Select(PathOf));

    private static string SlotFileStem(GearSlot slot) => slot switch
    {
        GearSlot.WEAPON => "weapon",
        GearSlot.HELMET => "helmet",
        GearSlot.ARMOR => "armor",
        GearSlot.BOOTS => "boots",
        GearSlot.RING => "ring",
        GearSlot.AMULET => "amulet",
        _ => throw new ArgumentOutOfRangeException(
            nameof(slot), slot, "this slot has no glyph named for it; add its row here before a scene loads it."),
    };

    private static string MarkFileStem(GearMark mark) => mark switch
    {
        GearMark.Lock => "lock",
        GearMark.Worn => "worn",
        GearMark.Upgrade => "upgrade",
        GearMark.Selected => "selected",
        _ => throw new ArgumentOutOfRangeException(
            nameof(mark), mark, "this mark has no glyph named for it; add its row here before a scene loads it."),
    };
}
