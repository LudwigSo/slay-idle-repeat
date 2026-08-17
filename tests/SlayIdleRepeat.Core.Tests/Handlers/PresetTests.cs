using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Tests.Content;
using SlayIdleRepeat.Core.Tests.Model;
using SlayIdleRepeat.Core.Tests.Model.Gear;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary>
/// <c>SAVE_PRESET</c> and <c>APPLY_PRESET</c> — `07` §4's three named loadout presets, the
/// <c>NOT_ENTITLED</c> allowance, and the mid-run refusal.
/// </summary>
public sealed class PresetTests
{
    private static readonly int FreeSlots = PresetTuning.Read(TuningDocuments.Shipped).FreeSlots;

    // ------------------------------------------------------------------------------ SAVE_PRESET

    /// <summary>Saving writes the hero's current loadout into the named slot.</summary>
    [Fact]
    public void Saving_records_what_the_hero_is_wearing()
    {
        var result = Save(Outside(Wearing("GI_1", GearSlot.WEAPON)), 1, "Boss push");

        result.Accepted.ShouldBeTrue();

        result.NewState.Player.TryGetPreset(1, out var preset).ShouldBeTrue();
        preset!.Name.ShouldBe("Boss push");
        preset.Loadout.TryGet(GearSlot.WEAPON, out var item).ShouldBeTrue();
        item.Value.ShouldBe("GI_1");
    }

    /// <summary>
    /// Every slot inside the authored free allowance is writable without Plus, and the first slot
    /// past it is not.
    /// </summary>
    /// <remarks>
    /// Both sides of the boundary, driven off the tunable rather than off the literal 3: a handler
    /// that compared against a hard-coded number would pass this while ignoring the document.
    /// `14` §662's own example of <c>NOT_ENTITLED</c> is "preset slot 4+".
    /// </remarks>
    [Fact]
    public void The_free_allowance_is_the_authored_one_and_the_slot_past_it_is_NOT_ENTITLED()
    {
        FreeSlots.ShouldBe(3, "12 §2: free players get 3, and ads.json#/plus/freePresets authors it.");

        for (var slot = PresetTuning.FirstSlot; slot <= FreeSlots; slot++)
        {
            Save(Outside(PlayerSnapshots.Valid), slot, "Build").Accepted.ShouldBeTrue(
                $"slot {slot} is inside the free allowance.");
        }

        var refused = Save(Outside(PlayerSnapshots.Valid), FreeSlots + 1, "Fourth");

        refused.Accepted.ShouldBeFalse();
        refused.Rejection.ShouldBe(RejectionReason.NOT_ENTITLED);
    }

    /// <summary>Plus lifts the allowance entirely: `12` §2 grants unlimited slots.</summary>
    [Fact]
    public void Plus_may_write_a_slot_past_the_free_allowance()
    {
        var result = Save(Outside(PlayerSnapshots.Valid), FreeSlots + 1, "Fourth", plus: true);

        result.Accepted.ShouldBeTrue();
        result.NewState.Player.TryGetPreset(FreeSlots + 1, out _).ShouldBeTrue();
    }

    /// <summary>
    /// A slot below the first and a blank name are refused as illegal moves, not as entitlement.
    /// </summary>
    /// <remarks>
    /// Told apart from <c>NOT_ENTITLED</c> deliberately: a player being told they need a
    /// subscription because they sent a malformed payload is the worst possible message, and a
    /// suite asserting only "refused" could not see the difference.
    /// </remarks>
    [Theory]
    [InlineData(0, "Build")]
    [InlineData(-1, "Build")]
    [InlineData(1, "   ")]
    public void A_malformed_preset_is_ILLEGAL_STATE_rather_than_NOT_ENTITLED(int slot, string name)
    {
        var result = Save(Outside(PlayerSnapshots.Valid), slot, name);

        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    /// <summary>Saving mid-run is allowed: it records the build, it does not change it.</summary>
    /// <remarks>
    /// `07` §4 forbids CHANGING the loadout during a run, which is <c>APPLY_PRESET</c>'s problem.
    /// Refusing here would block the moment a player is most likely to want a preset saved.
    /// </remarks>
    [Fact]
    public void Saving_is_allowed_during_a_run()
    {
        Save(InARun(PlayerSnapshots.Valid), 1, "Boss push").Accepted.ShouldBeTrue();
    }

    /// <summary>A preset carries no currency, so an accepted save publishes no event.</summary>
    [Fact]
    public void Saving_publishes_no_event()
    {
        Save(Outside(PlayerSnapshots.Valid), 1, "Boss push").Events.ShouldBeEmpty();
    }

    // ----------------------------------------------------------------------------- APPLY_PRESET

    /// <summary>Applying wears what the preset stored.</summary>
    [Fact]
    public void Applying_wears_what_the_preset_stored()
    {
        var saved = Save(Outside(Wearing("GI_1", GearSlot.WEAPON)), 1, "Boss push").NewState;
        var stripped = Unequipped(saved);

        var result = SlayIdleRepeat.Core.GameRules.Apply(
            stripped, new ApplyPresetCommand(1), Worlds.Context);

        result.Accepted.ShouldBeTrue();
        result.NewState.Player.Loadout.TryGet(GearSlot.WEAPON, out var item).ShouldBeTrue();
        item.Value.ShouldBe("GI_1");
    }

    /// <summary>Applying an empty slot is an illegal move rather than a silent strip.</summary>
    [Fact]
    public void Applying_a_slot_that_holds_no_preset_is_refused()
    {
        var result = SlayIdleRepeat.Core.GameRules.Apply(
            Outside(Wearing("GI_1", GearSlot.WEAPON)), new ApplyPresetCommand(2), Worlds.Context);

        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
        result.NewState.Player.Loadout.EquippedCount.ShouldBe(1, "a refusal changes nothing.");
    }

    /// <summary>🔒 Applying is refused during a run — `07` §4's "cannot be changed during a run".</summary>
    [Fact]
    public void Applying_is_refused_during_a_run()
    {
        var saved = Save(InARun(Wearing("GI_1", GearSlot.WEAPON)), 1, "Boss push").NewState;

        var result = SlayIdleRepeat.Core.GameRules.Apply(
            saved, new ApplyPresetCommand(1), Worlds.Context);

        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    /// <summary>
    /// 🔒 Applying is never entitlement-gated: `12` §66 keeps a preset past the allowance READ-ONLY.
    /// </summary>
    /// <remarks>
    /// Saved with Plus, applied without it. A handler that copied <c>SAVE_PRESET</c>'s check would
    /// pass every other case in this suite and delete the one promise `12` §66 makes to a lapsed
    /// subscriber.
    /// </remarks>
    [Fact]
    public void A_preset_past_the_free_allowance_is_still_applicable_without_Plus()
    {
        var saved = Save(
            Outside(Wearing("GI_1", GearSlot.WEAPON)), FreeSlots + 1, "Fourth", plus: true).NewState;

        var result = SlayIdleRepeat.Core.GameRules.Apply(
            Unequipped(saved), new ApplyPresetCommand(FreeSlots + 1), Worlds.Context);

        result.Accepted.ShouldBeTrue();
        result.NewState.Player.Loadout.EquippedCount.ShouldBe(1);
    }

    /// <summary>
    /// 🔒 Applying restores what the player still owns and silently leaves the rest.
    /// </summary>
    /// <remarks>
    /// The best-effort ruling, and both halves of it: the surviving item comes back, the missing one
    /// does not, and the preset itself is unchanged so the item can come back with it later.
    /// </remarks>
    [Fact]
    public void Applying_restores_what_is_still_owned_and_leaves_the_preset_alone()
    {
        var preset = new LoadoutPresetSnapshot(
            1,
            "Old build",
            new LoadoutSnapshot(PlayerSnapshots.Gear(
                (GearSlot.WEAPON, "GI_1"), (GearSlot.RING, "GI_SALVAGED"))));

        var world = Outside(PlayerSnapshots.With(
            inventory: new InventorySnapshot(0, [Inventories.Persist(Inventories.Item("GI_1"))], []),
            presets: [preset]));

        var result = SlayIdleRepeat.Core.GameRules.Apply(
            world, new ApplyPresetCommand(1), Worlds.Context);

        result.Accepted.ShouldBeTrue();
        result.NewState.Player.Loadout.EquippedCount.ShouldBe(1);
        result.NewState.Player.Loadout.TryGet(GearSlot.RING, out _).ShouldBeFalse();

        result.NewState.Player.TryGetPreset(1, out var stored).ShouldBeTrue();
        stored!.Loadout.EquippedCount.ShouldBe(
            2, "the preset is a record of a build; applying it does not rewrite it.");
    }

    /// <summary>Applying replaces the whole loadout rather than merging into it.</summary>
    /// <remarks>
    /// A merge would leave the player wearing pieces of two builds with no way to tell which — the
    /// case that makes "one tap fully reconfigures the character" false.
    /// </remarks>
    [Fact]
    public void Applying_replaces_the_loadout_rather_than_merging_into_it()
    {
        var stock = new InventorySnapshot(
            0,
            [Inventories.Persist(Inventories.Item("GI_1")), Inventories.Persist(Inventories.Item("GI_2"))],
            []);

        var world = Outside(PlayerSnapshots.With(
            inventory: stock,
            loadout: new LoadoutSnapshot(PlayerSnapshots.Gear((GearSlot.RING, "GI_2"))),
            presets:
            [
                new LoadoutPresetSnapshot(1, "Weapon only", new LoadoutSnapshot(
                    PlayerSnapshots.Gear((GearSlot.WEAPON, "GI_1")))),
            ]));

        var result = SlayIdleRepeat.Core.GameRules.Apply(
            world, new ApplyPresetCommand(1), Worlds.Context);

        result.NewState.Player.Loadout.EquippedCount.ShouldBe(1);
        result.NewState.Player.Loadout.TryGet(GearSlot.RING, out _).ShouldBeFalse();
    }

    // ------------------------------------------------------------------------------- the worlds

    private static CommandResult Save(
        WorldSlice world, int slot, string name, bool plus = false) =>
        SlayIdleRepeat.Core.GameRules.Apply(
            world,
            new SavePresetCommand(slot, name),
            plus
                ? Worlds.Context with { Entitlements = TestSupport.GameContexts.WithPlus }
                : Worlds.Context);

    private static WorldSlice Outside(PlayerSnapshot player) =>
        new(Worlds.Rehydrated(player), null);

    private static WorldSlice InARun(PlayerSnapshot player) =>
        new(Worlds.Rehydrated(player), Worlds.NewRun());

    /// <summary>A player row holding one item, worn in a slot.</summary>
    private static PlayerSnapshot Wearing(string itemId, GearSlot slot) =>
        PlayerSnapshots.With(
            inventory: new InventorySnapshot(0, [Inventories.Persist(Inventories.Item(itemId))], []),
            loadout: new LoadoutSnapshot(PlayerSnapshots.Gear((slot, itemId))));

    /// <summary>The same slice with the hero stripped, so an APPLY has something to restore.</summary>
    private static WorldSlice Unequipped(WorldSlice world)
    {
        var row = world.Player.ToSnapshot() with
        {
            Loadout = new LoadoutSnapshot(PlayerSnapshots.Gear()),
        };

        return new WorldSlice(Worlds.Rehydrated(row), world.Run);
    }
}
