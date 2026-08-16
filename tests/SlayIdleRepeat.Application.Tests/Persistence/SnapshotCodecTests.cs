using System.Text;
using System.Text.Json;
using Shouldly;
using SlayIdleRepeat.Application.Services.Persistence;
using SlayIdleRepeat.Application.Tests.UseCases;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Persistence;

/// <summary>
/// The codec round-trip, compared by canonical hash rather than by record equality — these rows carry
/// dictionaries, which compare by reference.
/// </summary>
public sealed class SnapshotCodecTests
{
    /// <summary>The lowest snapshot layout this build is written against.</summary>
    /// <remarks>A floor rather than the exact number, so a later migration bumping it does not fail this case.</remarks>
    private const int SchemaFloor = 13;

    [Fact]
    public void DecodeSlice_returns_a_player_row_identical_to_the_one_EncodeSlice_was_given()
    {
        var (game, player) = Worlds.InAPlayedRun();
        var original = Worlds.Stored(game.State(player));

        var decoded = SnapshotCodec.DecodeSlice(SnapshotCodec.EncodeSlice(original));

        Worlds.Hash(decoded).ShouldBe(
            Worlds.Hash(original),
            "a field lost, reordered or re-typed by the codec changes the canonical bytes, and a player " +
            "would come back from storage missing whatever it was.");
    }

    /// <summary>
    /// The discriminating half of the round-trip: the run carries the shapes a naive codec drops — a
    /// string-keyed counter map, an int-keyed map, and optional integers.
    /// </summary>
    [Fact]
    public void DecodeSlice_returns_a_run_row_carrying_the_draw_counters_it_was_given()
    {
        var (game, player) = Worlds.InAPlayedRun();
        var original = Worlds.Stored(game.State(player));

        original.Run!.RngStreamPositions.ShouldNotBeEmpty(
            "this case exists to round-trip a populated counter map; an empty one would round-trip " +
            "through a codec that dropped maps entirely.");

        var decoded = SnapshotCodec.DecodeSlice(SnapshotCodec.EncodeSlice(original));

        decoded.Run.ShouldNotBeNull("the run was dropped by the round-trip.");
        Worlds.RunHash(original.Player, decoded.Run!).ShouldBe(
            Worlds.RunHash(original.Player, original.Run),
            "the run came back differing from the one stored; the same player row is hashed on both " +
            "sides, so the run is the only thing that can have moved.");
    }

    /// <summary>
    /// The half of the player row every other case leaves empty. A fresh player owns nothing, so the
    /// gear tree — instance ids, affix rolls, the equipped map, the saved presets and the salvage
    /// filter — round-trips through the codec only if a case puts something in it, and each of those
    /// members reaches a value type the serializer builds through a zeroed default unless told not to.
    /// </summary>
    [Fact]
    public void DecodeSlice_returns_a_player_row_carrying_the_gear_tree_it_was_given()
    {
        var (game, player) = Worlds.InAPlayedRun();
        var original = Worlds.Stored(game.State(player));
        var outfitted = original with { Player = Outfitted(original.Player) };

        var decoded = SnapshotCodec.DecodeSlice(SnapshotCodec.EncodeSlice(outfitted));

        Worlds.Hash(decoded).ShouldBe(
            Worlds.Hash(outfitted),
            "a member of the gear tree came back as its type's zeroed default — an instance with no " +
            "id, an affix with no roll, a slot pointing at nothing. The row decodes and the aggregate " +
            "rehydrates either way, so nothing but this comparison would notice.");

        // Named individually as well, so a failure says which member moved rather than only that the
        // bytes differ. The affix value is the one double here, and it is refused unless rounded.
        var item = decoded.Player.Inventory!.Stored[0];

        item.InstanceId.ShouldBe(Equipped, "the stored item's identity did not survive the round trip.");
        item.Affixes[0].Value.ShouldBe(AffixValue, "the affix roll did not survive the round trip.");
        decoded.Player.Loadout!.Gear[GearSlot.WEAPON].ShouldBe(Equipped, "the equipped slot lost its item.");
        decoded.Player.Presets![0].Loadout.Gear[GearSlot.WEAPON].ShouldBe(Equipped, "the preset lost its item.");
        decoded.Player.AutoSalvageRules![0].ShouldBe(
            SalvageRule, "the salvage filter's row came back as a different rule.");
    }

    [Fact]
    public void DecodeSlice_preserves_the_absence_of_a_run()
    {
        var game = Worlds.Game();
        var player = game.CreatePlayer();
        var original = Worlds.Stored(game.State(player));

        var decoded = SnapshotCodec.DecodeSlice(SnapshotCodec.EncodeSlice(original));

        decoded.Run.ShouldBeNull(
            "a player outside a run must not come back holding one — an invented empty run would be a " +
            "run the domain then refuses to start over.");
    }

    [Fact]
    public void DecodeRun_returns_a_run_identical_to_the_one_EncodeRun_was_given()
    {
        var (game, player) = Worlds.InAPlayedRun();
        var slice = game.State(player);
        var original = slice.Run!.ToSnapshot();

        var decoded = SnapshotCodec.DecodeRun(SnapshotCodec.EncodeRun(original));

        Worlds.RunHash(slice.Player.ToSnapshot(), decoded).ShouldBe(
            Worlds.RunHash(slice.Player.ToSnapshot(), original),
            "the archived copy of a finished run is the only record of it left once the next run " +
            "starts, so a field lost here is lost for good.");
    }

    [Fact]
    public void DecodeSlice_preserves_the_layout_version_the_row_was_written_at()
    {
        var (game, player) = Worlds.InAPlayedRun();
        var original = Worlds.Stored(game.State(player));

        var decoded = SnapshotCodec.DecodeSlice(SnapshotCodec.EncodeSlice(original));

        decoded.Player.SchemaVersion.ShouldBe(
            original.Player.SchemaVersion,
            "the version a row was written at is what the aggregate's own loader checks; rewriting it " +
            "on the way through would make an unreadable row look readable.");

        decoded.Player.SchemaVersion.ShouldBeGreaterThanOrEqualTo(
            SchemaFloor,
            "the fixture is writing rows at a layout older than this build has ever shipped, so the " +
            "round-trip above is not exercising the current snapshot shape.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("not json at all")]
    [InlineData("[1,2,3]")]
    public void DecodeSlice_refuses_bytes_that_are_not_a_stored_slice(string text)
    {
        Should.Throw<JsonException>(() => SnapshotCodec.DecodeSlice(Encoding.UTF8.GetBytes(text)))
            .Message.ShouldNotBeEmpty(
                "a row that cannot be read is not a player who has nothing: answering with a blank or " +
                "half-filled state would hand the player a fresh account and overwrite the real one on " +
                "the next command.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("not json at all")]
    public void DecodeRun_refuses_bytes_that_are_not_a_run_row(string text)
    {
        Should.Throw<JsonException>(() => SnapshotCodec.DecodeRun(Encoding.UTF8.GetBytes(text)))
            .Message.ShouldNotBeEmpty("an unreadable archive row is an error, not an absent run.");
    }

    /// <summary>
    /// Structurally valid JSON whose contents a value type in the tree refuses. It is the row being
    /// unreadable, and it has to arrive as that rather than as the argument failure the store also
    /// raises when a caller hands it an id it cannot key on — a caller seeing only the type would
    /// otherwise read "this row is corrupt" as "you passed the wrong player".
    /// </summary>
    [Fact]
    public void DecodeSlice_refuses_a_row_whose_identifier_is_blank()
    {
        var (game, player) = Worlds.InAPlayedRun();
        var blanked = Encoding.UTF8.GetString(SnapshotCodec.EncodeSlice(Worlds.Stored(game.State(player))))
            .Replace("\"" + player.Value + "\"", "\"\"", StringComparison.Ordinal);

        Should.Throw<JsonException>(() => SnapshotCodec.DecodeSlice(Encoding.UTF8.GetBytes(blanked)))
            .Message.ShouldContain(
                nameof(PlayerId),
                Case.Sensitive,
                "the failure has to name what the row got wrong. A blank id decoded silently would be " +
                "an aggregate that names nothing, refusable afterwards only by a lookup finding no rows.");
    }

    // ═════════════════════════════════════════════════════════════════ fixtures

    /// <summary>The one item the gear case owns, named so the assertions can point at it.</summary>
    private static readonly GearInstanceId Equipped = new("GEAR_000000000001");

    /// <summary>A roll at the assembly's determinism precision — the value type refuses an unrounded one.</summary>
    private const double AffixValue = 0.1234;

    /// <summary>One row of a salvage filter, the only <c>readonly record struct</c> here with no converter.</summary>
    private static readonly AutoSalvageRule SalvageRule = new(Rarity.B, BelowEnhanceLevel: 3);

    /// <summary>The same player row, wearing something.</summary>
    private static PlayerSnapshot Outfitted(PlayerSnapshot player)
    {
        var item = new GearInstanceSnapshot(
            Equipped,
            "GEAR_BLADE_01",
            GearSlot.WEAPON,
            GearFamily.BLADE,
            Rarity.A,
            ChapterOrigin: 2,
            Quality: 0.8125,
            EnhanceLevel: 4,
            EnhanceFailures: 1,
            [new GearAffixRoll("AFX_CRIT_CHANCE", AffixValue), new GearAffixRoll("AFX_ATK_FLAT", 12.5)],
            Locked: true);

        var loadout = new LoadoutSnapshot(
            new Dictionary<GearSlot, GearInstanceId> { [GearSlot.WEAPON] = Equipped });

        return player with
        {
            Inventory = new InventorySnapshot(ExpansionsPurchased: 2, [item], [item]),
            Loadout = loadout,
            Presets = [new LoadoutPresetSnapshot(1, "Boss build", loadout)],
            AutoSalvageRules = [SalvageRule],
        };
    }
}
