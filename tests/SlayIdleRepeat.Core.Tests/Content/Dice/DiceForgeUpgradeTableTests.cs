using System.Linq;
using Shouldly;
using SlayIdleRepeat.Core.Content.Dice;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Content.Dice;

/// <summary>The authored <c>TILE_DICE_FORGE</c> upgrade menu.</summary>
public sealed class DiceForgeUpgradeTableTests
{
    [Fact]
    public void A_Pip_face_is_a_legal_source()
    {
        DiceForgeUpgradeTable.IsLegalSource(DieFace.Pip(1)).ShouldBeTrue();
    }

    [Theory]
    [InlineData(DieFaceKind.Void)]
    [InlineData(DieFaceKind.Star)]
    [InlineData(DieFaceKind.Surge)]
    [InlineData(DieFaceKind.Fortune)]
    [InlineData(DieFaceKind.Chain)]
    public void A_non_Pip_face_is_never_a_legal_source(DieFaceKind kind)
    {
        DiceForgeUpgradeTable.IsLegalSource(DieFace.Special(kind)).ShouldBeFalse();
    }

    [Fact]
    public void An_unset_face_is_not_a_legal_source()
    {
        DiceForgeUpgradeTable.IsLegalSource(default).ShouldBeFalse();
    }

    [Fact]
    public void The_menu_offers_exactly_the_five_options_the_report_will_justify()
    {
        var kinds = DiceForgeUpgradeTable.Options
            .Where(o => !o.IsHigherPip)
            .Select(o => o.Kind!.Value)
            .OrderBy(k => k)
            .ToArray();

        kinds.ShouldBe(
            new[] { DieFaceKind.Star, DieFaceKind.Surge, DieFaceKind.Fortune, DieFaceKind.Chain }
                .OrderBy(k => k)
                .ToArray());

        DiceForgeUpgradeTable.Options.Count(o => o.IsHigherPip).ShouldBe(1);
        DiceForgeUpgradeTable.Options.Count.ShouldBe(5);
    }

    [Fact]
    public void Void_can_never_be_named_as_a_target()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => DiceForgeUpgradeOption.ToKind(DieFaceKind.Void));
    }

    [Fact]
    public void Pip_is_not_a_ToKind_target_use_ToHigherPip()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => DiceForgeUpgradeOption.ToKind(DieFaceKind.Pip));
    }
}
