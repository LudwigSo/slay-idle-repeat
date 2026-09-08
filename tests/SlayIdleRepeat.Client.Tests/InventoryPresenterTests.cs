using Shouldly;
using SlayIdleRepeat.Application.Services;
using SlayIdleRepeat.Client.Game.Presenters;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Inventory;
using Xunit;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// `08` §5 — the Gear screen (S16): the slots, the stock, the sheet, and the Forge's three operations.
/// </summary>
/// <remarks>
/// The cases that matter are the ones about what the screen will and will not SUBMIT, and about what
/// it picks on the player's behalf: which two copies go under a fusion, which items a band quick-pick
/// sweeps into a salvage batch, which slot an equip names. Every one of those is a decision a scene
/// cannot be held to, so every one is held here.
/// </remarks>
public sealed class InventoryPresenterTests
{
    private static readonly PlayerId Player = new("PLAYER_inv_7f31");

    private static readonly GearInstanceId Keeper = new("blade_keeper");
    private static readonly GearInstanceId WeakCopy = new("blade_weak");
    private static readonly GearInstanceId BetterCopy = new("blade_better");
    private static readonly GearInstanceId LockedCopy = new("blade_locked");
    private static readonly GearInstanceId WornBlade = new("blade_worn");
    private static readonly GearInstanceId WornHood = new("hood_worn");
    private static readonly GearInstanceId SpareHood = new("hood_spare");

    // ---- what the screen submits, and what it refuses on its own -------------------------------

    [Fact]
    public async Task An_item_held_in_overflow_is_refused_without_reaching_the_host()
    {
        var host = RecordingGameHost.Finding(PlayerState.WithOverflowingStock(Player), null);
        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        presenter.Held.ShouldNotBeEmpty("the premise: this stock is holding something in overflow.");

        var before = host.SubmitCallCount;
        var outcome = await presenter.EquipAsync(presenter.Held[0], CancellationToken.None);

        outcome.ShouldBe(InventorySubmission.RefusedNotAvailable);
        host.SubmitCallCount.ShouldBe(
            before,
            "an item in overflow was sent to the host. EQUIP refuses it with INVENTORY_FULL, and the " +
            "card already carries that sentence — spending the round trip teaches the player the card " +
            "is not to be trusted.");
    }

    [Fact]
    public async Task Equipping_nothing_is_a_defect()
    {
        var presenter = Build(RecordingGameHost.FindingNoSuchPlayer());

        await Should.ThrowAsync<ArgumentNullException>(
            () => presenter.EquipAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task Equip_names_the_items_own_slot_and_reads_the_stock_again()
    {
        var host = RecordingGameHost.Finding(Forge(), null);
        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        var spare = Item(presenter, SpareHood);
        var outcome = await presenter.EquipAsync(spare, CancellationToken.None);

        outcome.ShouldBe(InventorySubmission.Submitted);
        host.SubmitCommand.ShouldBe(
            new EquipCommand(SpareHood, GearSlot.HELMET),
            "the slot is the item's, never the caller's — a blade asked into a boot slot is refused only after a round trip.");
        host.ReadCallCount.ShouldBe(2, "every accepted command is followed by a read, so the figures are the server's row.");
        presenter.Notice.ShouldBe(
            RunDecisionContent.EnglishValueOf(RunDecisionContent.InventoryEquippedStatusKey),
            "the fixture line carries no {item} parameter, so the resolved sentence is the whole notice.");
        presenter.NoticeKind.ShouldBe(InventoryNoticeKind.Gain);
    }

    [Fact]
    public async Task A_second_press_while_a_submission_is_outstanding_is_turned_away()
    {
        var host = RecordingGameHost.Finding(Forge(), null).PausingItsCommands();
        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        var spare = Item(presenter, SpareHood);
        var first = presenter.EquipAsync(spare, CancellationToken.None);
        var second = presenter.EquipAsync(spare, CancellationToken.None);

        host.ReleaseSubmissions();

        (await second).ShouldBe(InventorySubmission.RefusedNotAvailable);
        (await first).ShouldBe(InventorySubmission.Submitted);
        host.SubmitCallCount.ShouldBe(1, "one tap, one command; the latch is what the second press meets.");
    }

    [Fact]
    public async Task A_refusal_the_player_can_act_on_is_named_as_such()
    {
        var presenter = Build(RecordingGameHost.Finding(Forge(), null).RefusingCommands(RejectionReason.INSUFFICIENT_FUNDS));

        await presenter.StartAsync(CancellationToken.None);

        var outcome = await presenter.EquipAsync(Item(presenter, SpareHood), CancellationToken.None);

        outcome.ShouldBe(InventorySubmission.Rejected);
        presenter.RejectionText.ShouldBe(
            RunDecisionContent.EnglishValueOf(RunDecisionContent.InventoryRefusedFundsStatusKey),
            "a wallet refusal has a next step the generic line does not name.");
        presenter.Notice.ShouldBeEmpty("a refused command did nothing worth announcing.");
    }

    // ---- the slots and the grid ----------------------------------------------------------------

    [Fact]
    public async Task Tapping_a_worn_slot_opens_what_it_wears_and_narrows_the_grid_to_that_slot()
    {
        var presenter = Build(RecordingGameHost.Finding(Forge(), null));

        await presenter.StartAsync(CancellationToken.None);

        presenter.TapSlot(GearSlot.HELMET);

        presenter.Sheet.ShouldBe(GearSheetPage.Details);
        presenter.Inspected!.InstanceId.ShouldBe(WornHood);
        presenter.SlotFilter.ShouldBe(GearSlot.HELMET);
        presenter.Visible.ShouldAllBe(item => item.Slot == GearSlot.HELMET);
        presenter.Visible.Count.ShouldBe(2, "the worn hood and the spare, and nothing from another slot.");

        presenter.TapSlot(GearSlot.RING);

        presenter.Sheet.ShouldBe(GearSheetPage.Details, "an empty slot narrows the grid and leaves the sheet as it was.");
        presenter.Visible.ShouldBeEmpty();
        presenter.StatusText.ShouldBe(
            RunDecisionContent.EnglishValueOf("loc.inventory.filter_empty.status"),
            "an empty filtered grid says why it is empty rather than looking unrendered.");

        presenter.ClearSlotFilter();

        presenter.Visible.Count.ShouldBe(presenter.Stored.Count);
    }

    [Fact]
    public async Task The_upgrade_badge_lights_only_when_the_bag_out_powers_what_the_slot_wears()
    {
        var presenter = Build(RecordingGameHost.Finding(Forge(), null));

        await presenter.StartAsync(CancellationToken.None);

        presenter.UpgradeAvailable(GearSlot.HELMET).ShouldBeTrue(
            "the spare hood is an Epic against a worn Common, so the slot has something better waiting.");
        presenter.UpgradeAvailable(GearSlot.WEAPON).ShouldBeFalse(
            "every unworn blade is a Common like the worn one, and a tie is not an upgrade.");
        presenter.UpgradeAvailable(GearSlot.RING).ShouldBeFalse("nothing in the bag fits an empty ring slot.");
        presenter.IsBetterThanWorn(Item(presenter, SpareHood)).ShouldBeTrue();
        presenter.IsBetterThanWorn(Item(presenter, WornHood)).ShouldBeFalse("the worn item is not better than itself.");
    }

    [Fact]
    public async Task Cycling_the_order_reads_the_stock_again_in_the_new_order()
    {
        var host = RecordingGameHost.Finding(Forge(), null);
        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        presenter.Order.ShouldBe(InventorySortKey.POWER, "power first is the question a player brings to the bag.");

        await presenter.CycleOrderAsync(CancellationToken.None);

        presenter.Order.ShouldBe(InventorySortKey.RARITY);
        host.ReadCallCount.ShouldBe(2, "the ordering is the projection's, so a new order is a new read.");
    }

    [Fact]
    public async Task The_projected_hero_power_is_measured_over_the_loadout_with_one_slot_swapped()
    {
        var power = new RecordingPowerSource();
        var presenter = Build(RecordingGameHost.Finding(Forge(), null), power);

        await presenter.StartAsync(CancellationToken.None);

        presenter.Open(Item(presenter, SpareHood));

        _ = presenter.ProjectedHeroPower;

        var asked = power.LastLoadout.ShouldNotBeNull("the projection measures a hypothetical loadout.");

        asked.Gear[GearSlot.HELMET].ShouldBe(SpareHood, "the open item takes its own slot…");
        asked.Gear[GearSlot.WEAPON].ShouldBe(WornBlade, "…and every other slot keeps what it wears.");

        presenter.Open(Item(presenter, WornHood));

        presenter.ProjectedHeroPower.ShouldBeNull("the worn item projects nothing: it is already the figure shown.");
    }

    // ---- enhance --------------------------------------------------------------------------------

    [Fact]
    public async Task Enhance_is_refused_here_when_the_stones_do_not_cover_the_price()
    {
        var host = RecordingGameHost.Finding(Forge(stones: 0), null);
        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        presenter.Open(Item(presenter, Keeper));
        presenter.ShowEnhance();

        presenter.Sheet.ShouldBe(GearSheetPage.Enhance);
        presenter.CanEnhance.ShouldBeFalse();
        presenter.EnhanceBlockText.ShouldBe(
            RunDecisionContent.EnglishValueOf(RunDecisionContent.InventoryNotEnoughStonesBlockKey));

        (await presenter.EnhanceInspectedAsync(CancellationToken.None)).ShouldBe(InventorySubmission.RefusedNotAvailable);
        host.SubmitCallCount.ShouldBe(0, "ENHANCE would answer INSUFFICIENT_FUNDS; the screen knows the wallet already.");
    }

    [Theory]
    [InlineData("enhance_cost_success", InventoryNoticeKind.Gain, RunDecisionContent.InventoryEnhanceLandedStatusKey)]
    [InlineData("enhance_cost_failure", InventoryNoticeKind.Setback, RunDecisionContent.InventoryEnhanceFailedStatusKey)]
    public async Task Enhance_reads_whether_the_rung_landed_off_the_charges_reason(
        string reason, InventoryNoticeKind kind, string key)
    {
        var host = RecordingGameHost.Finding(Forge(), null)
            .Emitting(new CurrencyChanged(1, CurrencyId.ENHANCE_STONES, -2, reason));
        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        presenter.Open(Item(presenter, Keeper));
        presenter.ShowEnhance();

        var before = presenter.Inspected!;
        var outcome = await presenter.EnhanceInspectedAsync(CancellationToken.None);

        outcome.ShouldBe(InventorySubmission.Submitted);
        host.SubmitCommand.ShouldBe(new EnhanceCommand(Keeper));
        presenter.NoticeKind.ShouldBe(kind, "the reason token is the only wire signal of the outcome.");
        presenter.Notice.ShouldBe(
            RunDecisionContent.EnglishValueOf(key)
                .Replace("{item}", presenter.ItemTitle(before), StringComparison.Ordinal)
                .Replace("{level}", "1", StringComparison.Ordinal));
        presenter.Sheet.ShouldBe(GearSheetPage.Enhance, "the player is mid-session at the anvil; the page stays open for the next tap.");
    }

    // ---- merge ----------------------------------------------------------------------------------

    [Fact]
    public async Task The_merge_page_picks_the_two_copies_the_player_will_miss_least_and_sends_the_keeper_first()
    {
        var host = RecordingGameHost.Finding(Forge(), null);
        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        presenter.Open(Item(presenter, Keeper));
        presenter.MergeableCopies(presenter.Inspected!).ShouldBe(4, "keeper, weak, better and the worn blade; the locked one never counts.");
        presenter.ShowMerge();

        presenter.Sheet.ShouldBe(GearSheetPage.Merge);
        presenter.MergeCandidates.Select(c => c.InstanceId).ShouldBe(
            [WeakCopy, BetterCopy, WornBlade],
            "unworn before worn, then the weaker roll first; the locked copy is not a candidate at all.");
        presenter.MergePicks.Select(p => p.InstanceId).ShouldBe([WeakCopy, BetterCopy]);
        presenter.MergeNeedsDust.ShouldBeFalse();
        presenter.MergeWarnings.ShouldContain(
            RunDecisionContent.EnglishValueOf("loc.inventory.merge_consumes_better_roll.block"),
            "the second pick rolled better than the keeper, and the player is told before the tap.");
        presenter.CanMerge.ShouldBeTrue();

        var outcome = await presenter.MergeInspectedAsync(CancellationToken.None);

        outcome.ShouldBe(InventorySubmission.Submitted);
        host.SubmitCommand.ShouldBe(
            new MergeCommand([Keeper, WeakCopy, BetterCopy], dustSubstituted: false),
            "the rules keep the FIRST input's identity; picks first would fuse into the fodder.");
        presenter.Sheet.ShouldBe(GearSheetPage.Details, "the sheet stays on the keeper, which is now the fused item under the same id.");
        presenter.NoticeKind.ShouldBe(InventoryNoticeKind.Gain);
    }

    [Fact]
    public async Task Swapping_a_pick_for_the_worn_copy_says_so()
    {
        var presenter = Build(RecordingGameHost.Finding(Forge(), null));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Open(Item(presenter, Keeper));
        presenter.ShowMerge();
        presenter.ToggleMergePick(Item(presenter, BetterCopy));
        presenter.ToggleMergePick(Item(presenter, WornBlade));

        presenter.MergePicks.Select(p => p.InstanceId).ShouldBe([WeakCopy, WornBlade]);
        presenter.MergeWarnings.ShouldContain(
            RunDecisionContent.EnglishValueOf(RunDecisionContent.InventoryMergeConsumesWornBlockKey));
    }

    [Theory]
    [InlineData(1000L, true)]
    [InlineData(0L, false)]
    public async Task A_merge_one_copy_short_stands_dust_in_only_when_the_dust_is_there(long dust, bool canMerge)
    {
        var host = RecordingGameHost.Finding(Forge(dust: dust, copies: 1), null);
        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        presenter.Open(Item(presenter, Keeper));
        presenter.ShowMerge();

        presenter.MergePicks.Count.ShouldBe(1, "the premise: one copy at hand besides the keeper.");
        presenter.MergeNeedsDust.ShouldBeTrue();
        presenter.MergeDustText.ShouldNotBeEmpty();
        presenter.CanMerge.ShouldBe(canMerge);

        if (!canMerge)
        {
            presenter.MergeWarnings.ShouldContain(RunDecisionContent.EnglishValueOf("loc.inventory.not_enough_dust.block"));
            (await presenter.MergeInspectedAsync(CancellationToken.None)).ShouldBe(InventorySubmission.RefusedNotAvailable);
            host.SubmitCallCount.ShouldBe(0);

            return;
        }

        await presenter.MergeInspectedAsync(CancellationToken.None);

        host.SubmitCommand.ShouldBe(new MergeCommand([Keeper, WeakCopy], dustSubstituted: true));
    }

    [Fact]
    public async Task A_locked_keeper_cannot_open_the_merge_page()
    {
        var presenter = Build(RecordingGameHost.Finding(Forge(), null));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Open(Item(presenter, LockedCopy));

        presenter.CanShowMerge.ShouldBeFalse();
        presenter.CanSalvageInspected.ShouldBeFalse();
        presenter.InspectedBlockText.ShouldBe(
            RunDecisionContent.EnglishValueOf(RunDecisionContent.InventoryLockedNoForgeBlockKey));

        presenter.ShowMerge();

        presenter.Sheet.ShouldBe(GearSheetPage.Details, "MERGE refuses a locked input, so the page never opens.");
    }

    // ---- salvage --------------------------------------------------------------------------------

    [Fact]
    public async Task Select_mode_keeps_worn_and_locked_items_out_of_the_batch_and_a_band_pick_sweeps_the_rest()
    {
        var presenter = Build(RecordingGameHost.Finding(Forge(), null));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Open(Item(presenter, Keeper));
        presenter.EnterSelectMode();

        presenter.Sheet.ShouldBe(GearSheetPage.Closed, "a batch is picked from the grid, not from a sheet.");
        presenter.Mode.ShouldBe(InventoryMode.Select);

        presenter.Open(Item(presenter, WornBlade));
        presenter.Open(Item(presenter, LockedCopy));

        presenter.SelectedCount.ShouldBe(0, "in select mode a tap picks, and neither of these may be picked.");

        presenter.SelectBand(Rarity.C);

        presenter.Selected.Select(item => item.InstanceId).ShouldBe(
            new[] { Keeper, WeakCopy, BetterCopy },
            ignoreOrder: true,
            "every unworn, unlocked Common — the worn hood and the locked blade stay out.");
        presenter.SelectedCount.ShouldBe(3);

        presenter.LeaveSelectMode();

        presenter.SelectedCount.ShouldBe(0);
        presenter.Mode.ShouldBe(InventoryMode.Browse);
    }

    [Fact]
    public async Task Salvage_needs_the_second_tap_and_sends_the_batch_in_pick_order()
    {
        var host = RecordingGameHost.Finding(Forge(), null)
            .ThenFinding(Forge(copies: 0), null)
            .Emitting(
                new CurrencyChanged(1, CurrencyId.MERGE_DUST, 20, "salvage_dust"),
                new CurrencyChanged(2, CurrencyId.ENHANCE_STONES, 0, "salvage_stone_refund"));
        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        presenter.EnterSelectMode();
        presenter.Open(Item(presenter, BetterCopy));
        presenter.Open(Item(presenter, WeakCopy));

        (await presenter.SalvageAsync(CancellationToken.None)).ShouldBe(InventorySubmission.RefusedNotAvailable);
        host.SubmitCallCount.ShouldBe(0, "a destructive command waits for its second tap.");

        presenter.RequestSalvage();

        presenter.SalvageConfirmPending.ShouldBeTrue();
        presenter.CanSalvage.ShouldBeTrue();

        (await presenter.SalvageAsync(CancellationToken.None)).ShouldBe(InventorySubmission.Submitted);

        host.SubmitCommand.ShouldBe(new SalvageCommand([BetterCopy, WeakCopy]));
        presenter.Notice.ShouldBe(
            RunDecisionContent.EnglishValueOf(RunDecisionContent.InventorySalvagedStatusKey)
                .Replace("{count}", "2", StringComparison.Ordinal)
                .Replace("{dust}", "20", StringComparison.Ordinal)
                .Replace("{stones}", "0", StringComparison.Ordinal),
            "the payout is read off the events, never assumed from the preview.");
        presenter.Mode.ShouldBe(InventoryMode.Browse);
        presenter.SelectedCount.ShouldBe(0);
        presenter.Stored.ShouldNotContain(item => item.InstanceId == WeakCopy, "the second read no longer holds it.");
    }

    [Fact]
    public async Task A_worn_item_can_be_salvaged_from_its_own_sheet_and_the_confirmation_says_it_is_worn()
    {
        var host = RecordingGameHost.Finding(Forge(), null);
        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        presenter.Open(Item(presenter, WornHood));
        presenter.RequestSalvageInspected();

        presenter.SalvageConfirmPending.ShouldBeTrue();
        presenter.Selected.Select(item => item.InstanceId).ShouldBe([WornHood]);
        presenter.SalvageWarnings.ShouldContain(
            RunDecisionContent.EnglishValueOf(RunDecisionContent.InventoryMergeConsumesWornBlockKey));

        presenter.CancelSalvage();

        presenter.SalvageConfirmPending.ShouldBeFalse();
        presenter.SelectedCount.ShouldBe(0, "a cancelled single-item salvage leaves no batch behind.");
    }

    // ---- the read's states ---------------------------------------------------------------------

    [Fact]
    public async Task An_empty_stock_says_so()
    {
        var presenter = Build(RecordingGameHost.Finding(PlayerState.WithEmptyStock(Player), null));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Stage.ShouldBe(InventoryStage.Empty);
        presenter.StatusText.ShouldNotBeNullOrWhiteSpace(
            "an empty grid with no sentence reads as a screen that failed to draw.");
    }

    [Fact]
    public async Task A_read_that_answers_nothing_is_a_state_rather_than_an_escape()
    {
        var presenter = Build(RecordingGameHost.FindingNoSuchPlayer());

        await presenter.StartAsync(CancellationToken.None);

        presenter.Stage.ShouldBe(InventoryStage.ReadUnavailable);
        presenter.Stored.ShouldBeEmpty();
        presenter.Held.ShouldBeEmpty();
        presenter.HeroPower.ShouldBeNull();
        presenter.StatusText.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Building_the_screen_reaches_the_host_in_no_way_at_all()
    {
        var host = RecordingGameHost.Finding(PlayerState.WithEmptyStock(Player), null);

        _ = Build(host);

        host.ReadCallCount.ShouldBe(0);
        host.SubmitCallCount.ShouldBe(0);
    }

    // ---- the captions --------------------------------------------------------------------------

    [Fact]
    public async Task The_occupancy_counts_held_items_and_names_the_ceiling()
    {
        var presenter = Build(RecordingGameHost.Finding(PlayerState.WithOverflowingStock(Player), null));

        await presenter.StartAsync(CancellationToken.None);

        var occupied = presenter.Stored.Count + presenter.Held.Count;

        presenter.CapacityValue.ShouldBe(
            occupied.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/" +
            presenter.Capacity.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "the occupancy has to count what the bag is holding as well as what is in it, and it has " +
            "to name the ceiling it is measured against.");
    }

    [Fact]
    public async Task The_overflow_heading_appears_only_when_something_is_held()
    {
        var empty = Build(RecordingGameHost.Finding(PlayerState.WithEmptyStock(Player), null));

        await empty.StartAsync(CancellationToken.None);

        empty.HeldLabel.ShouldBeEmpty("nothing is held, so a heading over nothing would be a lie.");

        var overflowing = Build(RecordingGameHost.Finding(PlayerState.WithOverflowingStock(Player), null));

        await overflowing.StartAsync(CancellationToken.None);

        overflowing.HeldLabel.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task The_sheet_writes_an_items_figures_in_their_own_units_with_three_channels_per_delta()
    {
        var presenter = Build(RecordingGameHost.Finding(Forge(), null));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Open(Item(presenter, SpareHood));

        presenter.InspectedTitle.ShouldBe(presenter.RarityName(Rarity.A) + " " + presenter.FamilyName(GearFamily.HOOD));
        presenter.InspectedPowerDelta.ShouldStartWith(
            "↑ +", Case.Sensitive, "an Epic against a Common is a gain, and the arrow and the sign both say so.");
        presenter.InspectedStats.Count.ShouldBe(2);
        presenter.InspectedStats.ShouldAllBe(line => line.Sign > 0 && line.Delta.StartsWith("↑ +", StringComparison.Ordinal));
        presenter.InspectedEnhanceText.ShouldBe("+0 / +" + presenter.MaxEnhanceLevel);
        presenter.InspectedBadges.ShouldBeEmpty("the spare is neither worn, locked nor held.");

        presenter.Open(Item(presenter, WornHood));

        presenter.InspectedBadges.ShouldBe([presenter.EquippedBadge]);
        presenter.InspectedPowerDelta.ShouldBeEmpty("the worn item has nothing to compare against.");
        presenter.CanUnequip.ShouldBeTrue();
        presenter.CanEquip.ShouldBeFalse();
    }

    [Fact]
    public void Every_caption_resolves()
    {
        var presenter = Build(RecordingGameHost.FindingNoSuchPlayer());

        var captions = new List<string>
        {
            presenter.Title, presenter.CapacityLabel, presenter.EquippedBadge, presenter.LockedBadge,
            presenter.HeldBadge, presenter.UpgradeBadge, presenter.EquipText, presenter.UnequipText,
            presenter.EnhanceText, presenter.EnhanceOnceText, presenter.MergeText, presenter.MergeConfirmText,
            presenter.SalvageText, presenter.SalvageConfirmText, presenter.SelectText, presenter.CancelText,
            presenter.BackText, presenter.CloseText, presenter.ClearFilterText, presenter.HeldNotEquippableText,
            presenter.HeroPowerLabel, presenter.PowerLabel, presenter.AffixesLabel, presenter.MergeKeeperLabel,
            presenter.MergeInputsLabel, presenter.QuickPickLabel, presenter.EmptySlotText, presenter.SortText,
            presenter.NotOpenYetNotice, presenter.LockActionText,
        };

        captions.AddRange(Enum.GetValues<HomeTab>().Select(presenter.TabLabel));
        captions.AddRange(Enum.GetValues<GearSlot>().Select(presenter.SlotName));
        captions.AddRange(Enum.GetValues<GearFamily>().Select(presenter.FamilyName));
        captions.AddRange(Enum.GetValues<Rarity>().Select(presenter.RarityName));
        captions.AddRange(new[] { "ATK", "DEF", "MAX_HP", "ASPD", "CRIT", "DODGE", "PEN", "LIFESTEAL" }.Select(presenter.StatName));

        foreach (var caption in captions)
        {
            caption.ShouldNotBeNullOrWhiteSpace();
            caption.StartsWith("loc.", StringComparison.Ordinal).ShouldBeFalse(
                "a caption fell through to its own key, so the string set does not carry it: " + caption);
        }
    }

    [Fact]
    public void No_member_of_this_screen_offers_to_buy_bag_space()
    {
        var offenders =
            from property in typeof(InventoryPresenter).GetProperties()
            where property.Name.Contains("Expand", StringComparison.Ordinal) ||
                  property.Name.Contains("Purchase", StringComparison.Ordinal)
            select property.Name;

        offenders.ShouldBeEmpty(
            "a member of this screen reports bag expansion. No command can move the ceiling — 08 §5's " +
            "errata made capacity flat and EXPAND_INVENTORY was explicitly not added to the vocabulary " +
            "— so whatever this member offers, nothing can deliver it.");
    }

    // ---- fixtures ------------------------------------------------------------------------------

    /// <summary>
    /// A forge-ready stock: a Common blade to keep, copies of it to consume (a weak roll, a better roll,
    /// a locked one, and the worn one), a worn Common hood with an Epic spare, and a wallet.
    /// </summary>
    /// <param name="copies">How many unworn, unlocked copies stand beside the keeper: two by default, so a fusion is complete.</param>
    private static PlayerSnapshot Forge(long crowns = 1000, long stones = 1000, long dust = 1000, int copies = 2)
    {
        var stored = new List<GearInstanceSnapshot>
        {
            Item(Keeper, GearSlot.WEAPON, GearFamily.BLADE, Rarity.C, quality: 0.5),
            Item(LockedCopy, GearSlot.WEAPON, GearFamily.BLADE, Rarity.C, quality: 0.5, locked: true),
            Item(WornBlade, GearSlot.WEAPON, GearFamily.BLADE, Rarity.C, quality: 0.5),
            Item(WornHood, GearSlot.HELMET, GearFamily.HOOD, Rarity.C, quality: 0.5),
            Item(SpareHood, GearSlot.HELMET, GearFamily.HOOD, Rarity.A, quality: 0.5),
        };

        if (copies >= 1)
        {
            stored.Add(Item(WeakCopy, GearSlot.WEAPON, GearFamily.BLADE, Rarity.C, quality: 0.2));
        }

        if (copies >= 2)
        {
            stored.Add(Item(BetterCopy, GearSlot.WEAPON, GearFamily.BLADE, Rarity.C, quality: 0.9));
        }

        return PlayerState.Rehydratable(Player) with
        {
            Wallet = new Dictionary<CurrencyId, long>
            {
                [CurrencyId.CROWNS] = crowns,
                [CurrencyId.SOUL_SHARDS] = 0,
                [CurrencyId.ENHANCE_STONES] = stones,
                [CurrencyId.MERGE_DUST] = dust,
                [CurrencyId.BEAST_FEED] = 0,
                [CurrencyId.HONOR] = 0,
            },
            Inventory = new InventorySnapshot(0, stored, []),
            Loadout = new LoadoutSnapshot(new Dictionary<GearSlot, GearInstanceId>
            {
                [GearSlot.WEAPON] = WornBlade,
                [GearSlot.HELMET] = WornHood,
            }),
        };
    }

    private static GearInstanceSnapshot Item(
        GearInstanceId id, GearSlot slot, GearFamily family, Rarity rarity, double quality, bool locked = false) => new(
        id,
        DefId: "GEAR_" + slot + "_" + family,
        slot,
        family,
        rarity,
        ChapterOrigin: 1,
        Quality: quality,
        EnhanceLevel: 0,
        EnhanceFailures: 0,
        Affixes: [],
        Locked: locked);

    private static InventoryItemView Item(InventoryPresenter presenter, GearInstanceId id) =>
        presenter.Stored.Single(item => item.InstanceId == id);

    private static InventoryPresenter Build(RecordingGameHost host, IHeroPowerSource? power = null)
    {
        var strings = RunDecisionContent.Strings();

        return new InventoryPresenter(
            host, RunDecisionContent.Catalogue(strings), BootContent.Shipped, Player, power ?? new RecordingPowerSource());
    }

    /// <summary>A power source that answers a fixed figure and remembers the loadout it was last asked about.</summary>
    private sealed class RecordingPowerSource : IHeroPowerSource
    {
        internal LoadoutSnapshot? LastLoadout { get; private set; }

        public HeroPowerReading Read(PlayerSnapshot player, RunSnapshot? run)
        {
            LastLoadout = player.Loadout;

            return new HeroPowerReading(HeroPowerStanding.Computed, 100.0);
        }
    }
}
