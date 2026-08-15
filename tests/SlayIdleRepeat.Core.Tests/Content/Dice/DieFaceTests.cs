using Shouldly;
using SlayIdleRepeat.Core.Content.Dice;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Content.Dice;

/// <summary>`04` §1's <c>DieFace</c> struct: Pip faces carry a value, special faces carry a tier.</summary>
public sealed class DieFaceTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    public void A_Pip_face_carries_its_value_at_tier_zero(int pips)
    {
        var face = DieFace.Pip(pips);

        face.Kind.ShouldBe(DieFaceKind.Pip);
        face.Value.ShouldBe(pips);
        face.Tier.ShouldBe(0);
        face.IsUnset.ShouldBeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public void A_Pip_face_outside_one_to_six_is_rejected(int pips)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => DieFace.Pip(pips));
    }

    [Theory]
    [InlineData(DieFaceKind.Star, 0)]
    [InlineData(DieFaceKind.Surge, 3)]
    [InlineData(DieFaceKind.Fortune, 2)]
    [InlineData(DieFaceKind.Chain, 1)]
    [InlineData(DieFaceKind.Void, 0)]
    public void A_special_face_carries_its_tier_and_no_pips(DieFaceKind kind, int tier)
    {
        var face = DieFace.Special(kind, tier);

        face.Kind.ShouldBe(kind);
        face.Value.ShouldBe(0);
        face.Tier.ShouldBe(tier);
    }

    [Fact]
    public void A_Pip_kind_is_refused_by_Special_use_Pip_instead()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => DieFace.Special(DieFaceKind.Pip));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void A_tier_outside_zero_to_three_is_rejected(int tier)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => DieFace.Special(DieFaceKind.Star, tier));
    }

    [Fact]
    public void Default_is_unset_and_names_no_face()
    {
        default(DieFace).IsUnset.ShouldBeTrue();
    }

    [Fact]
    public void An_undefined_kind_is_rejected()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => DieFace.Special((DieFaceKind)999));
    }
}
