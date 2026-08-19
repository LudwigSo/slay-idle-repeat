using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Hero;
using SlayIdleRepeat.Core.Tests.Content;
using SlayIdleRepeat.Core.Tests.Model.Gear;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Model;

/// <summary>
/// The hero's state on the <c>Player</c> aggregate: name, Talent Points, loadout, presets — the
/// invariants each holds on the way in and out. Equip/preset command behaviour is covered at the
/// <c>Apply</c> seam by <c>EquipTests</c>/<c>UnequipTests</c>/<c>PresetTests</c>.
/// </summary>
public sealed class PlayerHeroTests
{
    private static readonly ContentSnapshot Content = TuningDocuments.Shipped;

    private static readonly LegendTuning Legend = LegendTuning.Read(Content);

    private static readonly ProfanityLexicon Lexicon =
        ProfanityLexicon.Read(ProfanityDocuments.Shipped);

    /// <summary>
    /// 🔒 The rename takes a <see cref="HeroName"/> — only <c>HeroNameRule.Validate</c> can produce
    /// one, so no route into the field skips `27` §1's on-every-edit filter.
    /// </summary>
    [Fact]
    public void Renaming_takes_a_checked_name_and_stores_it_verbatim()
    {
        var player = Rehydrated(PlayerSnapshots.Valid);

        player.Rename(HeroNameRule.Validate("Ludwig", Lexicon).Name!);

        player.DisplayName.ShouldBe("Ludwig");
    }

    /// <summary>
    /// 🔒 A stored name is loaded as written: the word lists are content, and a term added tomorrow
    /// must not make every matching account unloadable.
    /// </summary>
    [Fact]
    public void A_stored_name_the_word_lists_would_now_refuse_still_loads()
    {
        HeroNameRule.Validate(ProfanityDocuments.EnglishTerm, Lexicon)
            .Refusal.ShouldBe(
                HeroNameRefusal.PROFANE_EN,
                "the fixture only discriminates while the filter really would refuse this name.");

        Core.Model.Player
            .Rehydrate(PlayerSnapshots.With(displayName: ProfanityDocuments.EnglishTerm), Content)
            .IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Advancing_a_level_writes_the_level_and_the_points_together()
    {
        var player = Rehydrated(PlayerSnapshots.Valid);

        player.AdvanceLegendLevel(4, 3L, Legend);

        player.LegendLevel.ShouldBe(4);
        player.TalentPoints.ShouldBe(3L);
    }

    [Fact]
    public void Talent_Points_accumulate()
    {
        var player = Rehydrated(PlayerSnapshots.Valid);

        player.AdvanceLegendLevel(2, 1L, Legend);
        player.AdvanceLegendLevel(5, 3L, Legend);

        player.TalentPoints.ShouldBe(4L);
    }

    [Fact]
    public void A_Legend_Level_never_goes_backwards()
    {
        var player = Rehydrated(PlayerSnapshots.With(legendLevel: 10));

        Should.Throw<ArgumentOutOfRangeException>(() => player.AdvanceLegendLevel(9, 0L, Legend))
            .Message.ShouldContain("only ever rises");
    }

    [Fact]
    public void The_cap_is_the_documents_and_the_cap_itself_is_legal()
    {
        var player = Rehydrated(PlayerSnapshots.Valid);

        Should.Throw<ArgumentOutOfRangeException>(
            () => player.AdvanceLegendLevel(Legend.Maximum + 1, 0L, Legend));

        player.AdvanceLegendLevel(Legend.Maximum, 199L, Legend);
        player.LegendLevel.ShouldBe(Legend.Maximum);
    }

    /// <summary>Nothing takes a Talent Point back.</summary>
    [Fact]
    public void A_negative_Talent_Point_grant_is_refused()
    {
        var player = Rehydrated(PlayerSnapshots.Valid);

        Should.Throw<ArgumentOutOfRangeException>(() => player.AdvanceLegendLevel(2, -1L, Legend));
    }

    [Fact]
    public void A_negative_persisted_Talent_Point_total_is_a_fault()
    {
        var result = Core.Model.Player.Rehydrate(PlayerSnapshots.With(talentPoints: -1L), Content);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain(nameof(PlayerSnapshot.TalentPoints));
    }

    /// <summary>🔒 A slot names an item the stock holds — it does not copy one.</summary>
    [Fact]
    public void A_loadout_naming_an_item_the_stock_does_not_hold_is_a_fault()
    {
        var result = Core.Model.Player.Rehydrate(
            PlayerSnapshots.With(loadout: new LoadoutSnapshot(
                PlayerSnapshots.Gear((GearSlot.WEAPON, "GI_GONE")))),
            Content);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("does not hold it");
    }

    /// <summary>Negative control: without it the fault could be firing on the loadout's mere presence.</summary>
    [Fact]
    public void The_same_loadout_loads_once_the_stock_holds_the_item()
    {
        Core.Model.Player.Rehydrate(WithStock("GI_1", GearSlot.WEAPON), Content)
            .IsSuccess.ShouldBeTrue();
    }

    /// <summary>
    /// Owned-but-unreachable is not gone: the reference resolves, so the row is not corrupt.
    /// Whether such an item may be EQUIPPED is <c>LoadoutRules</c>' question, and it says no.
    /// </summary>
    [Fact]
    public void An_item_waiting_in_overflow_is_still_owned()
    {
        var item = Inventories.Item("GI_HELD");

        var row = PlayerSnapshots.With(
            inventory: new InventorySnapshot(0, [], [Inventories.Persist(item)]),
            loadout: new LoadoutSnapshot(PlayerSnapshots.Gear((GearSlot.WEAPON, "GI_HELD"))));

        Core.Model.Player.Rehydrate(row, Content).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Saving_over_a_slot_replaces_it()
    {
        var player = Rehydrated(PlayerSnapshots.Valid);

        player.SavePreset(new LoadoutPreset(1, "Old", Loadout.Empty));
        player.SavePreset(new LoadoutPreset(1, "New", Loadout.Empty));

        player.Presets.Count.ShouldBe(1);
        player.Presets[1].Name.ShouldBe("New");
    }

    /// <summary>
    /// 🔒 The persisted shape is a LIST and the canonical writer preserves list order, so two
    /// players holding the same presets must not hash differently by save order.
    /// </summary>
    [Fact]
    public void The_order_presets_were_saved_in_is_not_state()
    {
        var forwards = Rehydrated(PlayerSnapshots.Valid);
        forwards.SavePreset(new LoadoutPreset(1, "A", Loadout.Empty));
        forwards.SavePreset(new LoadoutPreset(3, "C", Loadout.Empty));

        var backwards = Rehydrated(PlayerSnapshots.Valid);
        backwards.SavePreset(new LoadoutPreset(3, "C", Loadout.Empty));
        backwards.SavePreset(new LoadoutPreset(1, "A", Loadout.Empty));

        CanonicalStateWriter.CanonicalBytes(forwards.ToSnapshot())
            .ShouldBe(CanonicalStateWriter.CanonicalBytes(backwards.ToSnapshot()));

        forwards.ToSnapshot().Presets!.Select(p => p.Slot).ShouldBe(new[] { 1, 3 });
    }

    [Fact]
    public void Two_persisted_presets_in_one_slot_are_a_fault()
    {
        var row = PlayerSnapshots.With(presets:
        [
            new LoadoutPresetSnapshot(1, "A", new LoadoutSnapshot(PlayerSnapshots.Gear())),
            new LoadoutPresetSnapshot(1, "B", new LoadoutSnapshot(PlayerSnapshots.Gear())),
        ]);

        var result = Core.Model.Player.Rehydrate(row, Content);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("two presets in slot 1");
    }

    /// <summary>`12` §2.2 keeps presets a player may no longer write as presets they may still load.</summary>
    [Fact]
    public void An_absent_preset_list_is_a_fault()
    {
        var result = Core.Model.Player.Rehydrate(PlayerSnapshots.WithNull(presets: true), Content);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain(nameof(PlayerSnapshot.Presets));
    }

    [Fact]
    public void An_absent_loadout_is_its_own_fault()
    {
        var result = Core.Model.Player.Rehydrate(PlayerSnapshots.WithNull(loadout: true), Content);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain(nameof(PlayerSnapshot.Loadout));
        result.Error.ShouldNotContain(nameof(PlayerSnapshot.Presets) + " is null");
    }

    /// <summary>
    /// 🔒 A preset is a record of a build, not a claim of ownership — validating it like the live
    /// loadout would let a salvage corrupt a save.
    /// </summary>
    [Fact]
    public void A_preset_naming_an_item_the_player_no_longer_owns_still_loads()
    {
        var row = PlayerSnapshots.With(presets:
        [
            new LoadoutPresetSnapshot(1, "Old build", new LoadoutSnapshot(
                PlayerSnapshots.Gear((GearSlot.WEAPON, "GI_SALVAGED")))),
        ]);

        Core.Model.Player.Rehydrate(row, Content).IsSuccess.ShouldBeTrue(
            "12 §2.2 keeps a preset loadable; only the LIVE loadout must resolve against the stock.");
    }

    [Fact]
    public void The_hero_state_round_trips()
    {
        var player = Rehydrated(WithStock("GI_1", GearSlot.WEAPON));
        player.AdvanceLegendLevel(3, 2L, Legend);
        player.SavePreset(new LoadoutPreset(2, "Gold farm", player.Loadout));

        var round = Core.Model.Player.Rehydrate(player.ToSnapshot(), Content);

        round.IsSuccess.ShouldBeTrue();
        CanonicalStateWriter.CanonicalBytes(round.Value.ToSnapshot())
            .ShouldBe(CanonicalStateWriter.CanonicalBytes(player.ToSnapshot()));
    }

    private static Core.Model.Player Rehydrated(PlayerSnapshot snapshot)
    {
        var player = Core.Model.Player.Rehydrate(snapshot, Content);

        return player.IsSuccess
            ? player.Value
            : throw new InvalidOperationException(
                "The fixture PlayerSnapshot does not rehydrate: " + player.Error);
    }

    /// <summary>A player row holding one item, optionally worn in a slot.</summary>
    private static PlayerSnapshot WithStock(string itemId, GearSlot? worn = null) =>
        PlayerSnapshots.With(
            inventory: new InventorySnapshot(0, [Inventories.Persist(Inventories.Item(itemId))], []),
            loadout: worn is { } slot
                ? new LoadoutSnapshot(PlayerSnapshots.Gear((slot, itemId)))
                : null);
}
