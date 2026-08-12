using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Content.Effects;

/// <summary>
/// <see cref="EffectOp.MODIFY_DIE_FACE"/>'s <c>faceIndex</c> — `18` §7.9's <c>"PLAYER_CHOICE"</c>
/// and the numbered form over `04` §1's six faces.
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
        index.ToString().ShouldBe(DieFaceIndex.PlayerChoiceToken);
        DieFaceIndex.PlayerChoiceToken.ShouldBe("PLAYER_CHOICE");
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

    /// <summary>`04` §1 gives the die six faces, and the numbering is 1-based — recorded as an inference.</summary>
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

    [Fact]
    public void The_bounds_are_the_six_faces_04_gives_the_die()
    {
        DieFaceIndex.MinFace.ShouldBe(1);
        DieFaceIndex.MaxFace.ShouldBe(6);
    }

    /// <summary>
    /// 🔒 A <c>default</c> value names no face, and must not read as <see cref="DieFaceIndex.PlayerChoice"/>.
    /// </summary>
    /// <remarks>
    /// This is the same hazard every enum in the DSL avoids by having no <c>0</c> member and that
    /// <see cref="StatSelector"/> avoids by throwing on a default selector. If "player choice" were
    /// modelled as the <em>absence</em> of a face number, a forgotten assignment would become a real
    /// instruction to the run controller — a die face replaced at the player's choosing, arrived at
    /// by accident.
    /// </remarks>
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

    /// <summary>`18` §7.9's <c>TILE_DICE_FORGE</c>, as a whole effect.</summary>
    [Fact]
    public void TILE_DICE_FORGE_replaces_the_player_chosen_face_with_a_four_pip()
    {
        var effect = new EffectDefinition
        {
            Id = "TILE_DICE_FORGE_FACE",
            Op = EffectOp.MODIFY_DIE_FACE,
            FaceIndex = DieFaceIndex.PlayerChoice,
            NewFace = new DieFaceSpec("Pip", 4),
            Duration = new EffectDuration { Scope = DurationScope.RUN },
            Trigger = new EffectTrigger { Kind = TriggerKind.ON_TILE_RESOLVED, TileType = "TILE_DICE_FORGE" },
        };

        effect.FaceIndex!.Value.IsPlayerChoice.ShouldBeTrue();
        effect.NewFace!.Kind.ShouldBe("Pip");
        effect.NewFace.Value.ShouldBe(4);
        effect.Family.ShouldBe(EffectOpFamily.RUN_AND_BOARD);
    }

    /// <summary>`18` §9.2's <c>PET_DICEBEAST</c> — a <c>Star</c> face with no pip value.</summary>
    [Fact]
    public void PET_DICEBEAST_grants_a_star_face_for_the_next_three_rolls()
    {
        var effect = new EffectDefinition
        {
            Id = "PET_DICEBEAST_ACTIVE",
            Op = EffectOp.MODIFY_DIE_FACE,
            NewFace = new DieFaceSpec("Star"),
            Scope = DieFaceScope.NEXT_3_ROLLS,
            Trigger = new EffectTrigger { Kind = TriggerKind.ON_BATTLE_END, OnlyIfWon = true },
        };

        effect.NewFace!.Value.ShouldBeNull("a Star face has no pip count — 04 §1");
        effect.Scope.ShouldBe(DieFaceScope.NEXT_3_ROLLS);
        effect.Trigger!.OnlyIfWon.ShouldBe(true);
    }
}
