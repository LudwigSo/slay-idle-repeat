using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Effects.Ops;
using SlayIdleRepeat.Core.Rules.Stats;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects.Ops;

/// <summary>
/// 🔒 R12 — <c>drawback</c> is a reserved <b>effect</b> tag and a status tag group is a different
/// vocabulary, kept apart at the type level.
/// </summary>
public sealed class EffectTaggingTests
{
    /// <summary>
    /// The marker `18` §7.5 defines by example and `05` §4.1's bypass list (b) keys on without naming.
    /// </summary>
    [Fact]
    public void The_reserved_marker_is_the_drawback_tag_18_7_5_authors()
    {
        AuthorTag.Drawback.Value.ShouldBe("drawback");
        AuthorTag.Drawback.IsDrawback.ShouldBeTrue();
        new AuthorTag("offense").IsDrawback.ShouldBeFalse();
    }

    /// <summary>
    /// 🔒 The two tag types are not interchangeable — the reason R12 asked for a type-level split.
    /// A single <c>string</c> vocabulary would let a <c>REMOVE_STATUS</c> clear the ward-bypass
    /// marker, or let a status label satisfy a ward-bypass check.
    /// </summary>
    [Fact]
    public void An_author_tag_and_a_status_tag_are_different_types_over_the_same_spelling()
    {
        // 🔒 The type separation itself is the COMPILER's to enforce — asserting that two declared
        //    types differ is a test that cannot fail. What can go wrong at run time is the two
        //    vocabularies leaking into each other, and that is what is asserted below.
        var status = new StatusTag("control");
        var author = new AuthorTag("control");

        var effect = new EffectDefinition
        {
            Id = "PK_X",
            Op = EffectOp.REMOVE_STATUS,
            StatusTag = status,
            Tags = ["control"],
        };

        EffectTagging.StatusTagOf(effect).ShouldBe(status);
        EffectTagging.AuthorTags(effect).ShouldBe([author]);
    }

    /// <summary>
    /// 🔒 The leak that matters: a status tag spelled <c>drawback</c> must NOT satisfy `05` §4.1's
    /// ward bypass. Under one shared string vocabulary it would.
    /// </summary>
    [Fact]
    public void A_status_tag_spelled_drawback_does_not_satisfy_the_ward_bypass()
    {
        var effect = new EffectDefinition
        {
            Id = "PK_X",
            Op = EffectOp.REMOVE_STATUS,
            Target = EffectTarget.SELF,
            StatusTag = new StatusTag(AuthorTag.Drawback.Value),
        };

        EffectTagging.IsDrawback(effect).ShouldBeFalse();
        EffectTagging.IsSelfInflictedCost(effect).ShouldBeFalse();
    }

    /// <summary>
    /// 🔒 `05` §4.1's class (b) is <em>"self-inflicted costs"</em> — the tag alone is not enough, the
    /// damage must point at the holder. Otherwise every cursed perk would get ward penetration.
    /// </summary>
    /// <remarks>
    /// 🔴 The <c>target: null</c> row <b>flipped</b> when the default was ruled: an absent <c>target</c> is
    /// <c>SELF</c>, so an untargeted drawback is a self-inflicted cost and keeps its bypass. The row is
    /// kept rather than deleted precisely because it is the one the ruling moved.
    /// </remarks>
    [Theory]
    [InlineData(true, EffectTarget.SELF, true)]
    [InlineData(true, EffectTarget.ALL_ENEMIES, false)]
    [InlineData(false, EffectTarget.SELF, false)]
    [InlineData(true, null, true)]
    [InlineData(false, null, false)]
    public void A_self_inflicted_cost_is_a_drawback_tag_AND_a_SELF_target(
        bool tagged, EffectTarget? target, bool expected)
    {
        var effect = new EffectDefinition
        {
            Id = "CP_X",
            Op = EffectOp.DAMAGE_MAXHP_PCT,
            Value = 0.03,
            Target = target,
            Tags = tagged ? ["drawback"] : [],
        };

        EffectTagging.IsSelfInflictedCost(effect).ShouldBe(expected);
    }
}

/// <summary>
/// 🔒 `05` §1.1's rounding is <b>one</b> rule, stated once in <c>Core.Primitives.DeterminismRounding</c>,
/// which `30` §11.4 puts beneath every layer that rounds.
/// </summary>
/// <remarks>
/// 🔴 This class's claim changed when the duplication was removed: it used to be the mechanism stopping
/// <c>OpRounding</c> and <c>StatRounding</c> — forced apart by R17 — from drifting. Both now delegate to
/// the shared primitive, so the comparison is close to a tautology.
/// <para>
/// 🔒 Kept anyway: the two types still exist and still carry deliberately different failure messages
/// (<c>OpRounding</c> names the effect, <c>StatRounding</c> the §8 step and the stat), and nothing but
/// this says their <em>arithmetic</em> must stay identical — the cheapest moment for them to diverge
/// again being the next edit to either file.
/// </para>
/// </remarks>
public sealed class OpRoundingTests
{
    [Theory]
    [InlineData(1.00004999)]
    [InlineData(1.00005)]
    [InlineData(-0.00004)]
    [InlineData(123.456789)]
    [InlineData(0.0)]
    [InlineData(-7.5)]
    [InlineData(2400.0)]
    public void The_op_rounding_and_the_stat_rounding_are_one_rule(double value)
    {
        var op = OpRounding.Round(value, "X", "a value");

        op.ShouldBe(StatRounding.Round(value, StatId.ATK, "a step"));

        // 🔒 …and both are the shared primitive, not merely each other. Comparing the two constants
        //    with one another would be `4.ShouldBe(4)` after Roslyn folds them — a comparison that
        //    cannot fail under any edit, which is steering S1's definition of a defect. Comparing
        //    each ROUNDED VALUE against the primitive is what actually bites if either stops
        //    delegating.
        op.ShouldBe(DeterminismRounding.Round(value));
        OpRounding.Decimals.ShouldBe(DeterminismRounding.Decimals);
        StatRounding.Decimals.ShouldBe(DeterminismRounding.Decimals);
    }

    /// <summary>
    /// The <c>-0.0</c> normalisation both perform. <c>CanonicalStateWriter</c> refuses a negative
    /// zero, and <c>CombatLog</c> refuses one after it, so an op must not produce one.
    /// </summary>
    [Fact]
    public void A_value_rounding_to_negative_zero_is_normalised_to_positive_zero()
    {
        var rounded = OpRounding.Round(-0.00004, "X", "a value");

        rounded.ShouldBe(0.0);
        double.IsNegative(rounded).ShouldBeFalse();
    }

    /// <summary>NaN and infinity name the effect that produced them, not the serialiser three layers on.</summary>
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void A_NaN_or_an_infinity_names_the_effect_that_produced_it(double value)
    {
        var thrown = Should.Throw<SlayIdleRepeat.Core.Rules.Effects.EffectContextException>(
            () => OpRounding.Round(value, "PK_OVERFLOW", "its damage"));

        thrown.Token.ShouldBe("PK_OVERFLOW");
        thrown.Message.ShouldContain("its damage", Case.Sensitive);
    }
}
