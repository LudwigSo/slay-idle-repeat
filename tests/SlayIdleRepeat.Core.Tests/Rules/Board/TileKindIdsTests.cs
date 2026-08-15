using SlayIdleRepeat.Core.Rules.Board;
using Shouldly;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Board;

public sealed class TileKindIdsTests
{
    [Theory]
    [InlineData(TileKind.Enemy, "TILE_ENEMY")]
    [InlineData(TileKind.Elite, "TILE_ELITE")]
    [InlineData(TileKind.Boss, "TILE_BOSS")]
    [InlineData(TileKind.Shrine, "TILE_SHRINE")]
    [InlineData(TileKind.Curse, "TILE_CURSE")]
    [InlineData(TileKind.Treasure, "TILE_TREASURE")]
    [InlineData(TileKind.Shop, "TILE_SHOP")]
    [InlineData(TileKind.Campfire, "TILE_CAMPFIRE")]
    [InlineData(TileKind.Minigame, "TILE_MINIGAME")]
    [InlineData(TileKind.Event, "TILE_EVENT")]
    [InlineData(TileKind.Portal, "TILE_PORTAL")]
    [InlineData(TileKind.Cache, "TILE_CACHE")]
    [InlineData(TileKind.DiceForge, "TILE_DICE_FORGE")]
    [InlineData(TileKind.Empty, "TILE_EMPTY")]
    public void Every_tile_kind_round_trips_through_its_id(TileKind kind, string id)
    {
        TileKindIds.ToId(kind).ShouldBe(id);
        TileKindIds.Parse(id).ShouldBe(kind);
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
