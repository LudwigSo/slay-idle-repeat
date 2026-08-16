using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Model.Gear;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary>
/// The <c>DROP_RUN</c> grant path: what a won battle banks, what a lost one does not, and what the
/// dry-streak breakers owe a player who has been unlucky — all of it driven through
/// <c>GameRules.Apply</c> answering <c>CONFIRM_BATTLE_RESULT</c>.
/// </summary>
/// <remarks>
/// The counts and the chance are read out of <c>tuning/drops.json#/acquisitionRates</c> through the
/// production reader, never transcribed: a case that spelled the number itself would keep passing
/// after the document moved and the engine did not.
/// </remarks>
public sealed class RunDropGrantTests
{
    /// <summary>Any well-formed <c>LogHash</c>. The handler parses the shape and does not recompute it.</summary>
    private const string BattleLog = "1";

    /// <summary>How many kills the identity case drives. More than one command, so an ordinal that
    /// restarted per command would collide.</summary>
    private const int KillsInARow = 8;

    // ═══════════════════════════════════════════════════════════ what a kill banks

    /// <summary>An Elite kill banks exactly the authored number of items, each one a DROP_RUN grant.</summary>
    /// <remarks>
    /// The Elite arm takes no count draw at all — the document states a flat number — so this is the
    /// one kill kind whose banked count is knowable without pinning a seed.
    /// </remarks>
    [Fact]
    public void An_Elite_kill_banks_the_authored_number_of_items_as_DROP_RUN_grants()
    {
        var expected = GearGrantWorlds.Rates().EliteKillItems;

        var result = Win(GearGrantWorlds.OnKill(TileKind.Elite));

        result.Accepted.ShouldBeTrue("the kill itself was refused " + result.Rejection + ".");

        Banked(result).Count.ShouldBe(
            expected,
            "drops.json#/acquisitionRates/eliteKillItems authors " + expected + " item(s) per Elite " +
            "kill and this kill banked " + Banked(result).Count + ". An Elite is the kill kind the " +
            "document guarantees a drop from, so a count of zero means the grant is not wired at all.");

        Granted(result).Count.ShouldBe(
            expected,
            "the items reached the stock and no GearGranted reported them, so the economy log, the " +
            "analytics feed and the client's drop toast would all miss the drop that happened.");

        Granted(result).Select(grant => grant.Source).ShouldAllBe(
            source => source == SourceClass.DROP_RUN,
            "an in-run kill drop is the DROP_RUN class. A grant reported under another class is " +
            "routed to another set of guarantees than the one that actually protected it.");

        Granted(result).Select(grant => grant.Item.InstanceId).ShouldBe(
            Banked(result),
            ignoreOrder: true,
            "the item a GearGranted names is not the item that reached the stock, so the event " +
            "reports one drop and the player receives another.");
    }

    /// <summary>An ordinary kill banks one item when its single draw falls under the authored chance.</summary>
    [Fact]
    public void A_normal_enemy_kill_banks_one_item_when_its_draw_falls_under_the_authored_chance()
    {
        var chance = GearGrantWorlds.Rates().NormalEnemyChance;
        var world = GearGrantWorlds.OnKill(
            TileKind.Enemy, dropsPosition: GearGrantWorlds.NormalEnemyDrawPosition(drops: true, chance));

        var result = Win(world);

        Banked(result).Count.ShouldBe(
            1,
            "the run's drops stream was committed at a position whose first draw is below the " +
            "authored normalEnemyChance of " + chance + ", so this kill is on the dropping side of " +
            "the coin and owes exactly one item.");

        Granted(result).Count.ShouldBe(1, "and the drop that happened is reported.");
    }

    /// <summary>…and banks nothing when the same draw falls at or above it.</summary>
    /// <remarks>
    /// The control is what makes this an assertion about the chance rather than about a handler that
    /// grants nothing at all: the two worlds differ only in where the drop stream was committed, so
    /// a kill that drops on one and not on the other is the authored chance being read.
    /// </remarks>
    [Fact]
    public void A_normal_enemy_kill_banks_nothing_when_its_draw_falls_at_or_above_the_authored_chance()
    {
        var chance = GearGrantWorlds.Rates().NormalEnemyChance;

        var dropping = Win(GearGrantWorlds.OnKill(
            TileKind.Enemy, dropsPosition: GearGrantWorlds.NormalEnemyDrawPosition(drops: true, chance)));
        var empty = Win(GearGrantWorlds.OnKill(
            TileKind.Enemy, dropsPosition: GearGrantWorlds.NormalEnemyDrawPosition(drops: false, chance)));

        Banked(dropping).ShouldNotBeEmpty(
            "the control banked nothing either, so the emptiness below is not the chance being read " +
            "— it is a kill that never drops at all.");

        Banked(empty).ShouldBeEmpty(
            "the first draw at this stream position is at or above the authored normalEnemyChance of " +
            chance + ", so this kill is on the empty side of the coin — an item here means the " +
            "chance is not being read at all.");

        Granted(empty).ShouldBeEmpty("and nothing was granted, so nothing may be reported.");
    }

    /// <summary>A Boss kill banks between the authored minimum and maximum.</summary>
    /// <remarks>
    /// Driven over a rehydrated <c>BattlePending</c> run: the stage-boundary clamp parks every
    /// command-driven run in stage 1, so no run this suite can play reaches a Boss node.
    /// </remarks>
    [Fact]
    public void A_Boss_kill_banks_between_the_authored_minimum_and_maximum_items()
    {
        var rates = GearGrantWorlds.Rates();

        var result = Win(GearGrantWorlds.OnKill(TileKind.Boss, stage: 3));

        result.NewState.Run!.BossDefeated.ShouldBeTrue("the case is only about a Boss kill if one happened.");

        Banked(result).Count.ShouldBeInRange(
            rates.BossKillItemsMin,
            rates.BossKillItemsMax,
            "drops.json#/acquisitionRates authors a Boss kill at " + rates.BossKillItemsMin + ".." +
            rates.BossKillItemsMax + " items and this one banked " + Banked(result).Count + ". A " +
            "count of one would mean the Boss is being paid as an ordinary kill.");

        Granted(result).Select(grant => grant.Source).ShouldAllBe(
            source => source == SourceClass.DROP_RUN, "a Boss drop is still an in-run kill drop.");
    }

    /// <summary>A lost battle banks nothing, and spends no draw doing it.</summary>
    /// <remarks>
    /// The won control is what makes this a claim about the loss: the same Elite tile, the same
    /// stream position, the same empty stock, and the only difference is which way the fight went.
    /// The stream position matters as much as the empty stock — a loss that consumed a draw index
    /// would shift every drop the run makes afterwards, and a resumed run would replay differently
    /// for the rest of its life.
    /// </remarks>
    [Fact]
    public void A_lost_battle_banks_no_gear_and_spends_no_drops_draw()
    {
        var world = GearGrantWorlds.OnKill(TileKind.Elite);

        Banked(Win(GearGrantWorlds.OnKill(TileKind.Elite))).ShouldNotBeEmpty(
            "the control banked nothing, so the emptiness below says nothing about the loss — an " +
            "Elite kill is the one kind the acquisition rates guarantee a drop from.");

        var result = SlayIdleRepeat.Core.GameRules.Apply(
            world, new ConfirmBattleResultCommand(BattleLog, Won: false), GearGrantWorlds.Context);

        result.Accepted.ShouldBeTrue("the loss itself was refused " + result.Rejection + ".");

        Banked(result).ShouldBeEmpty(
            "a LOST Elite battle banked gear. An Elite kill is the one kind that always drops, so " +
            "this is the shape of a handler that grants before it reads which way the fight went.");

        Granted(result).ShouldBeEmpty("and nothing was granted, so nothing may be reported.");

        GearGrantWorlds.DropsPositionOf(result.NewState.Run!).ShouldBe(
            0UL,
            "the loss moved the drops stream, so a run that lost a fight and was resumed would draw " +
            "a different sequence from the one it would have drawn had the command been replayed.");
    }

    /// <summary>A drop arriving at a full stock is held, never refused and never destroyed.</summary>
    /// <remarks>
    /// The hold-not-lose rule seen from the grant side: the command still succeeds, the item still
    /// exists, and the event still reports it — the player simply cannot reach it until space opens.
    /// </remarks>
    [Fact]
    public void A_drop_arriving_at_a_full_stock_is_held_rather_than_refused_or_destroyed()
    {
        var capacity = GearGrantWorlds.Stock.CapacityAt(0);
        var world = GearGrantWorlds.OnKill(TileKind.Elite, inventory: GearGrantWorlds.FullStock());

        world.Player.Inventory.Stored.Count.ShouldBe(
            capacity, "the premise: this stock is full, or the case is not about overflow at all.");

        var result = Win(world);

        result.Accepted.ShouldBeTrue(
            "the kill was refused " + result.Rejection + ". A grant is never refused for want of " +
            "space — no random source in this game may be able to starve a player, and a kill that " +
            "failed because the bag was full is that wound from the other side.");

        result.NewState.Player.Inventory.Stored.Count.ShouldBe(
            capacity, "a full stock takes nothing more, and loses nothing either.");

        result.NewState.Player.Inventory.Held.Count.ShouldBe(
            GearGrantWorlds.Rates().EliteKillItems,
            "the drop was destroyed rather than parked in the holding list, so a player who filled " +
            "their bag would silently stop receiving the gear they earned.");

        Granted(result).ShouldNotBeEmpty(
            "a held item was still granted, and an event that fired only for a stored one would " +
            "under-report every drop a full player makes.");
    }

    /// <summary>The same seed and the same kill leave the player byte-identical.</summary>
    /// <remarks>
    /// Compared as canonical bytes rather than by record equality: an inventory snapshot's item list
    /// and a pity map are both reference-compared under a record's synthesized equality, so two
    /// genuinely different stocks would compare equal and two identical ones would not.
    /// </remarks>
    [Fact]
    public void The_same_seed_and_kill_leave_the_player_byte_identical()
    {
        var first = Win(GearGrantWorlds.OnKill(TileKind.Elite)).NewState.Player;
        var second = Win(GearGrantWorlds.OnKill(TileKind.Elite)).NewState.Player;

        // The premise, and NOT "the player's bytes moved": every accepted command advances the
        // applied-at anchor, so that comparison passes over a handler that grants nothing.
        first.Inventory.Stored.ShouldNotBeEmpty(
            "the kill banked nothing, so the comparison below would hold just as well over a " +
            "handler that does nothing at all.");

        Bytes(second.ToSnapshot()).ShouldBe(
            Bytes(first.ToSnapshot()),
            "two runs from the same seed banked different gear. Every in-run draw is " +
            "Hash64(runSeed, stream, index), so a replay that diverges means something in the grant " +
            "path is reading entropy the run does not carry.");
    }

    // ═══════════════════════════════════════════════════════════ the dry-streak breakers

    /// <summary>An Elite drop below the breaker's band advances the Elite dry-streak counter.</summary>
    [Fact]
    public void An_Elite_drop_below_the_breakers_band_advances_the_elite_dry_streak_counter()
    {
        var breaker = GearGrantWorlds.DropRun.EliteMercy;
        var world = GearGrantWorlds.OnKill(
            TileKind.Elite,
            dropsPosition: GearGrantWorlds.EliteDrawPosition(band => band < breaker.BelowRarity));

        var result = Win(world);

        Banked(result).Count.ShouldBe(1, "the premise: a drop happened, or no counter could move.");

        result.NewState.Player.PityCounters.Get(GearGrantWorlds.EliteCounterKey).ShouldBe(
            1,
            "the drop landed below " + breaker.BelowRarity + ", which is a miss, and the counter did " +
            "not move. A counter that never advances is a guarantee that never fires, and that is " +
            "invisible until a player has killed " + breaker.ForceOnNthKill + " Elites for nothing.");
    }

    /// <summary>An Elite drop at or above the breaker's band resets it.</summary>
    /// <remarks>
    /// The other half, and the one that keeps the case above from passing over a counter that only
    /// ever grows: a natural drop that reaches the guaranteed band satisfies it exactly as a forced
    /// one does, so the player is never punished for good luck by keeping a spent streak.
    /// </remarks>
    [Fact]
    public void An_Elite_drop_at_or_above_the_breakers_band_resets_the_elite_dry_streak_counter()
    {
        var breaker = GearGrantWorlds.DropRun.EliteMercy;
        var standing = GearGrantWorlds.EliteMercyAt(breaker.ForceOnNthKill - 2);
        var world = GearGrantWorlds.OnKill(
            TileKind.Elite,
            dropsPosition: GearGrantWorlds.EliteDrawPosition(band => band >= breaker.BelowRarity, standing),
            pity: standing);

        var result = Win(world);
        var granted = Granted(result);

        granted.Count.ShouldBe(1, "the premise: a drop happened, or no counter could move.");

        granted[0].FromPity.ShouldBeFalse(
            "the premise: this streak is one kill short of the forced one, so the drop above the " +
            "band is a natural one and the reset it earns is not the breaker's own.");

        result.NewState.Player.PityCounters.Get(GearGrantWorlds.EliteCounterKey).ShouldBe(
            0,
            "the drop reached " + breaker.BelowRarity + " or better, so the dry streak is over and " +
            "the counter has to go back to zero — a streak that survived a hit would fire its " +
            "guarantee early for the rest of the player's life.");
    }

    /// <summary>The authored Nth Elite kill of a dry streak is forced, and says so.</summary>
    [Fact]
    public void The_authored_Nth_Elite_kill_of_a_dry_streak_is_forced_and_reported_as_pity()
    {
        var breaker = GearGrantWorlds.DropRun.EliteMercy;
        var world = GearGrantWorlds.OnKill(
            TileKind.Elite, pity: GearGrantWorlds.EliteMercyAt(breaker.ForceOnNthKill - 1));

        var result = Win(world);

        var granted = Granted(result);

        granted.Count.ShouldBe(1, "the premise: the forced kill still drops exactly what an Elite drops.");

        granted[0].FromPity.ShouldBeTrue(
            "the " + breaker.ForceOnNthKill + "th Elite kill of a dry streak is the forced one, and " +
            "this drop reported itself as ordinary. A grant that hides its guarantee makes the " +
            "protection unauditable from the event log.");

        (granted[0].Item.Rarity >= breaker.ForceRarityAtLeast).ShouldBeTrue(
            "the forced drop landed on " + granted[0].Item.Rarity + ", below the guaranteed " +
            breaker.ForceRarityAtLeast + " — so the counter was read and the table was not floored.");

        result.NewState.Player.PityCounters.Get(GearGrantWorlds.EliteCounterKey).ShouldBe(
            0, "and the streak the guarantee satisfied goes back to zero.");
    }

    /// <summary>The Boss breaker is the same shape over its own counter and its own band.</summary>
    [Fact]
    public void The_authored_Nth_Boss_kill_of_a_dry_streak_is_forced_and_reported_as_pity()
    {
        var breaker = GearGrantWorlds.DropRun.BossMercy;
        var world = GearGrantWorlds.OnKill(
            TileKind.Boss, pity: GearGrantWorlds.BossMercyAt(breaker.ForceOnNthKill - 1), stage: 3);

        var result = Win(world);

        var granted = Granted(result);

        granted.ShouldNotBeEmpty("the premise: a Boss kill drops, or there is nothing to force.");

        granted[0].FromPity.ShouldBeTrue(
            "the " + breaker.ForceOnNthKill + "th Boss kill of a dry streak is the forced one, and " +
            "this drop reported itself as ordinary.");

        (granted[0].Item.Rarity >= breaker.ForceRarityAtLeast).ShouldBeTrue(
            "the forced Boss drop landed on " + granted[0].Item.Rarity + ", below the guaranteed " +
            breaker.ForceRarityAtLeast + ".");
    }

    /// <summary>A Boss kill leaves the Elite dry streak exactly where it stood.</summary>
    /// <remarks>
    /// The two breakers count different kills, and a shared counter would silently pay the Elite
    /// guarantee out of Boss kills the player made for another reason entirely.
    /// </remarks>
    [Fact]
    public void A_Boss_kill_leaves_the_elite_dry_streak_where_it_stood()
    {
        var standing = GearGrantWorlds.DropRun.EliteMercy.ForceOnNthKill - 1;
        var world = GearGrantWorlds.OnKill(
            TileKind.Boss, pity: GearGrantWorlds.EliteMercyAt(standing), stage: 3);

        var result = Win(world);

        Banked(result).ShouldNotBeEmpty("the premise: the Boss kill dropped, so counters were in play.");

        result.NewState.Player.PityCounters.Get(GearGrantWorlds.EliteCounterKey).ShouldBe(
            standing,
            "a Boss kill moved the ELITE streak. The breakers count consecutive kills of their own " +
            "kind, so a player farming Bosses would otherwise be handed the Elite guarantee for free.");
    }

    // ═══════════════════════════════════════════════════════════ identity

    /// <summary>Every drop in one run gets an identity of its own.</summary>
    /// <remarks>
    /// A repeated identity is refused by the container rather than stored, so a scheme that restarted
    /// its ordinal per command would take the whole run down on the second kill — which is what makes
    /// this a claim about the mint rather than about the container.
    /// </remarks>
    [Fact]
    public void Every_drop_in_one_run_gets_its_own_instance_id()
    {
        var owned = GearGrantWorlds.Owned(DriveKills(KillsInARow).Player);

        owned.Count.ShouldBe(
            KillsInARow * GearGrantWorlds.Rates().EliteKillItems,
            "the run made " + KillsInARow + " Elite kills and banked " + owned.Count + " items.");

        owned.Distinct().Count().ShouldBe(
            owned.Count,
            "two drops in one run were minted under the same identity. One instance id names one " +
            "rolled item, so a repeat makes every command that names it ambiguous — including the " +
            "salvage that would then destroy the wrong one.");
    }

    // ═══════════════════════════════════════════════════════════ the drives

    /// <summary>Wins the open battle, insisting the command landed.</summary>
    private static CommandResult Win(WorldSlice world)
    {
        var result = SlayIdleRepeat.Core.GameRules.Apply(
            world, new ConfirmBattleResultCommand(BattleLog, Won: true), GearGrantWorlds.Context);

        result.Accepted.ShouldBeTrue(
            "CONFIRM_BATTLE_RESULT was refused " + result.Rejection + ", so nothing below is about " +
            "a kill at all.");

        return result;
    }

    /// <summary>One run, <paramref name="kills"/> Elite kills, each command carrying the last one's state.</summary>
    private static WorldSlice DriveKills(int kills)
    {
        var state = GearGrantWorlds.OnKill(TileKind.Elite);

        for (var kill = 0; kill < kills; kill++)
        {
            state = Win(state).NewState;

            if (kill + 1 < kills)
            {
                state = GearGrantWorlds.ReArmed(state, TileKind.Elite, node: 8 + kill);
            }
        }

        return state;
    }

    private static IReadOnlyList<GearInstanceId> Banked(CommandResult result) =>
        GearGrantWorlds.Owned(result.NewState.Player);

    private static IReadOnlyList<GearGranted> Granted(CommandResult result) =>
        result.Events.OfType<GearGranted>().ToArray();

    private static byte[] Bytes(PlayerSnapshot snapshot) =>
        CanonicalStateWriter.CanonicalBytes(snapshot);
}
