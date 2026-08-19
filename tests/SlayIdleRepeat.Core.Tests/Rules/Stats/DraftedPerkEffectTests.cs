using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Stats;
using SlayIdleRepeat.Core.Tests.Model;
using SlayIdleRepeat.Core.Tests.Rules.Combat;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Stats;

/// <summary>
/// What a drafted perk does to the hero the game fights with.
/// </summary>
/// <remarks>
/// The seam is the public build door, because that is what a fight and a hero screen are both handed
/// and it is the outermost place the answer is observable. The question underneath is not "does the
/// source collect" but the player-visible one: two runs identical except for a drafted perk have to
/// be two different heroes.
/// </remarks>
public sealed class DraftedPerkEffectTests
{
    /// <summary>An ATK perk the run owns raises the ATK the fight is run with.</summary>
    [Fact]
    public void A_drafted_attack_perk_raises_the_heros_attack()
    {
        var without = HeroBuild.Of(
            RunBattleWorlds.PlayerRow(),
            RunBattleWorlds.RunRow(),
            RunBattleWorlds.Content);

        var with = HeroBuild.Of(
            RunBattleWorlds.PlayerRow(),
            RunBattleWorlds.RunRow() with
            {
                OwnedPerkTiers = RunSnapshots.OwnedPerkTiers((AttackPerk, 1)),
            },
            RunBattleWorlds.Content);

        with.Stats[StatId.ATK].ShouldBeGreaterThan(
            without.Stats[StatId.ATK],
            "a run that drafted an ATK perk has to fight harder than one that did not");
    }

    /// <summary>A higher owned tier of one perk is a stronger hero than a lower one.</summary>
    [Fact]
    public void A_higher_owned_tier_is_a_stronger_hero()
    {
        var atTierOne = HeroBuild.Of(
            RunBattleWorlds.PlayerRow(),
            RunBattleWorlds.RunRow() with
            {
                OwnedPerkTiers = RunSnapshots.OwnedPerkTiers((AttackPerk, 1)),
            },
            RunBattleWorlds.Content);

        var atTierThree = HeroBuild.Of(
            RunBattleWorlds.PlayerRow(),
            RunBattleWorlds.RunRow() with
            {
                OwnedPerkTiers = RunSnapshots.OwnedPerkTiers((AttackPerk, 3)),
            },
            RunBattleWorlds.Content);

        atTierThree.Stats[StatId.ATK].ShouldBeGreaterThan(
            atTierOne.Stats[StatId.ATK],
            "upgrading a perk is the draft's whole reward for a repeat offer");
    }

    /// <summary>Outside a run there are no drafted perks, and the build says so rather than throwing.</summary>
    [Fact]
    public void A_build_outside_a_run_carries_no_perk_effects()
    {
        var outsideARun = HeroBuild.Of(RunBattleWorlds.PlayerRow(), run: null, RunBattleWorlds.Content);
        var inAnUndraftedRun = HeroBuild.Of(
            RunBattleWorlds.PlayerRow(), RunBattleWorlds.RunRow(), RunBattleWorlds.Content);

        outsideARun.Stats[StatId.ATK].ShouldBe(inAnUndraftedRun.Stats[StatId.ATK]);
    }

    /// <summary>The offense category's base perk, whose every tier is a plain ATK grant.</summary>
    private const string AttackPerk = "PK_MIGHT";
}
