using Shouldly;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Tests.Content;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Model;

/// <summary>
/// The pity counters as a field of the <c>Player</c> aggregate. Compared by canonical bytes, never
/// record equality — a synthesized <c>Equals</c> compares the map component by reference. Pity
/// behaviour itself is covered at the <c>Apply</c> seam by <c>MinigameChestPickPityTests</c>.
/// </summary>
public sealed class PlayerPityCounterTests
{
    private static SlayIdleRepeat.Core.Content.ContentSnapshot Content => TuningDocuments.Shipped;

    /// <summary>A counter id formed the way production forms it.</summary>
    private static string ChestLadder =>
        SlayIdleRepeat.Core.Content.LuckTuning.Read(LuckDocuments.LuckOnly())
            .CounterKey(Core.Primitives.SourceClass.CHEST_STANDARD, Core.Primitives.Rarity.A);

    private static Core.Model.Player Rehydrate(PlayerSnapshot snapshot) =>
        Worlds.Rehydrated(snapshot);

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

    /// <summary>Canonical bytes rather than fields: a map that survived the aggregate but never reached the encoder passes a per-field check.</summary>
    [Fact]
    public void The_row_round_trips_byte_identically()
    {
        var stored = PlayerSnapshots.With(pityCounters: PlayerSnapshots.Pity((ChestLadder, 7)));

        CanonicalStateWriter.CanonicalBytes(Rehydrate(stored).ToSnapshot())
            .ShouldBe(CanonicalStateWriter.CanonicalBytes(stored));
    }

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

    /// <summary>A writer that encoded only values would pass the case above while two players on different ladders shared a hash.</summary>
    [Fact]
    public void A_counter_under_a_different_key_changes_the_canonical_bytes()
    {
        var onOneLadder = PlayerSnapshots.With(pityCounters: PlayerSnapshots.Pity((ChestLadder, 7)));
        var onAnother = PlayerSnapshots.With(pityCounters: PlayerSnapshots.Pity(("egg.pet:S", 7)));

        CanonicalStateWriter.CanonicalBytes(onAnother)
            .ShouldNotBe(CanonicalStateWriter.CanonicalBytes(onOneLadder));
    }

    /// <summary>A pity map read as empty is every ladder silently starting over — what `24` §1.1's "counters never reset" forbids.</summary>
    [Fact]
    public void A_null_counter_map_is_refused()
    {
        var result = Core.Model.Player.Rehydrate(PlayerSnapshots.WithNull(pity: true), Content);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain(nameof(PlayerSnapshot.PityCounters), Case.Sensitive);
    }

    /// <summary>An empty map is not a fault: a brand-new player has drawn nothing.</summary>
    [Fact]
    public void An_empty_counter_map_is_accepted()
    {
        Core.Model.Player.Rehydrate(PlayerSnapshots.With(pityCounters: PlayerSnapshots.Pity()), Content)
            .IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void A_negative_stored_counter_is_refused()
    {
        var result = Core.Model.Player.Rehydrate(
            PlayerSnapshots.With(pityCounters: PlayerSnapshots.Pity((ChestLadder, -1))), Content);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain(nameof(PlayerSnapshot.PityCounters), Case.Sensitive);
    }

    [Fact]
    public void A_blank_stored_counter_id_is_refused()
    {
        var result = Core.Model.Player.Rehydrate(
            PlayerSnapshots.With(pityCounters: PlayerSnapshots.Pity(("   ", 1))), Content);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain(nameof(PlayerSnapshot.PityCounters), Case.Sensitive);
    }
}
