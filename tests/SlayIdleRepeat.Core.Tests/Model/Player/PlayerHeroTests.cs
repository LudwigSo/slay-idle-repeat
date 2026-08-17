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
/// The hero's own state on the <c>Player</c> aggregate: the name, the Talent Points, the loadout and
/// the presets — and the invariants each of them holds on the way in and on the way out.
/// </summary>
public sealed class PlayerHeroTests
{
    private static readonly ContentSnapshot Content = TuningDocuments.Shipped;

    private static readonly LegendTuning Legend = LegendTuning.Read(Content);

    private static readonly ProfanityLexicon Lexicon =
        ProfanityLexicon.Read(ProfanityDocuments.Shipped);

    // ------------------------------------------------------------------------------- the name

    /// <summary>
    /// 🔒 The rename takes a <see cref="HeroName"/>, so there is no route into the field that skips
    /// the filter.
    /// </summary>
    /// <remarks>
    /// The seam `27` §1's "on every edit" rests on: only <c>HeroNameRule.Validate</c> can produce
    /// that type. A <c>Rename(string)</c> beside it would make the filter advisory.
    /// </remarks>
    [Fact]
    public void Renaming_takes_a_checked_name_and_stores_it_verbatim()
    {
        var player = Rehydrated(PlayerSnapshots.Valid);

        player.Rename(HeroNameRule.Validate("Ludwig", Lexicon).Name!);

        player.DisplayName.ShouldBe("Ludwig");
    }

    /// <summary>
    /// 🔒 A stored name is loaded as written: the word lists are content and must not be able to
    /// brick an account.
    /// </summary>
    /// <remarks>
    /// The whole reason the filter runs on mutation. A term added to <c>content/profanity/</c>
    /// tomorrow would otherwise make every account whose name matches it unloadable — an authoring
    /// edit becoming an outage, which is the trade the Energy ceiling and the inventory capacity
    /// already refuse.
    /// </remarks>
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

    /// <summary>A blank name is still the one thing rehydration refuses about it.</summary>
    [Fact]
    public void A_blank_stored_name_is_still_a_fault()
    {
        var result = Core.Model.Player.Rehydrate(PlayerSnapshots.With(displayName: "   "), Content);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("renders as nothing");
    }

    // ---------------------------------------------------------------- Legend Level and Talent Points

    /// <summary>A level-up writes the level and the points it granted together.</summary>
    [Fact]
    public void Advancing_a_level_writes_the_level_and_the_points_together()
    {
        var player = Rehydrated(PlayerSnapshots.Valid);

        player.AdvanceLegendLevel(4, 3L, Legend);

        player.LegendLevel.ShouldBe(4);
        player.TalentPoints.ShouldBe(3L);
    }

    /// <summary>Points accumulate across level-ups rather than being replaced.</summary>
    [Fact]
    public void Talent_Points_accumulate()
    {
        var player = Rehydrated(PlayerSnapshots.Valid);

        player.AdvanceLegendLevel(2, 1L, Legend);
        player.AdvanceLegendLevel(5, 3L, Legend);

        player.TalentPoints.ShouldBe(4L);
    }

    /// <summary>🔒 A Legend Level never goes backwards, whatever the curve says.</summary>
    [Fact]
    public void A_Legend_Level_never_goes_backwards()
    {
        var player = Rehydrated(PlayerSnapshots.With(legendLevel: 10));

        Should.Throw<ArgumentOutOfRangeException>(() => player.AdvanceLegendLevel(9, 0L, Legend))
            .Message.ShouldContain("only ever rises");
    }

    /// <summary>A level above the authored cap is refused, and the cap itself is not.</summary>
    [Fact]
    public void The_cap_is_the_documents_and_the_cap_itself_is_legal()
    {
        var player = Rehydrated(PlayerSnapshots.Valid);

        Should.Throw<ArgumentOutOfRangeException>(
            () => player.AdvanceLegendLevel(Legend.Maximum + 1, 0L, Legend));

        player.AdvanceLegendLevel(Legend.Maximum, 199L, Legend);
        player.LegendLevel.ShouldBe(Legend.Maximum);
    }

    /// <summary>A negative grant is refused: nothing takes a Talent Point back.</summary>
    [Fact]
    public void A_negative_Talent_Point_grant_is_refused()
    {
        var player = Rehydrated(PlayerSnapshots.Valid);

        Should.Throw<ArgumentOutOfRangeException>(() => player.AdvanceLegendLevel(2, -1L, Legend));
    }

    /// <summary>A negative persisted Talent Point total is a corrupt row.</summary>
    [Fact]
    public void A_negative_persisted_Talent_Point_total_is_a_fault()
    {
        var result = Core.Model.Player.Rehydrate(PlayerSnapshots.With(talentPoints: -1L), Content);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain(nameof(PlayerSnapshot.TalentPoints));
    }

    // ------------------------------------------------------------------------------ the loadout

    /// <summary>Equipping and unequipping move the aggregate's loadout.</summary>
    [Fact]
    public void Equipping_moves_the_players_loadout()
    {
        var player = Rehydrated(WithStock("GI_1"));

        player.Equip(GearSlot.WEAPON, new GearInstanceId("GI_1"));
        player.Loadout.EquippedCount.ShouldBe(1);

        player.Unequip(GearSlot.WEAPON);
        player.Loadout.EquippedCount.ShouldBe(0);
    }

    /// <summary>
    /// 🔒 Every equipped identity is one the stock holds — a slot names an item, it does not copy one.
    /// </summary>
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

    /// <summary>…and the same row loads once the stock actually holds the item.</summary>
    /// <remarks>
    /// The negative control for the case above. Without it, the fault could be firing on the
    /// loadout's mere presence rather than on the dangling identity, and nothing would say so.
    /// </remarks>
    [Fact]
    public void The_same_loadout_loads_once_the_stock_holds_the_item()
    {
        Core.Model.Player.Rehydrate(WithStock("GI_1", GearSlot.WEAPON), Content)
            .IsSuccess.ShouldBeTrue();
    }

    /// <summary>An item held in overflow still counts as owned for the dangling-reference check.</summary>
    /// <remarks>
    /// Owned and unreachable is not the same as gone: the reference resolves, so the row is not
    /// corrupt. Whether such an item may be EQUIPPED is <c>LoadoutRules</c>' question, and it says
    /// no — the two rules are deliberately different, and this pins that they are.
    /// </remarks>
    [Fact]
    public void An_item_waiting_in_overflow_is_still_owned()
    {
        var item = Inventories.Item("GI_HELD");

        var row = PlayerSnapshots.With(
            inventory: new InventorySnapshot(0, [], [Inventories.Persist(item)]),
            loadout: new LoadoutSnapshot(PlayerSnapshots.Gear((GearSlot.WEAPON, "GI_HELD"))));

        Core.Model.Player.Rehydrate(row, Content).IsSuccess.ShouldBeTrue();
    }

    // ------------------------------------------------------------------------------- the presets

    /// <summary>A saved preset is readable by its slot.</summary>
    [Fact]
    public void A_saved_preset_is_readable_by_slot()
    {
        var player = Rehydrated(PlayerSnapshots.Valid);

        player.SavePreset(new LoadoutPreset(2, "Boss push", Loadout.Empty));

        player.TryGetPreset(2, out var preset).ShouldBeTrue();
        preset!.Name.ShouldBe("Boss push");
        player.TryGetPreset(1, out _).ShouldBeFalse();
    }

    /// <summary>Saving over an occupied slot replaces it rather than adding a second.</summary>
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
    /// 🔒 Presets persist in ascending slot order whatever order they were saved in.
    /// </summary>
    /// <remarks>
    /// The persisted shape is a LIST and the canonical writer preserves a list's order, so two
    /// players holding the same three presets would otherwise hash differently depending on which
    /// slot each happened to save first — a client mirror that reported disagreement between two
    /// identical accounts.
    /// </remarks>
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

    /// <summary>Two presets in one slot is a corrupt row rather than an ordering accident.</summary>
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

    /// <summary>An absent preset list is a fault, never a player who has saved none.</summary>
    /// <remarks>
    /// `12` §2.2 keeps presets a player may no longer write as presets they may still load, so
    /// reading absent as empty deletes builds the design set promises to keep.
    /// </remarks>
    [Fact]
    public void An_absent_preset_list_is_a_fault()
    {
        var result = Core.Model.Player.Rehydrate(PlayerSnapshots.WithNull(presets: true), Content);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain(nameof(PlayerSnapshot.Presets));
    }

    /// <summary>An absent loadout is a fault, and it is told apart from an absent preset list.</summary>
    [Fact]
    public void An_absent_loadout_is_its_own_fault()
    {
        var result = Core.Model.Player.Rehydrate(PlayerSnapshots.WithNull(loadout: true), Content);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain(nameof(PlayerSnapshot.Loadout));
        result.Error.ShouldNotContain(nameof(PlayerSnapshot.Presets) + " is null");
    }

    /// <summary>
    /// 🔒 A preset naming an item the player no longer owns still loads — unlike the live loadout.
    /// </summary>
    /// <remarks>
    /// The asymmetry, asserted as an asymmetry. A preset is a record of a build rather than a claim
    /// of ownership: an item can be salvaged long after a preset named it, and validating a preset
    /// like the live loadout would let a salvage corrupt a save.
    /// </remarks>
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

    /// <summary>The whole hero state round-trips through the snapshot.</summary>
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

    /// <summary>A row a player snapshot fixture built, rehydrated or thrown.</summary>
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
