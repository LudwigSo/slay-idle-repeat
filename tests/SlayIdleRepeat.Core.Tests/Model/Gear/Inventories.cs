using System.Globalization;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Gear;
using SlayIdleRepeat.Core.Model.Gear;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Tests.Content;

namespace SlayIdleRepeat.Core.Tests.Model.Gear;

/// <summary>
/// Hermetic inventory fixtures. Items are built from the gear catalogue's own rows so a fixture's
/// slot always agrees with its family; <see cref="Empty"/> goes through <c>Rehydrate</c>, the same
/// door the persistence adapter uses.
/// </summary>
internal static class Inventories
{
    /// <summary>The inventory numbers every case here reads.</summary>
    internal static InventoryTuning Tuning { get; } =
        InventoryTuning.Read(InventoryDocuments.Shipped);

    /// <summary>The gear catalogue the item builder takes its rows from.</summary>
    internal static GearCatalogue Catalogue { get; } = GearCatalogue.Read(GearDocuments.Shipped);

    /// <summary>The gear tables the sorting and comparison rules read.</summary>
    internal static DropsTuning Drops { get; } = DropsTuning.Read(GearDocuments.Shipped);

    /// <summary>The par table the stat derivation reads.</summary>
    internal static ParPowerTuning Par { get; } = ParPowerTuning.Read(GearDocuments.Shipped);

    /// <summary>The forge numbers the strongest-first ordering reads an item's enhancement from.</summary>
    internal static ForgeTuning Forge { get; } = ForgeTuning.Read(ForgeDocuments.Shipped);

    /// <summary>An inventory holding nothing, with nothing bought.</summary>
    internal static Inventory Empty() => Rehydrated(new InventorySnapshot(0, [], []));

    /// <summary>An inventory rehydrated from a row, failing loudly rather than silently empty.</summary>
    internal static Inventory Rehydrated(InventorySnapshot snapshot)
    {
        var inventory = Inventory.Rehydrate(snapshot, Tuning);

        return inventory.IsSuccess
            ? inventory.Value
            : throw new InvalidOperationException(
                "The fixture InventorySnapshot does not rehydrate: " + inventory.Error);
    }

    /// <summary>
    /// One item, built off the catalogue row its family names so the slot and the def id agree with it.
    /// </summary>
    /// <remarks>
    /// <paramref name="enhanceFailures"/> is appended last and passed by name at every call site: a
    /// parameter inserted ahead of an existing optional one silently re-binds every positional
    /// argument after it.
    /// </remarks>
    internal static GearInstance Item(
        string id,
        GearFamily family = GearFamily.BLADE,
        Rarity rarity = Rarity.C,
        int chapterOrigin = 1,
        double quality = 0.5,
        int enhanceLevel = 0,
        IReadOnlyList<GearAffixRoll>? affixes = null,
        bool locked = false,
        int enhanceFailures = GearInstance.NoFailures)
    {
        var definition = Catalogue.Definition(family);

        return new GearInstance(
            new GearInstanceId(id),
            definition.DefId,
            definition.Slot,
            family,
            rarity,
            chapterOrigin,
            quality,
            enhanceLevel,
            enhanceFailures,
            affixes ?? [],
            locked);
    }

    /// <summary>
    /// <paramref name="count"/> items, distinguishable by identity alone — <c>fill_00000001</c>
    /// upwards. Zero-padded and ordinal, so arrival order and ordinal order by id are the same
    /// sequence.
    /// </summary>
    internal static IReadOnlyList<GearInstance> Fill(int count, string prefix = "fill") =>
        Enumerable.Range(1, count).Select(n => Item(Id(prefix, n).Value)).ToArray();

    /// <summary>The identity <see cref="Fill"/> gives its <paramref name="ordinal"/>th item, counted from 1.</summary>
    internal static GearInstanceId Id(string prefix, int ordinal) =>
        new(prefix + "_" + ordinal.ToString("D8", CultureInfo.InvariantCulture));

    /// <summary>
    /// The persisted form of one item — written out member by member, so a field added to one type
    /// and not the other stops this compiling rather than persisting a default.
    /// </summary>
    internal static GearInstanceSnapshot Persist(GearInstance item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return new GearInstanceSnapshot(
            item.InstanceId,
            item.DefId,
            item.Slot,
            item.Family,
            item.Rarity,
            item.ChapterOrigin,
            item.Quality,
            item.EnhanceLevel,
            item.EnhanceFailures,
            item.Affixes,
            item.Locked);
    }

    /// <summary>
    /// A persisted inventory holding <paramref name="items"/> stored items and zero expansions —
    /// no command sells an expansion, so no real row can carry a purchase.
    /// </summary>
    internal static InventorySnapshot Stock(int items) =>
        new(0, Fill(items).Select(Persist).ToArray(), []);

    /// <summary>Places every item in order and hands the inventory back, for a caller that wants a stocked one.</summary>
    internal static Inventory Holding(params GearInstance[] items)
    {
        var inventory = Empty();

        foreach (var item in items)
        {
            inventory.Place(item, Tuning);
        }

        return inventory;
    }
}
