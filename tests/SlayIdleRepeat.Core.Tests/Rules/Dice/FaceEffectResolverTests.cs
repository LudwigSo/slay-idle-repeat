using Shouldly;
using SlayIdleRepeat.Core.Content.Dice;
using SlayIdleRepeat.Core.Rules.Dice;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Dice;

/// <summary>`04` §1 — resolving each face kind into movement and side effect.</summary>
public sealed class FaceEffectResolverTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    public void Pip_moves_exactly_its_value(int pips)
    {
        var outcome = FaceEffectResolver.Resolve(DieFace.Pip(pips));

        outcome.Movement.ShouldBe(pips);
        outcome.RequiresPlayerChoice.ShouldBeFalse();
        outcome.HealPct.ShouldBeNull();
        outcome.DoubleReward.ShouldBeFalse();
    }

    [Fact]
    public void Star_with_no_player_choice_requires_one_and_moves_nothing()
    {
        var outcome = FaceEffectResolver.Resolve(DieFace.Special(DieFaceKind.Star));

        outcome.RequiresPlayerChoice.ShouldBeTrue();
        outcome.Movement.ShouldBe(0);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    public void Star_with_a_player_choice_moves_that_far(int choice)
    {
        var outcome = FaceEffectResolver.Resolve(DieFace.Special(DieFaceKind.Star), playerChosenMovement: choice);

        outcome.RequiresPlayerChoice.ShouldBeFalse();
        outcome.Movement.ShouldBe(choice);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public void A_player_choice_outside_one_to_six_is_rejected(int choice)
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => FaceEffectResolver.Resolve(DieFace.Special(DieFaceKind.Star), playerChosenMovement: choice));
    }

    [Theory]
    [InlineData(0, 0.12)]
    [InlineData(1, 0.15)]
    [InlineData(2, 0.18)]
    [InlineData(3, 0.21)]
    public void Surge_moves_3_and_heals_by_tier(int tier, double expectedHealPct)
    {
        var outcome = FaceEffectResolver.Resolve(DieFace.Special(DieFaceKind.Surge, tier));

        outcome.Movement.ShouldBe(3);
        outcome.HealPct.ShouldNotBeNull();
        outcome.HealPct!.Value.ShouldBe(expectedHealPct, tolerance: 1e-9);
    }

    [Fact]
    public void Fortune_moves_4_and_doubles_the_landed_reward()
    {
        var outcome = FaceEffectResolver.Resolve(DieFace.Special(DieFaceKind.Fortune));

        outcome.Movement.ShouldBe(4);
        outcome.DoubleReward.ShouldBeTrue();
        FaceEffectResolver.FortuneRewardMultiplier.ShouldBe(2.0);
    }

    [Fact]
    public void Void_moves_nothing_and_re_resolves_at_half_reward()
    {
        var outcome = FaceEffectResolver.Resolve(DieFace.Special(DieFaceKind.Void));

        outcome.Movement.ShouldBe(0);
        outcome.ReResolveAtHalfReward.ShouldBeTrue();
        FaceEffectResolver.VoidReResolveRewardMultiplier.ShouldBe(0.5);
    }

    [Fact]
    public void Chain_moves_2_and_rolls_again_under_the_link_cap()
    {
        var outcome = FaceEffectResolver.Resolve(DieFace.Special(DieFaceKind.Chain), chainLinksSoFar: 0);

        outcome.Movement.ShouldBe(2);
        outcome.RollAgain.ShouldBeTrue();
        outcome.ForcedStop.ShouldBeFalse();
    }

    [Fact]
    public void Chain_forces_a_stop_at_the_link_cap()
    {
        var outcome = FaceEffectResolver.Resolve(
            DieFace.Special(DieFaceKind.Chain), chainLinksSoFar: FaceEffectResolver.ChainMaxLinks);

        outcome.Movement.ShouldBe(2);
        outcome.RollAgain.ShouldBeFalse();
        outcome.ForcedStop.ShouldBeTrue();
    }

    [Fact]
    public void Chain_still_rolls_again_one_below_the_cap()
    {
        var outcome = FaceEffectResolver.Resolve(
            DieFace.Special(DieFaceKind.Chain), chainLinksSoFar: FaceEffectResolver.ChainMaxLinks - 1);

        outcome.RollAgain.ShouldBeTrue();
    }

    [Fact]
    public void An_unset_face_is_rejected()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => FaceEffectResolver.Resolve(default));
    }

    [Fact]
    public void A_negative_chain_link_count_is_rejected()
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => FaceEffectResolver.Resolve(DieFace.Pip(1), chainLinksSoFar: -1));
    }
}
