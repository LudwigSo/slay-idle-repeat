using Shouldly;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Tests.Model;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat;

/// <summary>
/// What a drafted perk does to a fight.
/// </summary>
/// <remarks>
/// The seam is the whole simulation, because the question is the player's: a perk that aggregates on
/// a screen and never reaches the tick loop is the failure this suite exists to catch, and it looks
/// identical to a working perk from every other angle. A stat perk is visible in the stat block; an
/// ailment perk is only visible here, because <c>APPLY_STATUS</c> writes nothing a build can publish.
/// </remarks>
public sealed class DraftedPerkFightTests
{
    /// <summary>An ailment perk the run owns puts its ailment on an enemy in the battle log.</summary>
    /// <remarks>
    /// The status is identified by its wire ordinal rather than by its id: that ordinal is what a
    /// replay carries, so asserting on it is asserting on the thing a client would draw.
    /// </remarks>
    [Fact]
    public void An_ailment_perk_the_run_owns_applies_its_ailment_during_the_battle()
    {
        var without = Fight(owning: null);
        var with = Fight(owning: BurnPerk);

        Applied(without, Burn).ShouldBe(
            0, "nothing else in this fixture's fight burns anything — that is what makes the perk the cause");

        Applied(with, Burn).ShouldBeGreaterThan(
            0, "a run that drafted Ignite has to set enemies on fire, and no stat block can show that");
    }

    /// <summary>…and the ailment it applied actually ticks damage, rather than being recorded and idle.</summary>
    /// <remarks>
    /// The discriminating half. A status the timeline records but never drives produces exactly the
    /// applications above and no ticks at all, and the fight plays out as if the perk were absent.
    /// </remarks>
    [Fact]
    public void The_ailment_a_drafted_perk_applies_ticks_damage()
    {
        var with = Fight(owning: BurnPerk);

        var ticks = with.Log
            .Where(e => e.Type == CombatEventType.StatusTick && e.DataId == Burn)
            .ToArray();

        ticks.ShouldNotBeEmpty("an applied burn that never ticks is a perk that reads as working and is not");
        ticks.ShouldAllBe(e => e.Value > 0.0, "a tick that deals nothing is the same absence, one layer down");
    }

    /// <summary>Two runs identical but for one drafted perk do not fight the same battle.</summary>
    /// <remarks>
    /// The blunt instrument, and the one that would still catch a perk whose op this suite has no
    /// specific assertion for: same seed, same loadout, same enemy, one perk of difference. A
    /// byte-identical replay is a perk that reached nothing.
    /// </remarks>
    [Fact]
    public void A_drafted_perk_changes_the_replay_a_run_produces()
    {
        Fight(owning: BurnPerk).LogHash.ShouldNotBe(
            Fight(owning: null).LogHash,
            "a fight that hashes the same with and without a drafted perk is a perk the tick loop never saw");
    }

    /// <summary>The run's fight, with or without one perk owned at Tier I.</summary>
    private static SimulationResult Fight(string? owning)
    {
        var run = RunBattleWorlds.RunRow();

        if (owning is not null)
        {
            run = run with { OwnedPerkTiers = RunSnapshots.OwnedPerkTiers((owning, 1)) };
        }

        return RunBattle.Simulate(RunBattleWorlds.PlayerRow(), run, RunBattleWorlds.Content);
    }

    private static int Applied(SimulationResult fight, ushort status) =>
        fight.Log.Count(e => e.Type == CombatEventType.StatusApplied && e.DataId == status);

    /// <summary>The fire category's base perk — every tier applies <c>BURN</c> on a landed hit.</summary>
    private const string BurnPerk = "PK_IGNITE";

    /// <summary><c>BURN</c>'s wire ordinal: row 1 of the status table, one-based.</summary>
    private const ushort Burn = 1;
}
