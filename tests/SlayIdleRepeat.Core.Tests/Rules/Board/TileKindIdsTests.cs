using SlayIdleRepeat.Core.Rules.Board;
using Shouldly;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Board;

public sealed class TileKindIdsTests
{
    // TileKind is internal, so a [Theory]/[InlineData] cannot take it directly as a public test
    // method parameter (CS0051) — every case is driven by its string id instead, resolved to the
    // enum value inside the (internal-type-touching but public-signature) test body.
    [Theory]
    [InlineData("TILE_ENEMY")]
    [InlineData("TILE_ELITE")]
    [InlineData("TILE_BOSS")]
    [InlineData("TILE_SHRINE")]
    [InlineData("TILE_CURSE")]
    [InlineData("TILE_TREASURE")]
    [InlineData("TILE_SHOP")]
    [InlineData("TILE_CAMPFIRE")]
    [InlineData("TILE_MINIGAME")]
    [InlineData("TILE_EVENT")]
    [InlineData("TILE_PORTAL")]
    [InlineData("TILE_CACHE")]
    [InlineData("TILE_DICE_FORGE")]
    [InlineData("TILE_EMPTY")]
    [InlineData("TILE_MINIBOSS")]
    public void Every_tile_kind_round_trips_through_its_id(string id)
    {
        var kind = TileKindIds.Parse(id);
        TileKindIds.ToId(kind).ShouldBe(id);
        TileKindIds.Parse(TileKindIds.ToId(kind)).ShouldBe(kind);
    }

    [Fact]
    public void An_unrecognized_id_fails_to_parse()
    {
        TileKindIds.TryParse("TILE_NOPE", out _).ShouldBeFalse();
        Should.Throw<ArgumentException>(() => TileKindIds.Parse("TILE_NOPE"));
    }

    /// <summary>
    /// The newest kind is numbered LAST. The client's tile-name table is indexed by this number and
    /// transcribes the enum positionally, so a kind inserted mid-enum renumbers every kind after it
    /// and silently mislabels each one on the board; appending keeps every existing number.
    /// </summary>
    [Fact]
    public void MiniBoss_is_the_last_numbered_kind_so_no_existing_kinds_number_moved()
    {
        ((int)TileKind.MiniBoss).ShouldBe(14);
        ((int)TileKind.Empty).ShouldBe(13, "the kind MiniBoss was appended after.");
    }

    [Fact]
    public void Every_declared_TileKind_value_is_covered_by_ToId()
    {
        // Floor against the enum silently growing a member ToId doesn't know about.
        foreach (var kind in Enum.GetValues<TileKind>())
        {
            Should.NotThrow(() => TileKindIds.ToId(kind));
        }
    }
}
