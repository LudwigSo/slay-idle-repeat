using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Model.Gear;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Tests.Model;
using SlayIdleRepeat.Core.Tests.Model.Gear;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary>
/// <c>LOCK_ITEM</c> — the two refusals, the idempotence the explicit boolean buys, and the thing this
/// command is really for: making <c>LOCKED</c> a state a player can actually put an item into.
/// </summary>
public sealed class LockItemTests
{
    /// <summary>Any well-formed <c>LogHash</c>, for the case that reaches overflow through a real drop.</summary>
    private const string BattleLog = "1";

    /// <summary>A blade the fixtures hold in stock.</summary>
    private const string Blade = "GI_BLADE";

    /// <summary>An identity no stock in this file holds.</summary>
    private const string Ghost = "GI_GHOST";

    /// <remarks>
    /// Stated as the round trip rather than as two cases: a handler that only ever set the flag would
    /// pass a lock-only case, and would leave the player with no way back.
    /// </remarks>
    [Fact]
    public void An_item_can_be_locked_and_unlocked_again()
    {
        var world = new WorldSlice(Worlds.Rehydrated(Holding(Blade)), null);

        world.Player.Inventory.Stored[0].Locked.ShouldBeFalse(
            "the premise: the item starts unlocked, or the lock below proves nothing.");

        var locked = Lock(world, Blade, locked: true);

        locked.Accepted.ShouldBeTrue("the lock was refused " + locked.Rejection + ".");
        locked.NewState.Player.Inventory.Stored[0].Locked.ShouldBeTrue(
            "LOCK_ITEM was accepted and the flag did not move, so nothing was actually locked.");
        locked.NewState.Player.Inventory
            .Availability(new GearInstanceId(Blade))
            .ShouldBe(
                ItemAvailability.LOCKED,
                "…and the container reports the state the rest of the domain reads.");

        var unlocked = Lock(locked.NewState, Blade, locked: false);

        unlocked.Accepted.ShouldBeTrue("the unlock was refused " + unlocked.Rejection + ".");
        unlocked.NewState.Player.Inventory.Stored[0].Locked.ShouldBeFalse(
            "a locked item could not be unlocked, so the lock is a one-way door and the player has " +
            "no way to salvage what they protected.");

        locked.Events.ShouldBeEmpty(
            "a lock grants nothing and moves no currency, so it has nothing to report.");
    }

    /// <remarks>
    /// A TOGGLE would pass the acceptance arm — it would accept, and flip the flag to false — so the
    /// assertion that has to fail against a toggle is the flag's VALUE after the second command.
    /// </remarks>
    [Fact]
    public void Setting_the_flag_to_the_state_it_already_holds_is_accepted_and_changes_nothing()
    {
        var world = new WorldSlice(Worlds.Rehydrated(Holding(Blade)), null);

        var first = Lock(world, Blade, locked: true);
        first.Accepted.ShouldBeTrue("the lock was refused " + first.Rejection + ".");

        var second = Lock(first.NewState, Blade, locked: true);

        second.Accepted.ShouldBeTrue(
            "a repeat of an accepted lock was refused " + second.Rejection + ". A client retrying " +
            "under a fresh commandId after a timeout must not be told no.");

        second.NewState.Player.Inventory.Stored[0].Locked.ShouldBeTrue(
            "…and it is still LOCKED — a toggle would have quietly unprotected the item the player " +
            "just protected, which is why 14 §2.3's payload is a state and not a verb.");

        // The other direction, so "idempotent" is not proved only on the value the fixture started
        // away from: unlocking an already-unlocked item is a no-op too.
        var unlockTwice = Lock(Lock(world, Blade, locked: false).NewState, Blade, locked: false);

        unlockTwice.Accepted.ShouldBeTrue(
            "an unlock of an already-unlocked item was refused " + unlockTwice.Rejection + ".");
        unlockTwice.NewState.Player.Inventory.Stored[0].Locked.ShouldBeFalse();
    }

    /// <remarks>
    /// The stock is empty, so nothing is in overflow and the overflow rule has nothing to read.
    /// </remarks>
    [Fact]
    public void Locking_an_item_the_player_does_not_own_is_NOT_OWNED()
    {
        var world = new WorldSlice(Worlds.Rehydrated(PlayerSnapshots.Valid), null);

        world.Player.Inventory.Held.ShouldBeEmpty(
            "the premise: nothing is in overflow, so the overflow rule cannot be what refuses this.");

        var result = Lock(world, Ghost, locked: true);

        result.Rejection.ShouldBe(
            RejectionReason.NOT_OWNED,
            "a flag was set on an item nobody holds. NOT_OWNED is what tells the player their client " +
            "is naming an item that no longer exists, and it is the one refusal here that cannot be " +
            "confused with a stock that is merely full.");
    }

    /// <remarks>
    /// The overflow is produced by a kill arriving at a full stock rather than hand-written, because
    /// that is the only way an item gets there in the real game.
    /// </remarks>
    [Fact]
    public void Locking_an_item_held_in_overflow_is_INVENTORY_FULL()
    {
        var kill = SlayIdleRepeat.Core.GameRules.Apply(
            GearGrantWorlds.OnKill(TileKind.Elite, inventory: GearGrantWorlds.FullStock()),
            new ConfirmBattleResultCommand(BattleLog, Won: true),
            GearGrantWorlds.Context);

        kill.Accepted.ShouldBeTrue("the kill was refused " + kill.Rejection + ".");

        var held = kill.NewState.Player.Inventory.Held;

        held.ShouldNotBeEmpty(
            "the premise: the drop did not overflow, so this case is not about an item in overflow " +
            "at all. A grant is never refused for want of space, so a full stock has to park it.");

        var result = SlayIdleRepeat.Core.GameRules.Apply(
            GearGrantWorlds.WithEndedRun(kill.NewState),
            new LockItemCommand(held[0].InstanceId, Locked: true),
            GearGrantWorlds.Context);

        result.Rejection.ShouldBe(
            RejectionReason.INVENTORY_FULL,
            "a flag was set on an item waiting in overflow. An item in the holding list is one " +
            "NOTHING can be done to, and Inventory.SetLock only ever reaches the STORED list — so a " +
            "handler that did not refuse would answer 'done' to a command that changed nothing.");

        result.NewState.Player.Inventory.Held[0].Locked.ShouldBeFalse(
            "…and the held item is untouched.");
    }

    /// <summary>
    /// The point of the whole command: a lock set by LOCK_ITEM — not a hand-written fixture — refuses
    /// SALVAGE, and the pair of worlds differs only in whether the lock was sent first.
    /// </summary>
    [Fact]
    public void A_locked_item_refuses_SALVAGE_where_the_same_unlocked_item_is_salvaged()
    {
        var unlockedWorld = new WorldSlice(Worlds.Rehydrated(Holding(Blade)), null);

        var salvagedUnlocked = SlayIdleRepeat.Core.GameRules.Apply(
            unlockedWorld,
            new SalvageCommand([new GearInstanceId(Blade)]),
            Worlds.Context);

        salvagedUnlocked.Accepted.ShouldBeTrue(
            "the control was refused " + salvagedUnlocked.Rejection + ". This is the same item in " +
            "the same stock as the refused case below, minus the lock — with it failing, the " +
            "refusal below says nothing about locks at all.");

        salvagedUnlocked.NewState.Player.Inventory.Stored.ShouldBeEmpty(
            "…and the unlocked item really was destroyed.");

        var locked = Lock(unlockedWorld, Blade, locked: true);
        locked.Accepted.ShouldBeTrue("the lock was refused " + locked.Rejection + ".");

        var salvagedLocked = SlayIdleRepeat.Core.GameRules.Apply(
            locked.NewState,
            new SalvageCommand([new GearInstanceId(Blade)]),
            Worlds.Context);

        salvagedLocked.Accepted.ShouldBeFalse(
            "a LOCKED item was salvaged. The lock's entire stated purpose is to exclude an item from " +
            "the destructive operations.");

        salvagedLocked.Rejection.ShouldBe(
            RejectionReason.ILLEGAL_STATE,
            "InventoryAccess maps LOCKED to ILLEGAL_STATE. If this answers NOT_OWNED or " +
            "INVENTORY_FULL, the salvage is refusing for some other reason and the LOCKED arm is " +
            "still unreachable.");

        salvagedLocked.NewState.Player.Inventory.Stored.Count.ShouldBe(
            1, "…and the protected item is still there.");
    }

    private static CommandResult Lock(WorldSlice world, string itemId, bool locked) =>
        SlayIdleRepeat.Core.GameRules.Apply(
            world, new LockItemCommand(new GearInstanceId(itemId), locked), Worlds.Context);

    /// <summary>A player row holding one blade in stock and wearing nothing.</summary>
    private static PlayerSnapshot Holding(string itemId) =>
        PlayerSnapshots.With(inventory: new InventorySnapshot(
            0, [Inventories.Persist(Inventories.Item(itemId))], []));
}
