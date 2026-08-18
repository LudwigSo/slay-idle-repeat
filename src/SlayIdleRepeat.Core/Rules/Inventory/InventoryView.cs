using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Gear;
using SlayIdleRepeat.Core.Model.Gear;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using PlayerAggregate = SlayIdleRepeat.Core.Model.Player;

namespace SlayIdleRepeat.Core.Rules.Inventory;

/// <summary>
/// A player's gear stock and what each item would change if it were worn, projected into read-only
/// records the Inventory screen (S16) can draw.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>The comparison is the whole point of this projection, and it is not computed here.</b>
/// <c>08</c> §5 requires that <em>"tapping an item always shows a side-by-side delta vs the currently
/// equipped item in that slot, with green/red arrows per stat"</em>. That delta is
/// <see cref="InventoryComparison"/>'s, which is the same derivation the hero's own stat block comes
/// from — a second subtraction on the client would be a second answer to "what does this item give",
/// and the two would disagree the first time either moved.
/// </para>
/// <para>
/// 🔒 <b>Every stored item carries its delta, rather than the screen asking for one on tap.</b> A
/// per-tap door would need a second entry point taking an instance id, which is a second place to get
/// the slot pairing wrong — and the pairing is the part that matters: a boot compared against a blade
/// reports each item's own value as a gain and a loss of two unrelated quantities.
/// <see cref="InventoryComparison.Compare"/> refuses that outright, and building the pairs here means
/// it is refused once, at the only place that knows what is worn.
/// </para>
/// <para>
/// ⚠️ <b>Sorting, filters, the lock control and the Forge tabs are deliberately absent.</b> They are
/// M9-01's, and <c>InventorySorting</c> already exists in the rules layer for when that lands. What
/// this projection is scoped to is the path M7's exit criterion names: carrying M4-03's loot into the
/// next run, which is a grid, a comparison and an equip.
/// </para>
/// <para>
/// 🔒 <b>Held items are projected too, and marked.</b> <c>08</c> §5's stock is capped and a drop
/// arriving at a full one is <em>held</em> rather than refused — so a screen that showed only
/// <see cref="InventorySnapshot.Stored"/> would hide the items a player most needs to see, which are
/// the ones they are about to lose room for. They carry no delta: an item in overflow cannot be
/// equipped, and offering a comparison for it would invite a tap that the rules layer refuses with
/// <c>INVENTORY_FULL</c>.
/// </para>
/// </remarks>
public sealed class InventoryView
{
    private InventoryView(
        int capacity,
        IReadOnlyList<InventoryItemView> stored,
        IReadOnlyList<InventoryItemView> held)
    {
        Capacity = capacity;
        Stored = stored;
        Held = held;
    }

    /// <summary>
    /// How many items the stock holds, read from <c>tuning/forge.json</c>.
    /// </summary>
    /// <remarks>
    /// ⚠️ The authored ceiling, never a derived one. <c>08</c> §5's errata makes capacity a flat
    /// <b>1000</b> equal to the base, retiring the <c>120 + 10 × 20 = 320</c> ladder — and nothing can
    /// move it, because the same ruling declined to add <c>EXPAND_INVENTORY</c> to <c>14</c> §2.3's
    /// vocabulary. A screen that sized its grid from the old derivation would be drawing a limit the
    /// game does not have.
    /// </remarks>
    public int Capacity { get; }

    /// <summary>The items in the stock proper, each with what wearing it would change.</summary>
    public IReadOnlyList<InventoryItemView> Stored { get; }

    /// <summary>
    /// The items a full stock is holding for the player — <c>08</c> §5's overflow. Never comparable.
    /// </summary>
    public IReadOnlyList<InventoryItemView> Held { get; }

    /// <summary>Projects a player's stock from the persisted row.</summary>
    /// <param name="player">The player's row.</param>
    /// <param name="content">The loaded, schema-validated content snapshot.</param>
    /// <returns>The view.</returns>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    /// <exception cref="ArgumentException">The row does not rehydrate.</exception>
    /// <exception cref="MissingContentException">A document the projection reads is absent.</exception>
    public static InventoryView Project(PlayerSnapshot player, ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(content);

        return Project(RowDoor.Player(player, content, nameof(player)), content);
    }

    /// <summary>Projects a player's stock from the aggregate a command is holding.</summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>Through the aggregate, so nothing here ever CONSTRUCTS a gear instance.</b> The first
    /// draft rebuilt each item from its row, and the luck-routing rule refused it — correctly. That
    /// rule reads "produces a <c>GearInstance</c>" as "produces a grant", because a producer that rolls
    /// its own rarity skips the pity counter, and <c>24</c> §11 puts every protected grant behind one
    /// façade. A read-only comparison is not a grant, so the answer is not an exemption: it is to stop
    /// producing. <c>Inventory.Stored</c> already hands out the instances the aggregate holds, built
    /// once through <c>Player.Rehydrate</c> — the only validated construction path (<c>30</c> §11.3) —
    /// so this projection borrows them instead of making a fifth place that knows how.
    /// </para>
    /// <para>
    /// ⚠️ Which also means an exemption was available and declined. Adding this type to
    /// <c>RoutingExemptions</c> would have been two lines and would have left a real
    /// <c>GearInstance</c> factory sitting in a view, one refactor away from being handed a rarity.
    /// </para>
    /// </remarks>
    /// <param name="player">The player aggregate.</param>
    /// <param name="content">The loaded, schema-validated content snapshot.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    /// <exception cref="MissingContentException">A document the projection reads is absent.</exception>
    internal static InventoryView Project(PlayerAggregate player, ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(content);

        var par = ParPowerTuning.Read(content);
        var drops = DropsTuning.Read(content);
        var capacity = InventoryTuning.Read(content).MaxCapacity;
        var worn = Worn(player);

        return new InventoryView(
            capacity,
            Draw(player.Inventory.Stored, par, drops, worn, comparable: true),
            Draw(player.Inventory.Held, par, drops, worn, comparable: false));
    }

    /// <summary>The equipped item per slot, resolved out of the stock the loadout names.</summary>
    /// <remarks>
    /// 🔒 Resolved through the stock rather than trusted from the loadout alone, on <c>HeroBuild</c>'s
    /// own precedent: a loadout names identities and the stock is what holds them, and resolving them a
    /// second way is how a screen ends up disagreeing with the fight it describes. A slot naming an item
    /// the stock does not hold is left empty rather than throwing — <c>Player.Rehydrate</c> already
    /// refuses that row, so reaching it means a caller built one by hand.
    /// </remarks>
    private static IReadOnlyDictionary<GearSlot, GearInstance> Worn(PlayerAggregate player)
    {
        var worn = new Dictionary<GearSlot, GearInstance>();
        var byId = player.Inventory.Stored.ToDictionary(item => item.InstanceId);

        foreach (var (slot, instanceId) in player.Loadout.Gear)
        {
            if (byId.TryGetValue(instanceId, out var item))
            {
                worn[slot] = item;
            }
        }

        return worn;
    }

    private static IReadOnlyList<InventoryItemView> Draw(
        IReadOnlyList<GearInstance> items,
        ParPowerTuning par,
        DropsTuning drops,
        IReadOnlyDictionary<GearSlot, GearInstance> worn,
        bool comparable)
    {
        var views = new InventoryItemView[items.Count];

        for (var index = 0; index < views.Length; index++)
        {
            var item = items[index];
            var equipped = worn.TryGetValue(item.Slot, out var inSlot) ? inSlot : null;

            views[index] = new InventoryItemView(
                item.InstanceId,
                item.DefId,
                item.Slot,
                item.Family,
                item.Rarity,
                item.EnhanceLevel,
                item.Locked,
                IsEquipped: equipped is not null && equipped.InstanceId == item.InstanceId,
                Deltas: comparable
                    ? Deltas(par, drops, item, equipped)
                    : NoDeltas);
        }

        return Array.AsReadOnly(views);
    }

    /// <summary>What an item with nothing to compare against carries.</summary>
    private static IReadOnlyList<GearStatDeltaView> NoDeltas { get; } =
        Array.AsReadOnly(Array.Empty<GearStatDeltaView>());

    /// <summary>The per-stat comparison, or none when the item IS the one worn.</summary>
    /// <remarks>
    /// 🔒 An item compared against itself derives zero for every stat, which a screen would draw as a
    /// row of flat arrows — a green/red mark saying "this changes nothing" beside the item the player is
    /// already wearing. The empty list is the honest answer, and it is what lets the screen draw the
    /// worn item as worn rather than as a candidate that happens to tie.
    /// </remarks>
    private static IReadOnlyList<GearStatDeltaView> Deltas(
        ParPowerTuning par, DropsTuning drops, GearInstance item, GearInstance? equipped)
    {
        if (equipped is not null && equipped.InstanceId == item.InstanceId)
        {
            return NoDeltas;
        }

        var compared = InventoryComparison.Compare(par, drops, item, equipped);
        var deltas = new GearStatDeltaView[compared.Count];

        for (var index = 0; index < deltas.Length; index++)
        {
            var delta = compared[index];

            deltas[index] = new GearStatDeltaView(
                delta.Stat, delta.Candidate, delta.Equipped, delta.Delta, delta.IsPercent);
        }

        return Array.AsReadOnly(deltas);
    }
}

/// <summary>One item in the stock, as the Inventory screen draws it.</summary>
/// <param name="InstanceId">The instance, which <c>EQUIP</c> carries.</param>
/// <param name="DefId">The definition it was rolled from.</param>
/// <param name="Slot">The slot it occupies, which is also the slot its comparison is against.</param>
/// <param name="Family">The family, which the item's name and icon are keyed on.</param>
/// <param name="Rarity">The band. Drawn by the shared rarity treatment, never by colour alone.</param>
/// <param name="EnhanceLevel">The <c>+N</c> the forge has taken it to.</param>
/// <param name="Locked">Whether it is excluded from auto-salvage and merge selection.</param>
/// <param name="IsEquipped">Whether this is the item currently worn in <paramref name="Slot"/>.</param>
/// <param name="Deltas">
/// What wearing it would change, stat by stat. Empty for the item already worn and for anything in
/// overflow — see <see cref="InventoryView"/>'s remarks for why those two are empty rather than zero.
/// </param>
public sealed record InventoryItemView(
    GearInstanceId InstanceId,
    string DefId,
    GearSlot Slot,
    GearFamily Family,
    Rarity Rarity,
    int EnhanceLevel,
    bool Locked,
    bool IsEquipped,
    IReadOnlyList<GearStatDeltaView> Deltas);

/// <summary>One stat of a side-by-side comparison — <c>08</c> §5's green/red arrow, as data.</summary>
/// <remarks>
/// <see cref="Delta"/> is carried rather than left to the caller to subtract: a difference of two
/// rounded numbers is not itself guaranteed to be rounded, and this figure reaches a screen.
/// </remarks>
/// <param name="Stat">The stat id, as authored.</param>
/// <param name="Candidate">What the tapped item derives for it.</param>
/// <param name="Equipped">What the worn item derives for it, or zero when the slot is empty.</param>
/// <param name="Delta">
/// <paramref name="Candidate"/> minus <paramref name="Equipped"/>, rounded. The sign is the arrow.
/// </param>
/// <param name="IsPercent">
/// Whether the figures are fractions feeding a capped percentage rather than flat amounts. ⚠️ A screen
/// that formatted the two kinds the same way would show a number nothing in the game computes.
/// </param>
public sealed record GearStatDeltaView(
    string Stat, double Candidate, double Equipped, double Delta, bool IsPercent);
