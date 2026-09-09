using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Forge;
using SlayIdleRepeat.Core.Rules.Gear;
using SlayIdleRepeat.Core.Rules.Inventory;
using SlayIdleRepeat.Core.Tests.Rules.Forge;
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
        view.Held.ShouldAllBe(
            item => item.Stats.Count == 2 && item.Power > 0.0,
            "a player deciding what to make room for needs to know what is waiting; only the " +
            "comparison is withheld from a held item, not its figures.");
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
    // The item's own figures — power, stats, affixes — each the rules' own number.
    // ------------------------------------------------------------------------------------------

    // `08` §4.2: the fixture wears +15, a 2.05 multiplier, so a projection reading the raw derivation
    // is out by more than double on every stat and cannot pass by accident.
    [Fact]
    public void Stats_are_the_figures_the_hero_receives_with_enhancement_folded_in()
    {
        var view = InventoryView.Project(GearedRow(), Content);
        var blade = RunBattleWorlds.FarAbovePar[0];
        var projected = view.Stored.Single(item => item.InstanceId == blade.InstanceId);

        var raw = GearStatDerivation.Primary(Inventories.Par, Inventories.Drops, blade);

        projected.Stats[0].Stat.ShouldBe(raw.Stat);
        projected.Stats[0].Value.ShouldBe(
            GearStatDerivation.AsWorn(raw, Inventories.Forge, blade.EnhanceLevel).Value,
            "the stat a screen shows is the stat the fight adds — the derivation under the Forge multiplier.");
        projected.Stats[0].Value.ShouldNotBe(raw.Value, "the fixture is +15, so the two figures differ.");
    }

    [Fact]
    public void Power_is_the_figure_the_POWER_ordering_sorts_by()
    {
        var view = InventoryView.Project(GearedRow(), Content);

        view.Stored.ShouldNotBeEmpty();
        view.Stored.ShouldAllBe(
            item => item.Power == InventorySorting.PowerOf(
                Inventories.Par, Inventories.Drops, Inventories.Forge,
                RunBattleWorlds.FarAbovePar.Single(worn => worn.InstanceId == item.InstanceId)),
            "a screen sorting by a number it draws has to be sorting by the number it draws.");
    }

    [Fact]
    public void An_affix_carries_the_pools_name_key_and_is_written_as_a_share()
    {
        var view = InventoryView.Project(RunBattleWorlds.PlayerRow(), Content);
        var blade = view.Stored.Single(item => item.InstanceId == RunBattleWorlds.Worn[0].InstanceId);
        var roll = RunBattleWorlds.Worn[0].Affixes[0];

        var affix = blade.Affixes.ShouldHaveSingleItem("the fixture blade rolls exactly one affix.");

        affix.AffixId.ShouldBe(roll.AffixId);
        affix.Value.ShouldBe(roll.Value);
        affix.NameKey.ShouldBe(
            Inventories.Drops.Affix(roll.AffixId).DisplayNameKey,
            "the name is the pool's, not a spelling the projection invents from the id.");
        affix.IsPercent.ShouldBeTrue("a STAT_ADD_PCT roll is a share whatever stat it adds onto.");
        affix.Favourable.ShouldBeTrue("more attack speed is what a player wants.");
    }

    // The damage-reduction affix is authored negative because damage taken is a multiplier a player
    // wants LOW; a flat crit roll is a share because crit chance is one. Both facts are the stat's, and
    // a sheet reading the raw sign or the op alone gets one of the two wrong.
    [Theory]
    [InlineData("AFX_DAMAGE_REDUCTION", -0.05, GearFamily.LEATHERS, true, true)]
    [InlineData("AFX_CRIT_CHANCE", 0.04, GearFamily.BLADE, true, true)]
    [InlineData("AFX_CRIT_CHANCE", -0.04, GearFamily.BLADE, true, false)]
    public void An_affix_is_signed_by_what_its_stat_wants_and_written_in_its_stats_unit(
        string affixId, double value, GearFamily family, bool percent, bool favourable)
    {
        var rolled = Inventories.Item(
            "rolled", family, Rarity.B, affixes: [new GearAffixRoll(affixId, value)]);
        var view = InventoryView.Project(WithCandidates(rolled), Content);

        var affix = view.Stored.Single(item => item.InstanceId == rolled.InstanceId).Affixes.Single();

        affix.IsPercent.ShouldBe(percent);
        affix.Favourable.ShouldBe(favourable, "the sign a screen draws is the benefit, not the raw value.");
    }

    // `24` §11: every floor is a real number on the screen. Walked through the handler's own rate, so a
    // rung inside the certain band is certain now and a rung with a slope reaches certainty in a
    // countable number of failures.
    [Fact]
    public void The_enhance_preview_counts_the_failures_until_the_next_attempt_is_certain()
    {
        var certain = Inventories.Item("certain", GearFamily.BLADE, Rarity.C, enhanceLevel: 0);
        var chancy = Inventories.Item("chancy", GearFamily.BLADE, Rarity.C, enhanceLevel: 12, enhanceFailures: 2);
        var view = InventoryView.Project(WithCandidates(certain, chancy), Content);

        var sure = view.Stored.Single(item => item.InstanceId == certain.InstanceId).Enhance!;
        var risky = view.Stored.Single(item => item.InstanceId == chancy.InstanceId).Enhance!;

        sure.FailuresUntilCertain.ShouldBe(0, "the first rungs are authored certain, so there is nothing to wait for.");
        risky.ConsecutiveFailures.ShouldBe(2);

        var horizon = risky.FailuresUntilCertain.ShouldNotBeNull("the authored slope reaches the cap.");

        horizon.ShouldBeGreaterThan(0);
        GearEnhancement.EffectiveRate(12, 2 + horizon, GearEnhancement.NoLuckyBonus, Inventories.Forge, Forges.Mercy)
            .ShouldBeGreaterThanOrEqualTo(1.0, "after that many more failures the handler's own rate is certain…");
        GearEnhancement.EffectiveRate(12, 2 + horizon - 1, GearEnhancement.NoLuckyBonus, Inventories.Forge, Forges.Mercy)
            .ShouldBeLessThan(1.0, "…and one fewer is not, so the count is exact.");
    }

    // ------------------------------------------------------------------------------------------
    // The Forge's three previews — each the command's own arithmetic, none decided here.
    // ------------------------------------------------------------------------------------------

    [Fact]
    public void The_enhance_preview_is_absent_at_the_ceiling_and_prices_the_rung_below_it()
    {
        var below = Inventories.Item(
            "below", GearFamily.BLADE, Rarity.S, enhanceLevel: Inventories.Forge.MaxEnhanceLevel - 1);
        var view = InventoryView.Project(WithCandidates(below), Content);

        view.Stored.Single(item => item.IsEquipped && item.Slot == GearSlot.WEAPON).Enhance.ShouldBeNull(
            "the fixture wears +15, the ceiling, and ENHANCE answers CAP_REACHED there.");

        var preview = view.Stored.Single(item => item.InstanceId == below.InstanceId).Enhance
            .ShouldNotBeNull("one rung below the ceiling there is still an attempt to price.");

        preview.NextLevel.ShouldBe(Inventories.Forge.MaxEnhanceLevel);
        preview.StoneCost.ShouldBe(Inventories.Forge.EnhanceStoneCost(preview.NextLevel));
        preview.SuccessRate.ShouldBe(GearEnhancement.EffectiveRate(
            below.EnhanceLevel, below.EnhanceFailures, GearEnhancement.NoLuckyBonus, Inventories.Forge, Forges.Mercy));
        preview.NextPower.ShouldBe(InventorySorting.PowerOf(
            Inventories.Par, Inventories.Drops, Inventories.Forge, below, preview.NextLevel));
        preview.NextStats[0].Value.ShouldBe(
            GearStatDerivation.AsWorn(
                GearStatDerivation.Primary(Inventories.Par, Inventories.Drops, below),
                Inventories.Forge,
                preview.NextLevel).Value,
            "the stat after the attempt is the same derivation one multiplier step up.");
    }

    // `24` §4.6: a failed attempt raises the next rate, so a preview reading the ladder alone would
    // show a player who has failed three times the same odds as one who has not.
    [Fact]
    public void The_enhance_preview_counts_the_items_own_failures()
    {
        var fresh = Inventories.Item("fresh", GearFamily.AXE, Rarity.A, enhanceLevel: 6);
        var unlucky = Inventories.Item("unlucky", GearFamily.AXE, Rarity.A, enhanceLevel: 6, enhanceFailures: 3);
        var view = InventoryView.Project(WithCandidates(fresh, unlucky), Content);

        var freshRate = view.Stored.Single(item => item.InstanceId == fresh.InstanceId).Enhance!.SuccessRate;
        var unluckyRate = view.Stored.Single(item => item.InstanceId == unlucky.InstanceId).Enhance!.SuccessRate;

        unluckyRate.ShouldBeGreaterThan(
            freshRate, "mercy is earned per item, and the preview is the rate the handler will draw against.");
    }

    [Fact]
    public void The_salvage_preview_is_the_payout_the_command_pays()
    {
        var enhanced = Inventories.Item("enhanced", GearFamily.HOOD, Rarity.S, enhanceLevel: 7);
        var view = InventoryView.Project(WithCandidates(enhanced), Content);

        var (dust, stones) = GearSalvage.Payout(enhanced, Inventories.Forge);
        var preview = view.Stored.Single(item => item.InstanceId == enhanced.InstanceId).Salvage;

        stones.ShouldBeGreaterThan(0, "the premise: seven landed rungs refund something, so the two figures differ.");
        preview.Dust.ShouldBe(dust);
        preview.Stones.ShouldBe(stones);
    }

    // The key mirrors GearMerge.Refusal's identity check, so it is asserted AGAINST that check rather
    // than against a spelling: for each pair, keys agree exactly when a triple of them would fuse.
    [Theory]
    [InlineData("quality and chapter differ", 0.9, 5, 0, Rarity.A, GearFamily.BLADE, false, true)]
    [InlineData("the lock differs", 0.5, 1, 0, Rarity.A, GearFamily.BLADE, true, true)]
    [InlineData("the enhancement differs", 0.5, 1, 3, Rarity.A, GearFamily.BLADE, false, false)]
    [InlineData("the band differs", 0.5, 1, 0, Rarity.B, GearFamily.BLADE, false, false)]
    [InlineData("the item differs", 0.5, 1, 0, Rarity.A, GearFamily.AXE, false, false)]
    public void Merge_keys_agree_exactly_when_MERGE_would_accept_the_pair(
        string why,
        double quality,
        int chapter,
        int enhanceLevel,
        Rarity rarity,
        GearFamily family,
        bool locked,
        bool fuses)
    {
        var keeper = Inventories.Item("keeper", GearFamily.BLADE, Rarity.A);
        var other = Inventories.Item(
            "other", family, rarity, chapterOrigin: chapter, quality: quality, enhanceLevel: enhanceLevel, locked: locked);
        var third = Inventories.Item("third", GearFamily.BLADE, Rarity.A);

        var view = InventoryView.Project(WithCandidates(keeper, other, third), Content);
        var keys = view.Stored.ToDictionary(item => item.InstanceId, item => item.Merge.Identity);

        (GearMerge.Refusal([keeper, other, third], dustSubstituted: false, Inventories.Forge) is null)
            .ShouldBe(fuses, "the premise of this row: " + why);
        (keys[keeper.InstanceId] == keys[other.InstanceId]).ShouldBe(
            fuses,
            "the key has to agree with the rule when " + why + ", or the screen offers a merge the rules " +
            "refuse — or hides one they accept.");
    }

    [Fact]
    public void The_merge_preview_names_the_next_band_and_its_prices_and_nothing_above_the_top()
    {
        var bottom = Inventories.Item("bottom", GearFamily.BLADE, Rarity.C);
        var view = InventoryView.Project(WithCandidates(bottom), Content);

        var fromBottom = view.Stored.Single(item => item.InstanceId == bottom.InstanceId).Merge;
        var fromTop = view.Stored.Single(item => item.IsEquipped && item.Slot == GearSlot.WEAPON).Merge;

        fromBottom.OutputRarity.ShouldBe(Rarity.B);
        fromBottom.CrownCost.ShouldBe(Inventories.Forge.MergeCrownCost(Rarity.B));
        fromBottom.DustSubstituteCost.ShouldBe(Inventories.Forge.MergeDustSubstituteCost(Rarity.C));

        fromTop.OutputRarity.ShouldBeNull("the fixture wears SS, and MERGE answers NO_HIGHER_RARITY there.");
        fromTop.CrownCost.ShouldBe(0);
        fromTop.DustSubstituteCost.ShouldBeNull("no fusion exists for dust to stand in for.");
    }

    // ------------------------------------------------------------------------------------------
    // Sets and orderings.
    // ------------------------------------------------------------------------------------------

    // The over-par six are all BALANCED families, so the fixture is one complete set — and the
    // five-piece variant is what tells a resolver that counts pieces from one that counts slots.
    [Fact]
    public void Active_sets_count_the_SS_pieces_worn_per_axis()
    {
        var full = InventoryView.Project(GearedRow(), Content);

        var set = full.ActiveSets.ShouldHaveSingleItem();

        set.Set.ShouldBe(GearFamilyAxis.BALANCED);
        set.Pieces.ShouldBe(RunBattleWorlds.FarAbovePar.Count);
        set.BreakpointsMet.ShouldBe(full.SetBreakpoints);
        set.NameKey.ShouldBe(Inventories.Drops.SetNameKey(GearFamilyAxis.BALANCED));

        var fiveWorn = RunBattleWorlds.FarAboveParRow() with
        {
            Loadout = new LoadoutSnapshot(
                RunBattleWorlds.FarAbovePar.Skip(1).ToDictionary(item => item.Slot, item => item.InstanceId)),
        };

        InventoryView.Project(fiveWorn, Content).ActiveSets.Single().BreakpointsMet.ShouldBe(
            [2, 4], "five pieces reach the two-piece and four-piece bonuses and not the six-piece one.");

        InventoryView.Project(RunBattleWorlds.PlayerRow(geared: false), Content).ActiveSets.ShouldBeEmpty(
            "nothing worn, nothing built.");
    }

    [Fact]
    public void The_order_asked_for_is_the_order_answered()
    {
        var weak = Inventories.Item("weak", GearFamily.BLADE, Rarity.C);
        var strong = Inventories.Item("strong", GearFamily.BLADE, Rarity.S, enhanceLevel: 9);
        var row = WithCandidates(weak, strong);

        var byPower = InventoryView.Project(row, Content, InventorySortKey.POWER);
        var byGrant = InventoryView.Project(row, Content);

        byPower.Stored.Select(item => item.Power).ShouldBeInOrder(
            SortDirection.Descending, "POWER lists the strongest first.");
        byGrant.Stored.Select(item => item.InstanceId).TakeLast(2).ShouldBe(
            [weak.InstanceId, strong.InstanceId], "the two-argument door is grant order, as it always was.");
        byPower.Stored.Select(item => item.InstanceId).ShouldBe(
            InventorySorting.Sort(
                    [.. RunBattleWorlds.FarAbovePar, weak, strong],
                    InventorySortKey.POWER,
                    Inventories.Par,
                    Inventories.Drops,
                    Inventories.Forge,
                    Inventories.Catalogue)
                .Select(item => item.InstanceId),
            "the projection orders through the one sorter rather than a second one.");
    }

    [Fact]
    public void An_order_outside_the_vocabulary_is_refused() =>
        Should.Throw<ArgumentOutOfRangeException>(
            () => InventoryView.Project(GearedRow(), Content, (InventorySortKey)0)).ParamName.ShouldBe("order");

    // ------------------------------------------------------------------------------------------
    // Fixtures.
    // ------------------------------------------------------------------------------------------

    /// <summary>The over-par six worn, plus the given items lying in the bag.</summary>
    private static PlayerSnapshot WithCandidates(params Core.Model.Gear.GearInstance[] candidates) =>
        RunBattleWorlds.FarAboveParRow() with
        {
            Inventory = new InventorySnapshot(
                0,
                RunBattleWorlds.FarAbovePar.Concat(candidates).Select(Inventories.Persist).ToArray(),
                []),
        };

    private static PlayerSnapshot GearedRow() => RunBattleWorlds.FarAboveParRow();
}
