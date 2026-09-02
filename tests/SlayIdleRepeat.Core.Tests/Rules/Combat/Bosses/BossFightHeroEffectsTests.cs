using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Tests.TestSupport;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat.Bosses;

/// <summary>
/// The hero's own effects reach a boss fight.
/// </summary>
/// <remarks>
/// 🔴 <b>They did not.</b> <c>EncounterFight.Run</c> has taken a <c>heroEffects</c> list since it was
/// written, and the boss composition built its hero's plan with no <c>Effects</c> member at all — so
/// the same player, wearing the same loadout, fought every ordinary enemy with their gear and every
/// boss without it. Nothing threw and the fight completed; the boss was simply a different fight
/// from the one the player's build says they were in, and the exit criterion runs through a boss.
/// </remarks>
public sealed class BossFightHeroEffectsTests
{
    /// <summary>A flat attack bonus the hero holds for the fight — large enough to move the outcome.</summary>
    /// <remarks>
    /// <c>STAT_ADD_FLAT</c> on <c>ATK</c> rather than a percentage: the bench hero's attack is a
    /// plain number, so a flat add lands in the one bucket whose arrival is not conditional on
    /// anything else in the pipeline being populated.
    /// </remarks>
    private static EffectDefinition HeroAttackBonus => new()
    {
        Id = "TEST_HERO_HELD_ATTACK",
        Op = EffectOp.STAT_ADD_FLAT,
        Stat = StatSelector.Of(StatId.ATK),
        Trigger = EffectDefaults.Always,
        Target = EffectDefaults.AbsentTarget,
        Value = 2_000.0,
    };

    /// <summary>The boss both arms are fought against.</summary>
    private const string BossId = "BOSS_THORNMAW";

    /// <summary>A boss fight with the hero's effects is a different fight from one without them.</summary>
    /// <remarks>
    /// The hash, because "different" is the whole claim and an outcome comparison would be satisfied
    /// by a fight the effect never reached but which the seed happened to resolve differently — it
    /// would not, the seed is fixed, which is exactly why an identical hash here is proof the list
    /// was dropped on the floor.
    /// </remarks>
    [Fact]
    public void A_boss_fight_carries_the_heros_held_effects()
    {
        var without = Fight(heroEffects: null);
        var with = Fight(heroEffects: [HeroAttackBonus]);

        with.LogHash.ShouldNotBe(
            without.LogHash,
            "a boss fought without the player's loadout is a silently different fight");
    }

    /// <summary>And the direction is the effect's own: a +2000 attack hero kills the boss faster.</summary>
    /// <remarks>
    /// The negative control for the hash case above. Two different hashes could be two different
    /// fights for any reason; this pins that the difference is the one the effect authorises.
    /// </remarks>
    [Fact]
    public void The_heros_held_attack_bonus_shortens_the_boss_fight()
    {
        Fight(heroEffects: [HeroAttackBonus]).DurationTicks.ShouldBeLessThan(
            Fight(heroEffects: null).DurationTicks,
            "an attack bonus that reached the fight has to end it sooner");
    }

    /// <summary>An empty list and no list at all are the same fight.</summary>
    /// <remarks>
    /// The other control: without it, "with effects differs from without" would also pass a
    /// composition that changed the fight merely by being handed a list, rather than by what is in
    /// it.
    /// </remarks>
    [Fact]
    public void An_empty_effect_list_composes_the_same_fight_as_none_at_all()
    {
        Fight(heroEffects: []).LogHash.ShouldBe(Fight(heroEffects: null).LogHash);
    }

    private static SimulationResult Fight(IReadOnlyList<EffectDefinition>? heroEffects) =>
        CombatSimulator.SimulateBossFight(
            RealBossFight.BattleSeed,
            RealBossFight.Hero(),
            RealBossFight.Level,
            BossId,
            RealBossFight.BossPower,
            RealBossFight.Level,
            ShippedHarness.Content,
            firstClear: false,
            heroEffects: heroEffects);
}
