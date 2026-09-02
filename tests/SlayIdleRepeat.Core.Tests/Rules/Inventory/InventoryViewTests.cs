using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Inventory;
using SlayIdleRepeat.Core.Tests.TestSupport;
using SlayIdleRepeat.Core.Tests.Model;
using SlayIdleRepeat.Core.Tests.Model.Gear;
using SlayIdleRepeat.Core.Tests.Rules.Combat;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Inventory;

/// <summary>
/// <c>InventoryView</c> — the narrow public projection of a player's stock and, for each item in it,
/// what wearing it would change.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>The load-bearing claim is not "the stock is listed" — it is that each item's delta is
/// measured against what is worn in ITS OWN slot</b> (steering S2). A projection that paired every
/// candidate with the same item, or with none, would satisfy every structural case here and would put
/// a green arrow on a downgrade. <see cref="A_candidate_is_compared_against_the_item_worn_in_its_own_slot"/>
/// is the case that says otherwise.
/// </para>
/// <para>
/// The comparison itself belongs to <c>InventoryComparison</c> and is tested there; what these cases
/// are about is the pairing, the scope and the two kinds of item that carry no comparison at all.
/// </para>
/// </remarks>
public sealed class InventoryViewTests
{
    private static ContentSnapshot Content => ShippedHarness.Content;

    // ------------------------------------------------------------------------------------------
    // The scope.
    // ------------------------------------------------------------------------------------------

    /// <summary>Every stored item is projected, and none is dropped.</summary>
    [Fact]
    public void Every_stored_item_reaches_the_projection()
    {
        var view = InventoryView.Project(GearedRow(), Content);

        view.Stored.Count.ShouldBe(
            RunBattleWorlds.FarAbovePar.Count,
            "the grid draws what the stock holds; an item missing from the projection is an item the " +
            "player owns and cannot see.");
    }

    /// <summary>…and the capacity is the authored ceiling, not a derived one.</summary>
    /// <remarks>
    /// ⚠️ Asserted against the tuning reader rather than the literal 1000: <c>08</c> §5's errata makes
    /// the ceiling an authored 📐 equal to the base, so a retune moves this case with it. A literal here
    /// would re-introduce the retired <c>120 + 10 × 20</c> derivation as a hard-coded expectation.
    /// </remarks>
    [Fact]
    public void The_capacity_is_the_authored_ceiling()
    {
        var view = InventoryView.Project(GearedRow(), Content);

        view.Capacity.ShouldBe(
            InventoryTuning.Read(Content).MaxCapacity,
            "the grid is sized from tuning/forge.json#/inventory/maxCapacity and nothing else.");
    }

    // ------------------------------------------------------------------------------------------
    // 🔒 The pairing — the claim the rest of the file rests on.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// 🔒 A candidate's delta is measured against the item worn in the SAME slot, and the projection
    /// is what does the pairing.
    /// </summary>
    /// <remarks>
    /// 🔴 Stated over two slots at once, because one slot cannot tell a correct pairing from a
    /// constant. The fixture wears a full six, so a candidate blade compared against the worn blade
    /// reports a different figure from the same blade compared against the worn boots — and a
    /// projection that paired everything with one item, or with the first, would return the same
    /// numbers for both and pass a single-slot case.
    /// </remarks>
    [Fact]
    public void A_candidate_is_compared_against_the_item_worn_in_its_own_slot()
    {
        var worn = RunBattleWorlds.FarAbovePar;

        // A weaker candidate per slot: same family, bottom band, no enhancement.
        var candidates = worn
            .Select((item, index) => Inventories.Item(
                "cand_" + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                item.Family,
                Rarity.C))
            .ToArray();

        var row = RunBattleWorlds.FarAboveParRow() with
        {
            Inventory = new InventorySnapshot(
                0,
                worn.Concat(candidates).Select(Inventories.Persist).ToArray(),
                []),
        };

        var view = InventoryView.Project(row, Content);

        foreach (var candidate in candidates)
        {
            var projected = view.Stored.Single(item => item.InstanceId == candidate.InstanceId);
            var against = worn.Single(item => item.Slot == candidate.Slot);

            projected.Deltas.ShouldNotBeEmpty(
                "a candidate the player is not wearing has something to say about wearing it.");

            projected.Deltas.ShouldAllBe(
                delta => delta.Delta < 0,
                "a C-band unenhanced item is a downgrade against the SS+15 worn in its slot (" +
                against.InstanceId.Value + "), so every stat has to read as a loss. A delta at or " +
                "above zero means this candidate was compared against something other than its own " +
                "slot — which is how a screen puts a green arrow on a downgrade.");
        }
    }

    // ------------------------------------------------------------------------------------------
    // The two kinds of item that carry no comparison, each for its own reason.
    // ------------------------------------------------------------------------------------------

    /// <summary>The item already worn is marked as worn and carries no delta.</summary>
    /// <remarks>
    /// 🔒 Not zeroes. An item compared against itself derives the same figures on both sides, and a
    /// screen drawing that would put a row of flat green/red arrows beside the thing the player is
    /// already wearing — a comparison that says "this changes nothing" where the honest answer is that
    /// there is nothing to compare.
    /// </remarks>
    [Fact]
    public void The_worn_item_is_marked_and_carries_no_delta()
    {
        var view = InventoryView.Project(GearedRow(), Content);
        var equipped = view.Stored.Where(item => item.IsEquipped).ToArray();

        equipped.Length.ShouldBe(
            RunBattleWorlds.FarAbovePar.Count,
            "the fixture wears one item per slot, and the projection has to say which ones.");

        equipped.ShouldAllBe(
            item => item.Deltas.Count == 0,
            "the worn item carries a comparison against itself, which is a row of flat arrows beside " +
            "the item the player already has on.");
    }

    /// <summary>An item in overflow is projected, and carries no delta either.</summary>
    /// <remarks>
    /// 🔒 Projected rather than hidden: <c>08</c> §5 holds a drop that arrives at a full stock, and a
    /// screen showing only the stock proper would hide exactly the items a player most needs to see.
    /// Comparison-free because an item in overflow cannot be equipped — offering a delta for it invites
    /// a tap the rules layer refuses with <c>INVENTORY_FULL</c>.
    /// </remarks>
    [Fact]
    public void An_item_in_overflow_is_projected_without_a_comparison()
    {
        var held = Inventories.Item("overflowing", RunBattleWorlds.FarAbovePar[0].Family, Rarity.SS);

        var row = RunBattleWorlds.FarAboveParRow() with
        {
            Inventory = new InventorySnapshot(
                0,
                RunBattleWorlds.FarAbovePar.Select(Inventories.Persist).ToArray(),
                [Inventories.Persist(held)]),
        };

        var view = InventoryView.Project(row, Content);

        view.Held.Select(item => item.InstanceId).ShouldBe(
            [held.InstanceId],
            "an item the stock is holding is still owned, and a screen that omits it is a screen the " +
            "player cannot use to make room.");

        view.Held.ShouldAllBe(
            item => item.Deltas.Count == 0,
            "an item in overflow cannot be equipped, so a delta for it describes an action EQUIP " +
            "refuses with INVENTORY_FULL.");
    }

    // ------------------------------------------------------------------------------------------
    // The doors.
    // ------------------------------------------------------------------------------------------

    /// <summary>A stock with nothing worn compares every item against an empty slot.</summary>
    [Fact]
    public void With_nothing_worn_every_item_is_compared_against_an_empty_slot()
    {
        var row = RunBattleWorlds.PlayerRow(geared: false);

        var view = InventoryView.Project(row, Content);

        view.Stored.ShouldAllBe(item => !item.IsEquipped, "nothing is worn, so nothing is marked worn.");
        view.Stored.ShouldAllBe(
            item => item.Deltas.Count > 0,
            "an empty slot contributes nothing, so the whole of a candidate's figure is a gain — and " +
            "that is still a comparison worth drawing.");
        view.Stored.SelectMany(item => item.Deltas).ShouldAllBe(
            delta => delta.Equipped == 0.0,
            "there is nothing in the slot, so the equipped side of every delta is zero rather than " +
            "some other item's figure.");
    }

    /// <summary>Both arguments are required.</summary>
    [Fact]
    public void The_projection_refuses_a_null_argument()
    {
        Should.Throw<ArgumentNullException>(() => InventoryView.Project((PlayerSnapshot)null!, Content));
        Should.Throw<ArgumentNullException>(() => InventoryView.Project(GearedRow(), null!));
    }

    // ------------------------------------------------------------------------------------------
    // Fixtures.
    // ------------------------------------------------------------------------------------------

    private static PlayerSnapshot GearedRow() => RunBattleWorlds.FarAboveParRow();
}
