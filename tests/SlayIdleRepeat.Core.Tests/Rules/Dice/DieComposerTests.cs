using Shouldly;
using SlayIdleRepeat.Core.Content.Dice;
using SlayIdleRepeat.Core.Rules.Dice;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Dice;

/// <summary>The run-start die composition pipeline.</summary>
public sealed class DieComposerTests
{
    [Fact]
    public void The_starting_die_is_six_pips_one_through_six()
    {
        var faces = DieComposer.StartingDie;

        faces.Count.ShouldBe(6);

        for (var i = 0; i < 6; i++)
        {
            faces[i].Kind.ShouldBe(DieFaceKind.Pip);
            faces[i].Value.ShouldBe(i + 1);
        }
    }

    [Fact]
    public void No_layers_returns_the_base_die_unchanged()
    {
        var composed = DieComposer.Compose(DieComposer.StartingDie, Array.Empty<DieFaceOverride>());

        composed.ShouldBe(DieComposer.StartingDie);
    }

    [Fact]
    public void A_layer_replaces_exactly_the_face_it_names()
    {
        var star = DieFace.Special(DieFaceKind.Star);

        var composed = DieComposer.Compose(
            DieComposer.StartingDie, new[] { new DieFaceOverride(6, star) });

        composed[5].ShouldBe(star);

        for (var i = 0; i < 5; i++)
        {
            composed[i].ShouldBe(DieComposer.StartingDie[i]);
        }
    }

    [Fact]
    public void Later_layers_override_earlier_ones_at_the_same_index()
    {
        var surge = DieFace.Special(DieFaceKind.Surge);
        var fortune = DieFace.Special(DieFaceKind.Fortune);

        var composed = DieComposer.Compose(
            DieComposer.StartingDie,
            new[] { new DieFaceOverride(2, surge), new DieFaceOverride(2, fortune) });

        composed[1].ShouldBe(fortune);
    }

    [Fact]
    public void A_base_die_that_is_not_six_faces_is_rejected()
    {
        Should.Throw<ArgumentException>(
            () => DieComposer.Compose(new[] { DieFace.Pip(1) }, Array.Empty<DieFaceOverride>()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public void A_layer_naming_a_face_outside_one_to_six_is_rejected(int index)
    {
        Should.Throw<ArgumentException>(() => DieComposer.Compose(
            DieComposer.StartingDie, new[] { new DieFaceOverride(index, DieFace.Pip(1)) }));
    }
}
