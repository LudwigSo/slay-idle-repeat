namespace SlayIdleRepeat.Client.Game.Presenters;

/// <summary>
/// The run's pending tile arrives as a bare integer; this is the only thing that gives it a name.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>A transcription of a format another assembly owns, and it is one on purpose rather than by
/// omission.</b> The enum that assigns each tile kind its number, and the helper that turns one into
/// a <c>TILE_*</c> identifier, are both internal to the rules assembly. Nothing a client can
/// reference names them. So either the board renders the tile the player is standing on as a
/// number, or this table exists — and a board that cannot say what the player is standing on is not
/// a board.
/// </para>
/// <para>
/// The same shape <see cref="ChapterSelectPresenter"/> settled on for the clear-history key it also
/// cannot ask for: transcribe it, say so, and pin the literal shape in a case so it breaks loudly
/// here rather than quietly on a screen. What makes the pin possible at all is that the numbering is
/// a documented contract rather than an accident — the rules layer states in two places that the
/// kinds run 0..13 with no explicit values, because its own "no tile is pending" sentinel of -1
/// depends on no legal kind ever colliding with it.
/// </para>
/// <para>
/// 🔴 <b>What this table cannot notice.</b> A kind INSERTED in the middle of that enum renumbers
/// every kind after it, and this table would go on resolving each number to the name that used to
/// sit there — every tile after the insertion silently mislabelled, with nothing red. The
/// accompanying case pins the count and every name in order, which turns an insertion into a failing
/// case; it cannot turn a REORDERING of two adjacent names into one, because the count and the set
/// both survive it. That residue is the price of the boundary, and it is stated rather than implied.
/// </para>
/// </remarks>
public static class BoardTileKinds
{
    /// <summary>
    /// The value the run carries while it stands on no unresolved tile.
    /// </summary>
    /// <remarks>
    /// Transcribed with the rest of the table and for the same reason. It is not a fourteenth kind:
    /// the rules layer picked it precisely because it can never collide with one.
    /// </remarks>
    public const int NoPendingTile = -1;

    /// <summary>
    /// Each tile kind's caption key, indexed by the number the run reports it as.
    /// </summary>
    /// <remarks>
    /// Order is the whole content of this array — the index IS the tile kind — so it may never be
    /// sorted, deduplicated or appended to except to follow the rules layer's own enum.
    /// </remarks>
    private static readonly string[] NameKeysByKind =
    [
        "loc.tile.enemy.name",
        "loc.tile.elite.name",
        "loc.tile.boss.name",
        "loc.tile.shrine.name",
        "loc.tile.curse.name",
        "loc.tile.treasure.name",
        "loc.tile.shop.name",
        "loc.tile.campfire.name",
        "loc.tile.minigame.name",
        "loc.tile.event.name",
        "loc.tile.portal.name",
        "loc.tile.cache.name",
        "loc.tile.dice_forge.name",
        "loc.tile.empty.name",
    ];

    /// <summary>How many tile kinds this table claims the game has.</summary>
    public static int Count => NameKeysByKind.Length;


    /// <summary>The caption key for one tile kind, or null when the number names no kind.</summary>
    /// <remarks>
    /// 🔒 Null rather than a fallback caption. A number outside the table is a tile kind this build
    /// was never taught, and answering it with "Waypoint" — or with any other real tile's name —
    /// would put a plausible value in a hole, which is the one thing this codebase refuses to draw.
    /// The caller shows nothing and reports the number instead.
    /// </remarks>
    /// <param name="kind">The number the run carries in its pending-tile field.</param>
    public static string? NameKeyFor(int kind) =>
        kind >= 0 && kind < NameKeysByKind.Length ? NameKeysByKind[kind] : null;

    /// <summary>Every caption key, in tile-kind order — what the pinning case is stated over.</summary>
    public static IReadOnlyList<string> NameKeys => NameKeysByKind;
}
