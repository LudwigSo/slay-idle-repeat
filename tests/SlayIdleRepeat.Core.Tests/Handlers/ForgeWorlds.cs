using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Model.Gear;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Tests.Model;
using SlayIdleRepeat.Core.Tests.Model.Gear;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary>
/// Hermetic worlds for the three forge commands: a player holding a stock and enough of every
/// currency the forge spends.
/// </summary>
/// <remarks>
/// Built through <c>Player.Rehydrate</c> like every other world here, so a stock the aggregate would
/// refuse cannot reach a handler as a fixture — and the wallet is stated per case rather than
/// defaulted generously, since "can the player afford this" is one of the refusals under test.
/// </remarks>
internal static class ForgeWorlds
{
    /// <summary>A wallet with enough of everything the forge spends for any case here.</summary>
    internal static IReadOnlyDictionary<CurrencyId, long> Funded { get; } =
        PlayerSnapshots.Wallet(
            (CurrencyId.CROWNS, 1_000_000),
            (CurrencyId.MERGE_DUST, 1_000_000),
            (CurrencyId.ENHANCE_STONES, 1_000_000));

    /// <summary>The seed every forge case draws against unless it is about the seed.</summary>
    internal const ulong Seed = 0xF0_4E_00_04UL;

    /// <summary>A slice whose player holds these items and the given wallet, and is in no run.</summary>
    /// <param name="wallet">The balances. Pass <see cref="Funded"/> for a case that is not about price.</param>
    /// <param name="items">The stored stock, in grant order.</param>
    /// <returns>The slice.</returns>
    internal static WorldSlice Holding(
        IReadOnlyDictionary<CurrencyId, long> wallet, params GearInstance[] items)
    {
        ArgumentNullException.ThrowIfNull(items);

        return new WorldSlice(
            Worlds.Rehydrated(PlayerSnapshots.With(
                wallet: wallet,
                inventory: new InventorySnapshot(
                    Inventories.Tuning.MaxPurchases, items.Select(Inventories.Persist).ToArray(), []))),
            null);
    }

    /// <summary>A slice whose player holds these items and can afford anything.</summary>
    /// <param name="items">The stored stock, in grant order.</param>
    /// <returns>The slice.</returns>
    internal static WorldSlice Holding(params GearInstance[] items) => Holding(Funded, items);

    /// <summary>
    /// A slice whose player holds these items <b>and is wearing one of them</b> — the state a
    /// destructive forge command has to survive.
    /// </summary>
    /// <remarks>
    /// Built through <c>Player.Rehydrate</c>, so the pairing is one the aggregate itself accepts on
    /// the way in — a failure here is the command's doing, not a pre-broken fixture.
    /// </remarks>
    /// <param name="worn">Which slot the hero has filled, and with which of <paramref name="items"/>.</param>
    /// <param name="items">The stored stock, in grant order. Must contain the worn instance.</param>
    /// <returns>The slice.</returns>
    internal static WorldSlice Wearing(
        (GearSlot Slot, string InstanceId) worn, params GearInstance[] items)
    {
        ArgumentNullException.ThrowIfNull(items);

        return new WorldSlice(
            Worlds.Rehydrated(PlayerSnapshots.With(
                wallet: Funded,
                inventory: new InventorySnapshot(
                    Inventories.Tuning.MaxPurchases, items.Select(Inventories.Persist).ToArray(), []),
                loadout: new LoadoutSnapshot(PlayerSnapshots.Gear(worn)))),
            null);
    }

    /// <summary>A slice whose stock is full and whose overflow is holding one more item.</summary>
    /// <param name="held">The item waiting for space.</param>
    /// <returns>The slice.</returns>
    internal static WorldSlice Overflowing(GearInstance held)
    {
        ArgumentNullException.ThrowIfNull(held);

        return new WorldSlice(
            Worlds.Rehydrated(PlayerSnapshots.With(
                wallet: Funded,
                inventory: new InventorySnapshot(
                    0,
                    Inventories.Fill(Inventories.Tuning.MaxCapacity).Select(Inventories.Persist).ToArray(),
                    [Inventories.Persist(held)]))),
            null);
    }

    /// <summary>
    /// A slice whose stock is exactly full — <paramref name="items"/> at the front of it, padding
    /// behind them — with <paramref name="held"/> items waiting for space.
    /// </summary>
    /// <param name="held">How many items are waiting. Must exceed what the operation frees.</param>
    /// <param name="items">The items the command will name.</param>
    /// <returns>The slice.</returns>
    internal static WorldSlice FullWithOverflow(int held, params GearInstance[] items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var capacity = Inventories.Tuning.MaxCapacity;

        return new WorldSlice(
            Worlds.Rehydrated(PlayerSnapshots.With(
                wallet: Funded,
                inventory: new InventorySnapshot(
                    0,
                    [
                        .. items.Select(Inventories.Persist),
                        .. Inventories.Fill(capacity - items.Length, "pad").Select(Inventories.Persist),
                    ],
                    [.. Inventories.Fill(held, "waiting").Select(Inventories.Persist)]))),
            null);
    }

    /// <summary>What one currency moved by, summed across the events a command produced.</summary>
    /// <param name="events">The command's events.</param>
    /// <param name="currency">The column to total.</param>
    /// <returns>The signed total.</returns>
    internal static long Moved(IReadOnlyList<DomainEvent> events, CurrencyId currency)
    {
        ArgumentNullException.ThrowIfNull(events);

        return events.OfType<CurrencyChanged>().Where(e => e.Id == currency).Sum(e => e.Delta);
    }

    /// <summary>
    /// A stock compared by its canonical bytes rather than by record equality: a synthesized record
    /// <c>Equals</c> compares an <c>IReadOnlyList&lt;T&gt;</c> component by REFERENCE.
    /// </summary>
    /// <param name="slice">The slice whose stock is being described.</param>
    /// <returns>The canonical encoding of the stock.</returns>
    internal static byte[] StockBytes(WorldSlice slice)
    {
        ArgumentNullException.ThrowIfNull(slice);

        return CanonicalStateWriter.CanonicalBytes(slice.Player.Inventory.ToSnapshot());
    }
}
