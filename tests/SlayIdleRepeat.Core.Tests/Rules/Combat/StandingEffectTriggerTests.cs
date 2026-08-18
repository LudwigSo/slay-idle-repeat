using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Effects;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat;

/// <summary>
/// An absent trigger and an authored <c>ALWAYS</c> one are the same thing to a fight.
/// </summary>
/// <remarks>
/// 🔴 <b>They were not, and the gap was invisible from both sides.</b> An actor collected its
/// standing modifiers by asking whether the trigger was <em>absent</em>, while the DSL's own default
/// says an absent trigger IS <c>ALWAYS</c>. Every effect in this suite was written with no trigger at
/// all, so every aggregation test passed; and every gear stat, affix and set bonus the hero build
/// synthesises is written with an explicit <c>ALWAYS</c>, so none of them reached a fight. Gear
/// aggregated correctly on a hero screen and did nothing whatsoever in combat.
/// <para>
/// Three arms, because two would not separate the readings: the explicit form must match the absent
/// form (the rule), and both must differ from no effect at all (the control that stops "matches"
/// being satisfied by two effects that are equally ignored).
/// </para>
/// </remarks>
public sealed class StandingEffectTriggerTests
{
    private const double BaseAtk = 100.0;

    private const double Bonus = 40.0;

    /// <summary>An explicitly <c>ALWAYS</c>-triggered stat op is a standing modifier.</summary>
    [Fact]
    public void An_authored_ALWAYS_trigger_is_the_same_standing_modifier_as_no_trigger_at_all()
    {
        var absent = new EffectDefinition
        {
            Id = "TEST_STANDING_NO_TRIGGER",
            Op = EffectOp.STAT_ADD_FLAT,
            Stat = StatSelector.Of(StatId.ATK),
            Value = Bonus,
        };

        var explicitlyAlways = absent with
        {
            Id = "TEST_STANDING_ALWAYS_TRIGGER",
            Trigger = EffectDefaults.Always,
        };

        var withNothing = PublicFightBench.AggregatedAtk(BaseAtk);

        withNothing.ShouldBe(
            BaseAtk,
            "the floor: with no effect at all the hit carries the base attack, so the two arms below " +
            "are measured against a known zero");

        PublicFightBench.AggregatedAtk(BaseAtk, absent).ShouldBe(
            BaseAtk + Bonus, "the reading that already worked");

        PublicFightBench.AggregatedAtk(BaseAtk, explicitlyAlways).ShouldBe(
            BaseAtk + Bonus,
            "an authored ALWAYS is the DSL's own default spelled out, so it has to aggregate " +
            "identically — reading only the absent form leaves every gear effect inert in combat");
    }
}
