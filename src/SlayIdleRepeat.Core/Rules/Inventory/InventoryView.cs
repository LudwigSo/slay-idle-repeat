using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Gear;
using SlayIdleRepeat.Core.Model.Gear;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Forge;
using SlayIdleRepeat.Core.Rules.Gear;
using SlayIdleRepeat.Core.Rules.Luck;
using PlayerAggregate = SlayIdleRepeat.Core.Model.Player;

namespace SlayIdleRepeat.Core.Rules.Inventory;

/// <summary>
/// A player's gear stock and what each item in it is, does and would change, projected into read-only
/// records the Gear screen (S16) can draw — the grid, the slots, the comparison and the Forge's three
/// previews.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Every number the screen shows comes out of here, and none is computed on the client.</b>
/// <c>08</c> §5 requires that <em>"tapping an item always shows a side-by-side delta vs the currently
/// equipped item in that slot, with green/red arrows per stat"</em>; that delta is
/// <see cref="InventoryComparison"/>'s, which folds the Forge's multiplier in through the same helper
/// the hero build uses, so the screen and the fight agree to the digit. The item's power is
/// <c>InventorySorting</c>'s own figure — the one the POWER ordering sorts by — the enhance preview is
/// <c>GearEnhancement</c>'s rate and <c>ForgeTuning</c>'s price, the salvage preview is
/// <c>GearSalvage</c>'s payout, and the merge preview's identity is the same three fields
/// <c>GearMerge.Refusal</c> compares. A second copy of any of them on the client would be a second
/// answer, and the two would disagree the first time either moved.
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
/// 🔒 <b>The previews describe what a command WOULD do; they decide nothing.</b> <c>ENHANCE</c>,
/// <c>MERGE</c> and <c>SALVAGE</c> read the same tuning and the same item and answer for themselves,
/// so a preview can be stale by one command and never wrong about the rules. What a preview cannot
/// know is the player's wallet against the price — that is the screen's to read off the row beside
/// this projection — and whether a merge's other two inputs exist, which is a fact about the whole
/// stock the screen groups by <see cref="MergePreview.Key"/>.
/// </para>
/// <para>
/// 🔒 <b>Held items are projected too, and marked.</b> <c>08</c> §5's stock is capped and a drop
/// arriving at a full one is <em>held</em> rather than refused — so a screen that showed only
/// <see cref="InventorySnapshot.Stored"/> would hide the items a player most needs to see, which are
/// the ones they are about to lose room for. They carry no delta: an item in overflow cannot be
/// equipped, and offering a comparison for it would invite a tap that the rules layer refuses with
/// <c>INVENTORY_FULL</c>. They carry every other figure, because a player deciding what to make room
/// for needs to know what is waiting.
/// </para>
/// <para>
/// ⚠️ <b>Nothing here constructs a gear instance</b> — not even for the enhance preview, which asks
/// what the item would be worth one rung up. The first draft of this projection rebuilt items from
/// their rows and the luck-routing rule refused it, correctly: a producer of <c>GearInstance</c> is a
/// producer of a grant. The previews are arithmetic over the item's fields at a stated level, through
/// overloads that take the level as a number.
/// </para>
/// </remarks>
public sealed class InventoryView
{
    private const long NoCost = 0;

    /// <summary>
    /// How far the mercy horizon is searched. The authored slope makes any rung certain well inside
    /// this, and a horizon past it is reported as none rather than searched for ever.
    /// </summary>
    private const int MercyHorizon = 1000;

    private InventoryView(
        int capacity,
        int maxEnhanceLevel,
        int mergeInputCount,
        IReadOnlyList<int> setBreakpoints,
        IReadOnlyList<InventoryItemView> stored,
        IReadOnlyList<InventoryItemView> held,
        IReadOnlyList<ActiveSetView> activeSets)
    {
        Capacity = capacity;
        MaxEnhanceLevel = maxEnhanceLevel;
        MergeInputCount = mergeInputCount;
        SetBreakpoints = setBreakpoints;
        Stored = stored;
        Held = held;
        ActiveSets = activeSets;
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

    /// <summary>The top of the enhancement ladder — <c>+15</c> as authored — for the progress a screen draws towards it.</summary>
    public int MaxEnhanceLevel { get; }

    /// <summary>How many inputs a merge takes — three as authored — items and a Merge Dust substitute together.</summary>
    public int MergeInputCount { get; }

    /// <summary>The piece counts at which a set pays a bonus — <c>[2, 4, 6]</c> as authored.</summary>
    public IReadOnlyList<int> SetBreakpoints { get; }

    /// <summary>The items in the stock proper, each with what wearing it would change.</summary>
    public IReadOnlyList<InventoryItemView> Stored { get; }

    /// <summary>
    /// The items a full stock is holding for the player — <c>08</c> §5's overflow. Never comparable.
    /// </summary>
    public IReadOnlyList<InventoryItemView> Held { get; }

    /// <summary>
    /// The sets the worn gear is building, one entry per axis with at least one SS piece worn, in axis
    /// order. Empty when nothing worn is SS.
    /// </summary>
    public IReadOnlyList<ActiveSetView> ActiveSets { get; }

    /// <summary>Projects a player's stock from the persisted row, in grant order.</summary>
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

        return Project(RowDoor.Player(player, content, nameof(player)), content, order: null);
    }

    /// <summary>Projects a player's stock from the persisted row, listed in the order asked for.</summary>
    /// <remarks>
    /// The ordering is applied to the stock proper and to the overflow separately — the two are two
    /// bands on the screen, and an item cannot sort out of the one it is in.
    /// </remarks>
    /// <param name="player">The player's row.</param>
    /// <param name="content">The loaded, schema-validated content snapshot.</param>
    /// <param name="order">Which of the five orderings the list is in.</param>
    /// <returns>The view.</returns>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    /// <exception cref="ArgumentException">The row does not rehydrate.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="order"/> is not one of the five.</exception>
    /// <exception cref="MissingContentException">A document the projection reads is absent.</exception>
    public static InventoryView Project(PlayerSnapshot player, ContentSnapshot content, InventorySortKey order)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(content);

        if (!Enum.IsDefined(order))
        {
            throw new ArgumentOutOfRangeException(
                nameof(order), order, "That is not one of the five orderings the stock can be listed in.");
        }

        return Project(RowDoor.Player(player, content, nameof(player)), content, order);
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
    /// <param name="order">Which ordering the lists are in, or <c>null</c> for grant order.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    /// <exception cref="MissingContentException">A document the projection reads is absent.</exception>
    internal static InventoryView Project(PlayerAggregate player, ContentSnapshot content, InventorySortKey? order)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(content);

        var tables = new Tables(
            ParPowerTuning.Read(content),
            DropsTuning.Read(content),
            ForgeTuning.Read(content),
            GearCatalogue.Read(content),
            LuckTuning.Read(content).Enhance);
        var capacity = InventoryTuning.Read(content).MaxCapacity;
        var worn = Worn(player);

        return new InventoryView(
            capacity,
            tables.Forge.MaxEnhanceLevel,
            tables.Forge.MergeInputCount,
            tables.Drops.SetBreakpoints,
            Draw(Ordered(player.Inventory.Stored, order, tables), tables, worn, comparable: true),
            Draw(Ordered(player.Inventory.Held, order, tables), tables, worn, comparable: false),
            Sets(tables, worn));
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

    private static IReadOnlyList<GearInstance> Ordered(
        IReadOnlyList<GearInstance> items, InventorySortKey? order, Tables tables) =>
        order is { } key
            ? InventorySorting.Sort(items, key, tables.Par, tables.Drops, tables.Forge, tables.Catalogue)
            : items;

    private static IReadOnlyList<ActiveSetView> Sets(
        Tables tables, IReadOnlyDictionary<GearSlot, GearInstance> worn)
    {
        var active = SetBonusResolver.Resolve(tables.Catalogue, tables.Drops, worn.Values.ToArray());
        var views = new ActiveSetView[active.Count];

        for (var index = 0; index < views.Length; index++)
        {
            var set = active[index];

            views[index] = new ActiveSetView(
                set.Set, tables.Drops.SetNameKey(set.Set), set.Pieces, set.BreakpointsMet);
        }

        return Array.AsReadOnly(views);
    }

    private static IReadOnlyList<InventoryItemView> Draw(
        IReadOnlyList<GearInstance> items,
        Tables tables,
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
                tables.Catalogue.Definition(item.Family).Axis,
                item.Rarity,
                item.ChapterOrigin,
                item.Quality,
                item.EnhanceLevel,
                item.Locked,
                IsEquipped: equipped is not null && equipped.InstanceId == item.InstanceId,
                Power: InventorySorting.PowerOf(tables.Par, tables.Drops, tables.Forge, item),
                Stats: Stats(tables, item, item.EnhanceLevel),
                Affixes: Affixes(tables, item),
                Deltas: comparable ? Deltas(tables, item, equipped) : NoDeltas,
                Enhance: Enhance(tables, item),
                Salvage: Salvage(tables, item),
                Merge: Merge(tables, item));
        }

        return Array.AsReadOnly(views);
    }

    /// <summary>What an item with nothing to compare against carries.</summary>
    private static IReadOnlyList<GearStatDeltaView> NoDeltas { get; } =
        Array.AsReadOnly(Array.Empty<GearStatDeltaView>());

    /// <summary>The item's two stats as the hero receives them at the stated enhancement level.</summary>
    private static IReadOnlyList<GearStatView> Stats(Tables tables, GearInstance item, int enhanceLevel)
    {
        var primary = GearStatDerivation.AsWorn(
            GearStatDerivation.Primary(tables.Par, tables.Drops, item), tables.Forge, enhanceLevel);
        var secondary = GearStatDerivation.AsWorn(
            GearStatDerivation.Secondary(tables.Par, tables.Drops, item), tables.Forge, enhanceLevel);

        return Array.AsReadOnly(new[]
        {
            new GearStatView(primary.Stat, primary.Value, primary.IsPercent),
            new GearStatView(secondary.Stat, secondary.Value, secondary.IsPercent),
        });
    }

    /// <summary>The item's rolled affixes, each with the name the pool authors for it and how its roll is written.</summary>
    private static IReadOnlyList<GearAffixView> Affixes(Tables tables, GearInstance item)
    {
        var views = new GearAffixView[item.Affixes.Count];

        for (var index = 0; index < views.Length; index++)
        {
            var roll = item.Affixes[index];
            var definition = tables.Drops.Affix(roll.AffixId);

            views[index] = new GearAffixView(
                roll.AffixId,
                definition.DisplayNameKey,
                roll.Value,
                IsPercent: definition.WritesAStat &&
                           GearStatKinds.IsShare(definition.Stat!.Value, definition.Op!.Value),
                Favourable: !definition.WritesAStat ||
                            GearStatKinds.IsFavourable(definition.Stat!.Value, roll.Value));
        }

        return Array.AsReadOnly(views);
    }

    /// <summary>The per-stat comparison, or none when the item IS the one worn.</summary>
    /// <remarks>
    /// 🔒 An item compared against itself derives zero for every stat, which a screen would draw as a
    /// row of flat arrows — a green/red mark saying "this changes nothing" beside the item the player is
    /// already wearing. The empty list is the honest answer, and it is what lets the screen draw the
    /// worn item as worn rather than as a candidate that happens to tie.
    /// </remarks>
    private static IReadOnlyList<GearStatDeltaView> Deltas(
        Tables tables, GearInstance item, GearInstance? equipped)
    {
        if (equipped is not null && equipped.InstanceId == item.InstanceId)
        {
            return NoDeltas;
        }

        var compared = InventoryComparison.Compare(tables.Par, tables.Drops, tables.Forge, item, equipped);
        var deltas = new GearStatDeltaView[compared.Count];

        for (var index = 0; index < deltas.Length; index++)
        {
            var delta = compared[index];

            deltas[index] = new GearStatDeltaView(
                delta.Stat, delta.Candidate, delta.Equipped, delta.Delta, delta.IsPercent);
        }

        return Array.AsReadOnly(deltas);
    }

    /// <summary>What the next <c>ENHANCE</c> would cost, how likely it is to land and what it would make — or null at the ceiling.</summary>
    /// <remarks>
    /// The rate is <c>GearEnhancement.EffectiveRate</c> over the failure counter the item carries, which
    /// is the exact rate the handler will draw against on the next attempt (the Plus lucky bonus is not
    /// wired and the handler passes none either). A stated rate that differed from the drawn one by a
    /// mercy step would be a fairness floor shown wrong.
    /// </remarks>
    private static EnhancePreview? Enhance(Tables tables, GearInstance item)
    {
        if (GearEnhancement.IsAtCeiling(item.EnhanceLevel, tables.Forge))
        {
            return null;
        }

        var next = item.EnhanceLevel + 1;

        return new EnhancePreview(
            next,
            tables.Forge.EnhanceStoneCost(next),
            GearEnhancement.EffectiveRate(
                item.EnhanceLevel,
                item.EnhanceFailures,
                GearEnhancement.NoLuckyBonus,
                tables.Forge,
                tables.Mercy),
            item.EnhanceFailures,
            FailuresUntilCertain(tables, item),
            InventorySorting.PowerOf(tables.Par, tables.Drops, tables.Forge, item, next),
            Stats(tables, item, next));
    }

    /// <summary>
    /// How many more failed attempts, from where the item stands, make the next attempt certain — or
    /// <c>null</c> when the mercy rule never reaches certainty for this rung.
    /// </summary>
    /// <remarks>
    /// 🔒 Walked through the same <c>GearEnhancement.EffectiveRate</c> the handler draws against, one
    /// failure at a time, rather than solved from the slope: the rule has a cap and a slope and may gain
    /// a shape later, and a closed form here would be a second copy of it. <c>24</c> §11 makes every
    /// floor a real number on the screen; this is the enhance floor's number.
    /// </remarks>
    private static int? FailuresUntilCertain(Tables tables, GearInstance item)
    {
        for (var more = 0; more <= MercyHorizon; more++)
        {
            var rate = GearEnhancement.EffectiveRate(
                item.EnhanceLevel,
                item.EnhanceFailures + more,
                GearEnhancement.NoLuckyBonus,
                tables.Forge,
                tables.Mercy);

            if (rate >= 1.0)
            {
                return more;
            }
        }

        return null;
    }

    private static SalvagePreview Salvage(Tables tables, GearInstance item)
    {
        var (dust, stones) = GearSalvage.Payout(item, tables.Forge);

        return new SalvagePreview(dust, stones);
    }

    /// <summary>The item's merge identity and what fusing three of it would cost and make.</summary>
    private static MergePreview Merge(Tables tables, GearInstance item)
    {
        var output = LuckService.MergeOutputBand(item.Rarity);

        return new MergePreview(
            MergeIdentity.Of(item),
            output,
            output is { } band ? tables.Forge.MergeCrownCost(band) : NoCost,
            output is null ? null : tables.Forge.MergeDustSubstituteCost(item.Rarity));
    }

    /// <summary>The five tables every item is drawn against, read once per projection.</summary>
    private sealed record Tables(
        ParPowerTuning Par, DropsTuning Drops, ForgeTuning Forge, GearCatalogue Catalogue, EnhanceRule Mercy);
}

/// <summary>One item in the stock, as the Gear screen draws it.</summary>
/// <param name="InstanceId">The instance, which every gear command carries.</param>
/// <param name="DefId">The definition it was rolled from.</param>
/// <param name="Slot">The slot it occupies, which is also the slot its comparison is against.</param>
/// <param name="Family">The family, which the item's name and icon are keyed on.</param>
/// <param name="Axis">The family's axis — its build bias, and at SS the set it counts towards.</param>
/// <param name="Rarity">The band. Drawn by the shared rarity treatment, never by colour alone.</param>
/// <param name="ChapterOrigin">The chapter it dropped in, which scales its power.</param>
/// <param name="Quality">How well it rolled, 0..1.</param>
/// <param name="EnhanceLevel">The <c>+N</c> the forge has taken it to.</param>
/// <param name="Locked">Whether it is excluded from auto-salvage, salvage and merge selection.</param>
/// <param name="IsEquipped">Whether this is the item currently worn in <paramref name="Slot"/>.</param>
/// <param name="Power">The item's one number: band, chapter and enhancement together. What POWER sorts by.</param>
/// <param name="Stats">Its primary and secondary stat as the hero receives them, enhancement included.</param>
/// <param name="Affixes">Its rolled affixes, in roll order. Empty at the bottom band.</param>
/// <param name="Deltas">
/// What wearing it would change, stat by stat. Empty for the item already worn and for anything in
/// overflow — see <see cref="InventoryView"/>'s remarks for why those two are empty rather than zero.
/// </param>
/// <param name="Enhance">What the next enhancement would cost and make, or <c>null</c> at the ceiling.</param>
/// <param name="Salvage">What salvaging it would pay.</param>
/// <param name="Merge">Its merge identity and what a fusion would cost and make.</param>
public sealed record InventoryItemView(
    GearInstanceId InstanceId,
    string DefId,
    GearSlot Slot,
    GearFamily Family,
    GearFamilyAxis Axis,
    Rarity Rarity,
    int ChapterOrigin,
    double Quality,
    int EnhanceLevel,
    bool Locked,
    bool IsEquipped,
    double Power,
    IReadOnlyList<GearStatView> Stats,
    IReadOnlyList<GearAffixView> Affixes,
    IReadOnlyList<GearStatDeltaView> Deltas,
    EnhancePreview? Enhance,
    SalvagePreview Salvage,
    MergePreview Merge);

/// <summary>One of an item's two stats, as the hero receives it.</summary>
/// <param name="Stat">The stat id, as authored.</param>
/// <param name="Value">The figure, enhancement folded in, rounded.</param>
/// <param name="IsPercent">Whether the figure is a fraction feeding a capped percentage rather than a flat amount.</param>
public sealed record GearStatView(string Stat, double Value, bool IsPercent);

/// <summary>One rolled affix.</summary>
/// <param name="AffixId">The pool's id.</param>
/// <param name="NameKey">The locale key the pool authors as its display name.</param>
/// <param name="Value">The rolled value, rounded. Negative for the damage-reduction affix, by design.</param>
/// <param name="IsPercent">Whether the roll is a share — written as a percentage — rather than an amount.</param>
/// <param name="Favourable">
/// Whether the roll moves its stat the way a player wants: a positive roll onto a stat to raise, or a
/// negative roll onto one to lower. What a screen signs by — a benefit written with the loss glyph is
/// the failure this exists to rule out.
/// </param>
public sealed record GearAffixView(string AffixId, string NameKey, double Value, bool IsPercent, bool Favourable);

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

/// <summary>What the next <c>ENHANCE</c> on an item would cost, risk and make.</summary>
/// <param name="NextLevel">The rung it would reach.</param>
/// <param name="StoneCost">The Enhance Stones charged, landed or not.</param>
/// <param name="SuccessRate">The chance it lands, mercy included — the rate the handler draws against.</param>
/// <param name="ConsecutiveFailures">How many attempts in a row have failed on this item — what the mercy is earned by.</param>
/// <param name="FailuresUntilCertain">
/// How many MORE failures make the next attempt certain, or <c>null</c> when the rule never reaches
/// certainty for this rung. Zero means the next attempt is already certain.
/// </param>
/// <param name="NextPower">The item's power at <paramref name="NextLevel"/>.</param>
/// <param name="NextStats">Its two stats at <paramref name="NextLevel"/>, in the same order as the item's own.</param>
public sealed record EnhancePreview(
    int NextLevel,
    long StoneCost,
    double SuccessRate,
    int ConsecutiveFailures,
    int? FailuresUntilCertain,
    double NextPower,
    IReadOnlyList<GearStatView> NextStats);

/// <summary>What <c>SALVAGE</c> pays for one item.</summary>
/// <param name="Dust">Merge Dust.</param>
/// <param name="Stones">Enhance Stones refunded, from the successful attempts invested.</param>
public sealed record SalvagePreview(long Dust, long Stones);

/// <summary>An item's merge identity and the fusion it can be an input to.</summary>
/// <param name="Identity">Equal for two items exactly when <c>MERGE</c> would accept them together.</param>
/// <param name="OutputRarity">The band a fusion lands on, or <c>null</c> at the top of the ladder.</param>
/// <param name="CrownCost">The Crowns a fusion onto <paramref name="OutputRarity"/> costs. Zero when there is none.</param>
/// <param name="DustSubstituteCost">
/// The Merge Dust that stands in for the third input when only two items are at hand, or <c>null</c>
/// when no fusion exists to substitute into.
/// </param>
public sealed record MergePreview(MergeIdentity Identity, Rarity? OutputRarity, long CrownCost, long? DustSubstituteCost);

/// <summary>One set the worn gear is building.</summary>
/// <param name="Set">The set, by the axis that identifies it.</param>
/// <param name="NameKey">The locale key the set is called by.</param>
/// <param name="Pieces">How many SS pieces of it are worn.</param>
/// <param name="BreakpointsMet">Which of <see cref="InventoryView.SetBreakpoints"/> those pieces reach.</param>
public sealed record ActiveSetView(
    GearFamilyAxis Set, string NameKey, int Pieces, IReadOnlyList<int> BreakpointsMet);
