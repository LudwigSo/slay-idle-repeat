using Shouldly;
using SlayIdleRepeat.Client.Game.Presenters;
using Xunit;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// `03` §2 — the transcription that gives the run's pending-tile integer a name, pinned so it
/// breaks here rather than on a screen.
/// </summary>
/// <remarks>
/// 🔒 The table is a copy of an enum this assembly cannot see. A copy that agrees only with itself
/// is not evidence of anything, so the literal shape is pinned: the fifteen kinds of `03` §2 in the
/// order the rules layer numbers them, and the sentinel that is not one of them.
/// </remarks>
public sealed class BoardTileKindsTests
{
    /// <summary>
    /// The fifteen tile kinds of `03` §2, in the order the rules layer's own enum declares them —
    /// written out here rather than derived, because a derivation from the table under test would
    /// agree with any table at all.
    /// </summary>
    private static readonly string[] ExpectedKeysInKindOrder =
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
        "loc.tile.miniboss.name",
    ];

    [Fact]
    public void The_table_carries_the_fifteen_tile_kinds_in_the_rules_layers_own_order()
    {
        BoardTileKinds.NameKeys.ShouldBe(
            ExpectedKeysInKindOrder,
            "the tile-kind table no longer matches the enum it transcribes. 03 §2 authors fifteen " +
            "kinds and the rules layer numbers them 0..14 with no explicit values; a kind inserted, " +
            "removed or reordered there renumbers every kind after it, and this table would go on " +
            "resolving each number to the name that used to sit at it — every tile after the change " +
            "silently mislabelled on the board. Re-transcribe the enum rather than adjusting this list " +
            "to match the table.");
    }

    [Fact]
    public void The_count_is_the_fifteen_kinds_the_design_authors()
    {
        BoardTileKinds.Count.ShouldBe(15);
    }

    /// <summary>
    /// The sentinel is not a kind. The rules layer chose -1 precisely so it can never collide with
    /// one, and a table that answered it with a name would put a tile on a board standing on none.
    /// </summary>
    [Fact]
    public void The_no_tile_sentinel_names_no_kind()
    {
        BoardTileKinds.NoPendingTile.ShouldBe(-1);
        BoardTileKinds.NameKeyFor(BoardTileKinds.NoPendingTile).ShouldBeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    [InlineData(14)]
    public void Every_number_inside_the_table_resolves(int kind)
    {
        BoardTileKinds.NameKeyFor(kind).ShouldBe(ExpectedKeysInKindOrder[kind]);
    }

    /// <summary>
    /// 🔒 A number outside the table borrows nobody's caption. Answering an unknown kind with a real
    /// tile's name is a plausible value in a hole, which is worse than a visibly absent one.
    /// </summary>
    [Theory]
    [InlineData(-2)]
    [InlineData(15)]
    [InlineData(99)]
    public void A_number_outside_the_table_resolves_to_nothing(int kind)
    {
        BoardTileKinds.NameKeyFor(kind).ShouldBeNull();
    }

    [Fact]
    public void Every_key_is_distinct()
    {
        BoardTileKinds.NameKeys.Distinct(StringComparer.Ordinal)
                      .Count().ShouldBe(BoardTileKinds.Count);
    }
}
