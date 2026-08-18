using System.Globalization;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Gear;
using SlayIdleRepeat.Core.Model.Gear;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Tests.Content;

namespace SlayIdleRepeat.Core.Tests.Model.Gear;

/// <summary>
/// Hermetic inventory fixtures: the tuning every case reads, a coherent item builder, and the one
/// validated door an <c>Inventory</c> is built through.
/// </summary>
/// <remarks>
/// <para>
/// Items are built from the gear catalogue's own rows rather than from three loose arguments, so a
/// fixture can never produce a blade worn on the feet — the sorting cases order by slot, and a
/// fixture whose slot disagreed with its family would make that ordering assert nothing.
/// </para>
/// <para>
/// <see cref="Empty"/> goes through <c>Rehydrate</c>, the same door the persistence adapter uses, for
/// the reason <c>Worlds</c> does it for the player: nothing here invents a starting state.
/// </para>
/// </remarks>
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
    /// <param name="id">The instance identity. Distinct per item in every case here.</param>
    /// <param name="family">Which of the twenty-four base items. Decides the slot.</param>
    /// <param name="rarity">The band.</param>
    /// <param name="chapterOrigin">The chapter its power is scaled against.</param>
    /// <param name="quality">The quality scalar, already rounded.</param>
    /// <param name="enhanceLevel">How far it has been enhanced.</param>
    /// <param name="affixes">The rolled affixes, or none.</param>
    /// <param name="locked">Whether it is locked.</param>
    /// <param name="enhanceFailures">
    /// The item's mercy counter. Appended LAST and passed by name at every call site, for the reason
    /// <c>PlayerSnapshots.WithNull</c> records: a parameter inserted ahead of an existing optional
    /// one merges textually clean and silently re-binds every positional argument after it.
    /// </param>
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
    /// upwards.
    /// </summary>
    /// <remarks>
    /// Zero-padded and ordinal so "arrival order" and "ordinal order by id" are the same sequence,
    /// which is what lets a reclaim case assert the order it pulled items back in without a second
    /// bookkeeping list beside the assertion.
    /// </remarks>
    internal static IReadOnlyList<GearInstance> Fill(int count, string prefix = "fill") =>
        Enumerable.Range(1, count).Select(n => Item(Id(prefix, n).Value)).ToArray();

    /// <summary>The identity <see cref="Fill"/> gives its <paramref name="ordinal"/>th item, counted from 1.</summary>
    internal static GearInstanceId Id(string prefix, int ordinal) =>
        new(prefix + "_" + ordinal.ToString("D8", CultureInfo.InvariantCulture));

    /// <summary>The persisted form of one item.</summary>
    /// <remarks>
    /// Written out here rather than on the snapshot record, so a field added to
    /// <c>GearInstanceSnapshot</c> and not to <c>GearInstance</c> — or the reverse — stops this
    /// compiling rather than persisting a default.
    /// </remarks>
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

    /// <summary>A persisted inventory holding <paramref name="items"/> stored items.</summary>
    /// <remarks>
    /// For the callers that need a <em>large</em> inventory without paying for one command per item —
    /// the performance comparison in particular, whose whole subject is how the per-command cost moves
    /// with the size of this list.
    /// <para>
    /// It records <b>zero</b> expansions bought, and used to record every one the ladder prices.
    /// Neither number buys a slot since the 2026-08-17 ruling made capacity flat, and zero is the
    /// honest one: no command sells an expansion, so no real row can carry a purchase.
    /// </para>
    /// </remarks>
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
