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
/// command is really for: making <c>LOCKED</c> a state production can be in.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>Until this command, <c>Inventory.SetLock</c> had zero production callers.</b> Every rule
/// downstream of the flag — <c>InventoryAccess</c>'s <c>LOCKED</c> arm and with it <c>SALVAGE</c>'s
/// and <c>MERGE</c>'s refusal to destroy a protected item, <c>Enhance</c>'s lock-passthrough,
/// <c>Equip</c>'s "a locked item is equippable" ruling, <c>AutoSalvageFilter</c>'s first exclusion —
/// was written and tested against a hand-written fixture and reachable by nothing a player could
/// send. The last case in this file is the one that says so out loud: it locks an item with a
/// command and then watches <c>SALVAGE</c> refuse it.
/// </para>
/// <para>
/// The two refusals carry codes of their own (<c>NOT_OWNED</c>, <c>INVENTORY_FULL</c>), so they are
/// pinned by code — but each world is still built so that only its own rule can fire.
/// </para>
/// </remarks>
public sealed class LockItemTests
{
    /// <summary>Any well-formed <c>LogHash</c>, for the case that reaches overflow through a real drop.</summary>
    private const string BattleLog = "1";

    /// <summary>A blade the fixtures hold in stock.</summary>
    private const string Blade = "GI_BLADE";

    /// <summary>An identity no stock in this file holds.</summary>
    private const string Ghost = "GI_GHOST";

    // ═══════════════════════════════════════════════════════ 1 · setting the flag

    /// <summary>🔒 Locking sets the flag, and unlocking clears it — both through the same command.</summary>
    /// <remarks>
    /// Stated as the round trip rather than as two cases, because the round trip is what the explicit
    /// boolean is for: a handler that only ever <em>set</em> the flag would pass a lock-only case,
    /// and would leave the player with no way back.
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
                "…and the container reports the state the rest of the domain reads. This is the " +
                "first time in the game's life that a COMMAND can produce this value.");

        var unlocked = Lock(locked.NewState, Blade, locked: false);

        unlocked.Accepted.ShouldBeTrue("the unlock was refused " + unlocked.Rejection + ".");
        unlocked.NewState.Player.Inventory.Stored[0].Locked.ShouldBeFalse(
            "a locked item could not be unlocked, so the lock is a one-way door and the player has " +
            "no way to salvage what they protected.");

        locked.Events.ShouldBeEmpty(
            "a lock grants nothing and moves no currency, so it has nothing to report.");
    }

    /// <summary>
    /// 🔒 Asking for the state the item is already in is ACCEPTED, both ways round — which is what
    /// makes the explicit boolean idempotent and a toggle not.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>Both ways round is the discriminating half.</b> A handler that refused a no-op would
    /// fail the first arm; a TOGGLE would <em>pass</em> the first arm — it would accept, and flip the
    /// flag to false — so the assertion that has to fail against a toggle is the one about the flag's
    /// VALUE after the second identical command, not the acceptance.
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
            "…and it is still LOCKED. This is the assertion a toggle fails: a toggle would also have " +
            "been accepted here, and would have quietly unprotected the item the player just " +
            "protected. That is why 14 §2.3's payload is a state and not a verb.");

        // The other direction, so "idempotent" is not proved only on the value the fixture started
        // away from: unlocking an already-unlocked item is a no-op too.
        var unlockTwice = Lock(Lock(world, Blade, locked: false).NewState, Blade, locked: false);

        unlockTwice.Accepted.ShouldBeTrue(
            "an unlock of an already-unlocked item was refused " + unlockTwice.Rejection + ".");
        unlockTwice.NewState.Player.Inventory.Stored[0].Locked.ShouldBeFalse();
    }

    // ═══════════════════════════════════════════════════════ 2 · the item is not owned

    /// <summary>An item the player does not own is <c>NOT_OWNED</c>, and only that rule can fire.</summary>
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

    // ═══════════════════════════════════════════════════════ 3 · the item is in overflow

    /// <summary>
    /// 🔒 An item parked in overflow is <c>INVENTORY_FULL</c>, driven through a real overflowing drop.
    /// </summary>
    /// <remarks>
    /// The overflow is produced by a kill arriving at a full stock rather than hand-written, because
    /// that is the only way an item gets there in the real game. The item IS owned, so it is not
    /// <c>NOT_OWNED</c> — which is exactly the distinction the lock screen needs to show a player
    /// whose bag is full.
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

    // ═══════════════════════════════════════════ what the lock is FOR: the path it makes reachable

    /// <summary>
    /// 🔴 <b>The point of the whole command: a locked item refuses <c>SALVAGE</c>, and the pair of
    /// worlds differs only in whether <c>LOCK_ITEM</c> was sent first.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>InventoryAccess.RejectionFor</c>'s <c>LOCKED</c> arm has existed since M4-04 and nothing
    /// could reach it: <c>Inventory.SetLock</c> had no production caller, so no real player's item
    /// was ever locked. This is the case that proves the arm is live, and it does it the only way
    /// that means anything — by locking through the command rather than by hand-writing a locked
    /// fixture.
    /// </para>
    /// <para>
    /// The control salvages the same item from the same stock and must be ACCEPTED. Without it, a
    /// SALVAGE that refused everything would satisfy the refusal below.
    /// </para>
    /// </remarks>
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
            "the destructive operations, and until LOCK_ITEM shipped no command could put a real " +
            "player's item into this state.");

        salvagedLocked.Rejection.ShouldBe(
            RejectionReason.ILLEGAL_STATE,
            "InventoryAccess maps LOCKED to ILLEGAL_STATE. If this answers NOT_OWNED or " +
            "INVENTORY_FULL, the salvage is refusing for some other reason and the LOCKED arm is " +
            "still unreachable.");

        salvagedLocked.NewState.Player.Inventory.Stored.Count.ShouldBe(
            1, "…and the protected item is still there.");
    }

    /// <summary>
    /// 🔒 The negative control on the arm above: a locked item is still EQUIPPABLE, which is the
    /// ruling `07` §4 and <c>Equip</c>'s remarks record.
    /// </summary>
    /// <remarks>
    /// Without this, "locking an item refuses things" would be indistinguishable from "locking an
    /// item makes it unusable". The lock protects against destruction, which is the opposite of
    /// taking the item out of use — and the M4-10 suite could only assert it against a hand-written
    /// locked fixture. Now it can be asserted against a lock a player actually set.
    /// </remarks>
    [Fact]
    public void An_item_locked_by_the_command_is_still_equippable()
    {
        var locked = Lock(new WorldSlice(Worlds.Rehydrated(Holding(Blade)), null), Blade, locked: true);

        locked.NewState.Player.Inventory.Stored[0].Locked.ShouldBeTrue("the premise.");

        var equipped = SlayIdleRepeat.Core.GameRules.Apply(
            locked.NewState,
            new EquipCommand(new GearInstanceId(Blade), GearSlot.WEAPON),
            Worlds.Context);

        equipped.Accepted.ShouldBeTrue(
            "an item the player locked was refused " + equipped.Rejection + " when they tried to " +
            "WEAR it. Refusing to equip the item a player marked as their best is the opposite of " +
            "what they asked for.");

        equipped.NewState.Player.Loadout.TryGet(GearSlot.WEAPON, out var worn).ShouldBeTrue();
        worn.Value.ShouldBe(Blade);
    }

    // ═══════════════════════════════════════════════════════ the worlds

    private static CommandResult Lock(WorldSlice world, string itemId, bool locked) =>
        SlayIdleRepeat.Core.GameRules.Apply(
            world, new LockItemCommand(new GearInstanceId(itemId), locked), Worlds.Context);

    /// <summary>A player row holding one blade in stock and wearing nothing.</summary>
    private static PlayerSnapshot Holding(string itemId) =>
        PlayerSnapshots.With(inventory: new InventorySnapshot(
            0, [Inventories.Persist(Inventories.Item(itemId))], []));
}
