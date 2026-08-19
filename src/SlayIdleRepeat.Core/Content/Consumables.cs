namespace SlayIdleRepeat.Core.Content;

/// <summary>
/// `03` §7.1's four consumables: their ids, and the one fact that splits them — whether buying one
/// puts it in the pouch or spends it at the till.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Two of the four are never held.</b> The Reroll Token and the Draft Token convert to their
/// charge on purchase; only the Health Draught and the Escape Rope are held and used later, through
/// <c>USE_CONSUMABLE</c>. That split is what the held cap of 4 is counted against — "counted across
/// all held consumables (Draughts + Ropes)" — so a build that stored the tokens too would grey out
/// the pouch four purchases early.
/// </para>
/// <para>
/// Ids and the held/instant split live here rather than in the price table because the table is a
/// map keyed by id and states neither an order nor a behaviour. The prices themselves stay authored
/// in <c>currencies.json</c> and are read through <c>ShopTuning</c>; <c>ConsumableTests</c> pins
/// these four ids against that table in both directions, so a fifth consumable authored into the
/// data without a row here fails rather than becoming unbuyable.
/// </para>
/// </remarks>
internal static class Consumables
{
    /// <summary>Held. Use on the board: heal a share of Max HP. Disabled at full HP.</summary>
    internal const string HealthDraught = "CON_HEALTH_DRAUGHT";

    /// <summary>Instant: +1 Reroll Charge on purchase. Never held.</summary>
    internal const string RerollToken = "CON_REROLL_TOKEN";

    /// <summary>Instant: +1 free perk-draft reroll on purchase. Never held.</summary>
    internal const string DraftToken = "CON_DRAFT_TOKEN";

    /// <summary>Held. Use on the board: arms the rope, which skips the next tile landed on.</summary>
    internal const string EscapeRope = "CON_ESCAPE_ROPE";

    /// <summary>The four ids, in `03` §7.1's own order.</summary>
    internal static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[]
    {
        HealthDraught, RerollToken, DraftToken, EscapeRope,
    });

    /// <summary>The two that go into the pouch. The other two are spent at the till.</summary>
    internal static bool IsHeld(string? consumableId) =>
        consumableId is HealthDraught or EscapeRope;

    /// <summary>Whether this id is one of the four.</summary>
    internal static bool IsKnown(string? consumableId) =>
        consumableId is HealthDraught or RerollToken or DraftToken or EscapeRope;

    /// <summary>
    /// The held cap of `03` §7.1 — at most this many consumables in the pouch at once, counted
    /// across all held kinds.
    /// </summary>
    /// <remarks>
    /// 📐 TUNABLE in the document and a constant here, and the difference is stated rather than
    /// hidden: <c>currencies.json</c> authors no <c>heldCap</c> key today, so reading one would fail
    /// every content load. Steering S6 forbids inventing the key; what it does not forbid is
    /// transcribing the number the document already prints. The day the key is authored, this
    /// constant is what the reader replaces.
    /// </remarks>
    internal const int HeldCap = 4;
}
