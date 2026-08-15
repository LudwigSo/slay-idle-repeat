using Shouldly;
using SlayIdleRepeat.Core.Content.Dice;
using SlayIdleRepeat.Core.Rules.Dice;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Dice;

/// <summary>The pure resolver behind <c>TILE_DICE_FORGE</c>'s upgrade.</summary>
public sealed class DiceForgeUpgradeResolverTests
{
    [Fact]
    public void Raising_a_Pip_to_a_higher_value_succeeds()
    {
        var result = DiceForgeUpgradeResolver.Resolve(
            DieFace.Pip(1), DiceForgeUpgradeOption.ToHigherPip(), higherPipValue: 4);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Kind.ShouldBe(DieFaceKind.Pip);
        result.Value.Value.ShouldBe(4);
    }

    [Fact]
    public void Raising_to_a_value_that_is_not_higher_is_refused()
    {
        var result = DiceForgeUpgradeResolver.Resolve(
            DieFace.Pip(4), DiceForgeUpgradeOption.ToHigherPip(), higherPipValue: 4);

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Raising_to_a_value_above_six_is_refused()
    {
        var result = DiceForgeUpgradeResolver.Resolve(
            DieFace.Pip(5), DiceForgeUpgradeOption.ToHigherPip(), higherPipValue: 7);

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void HigherPip_with_no_value_supplied_is_refused()
    {
        var result = DiceForgeUpgradeResolver.Resolve(DieFace.Pip(1), DiceForgeUpgradeOption.ToHigherPip());

        result.IsFailure.ShouldBeTrue();
    }

    [Theory]
    [InlineData(DieFaceKind.Star)]
    [InlineData(DieFaceKind.Surge)]
    [InlineData(DieFaceKind.Fortune)]
    [InlineData(DieFaceKind.Chain)]
    public void Installing_a_special_kind_succeeds_at_tier_zero(DieFaceKind kind)
    {
        var result = DiceForgeUpgradeResolver.Resolve(DieFace.Pip(1), DiceForgeUpgradeOption.ToKind(kind));

        result.IsSuccess.ShouldBeTrue();
        result.Value.Kind.ShouldBe(kind);
        result.Value.Tier.ShouldBe(0);
    }

    [Fact]
    public void A_Void_face_is_never_a_legal_source()
    {
        var result = DiceForgeUpgradeResolver.Resolve(
            DieFace.Special(DieFaceKind.Void), DiceForgeUpgradeOption.ToKind(DieFaceKind.Star));

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void An_already_special_face_is_not_a_legal_source()
    {
        var result = DiceForgeUpgradeResolver.Resolve(
            DieFace.Special(DieFaceKind.Fortune), DiceForgeUpgradeOption.ToKind(DieFaceKind.Star));

        result.IsFailure.ShouldBeTrue();
    }
}
