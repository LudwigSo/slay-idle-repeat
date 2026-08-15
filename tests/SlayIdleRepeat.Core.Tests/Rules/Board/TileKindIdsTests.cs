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

    [Fact]
    public void Every_declared_TileKind_value_is_covered_by_ToId()
    {
        // A floor against the enum silently growing a member ToId doesn't know about (S1's
        // "reflection/enumeration whose subject set can go empty" failure shape, mirrored: here
        // the risk is the set growing unnoticed, not shrinking).
        foreach (var kind in Enum.GetValues<TileKind>())
        {
            Should.NotThrow(() => TileKindIds.ToId(kind));
        }
    }
}
