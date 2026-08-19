using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Content.Effects;

/// <summary>
/// <see cref="EffectOp.MODIFY_DIE_FACE"/>'s <c>faceIndex</c>: the <c>"PLAYER_CHOICE"</c> token, and the numbered form over six faces.
/// </summary>
public sealed class DieFaceIndexTests
{
    [Fact]
    public void PLAYER_CHOICE_names_no_number_and_renders_as_the_token_18_writes()
    {
        var index = DieFaceIndex.PlayerChoice;

        index.IsPlayerChoice.ShouldBeTrue();
        index.IsUnset.ShouldBeFalse();
        index.Face.ShouldBeNull();
        index.ToString().ShouldBe("PLAYER_CHOICE");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(6)]
    public void A_numbered_face_carries_its_number(int face)
    {
        var index = DieFaceIndex.At(face);

        index.Face.ShouldBe(face);
        index.IsPlayerChoice.ShouldBeFalse();
        index.IsUnset.ShouldBeFalse();
        index.ToString().ShouldBe(face.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>The die has six faces, numbered 1-based.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void A_face_outside_one_to_six_is_rejected(int face)
    {
        var thrown = Should.Throw<ArgumentOutOfRangeException>(() => DieFaceIndex.At(face));

        thrown.Message.ShouldContain("04 §1", Case.Sensitive);
        thrown.ParamName.ShouldBe("face");
    }

    /// <summary>
    /// A <c>default</c> value names no face, and must not read as <see cref="DieFaceIndex.PlayerChoice"/> —
    /// a forgotten assignment would silently become a real instruction to the run controller.
    /// </summary>
    [Fact]
    public void A_default_index_is_unset_rather_than_player_choice()
    {
        var unset = default(DieFaceIndex);

        unset.IsUnset.ShouldBeTrue();
        unset.IsPlayerChoice.ShouldBeFalse(
            "a zero-initialised struct must not read as PLAYER_CHOICE — that is a real instruction");
        unset.Face.ShouldBeNull();
        unset.ShouldNotBe(DieFaceIndex.PlayerChoice);

        var thrown = Should.Throw<InvalidOperationException>(() => unset.ToString());
        thrown.Message.ShouldContain("names no face", Case.Sensitive);
    }

    [Fact]
    public void Two_indices_for_the_same_face_are_equal()
    {
        DieFaceIndex.At(4).ShouldBe(DieFaceIndex.At(4));
        DieFaceIndex.At(4).ShouldNotBe(DieFaceIndex.At(5));
        DieFaceIndex.PlayerChoice.ShouldBe(DieFaceIndex.PlayerChoice);
    }
}
