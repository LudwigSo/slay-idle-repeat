using Shouldly;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Tests;
using SlayIdleRepeat.Core.Tests.Content;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Model;

/// <summary>
/// The pity counters as a field of the <c>Player</c> aggregate: they round-trip, they reach the
/// canonical bytes, and an absent map is a fault rather than an empty one.
/// </summary>
/// <remarks>
/// 🔒 Compared by <b>canonical bytes</b> throughout, never by record equality: the map is a
/// collection component, so a synthesized <c>Equals</c> compares it by reference and two snapshots
/// carrying different counters in different dictionary instances are unequal for a reason that has
/// nothing to do with their contents.
/// </remarks>
public sealed class PlayerPityCounterTests
{
    private static SlayIdleRepeat.Core.Content.ContentSnapshot Content => TuningDocuments.Shipped;

    /// <summary>A counter id formed the way production forms it.</summary>
    private static string ChestLadder =>
        SlayIdleRepeat.Core.Content.LuckTuning.Read(LuckDocuments.LuckOnly())
            .CounterKey(Core.Primitives.SourceClass.CHEST_STANDARD, Core.Primitives.Rarity.A);

    private static Core.Model.Player Rehydrate(PlayerSnapshot snapshot) =>
        Worlds.Rehydrated(snapshot);

    // ------------------------------------------------------------------ the schema version

    /// <summary>
    /// The counters arrived with a <c>SchemaVersion</c> bump, because a snapshot record gained a field.
    /// </summary>
    [Fact]
    public void The_snapshot_schema_moved_for_this_field()
    {
        SnapshotSchema.SchemaVersion.ShouldBe(
            10,
            "PlayerSnapshot gained PityCounters and RunSnapshot gained the three draft counters. " +
            "14 §16.6 makes an added field a versioned migration, never silent.");
    }

    // ------------------------------------------------------------------ round trip

    /// <summary>A stored counter map comes back exactly as it went in.</summary>
    [Fact]
    public void A_stored_counter_map_round_trips()
    {
        var stored = PlayerSnapshots.With(
            pityCounters: PlayerSnapshots.Pity((ChestLadder, 7), ("egg.pet:S", 2)));

        var round = Rehydrate(stored).ToSnapshot();

        round.PityCounters!.Count.ShouldBe(2);
        round.PityCounters[ChestLadder].ShouldBe(7);
        round.PityCounters["egg.pet:S"].ShouldBe(2);
    }

    /// <summary>The whole row round-trips byte-identically.</summary>
    /// <remarks>
    /// The canonical bytes rather than the record: a per-field comparison would miss a counter map
    /// that survived the aggregate and never reached the encoder, which is the failure a
    /// <c>stateHash</c> is for.
    /// </remarks>
    [Fact]
    public void The_row_round_trips_byte_identically()
    {
        var stored = PlayerSnapshots.With(pityCounters: PlayerSnapshots.Pity((ChestLadder, 7)));

        CanonicalStateWriter.CanonicalBytes(Rehydrate(stored).ToSnapshot())
            .ShouldBe(CanonicalStateWriter.CanonicalBytes(stored));
    }

    /// <summary>Two players one chest apart on the same ladder produce different canonical bytes.</summary>
    [Fact]
    public void Moving_one_counter_changes_the_canonical_bytes()
    {
        var before = PlayerSnapshots.With(pityCounters: PlayerSnapshots.Pity((ChestLadder, 7)));
        var after = PlayerSnapshots.With(pityCounters: PlayerSnapshots.Pity((ChestLadder, 8)));

        CanonicalStateWriter.CanonicalBytes(after).ShouldNotBe(
            CanonicalStateWriter.CanonicalBytes(before),
            "one chest apart on the same ladder is a material difference — the next open is forced " +
            "for one of them and not the other.");
    }

    /// <summary>And so does a counter that exists on one row and not the other.</summary>
    /// <remarks>
    /// The second probe. A writer that encoded only the values and not the keys would pass the case
    /// above and would let two players with the same numbers on different ladders share a hash.
    /// </remarks>
    [Fact]
    public void A_counter_under_a_different_key_changes_the_canonical_bytes()
    {
        var onOneLadder = PlayerSnapshots.With(pityCounters: PlayerSnapshots.Pity((ChestLadder, 7)));
        var onAnother = PlayerSnapshots.With(pityCounters: PlayerSnapshots.Pity(("egg.pet:S", 7)));

        CanonicalStateWriter.CanonicalBytes(onAnother)
            .ShouldNotBe(CanonicalStateWriter.CanonicalBytes(onOneLadder));
    }

    // ------------------------------------------------------------------ the mutator

    /// <summary>The aggregate's one writer stores a counter's value, not a delta.</summary>
    [Fact]
    public void The_aggregate_stores_the_value_a_resolution_answered()
    {
        var player = Rehydrate(PlayerSnapshots.Valid);

        player.SetPityCounter(ChestLadder, 4);
        player.PityCounters.Get(ChestLadder).ShouldBe(4);

        player.SetPityCounter(ChestLadder, 0);
        player.PityCounters.Get(ChestLadder).ShouldBe(
            0,
            "a reset is a value, not a negative delta — the two movements a counter makes are an " +
            "advance and a reset.");
    }

    /// <summary>A counter never advanced reads zero rather than throwing.</summary>
    [Fact]
    public void An_unstarted_counter_reads_zero()
    {
        Rehydrate(PlayerSnapshots.Valid).PityCounters.Get(ChestLadder).ShouldBe(0);
    }

    // ------------------------------------------------------------------ the null asymmetry

    /// <summary>An absent counter map is a fault, not an empty one.</summary>
    /// <remarks>
    /// The same asymmetry the lifetime feat counters carry, for a sharper reason: a pity map read as
    /// empty is every ladder in the game silently starting over, which is exactly what `24` §1.1's
    /// "counters never reset" exists to forbid — and it would be invisible in every test that builds
    /// its own map.
    /// </remarks>
    [Fact]
    public void A_null_counter_map_is_refused()
    {
        var result = Core.Model.Player.Rehydrate(PlayerSnapshots.WithNull(pity: true), Content);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain(nameof(PlayerSnapshot.PityCounters), Case.Sensitive);
    }

    /// <summary>An empty map is not: a brand-new player has drawn nothing.</summary>
    [Fact]
    public void An_empty_counter_map_is_accepted()
    {
        Core.Model.Player.Rehydrate(PlayerSnapshots.With(pityCounters: PlayerSnapshots.Pity()), Content)
            .IsSuccess.ShouldBeTrue();
    }

    /// <summary>A negative stored counter is refused rather than loaded.</summary>
    [Fact]
    public void A_negative_stored_counter_is_refused()
    {
        var result = Core.Model.Player.Rehydrate(
            PlayerSnapshots.With(pityCounters: PlayerSnapshots.Pity((ChestLadder, -1))), Content);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain(nameof(PlayerSnapshot.PityCounters), Case.Sensitive);
    }

    /// <summary>A blank counter id is refused: it addresses every counter and none.</summary>
    [Fact]
    public void A_blank_stored_counter_id_is_refused()
    {
        var result = Core.Model.Player.Rehydrate(
            PlayerSnapshots.With(pityCounters: PlayerSnapshots.Pity(("   ", 1))), Content);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain(nameof(PlayerSnapshot.PityCounters), Case.Sensitive);
    }
}
