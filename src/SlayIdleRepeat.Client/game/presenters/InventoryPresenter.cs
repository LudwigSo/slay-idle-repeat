using System.Globalization;
using SlayIdleRepeat.Application.Ports.Client;
using SlayIdleRepeat.Application.Services;
using SlayIdleRepeat.Application.UseCases;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Inventory;

namespace SlayIdleRepeat.Client.Game.Presenters;

/// <summary>How far the Gear screen has got with the read everything it draws depends on.</summary>
public enum InventoryStage
{
    /// <summary>The read has not happened yet. The state a freshly built presenter reports.</summary>
    NotYetRead = 1,

    /// <summary>The stock was read and holds something.</summary>
    Ready = 2,

    /// <summary>
    /// The stock was read and is empty. Named rather than folded into <see cref="Ready"/>: a fresh
    /// account really does own nothing, and a blank grid with no sentence reads as a rendering fault.
    /// </summary>
    Empty = 3,

    /// <summary>The read itself did not answer. A state, never an escape.</summary>
    ReadUnavailable = 4,
}

/// <summary>What one submission from this screen did.</summary>
public enum InventorySubmission
{
    /// <summary>The command went to the host and was accepted.</summary>
    Submitted = 1,

    /// <summary>Nothing was submitted, because the screen's own state does not permit it.</summary>
    RefusedNotAvailable = 2,

    /// <summary>The command went to the host and the rules refused it.</summary>
    Rejected = 3,

    /// <summary>The host never answered.</summary>
    HostUnavailable = 4,
}

/// <summary>Which page the item sheet is showing, or that it is closed.</summary>
public enum GearSheetPage
{
    /// <summary>No sheet. The grid and the hero are what the player is looking at.</summary>
    Closed = 1,

    /// <summary>One item: its figures, its comparison, and the actions it offers.</summary>
    Details = 2,

    /// <summary>The next enhancement: price, odds, and what it would make.</summary>
    Enhance = 3,

    /// <summary>A fusion: the keeper, the two inputs, the price, and what it would make.</summary>
    Merge = 4,
}

/// <summary>Whether the grid is being browsed or a batch is being picked for salvage.</summary>
public enum InventoryMode
{
    /// <summary>A tap opens an item.</summary>
    Browse = 1,

    /// <summary>A tap toggles an item into the salvage batch.</summary>
    Select = 2,
}

/// <summary>What a notice is about, so the screen can draw it in the matching accent.</summary>
public enum InventoryNoticeKind
{
    /// <summary>Nothing to say.</summary>
    None = 1,

    /// <summary>Something went the player's way — an equip, a landed rung, a fusion, a payout.</summary>
    Gain = 2,

    /// <summary>Something did not — a failed rung, a refusal, a host that did not answer.</summary>
    Setback = 3,

    /// <summary>Neutral: a lock toggled, a destination not built yet.</summary>
    Plain = 4,
}

/// <summary>One line of an item's stat block: the caption, the figure, and the sign of its comparison.</summary>
/// <param name="Name">The stat's name, resolved.</param>
/// <param name="Value">The figure, written for a player.</param>
/// <param name="Delta">The comparison against what is worn, written with its arrow and sign, or empty.</param>
/// <param name="Sign">The comparison's direction: positive, negative or zero. Zero when there is no comparison.</param>
public sealed record GearStatLine(string Name, string Value, string Delta, int Sign);

/// <summary>
/// S16 — the Gear screen: the hero and the six slots, the stock, and the Forge's three operations.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Every number on this screen comes from <c>InventoryView</c>, and none is computed here.</b>
/// The comparison, the item power, the enhance price and odds, the salvage payout and the merge
/// identity are all the projection's — the same derivations the fight and the handlers use. What this
/// type owns is the screen's STATE: which item is open, which page of the sheet, which slot the grid is
/// filtered to, which items are picked for a fusion or a salvage batch — and the one decision per
/// action of whether the command may be sent at all.
/// </para>
/// <para>
/// 🔒 <b>A refusal the screen can know in advance is made here, before the round trip.</b> An item in
/// overflow cannot be worn, enhanced, merged or salvaged; a locked item cannot be merged or salvaged;
/// a fusion needs three inputs and the price; an enhancement needs the stones. Each of those the
/// rules would refuse after a server round trip, with a code the player cannot read. Refusing here
/// keeps the control disabled with a sentence beside it rather than live with a surprise after it.
/// </para>
/// <para>
/// 🔒 <b>The outcome of a command is read off its events, never assumed.</b> An enhancement lands or
/// fails on a server draw, and the only signal on the wire is the reason token on the stones it
/// charged; a salvage's payout arrives as two currency movements. The notice the player sees is
/// composed from those, then the stock is read again so every figure on the screen is the row the
/// server now holds.
/// </para>
/// <para>
/// ⚠️ <b>Reforge and retune are not offered.</b> Both commands exist in the vocabulary and both are
/// deferred — the dispatch table answers <c>ILLEGAL_STATE</c> for either — so a control offering them
/// would be a control that cannot succeed. Bag space is likewise unpurchasable: capacity is a flat
/// authored ceiling and no command moves it.
/// </para>
/// </remarks>
public sealed class InventoryPresenter
{
    /// <summary>
    /// ⚠️ Deliberately absent, and named so it can be found. There is no control here for buying bag
    /// space, and there is nothing this screen could submit if there were.
    /// </summary>
    private const string ThereIsNoWayToBuyBagSpace =
        "08 §5's errata makes inventory capacity a flat 1000 equal to the base, retiring 10 §4's " +
        "+20-per-purchase Crown ladder and 10 §2's flat 400-Soul-Shard alternative — both stay " +
        "authored, priced and UNSPENDABLE. The same product ruling explicitly declined to add " +
        "EXPAND_INVENTORY to 14 §2.3's command vocabulary, so nothing in the game can move the " +
        "ceiling. A screen offering to expand the bag would be offering a command that does not " +
        "exist, and one showing the retired 320 would be drawing a limit the game does not have.";

    // ---- captions ------------------------------------------------------------------------------

    private const string TitleNameKey = "loc.inventory.title.name";
    private const string CapacityLabelKey = "loc.inventory.capacity.label";
    private const string HeldLabelKey = "loc.inventory.held.label";
    private const string PowerLabelKey = "loc.inventory.power.label";
    private const string HeroPowerLabelKey = "loc.inventory.hero_power.label";
    private const string EmptySlotLabelKey = "loc.inventory.empty_slot.label";
    private const string SortLabelKey = "loc.inventory.sort.label";
    private const string AffixesLabelKey = "loc.inventory.affixes.label";
    private const string SetLabelKey = "loc.inventory.set.label";
    private const string SetPiecesLabelKey = "loc.inventory.set_pieces.label";
    private const string QualityLabelKey = "loc.inventory.quality.label";
    private const string ChapterOriginLabelKey = "loc.inventory.chapter_origin.label";
    private const string EnhanceCostLabelKey = "loc.inventory.enhance_cost.label";
    private const string EnhanceOddsLabelKey = "loc.inventory.enhance_odds.label";
    private const string EnhanceMaxedLabelKey = "loc.inventory.enhance_maxed.label";
    private const string MergeKeeperLabelKey = "loc.inventory.merge_keeper.label";
    private const string MergeInputsLabelKey = "loc.inventory.merge_inputs.label";
    private const string MergeResultLabelKey = "loc.inventory.merge_result.label";
    private const string MergeCostLabelKey = "loc.inventory.merge_cost.label";
    private const string MergeDustLabelKey = "loc.inventory.merge_dust.label";
    private const string SalvageReturnsLabelKey = "loc.inventory.salvage_returns.label";
    private const string SelectedCountLabelKey = "loc.inventory.selected_count.label";
    private const string QuickPickLabelKey = "loc.inventory.quick_pick.label";

    // ---- badges ---------------------------------------------------------------------------------

    private const string EquippedBadgeKey = "loc.inventory.equipped.badge";
    private const string LockedBadgeKey = "loc.inventory.locked.badge";
    private const string HeldBadgeKey = "loc.inventory.held.badge";
    private const string UpgradeBadgeKey = "loc.inventory.upgrade.badge";

    // ---- actions --------------------------------------------------------------------------------

    private const string EquipActionKey = "loc.inventory.equip.action";
    private const string UnequipActionKey = "loc.inventory.unequip.action";
    private const string EnhanceActionKey = "loc.inventory.enhance.action";
    private const string EnhanceOnceActionKey = "loc.inventory.enhance_once.action";
    private const string MergeActionKey = "loc.inventory.merge.action";
    private const string MergeConfirmActionKey = "loc.inventory.merge_confirm.action";
    private const string SalvageActionKey = "loc.inventory.salvage.action";
    private const string SalvageConfirmActionKey = "loc.inventory.salvage_confirm.action";
    private const string LockActionKey = "loc.inventory.lock.action";
    private const string UnlockActionKey = "loc.inventory.unlock.action";
    private const string SelectActionKey = "loc.inventory.select.action";
    private const string CancelActionKey = "loc.inventory.cancel.action";
    private const string BackActionKey = "loc.inventory.back.action";
    private const string CloseActionKey = "loc.inventory.close.action";
    private const string ClearFilterActionKey = "loc.inventory.clear_filter.action";

    // ---- blocks: the sentence beside a control that is not live --------------------------------

    private const string HeldNotEquippableBlockKey = "loc.inventory.held_not_equippable.block";
    private const string LockedNoForgeBlockKey = "loc.inventory.locked_no_forge.block";
    private const string TopBandNoMergeBlockKey = "loc.inventory.top_band_no_merge.block";
    private const string MergeNeedsInputsBlockKey = "loc.inventory.merge_needs_inputs.block";
    private const string MergeConsumesWornBlockKey = "loc.inventory.merge_consumes_worn.block";
    private const string MergeConsumesBetterRollBlockKey = "loc.inventory.merge_consumes_better_roll.block";
    private const string MergeRerollsAffixesBlockKey = "loc.inventory.merge_rerolls_affixes.block";
    private const string NotEnoughCrownsBlockKey = "loc.inventory.not_enough_crowns.block";
    private const string NotEnoughStonesBlockKey = "loc.inventory.not_enough_stones.block";
    private const string NotEnoughDustBlockKey = "loc.inventory.not_enough_dust.block";
    private const string SalvageIncludesEpicBlockKey = "loc.inventory.salvage_includes_epic.block";
    private const string SalvageIncludesEnhancedBlockKey = "loc.inventory.salvage_includes_enhanced.block";
    private const string SalvageNothingSelectableBlockKey = "loc.inventory.salvage_nothing_selectable.block";

    // ---- status: one line per state, and one per thing a command did ----------------------------

    private const string LoadingStatusKey = "loc.inventory.loading.status";
    private const string EmptyStatusKey = "loc.inventory.empty.status";
    private const string FilterEmptyStatusKey = "loc.inventory.filter_empty.status";
    private const string UnavailableStatusKey = "loc.inventory.unavailable.status";
    private const string RefusedStatusKey = "loc.inventory.refused.status";
    private const string RefusedFundsStatusKey = "loc.inventory.refused_funds.status";
    private const string RefusedBattleStatusKey = "loc.inventory.refused_battle.status";
    private const string RefusedNotOwnedStatusKey = "loc.inventory.refused_not_owned.status";
    private const string HostUnavailableStatusKey = "loc.inventory.host_unavailable.status";
    private const string EquippedStatusKey = "loc.inventory.equipped.status";
    private const string UnequippedStatusKey = "loc.inventory.unequipped.status";
    private const string EnhanceLandedStatusKey = "loc.inventory.enhance_landed.status";
    private const string EnhanceFailedStatusKey = "loc.inventory.enhance_failed.status";
    private const string MergedStatusKey = "loc.inventory.merged.status";
    private const string SalvagedStatusKey = "loc.inventory.salvaged.status";
    private const string LockedStatusKey = "loc.inventory.locked.status";
    private const string UnlockedStatusKey = "loc.inventory.unlocked.status";
    private const string NotOpenYetStatusKey = "loc.home.not_open_yet.status";

    // ---- vocabularies the screen borrows -------------------------------------------------------

    private const string RarityNameKeyPrefix = "loc.rarity.";
    private const string SlotNameKeyPrefix = "loc.gear.slot.";
    private const string FamilyNameKeyPrefix = "loc.gear.family.";
    private const string StatNameKeyPrefix = "loc.gear.stat.";
    private const string SortNameKeyPrefix = "loc.inventory.sort.";
    private const string NameKeySuffix = ".name";
    private const string LabelKeySuffix = ".label";
    private const string TabHomeLabelKey = "loc.home.tab.home.label";
    private const string TabGearLabelKey = "loc.home.tab.gear.label";
    private const string TabTalentsLabelKey = "loc.home.tab.talents.label";
    private const string TabCollectionLabelKey = "loc.home.tab.collection.label";
    private const string TabShopLabelKey = "loc.home.tab.shop.label";

    /// <summary>The reason token an enhancement charge carries when the rung landed.</summary>
    private const string EnhanceLandedReason = "enhance_cost_success";

    /// <summary>The runtime parameters a status line may carry, replaced at render time.</summary>
    private const string ItemToken = "{item}";
    private const string LevelToken = "{level}";
    private const string CountToken = "{count}";
    private const string DustToken = "{dust}";
    private const string StonesToken = "{stones}";
    private const string SlotToken = "{slot}";

    /// <summary>What a line with nothing to say answers.</summary>
    private const string NothingLeftToSay = "";

    /// <summary>Separates what the bag holds from what it can hold, as the board separates HP.</summary>
    private const char OverSeparator = '/';

    private const string EnhancePrefix = "+";
    private const string PercentSuffix = "%";
    private const string FlatFormat = "0.#";
    private const string PercentFormat = "0.#";
    private const string GainArrow = "↑";
    private const string LossArrow = "↓";
    private const string LevelArrow = "–";
    private const string GainSign = "+";
    private const string LossSign = "−";
    private const string Space = " ";
    private const string OfSeparator = " / ";

    /// <summary>The slot columns as the screen lays them out: body order down the hero's left, weapon and jewels down the right.</summary>
    private static readonly GearSlot[] LeftSlotColumn = [GearSlot.HELMET, GearSlot.ARMOR, GearSlot.BOOTS];

    private static readonly GearSlot[] RightSlotColumn = [GearSlot.WEAPON, GearSlot.RING, GearSlot.AMULET];

    /// <summary>The orderings offered, in the order a tap on the sort control cycles them.</summary>
    private static readonly InventorySortKey[] SortCycle =
        [InventorySortKey.POWER, InventorySortKey.RARITY, InventorySortKey.SLOT, InventorySortKey.NEWEST];

    /// <summary>The bands the quick-pick gems in select mode add wholesale. Never the top two: nobody bulk-salvages a Legendary.</summary>
    private static readonly Rarity[] QuickPickBands = [Rarity.C, Rarity.B, Rarity.A];

    private readonly IGameHost _gameHost;
    private readonly LocaleStringCatalogue _strings;
    private readonly ContentSnapshot _content;
    private readonly PlayerId _player;
    private readonly IHeroPowerSource _power;

    private PlayerSnapshot? _row;
    private InventoryView? _view;
    private GearInstanceId? _inspected;
    private readonly List<GearInstanceId> _mergePicks = [];
    private readonly HashSet<GearInstanceId> _selected = [];
    private readonly List<GearInstanceId> _selectionOrder = [];
    private bool _submissionInFlight;

    // The lists the grid and the sheet draw, rebuilt once per state change by Recompute rather than
    // on every read: a renderer asks for each of them several times per frame.
    private IReadOnlyList<InventoryItemView> _visible = [];
    private IReadOnlyList<InventoryItemView> _visibleHeld = [];
    private IReadOnlyList<InventoryItemView> _selectedViews = [];
    private IReadOnlyList<InventoryItemView> _mergeCandidates = [];
    private IReadOnlyList<InventoryItemView> _mergePickViews = [];
    private IReadOnlyList<string> _inspectedAffixes = [];

    /// <summary>Builds the screen's driver.</summary>
    /// <param name="gameHost">The host every read and submission goes through.</param>
    /// <param name="strings">The device-locale string catalogue.</param>
    /// <param name="content">The loaded content set the projection reads against.</param>
    /// <param name="player">The profile whose stock this is.</param>
    /// <param name="power">Where the hero's power figure — worn and projected — comes from.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public InventoryPresenter(
        IGameHost gameHost,
        LocaleStringCatalogue strings,
        ContentSnapshot content,
        PlayerId player,
        IHeroPowerSource power)
    {
        ArgumentNullException.ThrowIfNull(gameHost);
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(power);

        _gameHost = gameHost;
        _strings = strings;
        _content = content;
        _player = player;
        _power = power;
    }

    // ============================================================================ the read

    /// <summary>How far the read has got.</summary>
    public InventoryStage Stage { get; private set; } = InventoryStage.NotYetRead;

    /// <summary>Whether a submission is outstanding. Every control is disabled while one is.</summary>
    public bool Busy => _submissionInFlight;

    /// <summary>What the rules said about the last submission, or <c>null</c> if they said nothing.</summary>
    public RejectionReason? RulesRejection { get; private set; }

    /// <summary>Whether the last submission never reached the host at all.</summary>
    public bool HostFaulted { get; private set; }

    /// <summary>The items in the bag proper, in the order asked for.</summary>
    public IReadOnlyList<InventoryItemView> Stored => _view?.Stored ?? [];

    /// <summary>The items a full bag is holding. Shown, never equippable.</summary>
    public IReadOnlyList<InventoryItemView> Held => _view?.Held ?? [];

    /// <summary>The sets the worn gear is building.</summary>
    public IReadOnlyList<ActiveSetView> ActiveSets => _view?.ActiveSets ?? [];

    /// <summary>How many items the bag can hold, as authored — see <see cref="ThereIsNoWayToBuyBagSpace"/>.</summary>
    public int Capacity => _view?.Capacity ?? 0;

    /// <summary>The top of the enhancement ladder, for the progress a sheet draws towards it.</summary>
    public int MaxEnhanceLevel => _view?.MaxEnhanceLevel ?? 0;

    /// <summary>The player's Crowns — what a fusion is priced in.</summary>
    public long Crowns => Balance(CurrencyId.CROWNS);

    /// <summary>The player's Enhance Stones — what an enhancement is priced in.</summary>
    public long EnhanceStones => Balance(CurrencyId.ENHANCE_STONES);

    /// <summary>The player's Merge Dust — what stands in for a missing fusion input.</summary>
    public long MergeDust => Balance(CurrencyId.MERGE_DUST);

    /// <summary>The hero's power as worn, or <c>null</c> while unread or unmeasurable.</summary>
    public double? HeroPower => _row is null ? null : _power.Read(_row, null).PowerIndex;

    /// <summary>
    /// The hero's power if the open item were worn instead of what is in its slot, or <c>null</c> when
    /// no equippable item is open or the figure cannot be measured.
    /// </summary>
    /// <remarks>
    /// 🔒 Measured by the same source that measures the worn figure, over the same row with one slot
    /// of the loadout swapped — so the two numbers differ by exactly the item and nothing else. This
    /// is the one comparison a player actually decides by, and a projection that guessed it from the
    /// item's own power would be wrong whenever a set bonus or a cap was in play.
    /// </remarks>
    public double? ProjectedHeroPower
    {
        get
        {
            if (_row is null || Inspected is not { IsEquipped: false } item || !IsStored(item))
            {
                return null;
            }

            var gear = new Dictionary<GearSlot, GearInstanceId>(_row.Loadout?.Gear ?? new Dictionary<GearSlot, GearInstanceId>())
            {
                [item.Slot] = item.InstanceId,
            };

            return _power.Read(_row with { Loadout = new LoadoutSnapshot(gear) }, null).PowerIndex;
        }
    }

    // ============================================================================ the slots

    /// <summary>The three slots down the hero's left, top to bottom.</summary>
    public IReadOnlyList<GearSlot> LeftSlots => LeftSlotColumn;

    /// <summary>The three slots down the hero's right, top to bottom.</summary>
    public IReadOnlyList<GearSlot> RightSlots => RightSlotColumn;

    /// <summary>The item worn in a slot, or <c>null</c> for an empty one.</summary>
    public InventoryItemView? Worn(GearSlot slot) => Stored.FirstOrDefault(item => item.IsEquipped && item.Slot == slot);

    /// <summary>Whether the bag holds something that would out-power what a slot wears — the one badge worth a dot.</summary>
    /// <remarks>
    /// Power alone, and the sheet is where the per-stat truth is: an item can out-power the worn one
    /// and still trade a stat the player values. The badge invites a look; it does not equip.
    /// </remarks>
    public bool UpgradeAvailable(GearSlot slot)
    {
        var worn = Worn(slot);

        return Stored.Any(item =>
            item.Slot == slot && !item.IsEquipped && (worn is null || item.Power > worn.Power));
    }

    /// <summary>Whether an item out-powers what is worn in its slot — the same fact, asked of the item.</summary>
    public bool IsBetterThanWorn(InventoryItemView item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.IsEquipped || !IsStored(item))
        {
            return false;
        }

        var worn = Worn(item.Slot);

        return worn is null || item.Power > worn.Power;
    }

    // ============================================================================ the grid

    /// <summary>Which slot the grid is narrowed to, or <c>null</c> for the whole bag.</summary>
    public GearSlot? SlotFilter { get; private set; }

    /// <summary>The ordering the grid is in. Power first, because that is the question a player brings to the bag.</summary>
    public InventorySortKey Order { get; private set; } = InventorySortKey.POWER;

    /// <summary>Whether a tap on a cell opens it or picks it.</summary>
    public InventoryMode Mode { get; private set; } = InventoryMode.Browse;

    /// <summary>The stored items the grid draws: the whole bag, or one slot of it.</summary>
    public IReadOnlyList<InventoryItemView> Visible => _visible;

    /// <summary>The held items the grid draws below the stock, under the same filter.</summary>
    public IReadOnlyList<InventoryItemView> VisibleHeld => _visibleHeld;

    /// <summary>Narrows the grid to one slot. Tapping a worn slot also opens what it wears.</summary>
    /// <param name="slot">The slot tapped.</param>
    public void TapSlot(GearSlot slot)
    {
        SlotFilter = slot;

        if (Mode == InventoryMode.Browse && Worn(slot) is { } worn)
        {
            Open(worn);
        }

        Recompute();
    }

    /// <summary>Widens the grid back to the whole bag.</summary>
    public void ClearSlotFilter()
    {
        SlotFilter = null;
        Recompute();
    }

    /// <summary>Moves the grid to the next ordering in the cycle and re-reads the stock in it.</summary>
    /// <param name="ct">Cancelled when the application shuts down.</param>
    public Task CycleOrderAsync(CancellationToken ct)
    {
        var at = Array.IndexOf(SortCycle, Order);

        Order = SortCycle[(at + 1) % SortCycle.Length];

        return _row is null ? Task.CompletedTask : StartAsync(ct);
    }

    /// <summary>The sort control's caption: the word "Sort" and the ordering's own name.</summary>
    public string SortText =>
        _strings.Resolve(SortLabelKey) + Space +
        _strings.Resolve(SortNameKeyPrefix + Order.ToString().ToLowerInvariant() + NameKeySuffix);

    /// <summary>The removable chip naming the slot the grid is narrowed to, or empty.</summary>
    public string SlotFilterText => SlotFilter is { } slot ? SlotName(slot) : NothingLeftToSay;

    /// <summary>The badge on a cell for its enhancement, or empty at +0.</summary>
    public static string EnhanceBadge(InventoryItemView item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return item.EnhanceLevel == 0 ? NothingLeftToSay : EnhancePrefix + Count(item.EnhanceLevel);
    }

    /// <summary>How many unlocked copies the bag holds that a fusion could take with this one, itself included.</summary>
    public int MergeableCopies(InventoryItemView item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return item.Locked || item.Merge.OutputRarity is null
            ? 0
            : Stored.Count(other => other.Merge.Key == item.Merge.Key && !other.Locked);
    }

    // ============================================================================ the sheet

    /// <summary>Which page the sheet is on, or that it is closed.</summary>
    public GearSheetPage Sheet { get; private set; } = GearSheetPage.Closed;

    /// <summary>The item the sheet is about, re-resolved against the latest read so it is never stale.</summary>
    public InventoryItemView? Inspected =>
        _inspected is { } id
            ? Stored.Concat(Held).FirstOrDefault(item => item.InstanceId == id)
            : null;

    /// <summary>Opens the sheet on one item.</summary>
    /// <param name="item">The item tapped.</param>
    public void Open(InventoryItemView item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (Mode == InventoryMode.Select)
        {
            ToggleSelected(item);

            return;
        }

        _inspected = item.InstanceId;
        Sheet = GearSheetPage.Details;
        _mergePicks.Clear();
        Recompute();
    }

    /// <summary>Closes the sheet, whatever page it is on.</summary>
    public void CloseSheet()
    {
        Sheet = GearSheetPage.Closed;
        _inspected = null;
        _mergePicks.Clear();
        Recompute();
    }

    /// <summary>Turns the sheet to the enhance page.</summary>
    public void ShowEnhance()
    {
        if (Inspected is { Enhance: not null } && IsStored(Inspected))
        {
            Sheet = GearSheetPage.Enhance;
        }
    }

    /// <summary>Turns the sheet to the merge page, with the two inputs picked for the player.</summary>
    public void ShowMerge()
    {
        if (Inspected is not { } keeper || !MergeIsConceivable(keeper))
        {
            return;
        }

        Sheet = GearSheetPage.Merge;
        _mergePicks.Clear();
        Recompute();

        // 🔒 Only UNWORN copies are picked on the player's behalf. A worn copy is still a candidate —
        // it can be tapped in — but a fusion that quietly undressed the hero is the one every reference
        // game gets complained about, so it is never the screen's own choice.
        _mergePicks.AddRange(
            MergeCandidates
                .Where(candidate => !candidate.IsEquipped)
                .Take(_view!.MergeInputCount - 1)
                .Select(candidate => candidate.InstanceId));
        Recompute();
    }

    /// <summary>Turns the sheet back to the item's details.</summary>
    public void BackToDetails()
    {
        if (Sheet != GearSheetPage.Closed)
        {
            Sheet = GearSheetPage.Details;
        }
    }

    /// <summary>What the open item is called: its band and its family, and the rung it stands on.</summary>
    public string InspectedTitle => Inspected is { } item ? ItemTitle(item) : NothingLeftToSay;

    /// <summary>The line under the title: the slot, the chapter it dropped in, and how well it rolled.</summary>
    public string InspectedCaption =>
        Inspected is { } item
            ? SlotName(item.Slot) + OfSeparator +
              _strings.Resolve(ChapterOriginLabelKey) + Space + Count(item.ChapterOrigin) + OfSeparator +
              _strings.Resolve(QualityLabelKey) + Space + Percent(item.Quality)
            : NothingLeftToSay;

    /// <summary>The open item's power, written.</summary>
    public string InspectedPowerText => Inspected is { } item ? Flat(item.Power) : NothingLeftToSay;

    /// <summary>The open item's power against the worn item's, with its arrow and sign, or empty when there is nothing to compare.</summary>
    public string InspectedPowerDelta
    {
        get
        {
            if (Inspected is not { IsEquipped: false } item || !IsStored(item))
            {
                return NothingLeftToSay;
            }

            var worn = Worn(item.Slot);

            return DeltaText(item.Power - (worn?.Power ?? 0.0), isPercent: false);
        }
    }

    /// <summary>The open item's stat block: each stat's figure and its comparison against what is worn.</summary>
    public IReadOnlyList<GearStatLine> InspectedStats
    {
        get
        {
            if (Inspected is not { } item)
            {
                return [];
            }

            var lines = new GearStatLine[item.Stats.Count];

            for (var index = 0; index < lines.Length; index++)
            {
                var stat = item.Stats[index];
                var delta = item.Deltas.FirstOrDefault(d => d.Stat == stat.Stat);

                lines[index] = new GearStatLine(
                    StatName(stat.Stat),
                    Figure(stat.Value, stat.IsPercent),
                    delta is null ? NothingLeftToSay : DeltaText(delta.Delta, delta.IsPercent),
                    delta is null ? 0 : Math.Sign(delta.Delta));
            }

            return lines;
        }
    }

    /// <summary>The open item's affixes, each written as its name and its roll.</summary>
    public IReadOnlyList<string> InspectedAffixes => _inspectedAffixes;

    /// <summary>The set line: the set the open item counts towards and how many pieces are worn, or empty below SS.</summary>
    public string InspectedSetText
    {
        get
        {
            if (Inspected is not { Rarity: Rarity.SS } item || _view is null)
            {
                return NothingLeftToSay;
            }

            var active = ActiveSets.FirstOrDefault(set => set.Set == item.Axis);
            var pieces = active?.Pieces ?? 0;
            var name = active is not null ? _strings.Resolve(active.NameKey) : NothingLeftToSay;

            return _strings.Resolve(SetLabelKey) + Space + name + OfSeparator +
                   _strings.Resolve(SetPiecesLabelKey)
                       .Replace(CountToken, Count(pieces), StringComparison.Ordinal) +
                   Space + string.Join(OfSeparator, _view.SetBreakpoints.Select(pieces => Count(pieces)));
        }
    }

    /// <summary>The enhancement progress: the rung over the ceiling.</summary>
    public string InspectedEnhanceText =>
        Inspected is { } item
            ? EnhancePrefix + Count(item.EnhanceLevel) + OfSeparator + EnhancePrefix + Count(MaxEnhanceLevel)
            : NothingLeftToSay;

    /// <summary>The badges on the open item, resolved: worn, locked, held — whichever apply.</summary>
    public IReadOnlyList<string> InspectedBadges
    {
        get
        {
            if (Inspected is not { } item)
            {
                return [];
            }

            var badges = new List<string>(3);

            if (item.IsEquipped)
            {
                badges.Add(EquippedBadge);
            }

            if (item.Locked)
            {
                badges.Add(LockedBadge);
            }

            if (!IsStored(item))
            {
                badges.Add(HeldBadge);
            }

            return badges;
        }
    }

    // ---- what the details page may do ----------------------------------------------------------

    /// <summary>Whether the open item can be put on: stored, not already worn, and the screen able to submit.</summary>
    public bool CanEquip => CanSubmit && Inspected is { IsEquipped: false } item && IsStored(item);

    /// <summary>Whether the open item can be taken off.</summary>
    public bool CanUnequip => CanSubmit && Inspected is { IsEquipped: true };

    /// <summary>Whether the enhance page can be opened: the item is stored and not at the ceiling.</summary>
    public bool CanShowEnhance => Inspected is { Enhance: not null } item && IsStored(item);

    /// <summary>Whether the merge page can be opened: the item is stored, unlocked, and below the top band.</summary>
    public bool CanShowMerge => Inspected is { } item && MergeIsConceivable(item);

    /// <summary>Whether the open item can go into a salvage batch: stored and unlocked.</summary>
    public bool CanSalvageInspected => Inspected is { Locked: false } item && IsStored(item);

    /// <summary>Whether the lock can be toggled: only a stored item has a lock the rules will move.</summary>
    public bool CanToggleLock => CanSubmit && Inspected is { } item && IsStored(item);

    /// <summary>The sentence beside a details action that is not live, or empty when every action is.</summary>
    public string InspectedBlockText
    {
        get
        {
            if (Inspected is not { } item)
            {
                return NothingLeftToSay;
            }

            if (!IsStored(item))
            {
                return _strings.Resolve(HeldNotEquippableBlockKey);
            }

            if (item.Locked)
            {
                return _strings.Resolve(LockedNoForgeBlockKey);
            }

            if (item.Merge.OutputRarity is null)
            {
                return _strings.Resolve(TopBandNoMergeBlockKey);
            }

            return NothingLeftToSay;
        }
    }

    /// <summary>The lock control's caption: the verb that would change the item's state.</summary>
    public string LockActionText =>
        _strings.Resolve(Inspected is { Locked: true } ? UnlockActionKey : LockActionKey);

    // ---- the enhance page ----------------------------------------------------------------------

    /// <summary>What the next rung would make of the item, stat by stat: the figure now and the figure after.</summary>
    public IReadOnlyList<GearStatLine> EnhanceStats
    {
        get
        {
            if (Inspected is not { Enhance: { } preview } item)
            {
                return [];
            }

            var lines = new GearStatLine[item.Stats.Count];

            for (var index = 0; index < lines.Length; index++)
            {
                var now = item.Stats[index];
                var after = preview.NextStats[index];

                lines[index] = new GearStatLine(
                    StatName(now.Stat),
                    Figure(now.Value, now.IsPercent),
                    DeltaText(after.Value - now.Value, now.IsPercent),
                    Math.Sign(after.Value - now.Value));
            }

            return lines;
        }
    }

    /// <summary>The rung the attempt would reach over the ceiling, or the line that says the item is maxed.</summary>
    public string EnhanceTargetText =>
        Inspected is { Enhance: { } preview }
            ? EnhancePrefix + Count(preview.NextLevel) + OfSeparator + EnhancePrefix + Count(MaxEnhanceLevel)
            : _strings.Resolve(EnhanceMaxedLabelKey);

    /// <summary>The stones the attempt costs against the stones held.</summary>
    public string EnhanceCostText =>
        Inspected is { Enhance: { } preview }
            ? _strings.Resolve(EnhanceCostLabelKey) + Space + Count(preview.StoneCost) + OverSeparator + Count(EnhanceStones)
            : NothingLeftToSay;

    /// <summary>The odds the attempt lands, mercy included — always a real number, never a hue.</summary>
    public string EnhanceOddsText =>
        Inspected is { Enhance: { } preview }
            ? _strings.Resolve(EnhanceOddsLabelKey) + Space + Percent(preview.SuccessRate)
            : NothingLeftToSay;

    /// <summary>Whether the attempt can be paid for.</summary>
    public bool EnhanceAffordable => Inspected is { Enhance: { } preview } && EnhanceStones >= preview.StoneCost;

    /// <summary>Whether the enhance control is live.</summary>
    public bool CanEnhance => CanSubmit && CanShowEnhance && EnhanceAffordable;

    /// <summary>The sentence beside a dead enhance control, or empty.</summary>
    public string EnhanceBlockText =>
        Inspected is { Enhance: { } } && !EnhanceAffordable ? _strings.Resolve(NotEnoughStonesBlockKey) : NothingLeftToSay;

    // ---- the merge page ------------------------------------------------------------------------

    /// <summary>
    /// The other unlocked copies a fusion could take, best fodder first: unworn before worn, then the
    /// weaker roll before the better one.
    /// </summary>
    /// <remarks>
    /// 🔒 Worn and better-rolled copies are never picked first and never silently. A fusion keeps the
    /// keeper's identity and re-rolls its affixes, so what is consumed is gone — the ordering spends
    /// the copies the player will miss least, and <see cref="MergeWarnings"/> says so when a pick
    /// reaches past them.
    /// </remarks>
    public IReadOnlyList<InventoryItemView> MergeCandidates => _mergeCandidates;

    /// <summary>The inputs picked to go under the keeper, in pick order.</summary>
    public IReadOnlyList<InventoryItemView> MergePicks => _mergePickViews;

    /// <summary>Whether a candidate is currently picked.</summary>
    public bool IsMergePick(InventoryItemView item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return _mergePicks.Contains(item.InstanceId);
    }

    /// <summary>Picks a candidate, or drops it if it was picked. A third pick replaces the oldest.</summary>
    /// <param name="item">The candidate tapped.</param>
    public void ToggleMergePick(InventoryItemView item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (Sheet != GearSheetPage.Merge || _view is null || !MergeCandidates.Any(c => c.InstanceId == item.InstanceId))
        {
            return;
        }

        if (!_mergePicks.Remove(item.InstanceId))
        {
            if (_mergePicks.Count >= _view.MergeInputCount - 1)
            {
                _mergePicks.RemoveAt(0);
            }

            _mergePicks.Add(item.InstanceId);
        }

        Recompute();
    }

    /// <summary>Whether Merge Dust has to stand in for a missing input — one short of the three, and only one.</summary>
    public bool MergeNeedsDust =>
        _view is not null && Inspected is { Merge.DustSubstituteCost: not null } &&
        MergePicks.Count == _view.MergeInputCount - 2;

    /// <summary>The Crowns the fusion costs against the Crowns held.</summary>
    public string MergeCostText =>
        Inspected is { } keeper
            ? _strings.Resolve(MergeCostLabelKey) + Space + Count(keeper.Merge.CrownCost) + OverSeparator + Count(Crowns)
            : NothingLeftToSay;

    /// <summary>The Merge Dust a substitute input costs against the dust held, or empty when no substitute is needed.</summary>
    public string MergeDustText =>
        MergeNeedsDust && Inspected is { Merge.DustSubstituteCost: { } dust }
            ? _strings.Resolve(MergeDustLabelKey) + Space + Count(dust) + OverSeparator + Count(MergeDust)
            : NothingLeftToSay;

    /// <summary>What the fusion would make: the next band's name, the family, and the rung it keeps.</summary>
    public string MergeResultText =>
        Inspected is { Merge.OutputRarity: { } band } keeper
            ? _strings.Resolve(MergeResultLabelKey) + Space + ItemTitle(band, keeper.Family, keeper.EnhanceLevel)
            : NothingLeftToSay;

    /// <summary>Whether every input is picked and every price is payable.</summary>
    public bool CanMerge =>
        CanSubmit && _view is not null && Inspected is { } keeper && MergeIsConceivable(keeper) &&
        KeeperAndPicks + (MergeNeedsDust ? 1 : 0) == _view.MergeInputCount &&
        Crowns >= keeper.Merge.CrownCost &&
        (!MergeNeedsDust || MergeDust >= keeper.Merge.DustSubstituteCost);

    /// <summary>The sentences a player should read before fusing: what is missing, and what would be lost.</summary>
    public IReadOnlyList<string> MergeWarnings
    {
        get
        {
            if (_view is null || Inspected is not { } keeper)
            {
                return [];
            }

            var warnings = new List<string>();
            var picks = MergePicks;

            if (KeeperAndPicks + (MergeNeedsDust ? 1 : 0) < _view.MergeInputCount)
            {
                warnings.Add(_strings.Resolve(MergeNeedsInputsBlockKey));
            }

            if (Crowns < keeper.Merge.CrownCost)
            {
                warnings.Add(_strings.Resolve(NotEnoughCrownsBlockKey));
            }

            if (MergeNeedsDust && MergeDust < keeper.Merge.DustSubstituteCost)
            {
                warnings.Add(_strings.Resolve(NotEnoughDustBlockKey));
            }

            if (picks.Any(pick => pick.IsEquipped))
            {
                warnings.Add(_strings.Resolve(MergeConsumesWornBlockKey));
            }

            if (picks.Any(pick => pick.Quality > keeper.Quality))
            {
                warnings.Add(_strings.Resolve(MergeConsumesBetterRollBlockKey));
            }

            if (keeper.Affixes.Count > 0 || picks.Any(pick => pick.Affixes.Count > 0))
            {
                warnings.Add(_strings.Resolve(MergeRerollsAffixesBlockKey));
            }

            return warnings;
        }
    }

    // ============================================================================ select mode

    /// <summary>Puts the grid into select mode, where a tap picks an item for salvage.</summary>
    public void EnterSelectMode()
    {
        CloseSheet();
        Mode = InventoryMode.Select;
        Recompute();
    }

    /// <summary>Leaves select mode and forgets the batch.</summary>
    public void LeaveSelectMode()
    {
        Mode = InventoryMode.Browse;
        SalvageConfirmPending = false;
        ClearSelection();
    }

    /// <summary>Whether an item may go into the batch: stored, unlocked and not worn.</summary>
    /// <remarks>
    /// Worn items are kept out of the BATCH rather than merely warned about: a batch is picked by band
    /// with one tap, and a player who quick-picks every Common should not have to notice that one of
    /// them is on the hero. A single worn item can still be salvaged from its own sheet, where it is
    /// the only thing on the page.
    /// </remarks>
    public bool IsSelectable(InventoryItemView item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return IsStored(item) && !item.Locked && !item.IsEquipped;
    }

    /// <summary>Whether an item is in the batch.</summary>
    public bool IsSelected(InventoryItemView item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return _selected.Contains(item.InstanceId);
    }

    /// <summary>Adds an item to the batch, or takes it out again.</summary>
    /// <param name="item">The item tapped.</param>
    public void ToggleSelected(InventoryItemView item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (!IsSelectable(item))
        {
            return;
        }

        if (_selected.Remove(item.InstanceId))
        {
            _selectionOrder.Remove(item.InstanceId);
        }
        else
        {
            _selected.Add(item.InstanceId);
            _selectionOrder.Add(item.InstanceId);
        }

        Recompute();
    }

    /// <summary>The bands a quick-pick gem exists for.</summary>
    public IReadOnlyList<Rarity> QuickPickRarities => QuickPickBands;

    /// <summary>Adds every selectable item of one band in the visible grid to the batch.</summary>
    /// <param name="rarity">The band.</param>
    public void SelectBand(Rarity rarity)
    {
        foreach (var item in Visible.Where(item => item.Rarity == rarity && IsSelectable(item) && !IsSelected(item)))
        {
            ToggleSelected(item);
        }
    }

    /// <summary>Empties the batch.</summary>
    public void ClearSelection()
    {
        _selected.Clear();
        _selectionOrder.Clear();
        Recompute();
    }

    /// <summary>The batch, in the order it was picked.</summary>
    public IReadOnlyList<InventoryItemView> Selected => _selectedViews;

    /// <summary>How many items are in the batch.</summary>
    public int SelectedCount => Selected.Count;

    /// <summary>The Merge Dust the batch would pay.</summary>
    public long SelectedDust => Selected.Sum(item => item.Salvage.Dust);

    /// <summary>The Enhance Stones the batch would refund.</summary>
    public long SelectedStones => Selected.Sum(item => item.Salvage.Stones);

    /// <summary>The batch's summary: how many picked, and what they would pay.</summary>
    public string SalvageSummaryText =>
        _strings.Resolve(SelectedCountLabelKey).Replace(CountToken, Count(SelectedCount), StringComparison.Ordinal) +
        OfSeparator + _strings.Resolve(SalvageReturnsLabelKey)
            .Replace(DustToken, Count(SelectedDust), StringComparison.Ordinal)
            .Replace(StonesToken, Count(SelectedStones), StringComparison.Ordinal);

    /// <summary>Whether the confirmation is up: the batch is named and awaits the second tap.</summary>
    public bool SalvageConfirmPending { get; private set; }

    /// <summary>Asks for the second tap on the batch picked in select mode.</summary>
    public void RequestSalvage()
    {
        if (SelectedCount > 0)
        {
            SalvageConfirmPending = true;
        }
    }

    /// <summary>Asks for the second tap on the one item the sheet is open on.</summary>
    /// <remarks>
    /// The single-item path goes through the same batch and the same confirmation as select mode, so
    /// there is one salvage flow with one set of warnings — and a worn item, which the batch never
    /// admits, is admitted here deliberately because it is the only item on the page.
    /// </remarks>
    public void RequestSalvageInspected()
    {
        if (!CanSalvageInspected || Inspected is not { } item)
        {
            return;
        }

        ClearSelection();
        _selected.Add(item.InstanceId);
        _selectionOrder.Add(item.InstanceId);
        SalvageConfirmPending = true;
        Recompute();
    }

    /// <summary>Takes the confirmation down without salvaging.</summary>
    public void CancelSalvage()
    {
        SalvageConfirmPending = false;

        if (Mode == InventoryMode.Browse)
        {
            ClearSelection();
        }
    }

    /// <summary>The sentences a player should read before the second tap.</summary>
    public IReadOnlyList<string> SalvageWarnings
    {
        get
        {
            var batch = Selected;
            var warnings = new List<string>();

            if (batch.Count == 0)
            {
                warnings.Add(_strings.Resolve(SalvageNothingSelectableBlockKey));

                return warnings;
            }

            if (batch.Any(item => item.IsEquipped))
            {
                warnings.Add(_strings.Resolve(MergeConsumesWornBlockKey));
            }

            var epic = batch.Count(item => item.Rarity >= Rarity.A);

            if (epic > 0)
            {
                warnings.Add(_strings.Resolve(SalvageIncludesEpicBlockKey)
                    .Replace(CountToken, Count(epic), StringComparison.Ordinal));
            }

            var enhanced = batch.Count(item => item.EnhanceLevel > 0);

            if (enhanced > 0)
            {
                warnings.Add(_strings.Resolve(SalvageIncludesEnhancedBlockKey)
                    .Replace(CountToken, Count(enhanced), StringComparison.Ordinal));
            }

            return warnings;
        }
    }

    /// <summary>Whether the confirmed salvage may be sent.</summary>
    public bool CanSalvage => CanSubmit && SalvageConfirmPending && SelectedCount > 0;

    // ============================================================================ captions

    /// <summary>The screen's heading, resolved.</summary>
    public string Title => _strings.Resolve(TitleNameKey);

    /// <summary>The caption the bag's occupancy is drawn beside, resolved.</summary>
    public string CapacityLabel => _strings.Resolve(CapacityLabelKey);

    /// <summary>
    /// The occupancy itself — what the bag holds over what it can hold.
    /// </summary>
    /// <remarks>
    /// 🔒 Held items are counted in, and that is the honest reading: <c>08</c> §5's overflow exists
    /// because the bag is FULL, so a figure that excluded them would read as room the player does not
    /// have. It is why the number can exceed the ceiling, and why the ceiling is shown beside it.
    /// </remarks>
    public string CapacityValue => Count(Stored.Count + Held.Count) + OverSeparator + Count(Capacity);

    /// <summary>The heading over the overflow band, resolved. Empty when nothing is held.</summary>
    public string HeldLabel => VisibleHeld.Count == 0 ? NothingLeftToSay : _strings.Resolve(HeldLabelKey);

    /// <summary>The caption beside the hero's power figure.</summary>
    public string HeroPowerLabel => _strings.Resolve(HeroPowerLabelKey);

    /// <summary>The caption beside an item's power figure.</summary>
    public string PowerLabel => _strings.Resolve(PowerLabelKey);

    /// <summary>The caption over an item's affixes.</summary>
    public string AffixesLabel => _strings.Resolve(AffixesLabelKey);

    /// <summary>The caption over the fusion's keeper.</summary>
    public string MergeKeeperLabel => _strings.Resolve(MergeKeeperLabelKey);

    /// <summary>The caption over the fusion's inputs.</summary>
    public string MergeInputsLabel => _strings.Resolve(MergeInputsLabelKey);

    /// <summary>The caption over the quick-pick gems.</summary>
    public string QuickPickLabel => _strings.Resolve(QuickPickLabelKey);

    /// <summary>What an empty slot tile says.</summary>
    public string EmptySlotText => _strings.Resolve(EmptySlotLabelKey);

    /// <summary>The mark on the item worn in a slot, resolved.</summary>
    public string EquippedBadge => _strings.Resolve(EquippedBadgeKey);

    /// <summary>The mark on a locked item, resolved.</summary>
    public string LockedBadge => _strings.Resolve(LockedBadgeKey);

    /// <summary>The mark on an item the bag is holding, resolved.</summary>
    public string HeldBadge => _strings.Resolve(HeldBadgeKey);

    /// <summary>The mark on a slot whose bag holds something better, resolved.</summary>
    public string UpgradeBadge => _strings.Resolve(UpgradeBadgeKey);

    /// <summary>The sentence on a held item's card saying why it cannot be worn, resolved.</summary>
    public string HeldNotEquippableText => _strings.Resolve(HeldNotEquippableBlockKey);

    /// <summary>The equip control's caption, resolved.</summary>
    public string EquipText => _strings.Resolve(EquipActionKey);

    /// <summary>The unequip control's caption, resolved.</summary>
    public string UnequipText => _strings.Resolve(UnequipActionKey);

    /// <summary>The control that opens the enhance page, resolved.</summary>
    public string EnhanceText => _strings.Resolve(EnhanceActionKey);

    /// <summary>The control that makes one attempt, resolved.</summary>
    public string EnhanceOnceText => _strings.Resolve(EnhanceOnceActionKey);

    /// <summary>The control that opens the merge page, resolved.</summary>
    public string MergeText => _strings.Resolve(MergeActionKey);

    /// <summary>The control that fuses, resolved.</summary>
    public string MergeConfirmText => _strings.Resolve(MergeConfirmActionKey);

    /// <summary>The control that asks for a salvage, resolved.</summary>
    public string SalvageText => _strings.Resolve(SalvageActionKey);

    /// <summary>The control that confirms a salvage, resolved.</summary>
    public string SalvageConfirmText => _strings.Resolve(SalvageConfirmActionKey);

    /// <summary>The control that enters select mode, resolved.</summary>
    public string SelectText => _strings.Resolve(SelectActionKey);

    /// <summary>The control that backs out of a page or a mode, resolved.</summary>
    public string CancelText => _strings.Resolve(CancelActionKey);

    /// <summary>The control that turns the sheet back a page, resolved.</summary>
    public string BackText => _strings.Resolve(BackActionKey);

    /// <summary>The way back off the screen, resolved.</summary>
    public string CloseText => _strings.Resolve(CloseActionKey);

    /// <summary>The control that widens a narrowed grid, resolved.</summary>
    public string ClearFilterText => _strings.Resolve(ClearFilterActionKey);

    /// <summary>The line for a destination this build has no screen for, borrowed from Home.</summary>
    public string NotOpenYetNotice => _strings.Resolve(NotOpenYetStatusKey);

    /// <summary>One tab's caption — the same words Home's bar uses, so the bar reads the same on both.</summary>
    /// <param name="tab">The tab asked about.</param>
    /// <exception cref="ArgumentOutOfRangeException">A tab this screen has no caption for.</exception>
    public string TabLabel(HomeTab tab) => _strings.Resolve(tab switch
    {
        HomeTab.Home => TabHomeLabelKey,
        HomeTab.Gear => TabGearLabelKey,
        HomeTab.Talents => TabTalentsLabelKey,
        HomeTab.Collection => TabCollectionLabelKey,
        HomeTab.Shop => TabShopLabelKey,
        _ => throw new ArgumentOutOfRangeException(
            nameof(tab), tab, "this tab has no caption. Author one before the bar carries it."),
    });

    /// <summary>A slot's name, resolved.</summary>
    public string SlotName(GearSlot slot) =>
        _strings.Resolve(SlotNameKeyPrefix + slot.ToString().ToLowerInvariant() + NameKeySuffix);

    /// <summary>A family's name, resolved.</summary>
    public string FamilyName(GearFamily family) =>
        _strings.Resolve(FamilyNameKeyPrefix + family.ToString().ToLowerInvariant() + NameKeySuffix);

    /// <summary>A band's name, resolved.</summary>
    public string RarityName(Rarity rarity) =>
        _strings.Resolve(RarityNameKeyPrefix + rarity.ToString().ToLowerInvariant() + NameKeySuffix);

    /// <summary>A stat's name, resolved.</summary>
    public string StatName(string stat) =>
        _strings.Resolve(StatNameKeyPrefix + (stat ?? string.Empty).ToLowerInvariant() + NameKeySuffix);

    /// <summary>What an item is called: its band and its family, and its rung when it has one.</summary>
    public string ItemTitle(InventoryItemView item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return ItemTitle(item.Rarity, item.Family, item.EnhanceLevel);
    }

    /// <summary>The one line a player reads for the state the screen is in, resolved.</summary>
    public string StatusText => Stage switch
    {
        InventoryStage.NotYetRead => _strings.Resolve(LoadingStatusKey),
        InventoryStage.Ready => Visible.Count + VisibleHeld.Count == 0 && SlotFilter is not null
            ? _strings.Resolve(FilterEmptyStatusKey)
            : NothingLeftToSay,
        InventoryStage.Empty => _strings.Resolve(EmptyStatusKey),
        _ => _strings.Resolve(UnavailableStatusKey),
    };

    /// <summary>The line about the last command the host answered, resolved — empty until one has.</summary>
    public string RejectionText => HostFaulted
        ? _strings.Resolve(HostUnavailableStatusKey)
        : RulesRejection switch
        {
            null => NothingLeftToSay,
            RejectionReason.INSUFFICIENT_FUNDS => _strings.Resolve(RefusedFundsStatusKey),
            RejectionReason.BATTLE_IN_PROGRESS => _strings.Resolve(RefusedBattleStatusKey),
            RejectionReason.NOT_OWNED => _strings.Resolve(RefusedNotOwnedStatusKey),
            _ => _strings.Resolve(RefusedStatusKey),
        };

    /// <summary>The one line the last successful command left behind, or empty.</summary>
    public string Notice { get; private set; } = NothingLeftToSay;

    /// <summary>What the notice is about, for the accent it is drawn in.</summary>
    public InventoryNoticeKind NoticeKind { get; private set; } = InventoryNoticeKind.None;

    /// <summary>Takes the notice down.</summary>
    public void ClearNotice()
    {
        Notice = NothingLeftToSay;
        NoticeKind = InventoryNoticeKind.None;
    }

    /// <summary>Puts up the line for a destination this build has no screen for.</summary>
    public void AcknowledgeNotOpenYet()
    {
        Notice = NotOpenYetNotice;
        NoticeKind = InventoryNoticeKind.Plain;
    }

    // ============================================================================ the read

    /// <summary>Reads the stock and settles what the screen draws.</summary>
    /// <param name="ct">Cancelled when the application shuts down.</param>
    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            Settle(await _gameHost.ReadOwnStateAsync(_player, null, ct).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // A read that faulted answered nothing, and a screen with nothing to draw says so rather
            // than drawing a stock it did not see.
            Unreadable();
        }
    }

    private void Settle(OwnStateResult state)
    {
        if (state.Lookup != OwnStateLookup.Found || state.View?.Player is not { } player)
        {
            Unreadable();

            return;
        }

        try
        {
            _row = player;
            _view = InventoryView.Project(player, _content, Order);
            Stage = Stored.Count + Held.Count == 0 ? InventoryStage.Empty : InventoryStage.Ready;
        }
        catch (ContentException)
        {
            // A content set that cannot answer is a state, not an exception to leak: the read itself
            // succeeded, so the screen says the stock could not be read rather than that the player
            // could not be found.
            Unreadable();

            return;
        }
        catch (ArgumentException)
        {
            // A row the domain will not rehydrate is the same state from the screen's side.
            Unreadable();

            return;
        }

        Prune();
    }

    private void Unreadable()
    {
        Stage = InventoryStage.ReadUnavailable;
        _row = null;
        _view = null;
        Prune();
    }

    /// <summary>Forgets every identity a fresh read no longer holds, so nothing points at a freed item.</summary>
    private void Prune()
    {
        var known = Stored.Concat(Held).Select(item => item.InstanceId).ToHashSet();

        if (_inspected is { } inspected && !known.Contains(inspected))
        {
            CloseSheet();
        }

        _mergePicks.RemoveAll(id => !known.Contains(id));

        foreach (var gone in _selectionOrder.Where(id => !known.Contains(id)).ToArray())
        {
            _selected.Remove(gone);
            _selectionOrder.Remove(gone);
        }

        if (_selected.Count == 0)
        {
            SalvageConfirmPending = false;
        }

        Recompute();
    }

    /// <summary>Rebuilds every derived list from the state as it now stands.</summary>
    private void Recompute()
    {
        _visible = SlotFilter is { } slot ? Stored.Where(item => item.Slot == slot).ToArray() : Stored;
        _visibleHeld = SlotFilter is { } heldSlot ? Held.Where(item => item.Slot == heldSlot).ToArray() : Held;

        var byId = Stored.ToDictionary(item => item.InstanceId);

        _selectedViews = _selectionOrder
            .Where(byId.ContainsKey)
            .Select(id => byId[id])
            .ToArray();

        var keeper = Inspected;

        _mergeCandidates = keeper is null
            ? []
            : Stored
                .Where(item => item.Merge.Key == keeper.Merge.Key &&
                               item.InstanceId != keeper.InstanceId &&
                               !item.Locked)
                .OrderBy(item => item.IsEquipped ? 1 : 0)
                .ThenBy(item => item.Quality)
                .ThenBy(item => item.Power)
                .ToArray();

        _mergePickViews = _mergePicks.Where(byId.ContainsKey).Select(id => byId[id]).ToArray();

        _inspectedAffixes = keeper is null
            ? []
            : keeper.Affixes
                .Select(affix => _strings.Resolve(affix.NameKey) + Space + Signed(affix.Value, affix.IsPercent))
                .ToArray();
    }

    // ============================================================================ the commands

    /// <summary>Submits <c>EQUIP</c> for one owned item into its own slot.</summary>
    /// <remarks>
    /// 🔒 The slot comes off the ITEM, never from the caller. An item's slot is a fact about the item
    /// (<c>08</c> §1's six slots × four families), so a screen that passed one would be able to ask for
    /// a blade in a boot slot — which the rules layer refuses, but only after a round trip that told the
    /// player their tap was wrong when the screen had all it needed to know it never was.
    /// </remarks>
    /// <param name="item">The item to wear.</param>
    /// <param name="ct">Cancelled when the application shuts down.</param>
    /// <exception cref="ArgumentNullException"><paramref name="item"/> is null.</exception>
    public Task<InventorySubmission> EquipAsync(InventoryItemView item, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(item);

        // 🔒 Refused here rather than sent: an item in overflow cannot be worn, EQUIP answers
        // INVENTORY_FULL, and the card already carries that sentence. Spending a round trip to be told
        // what the screen knew is how a player learns to distrust the card.
        if (!IsStored(item) || item.IsEquipped)
        {
            return Task.FromResult(InventorySubmission.RefusedNotAvailable);
        }

        return SubmitAsync(
            new EquipCommand(item.InstanceId, item.Slot),
            _ => (Say(EquippedStatusKey, item), InventoryNoticeKind.Gain),
            ct);
    }

    /// <summary>Submits <c>EQUIP</c> for the open item.</summary>
    /// <param name="ct">Cancelled when the application shuts down.</param>
    public Task<InventorySubmission> EquipInspectedAsync(CancellationToken ct) =>
        Inspected is { } item ? EquipAsync(item, ct) : Task.FromResult(InventorySubmission.RefusedNotAvailable);

    /// <summary>Submits <c>UNEQUIP</c> for the slot the open item is worn in.</summary>
    /// <param name="ct">Cancelled when the application shuts down.</param>
    public Task<InventorySubmission> UnequipInspectedAsync(CancellationToken ct)
    {
        if (Inspected is not { IsEquipped: true } item)
        {
            return Task.FromResult(InventorySubmission.RefusedNotAvailable);
        }

        return SubmitAsync(
            new UnequipCommand(item.Slot),
            _ => (Say(UnequippedStatusKey, item), InventoryNoticeKind.Plain),
            ct);
    }

    /// <summary>Submits <c>LOCK_ITEM</c> to flip the open item's lock.</summary>
    /// <param name="ct">Cancelled when the application shuts down.</param>
    public Task<InventorySubmission> ToggleLockInspectedAsync(CancellationToken ct)
    {
        if (Inspected is not { } item || !IsStored(item))
        {
            return Task.FromResult(InventorySubmission.RefusedNotAvailable);
        }

        var locking = !item.Locked;

        return SubmitAsync(
            new LockItemCommand(item.InstanceId, locking),
            _ => (Say(locking ? LockedStatusKey : UnlockedStatusKey, item), InventoryNoticeKind.Plain),
            ct);
    }

    /// <summary>Submits one <c>ENHANCE</c> attempt on the open item, and reads whether it landed off the charge's reason.</summary>
    /// <param name="ct">Cancelled when the application shuts down.</param>
    public Task<InventorySubmission> EnhanceInspectedAsync(CancellationToken ct)
    {
        if (!CanEnhance || Inspected is not { Enhance: { } preview } item)
        {
            return Task.FromResult(InventorySubmission.RefusedNotAvailable);
        }

        return SubmitAsync(
            new EnhanceCommand(item.InstanceId),
            events =>
            {
                var landed = events.OfType<CurrencyChanged>()
                    .Any(change => change.Reason == EnhanceLandedReason);

                return landed
                    ? (Say(EnhanceLandedStatusKey, item).Replace(LevelToken, Count(preview.NextLevel), StringComparison.Ordinal),
                        InventoryNoticeKind.Gain)
                    : (Say(EnhanceFailedStatusKey, item), InventoryNoticeKind.Setback);
            },
            ct);
    }

    /// <summary>Submits <c>MERGE</c> with the keeper first and the picks after it, dust in the third place when it stands in.</summary>
    /// <remarks>
    /// 🔒 The keeper is <em>first</em> in the payload because the rules keep the first input's identity
    /// and enhancement; a screen that sent the picks first would fuse into the fodder. The sheet stays
    /// open on the keeper afterwards, which is now the fused item under the same id.
    /// </remarks>
    /// <param name="ct">Cancelled when the application shuts down.</param>
    public Task<InventorySubmission> MergeInspectedAsync(CancellationToken ct)
    {
        if (!CanMerge || Inspected is not { Merge.OutputRarity: { } band } keeper)
        {
            return Task.FromResult(InventorySubmission.RefusedNotAvailable);
        }

        var inputs = new List<GearInstanceId> { keeper.InstanceId };

        inputs.AddRange(MergePicks.Select(pick => pick.InstanceId));

        var result = ItemTitle(band, keeper.Family, keeper.EnhanceLevel);

        return SubmitAsync(
            new MergeCommand(inputs, MergeNeedsDust),
            _ => (_strings.Resolve(MergedStatusKey).Replace(ItemToken, result, StringComparison.Ordinal), InventoryNoticeKind.Gain),
            ct,
            afterwards: () => Sheet = GearSheetPage.Details);
    }

    /// <summary>Submits <c>SALVAGE</c> for the confirmed batch, and reads the payout off the two currency movements.</summary>
    /// <param name="ct">Cancelled when the application shuts down.</param>
    public Task<InventorySubmission> SalvageAsync(CancellationToken ct)
    {
        if (!CanSalvage)
        {
            return Task.FromResult(InventorySubmission.RefusedNotAvailable);
        }

        var batch = Selected;
        var ids = batch.Select(item => item.InstanceId).ToArray();

        return SubmitAsync(
            new SalvageCommand(ids),
            events =>
            {
                var paid = events.OfType<CurrencyChanged>().ToArray();
                var dust = paid.Where(change => change.Id == CurrencyId.MERGE_DUST).Sum(change => change.Delta);
                var stones = paid.Where(change => change.Id == CurrencyId.ENHANCE_STONES).Sum(change => change.Delta);

                return (_strings.Resolve(SalvagedStatusKey)
                        .Replace(CountToken, Count(ids.Length), StringComparison.Ordinal)
                        .Replace(DustToken, Count(dust), StringComparison.Ordinal)
                        .Replace(StonesToken, Count(stones), StringComparison.Ordinal),
                    InventoryNoticeKind.Gain);
            },
            ct,
            afterwards: () =>
            {
                SalvageConfirmPending = false;
                ClearSelection();
                CloseSheet();
                Mode = InventoryMode.Browse;
            });
    }

    /// <summary>
    /// One path for every command: latch, send, read the outcome, re-read the stock, release.
    /// </summary>
    /// <param name="command">What the player intends.</param>
    /// <param name="notice">Composes the line for an accepted command out of its events.</param>
    /// <param name="ct">Cancelled when the application shuts down.</param>
    /// <param name="afterwards">What the screen does to its own state once the command is accepted.</param>
    private async Task<InventorySubmission> SubmitAsync(
        GameCommand command,
        Func<IReadOnlyList<DomainEvent>, (string Notice, InventoryNoticeKind Kind)> notice,
        CancellationToken ct,
        Action? afterwards = null)
    {
        if (!CanSubmit)
        {
            return InventorySubmission.RefusedNotAvailable;
        }

        _submissionInFlight = true;

        try
        {
            var outcome = await _gameHost.SubmitAsync(_player, null, command, ct).ConfigureAwait(false);

            HostFaulted = false;
            RulesRejection = outcome.Rejection;

            if (outcome.Rejection is not null)
            {
                Notice = NothingLeftToSay;
                NoticeKind = InventoryNoticeKind.None;

                return InventorySubmission.Rejected;
            }

            (Notice, NoticeKind) = notice(outcome.Events);
            afterwards?.Invoke();

            await StartAsync(ct).ConfigureAwait(false);

            return InventorySubmission.Submitted;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            HostFaulted = true;
            RulesRejection = null;

            return InventorySubmission.HostUnavailable;
        }
        finally
        {
            // Released on completion: a player taps again after a submission that never answered, and a
            // latch left shut strands them on the screen.
            _submissionInFlight = false;
        }
    }

    // ============================================================================ helpers

    private bool CanSubmit => Stage is InventoryStage.Ready or InventoryStage.Empty && !_submissionInFlight;

    private bool IsStored(InventoryItemView item) => Stored.Any(stored => stored.InstanceId == item.InstanceId);

    /// <summary>The fusion's inputs so far: the keeper in the first place, then whatever is picked under it.</summary>
    private int KeeperAndPicks => 1 + MergePicks.Count;

    private bool MergeIsConceivable(InventoryItemView item) =>
        IsStored(item) && !item.Locked && item.Merge.OutputRarity is not null;

    private long Balance(CurrencyId currency) =>
        _row?.Wallet is { } wallet && wallet.TryGetValue(currency, out var balance) ? balance : 0;

    private string ItemTitle(Rarity rarity, GearFamily family, int enhanceLevel)
    {
        var name = RarityName(rarity) + Space + FamilyName(family);

        return enhanceLevel == 0 ? name : name + Space + EnhancePrefix + Count(enhanceLevel);
    }

    private string Say(string key, InventoryItemView item) =>
        _strings.Resolve(key)
            .Replace(ItemToken, ItemTitle(item), StringComparison.Ordinal)
            .Replace(SlotToken, SlotName(item.Slot), StringComparison.Ordinal);

    /// <summary>
    /// A count as a player reads it. Never abbreviated: a bag holds at most four figures and the
    /// thousands rule has nothing to act on at this ceiling.
    /// </summary>
    private static string Count(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Flat(double value) => value.ToString(FlatFormat, CultureInfo.InvariantCulture);

    private static string Percent(double share) =>
        (share * 100.0).ToString(PercentFormat, CultureInfo.InvariantCulture) + PercentSuffix;

    /// <summary>
    /// 🔴 Percent and flat are formatted differently because they are different quantities. A crit
    /// fraction of 0.05 and an attack figure of 120 are not comparable, and drawing both as bare
    /// numbers would invite exactly that comparison.
    /// </summary>
    private static string Figure(double value, bool isPercent) => isPercent ? Percent(value) : Flat(value);

    private static string Signed(double value, bool isPercent)
    {
        var sign = value > 0 ? GainSign : value < 0 ? LossSign : string.Empty;

        return sign + Figure(Math.Abs(value), isPercent);
    }

    /// <summary>
    /// One delta as a player reads it: an arrow, a sign and a magnitude.
    /// </summary>
    /// <remarks>
    /// 🔒 <b>Three channels for one fact.</b> The arrow, the sign and the colour the scene adds all say
    /// which way the stat moves, so a player who reads none of the hues apart loses nothing. Colour
    /// alone on a green/red pair is the most common accessibility failure there is, and it costs two
    /// characters to avoid.
    /// </remarks>
    private static string DeltaText(double delta, bool isPercent)
    {
        var arrow = delta > 0 ? GainArrow : delta < 0 ? LossArrow : LevelArrow;

        return arrow + Space + Signed(delta, isPercent);
    }
}
