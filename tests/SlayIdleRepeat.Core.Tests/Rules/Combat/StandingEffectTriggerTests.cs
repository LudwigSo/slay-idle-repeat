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
/// Every gear stat, affix and set bonus is synthesised with an explicit <c>ALWAYS</c>, so a
/// collector reading only the absent spelling leaves gear aggregating correctly on a hero screen
/// while doing nothing whatsoever in combat. Three arms, because two would not separate the
/// readings: the explicit form must match the absent form, and both must differ from no effect at
/// all — the control that stops "matches" being satisfied by two equally ignored effects.
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

    /// <summary>An actor opens the fight on its AGGREGATED Max HP, not on its base block's.</summary>
    /// <remarks>
    /// An actor opening on its base bar while its aggregated Max HP is larger starts every fight
    /// part-dead, and the timeout is decided on HP fraction — a hero in full gear loses fights their
    /// own build wins. The defender deals no damage, so the health left at the end IS the health the
    /// hero opened on.
    /// </remarks>
    [Fact]
    public void An_actor_opens_the_fight_on_its_aggregated_max_hp_and_not_on_its_base_block()
    {
        var maxHpBonus = new EffectDefinition
        {
            Id = "TEST_STANDING_MAX_HP",
            Op = EffectOp.STAT_ADD_FLAT,
            Stat = StatSelector.Of(StatId.MAX_HP),
            Trigger = EffectDefaults.Always,
            Value = BaseMaxHp,
        };

        Unhurt().ShouldBe(BaseMaxHp, "the floor: with no effect the hero opens on its base block");

        Unhurt(maxHpBonus).ShouldBe(
            BaseMaxHp * 2.0,
            "an actor that opens on its base Max HP while its aggregated Max HP is twice that starts " +
            "every fight half dead, and nothing about the fight says so");
    }

    /// <summary>The hero's health at the end of a fight it was never hit in.</summary>
    private static double Unhurt(params EffectDefinition[] effects) =>
        PublicFightBench.Duel(
            PublicFightBench.Stats(BaseMaxHp, (StatId.ATK, BaseAtk)),
            PublicFightBench.Stats(500_000.0),
            attackerEffects: effects)
        .HeroHpRemaining;

    /// <summary>The base Max HP both arms start from.</summary>
    private const double BaseMaxHp = 500.0;
}
