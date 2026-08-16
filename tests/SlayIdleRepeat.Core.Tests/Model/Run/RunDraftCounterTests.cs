using Shouldly;
using SlayIdleRepeat.Core.Model.Snapshots;
using Xunit;
using RunAggregate = SlayIdleRepeat.Core.Model.Run;

namespace SlayIdleRepeat.Core.Tests.Model;

/// <summary>
/// The three run-scoped <c>DRAFT</c> counters on the <c>Run</c> aggregate: they round-trip, they
/// reach the canonical bytes, and a negative one is refused.
/// </summary>
/// <remarks>
/// 🔒 They are plain integers on the run rather than entries in the player's pity counter map, and
/// that is a design claim rather than a convenience: `24` §3 scopes <c>DRAFT</c> per run and
/// <c>luck.json</c> authors its <c>counterKey</c> as an explicit null, so there is no id to form and
/// nothing the profile could store them under. The counter-key formation point refuses to invent one
/// — see <c>ChestPickGuaranteeTests</c>.
/// </remarks>
public sealed class RunDraftCounterTests
{
    private static RunAggregate Rehydrate(RunSnapshot snapshot) => Worlds.NewRun(snapshot);

    // ------------------------------------------------------------------ round trip

    /// <summary>The three counters come back exactly as they went in.</summary>
    [Fact]
    public void The_three_draft_counters_round_trip()
    {
        var stored = RunSnapshots.With(
            draftsSinceLegendaryOffered: 11,
            draftsWithoutAboveCommon: 2,
            draftsWithoutOwnedUpgrade: 4);

        var round = Rehydrate(stored).ToSnapshot();

        round.DraftsSinceLegendaryOffered.ShouldBe(11);
        round.DraftsWithoutAboveCommon.ShouldBe(2);
        round.DraftsWithoutOwnedUpgrade.ShouldBe(4);
    }

    /// <summary>The whole row round-trips byte-identically.</summary>
    [Fact]
    public void The_row_round_trips_byte_identically()
    {
        var stored = RunSnapshots.With(
            draftsSinceLegendaryOffered: 11,
            draftsWithoutAboveCommon: 2,
            draftsWithoutOwnedUpgrade: 4);

        CanonicalStateWriter.CanonicalBytes(Rehydrate(stored).ToSnapshot())
            .ShouldBe(CanonicalStateWriter.CanonicalBytes(stored));
    }

    /// <summary>Each of the three moves the canonical bytes on its own.</summary>
    /// <remarks>
    /// One case per counter rather than one over all three: they move independently, and a writer
    /// that reached only the first would be invisible to a probe that changed all of them at once.
    /// </remarks>
    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(0, 1, 0)]
    [InlineData(0, 0, 1)]
    public void Moving_any_one_draft_counter_changes_the_canonical_bytes(
        int legendary, int common, int upgrade)
    {
        var baseline = RunSnapshots.With();
        var moved = RunSnapshots.With(
            draftsSinceLegendaryOffered: legendary,
            draftsWithoutAboveCommon: common,
            draftsWithoutOwnedUpgrade: upgrade);

        CanonicalStateWriter.CanonicalBytes(moved).ShouldNotBe(
            CanonicalStateWriter.CanonicalBytes(baseline),
            "two runs one draft apart on the same guarantee are materially different runs — the next " +
            "draft is floored for one of them and not the other.");
    }

    // ------------------------------------------------------------------ the mutator

    /// <summary>The aggregate's one writer stores values, not deltas.</summary>
    [Fact]
    public void The_aggregate_stores_the_values_a_resolution_answered()
    {
        var run = Rehydrate(RunSnapshots.Valid);

        run.SetDraftCounters(sinceLegendaryOffered: 9, withoutAboveCommon: 1, withoutOwnedUpgrade: 3);

        run.DraftsSinceLegendaryOffered.ShouldBe(9);
        run.DraftsWithoutAboveCommon.ShouldBe(1);
        run.DraftsWithoutOwnedUpgrade.ShouldBe(3);

        run.SetDraftCounters(sinceLegendaryOffered: 0, withoutAboveCommon: 0, withoutOwnedUpgrade: 0);

        run.DraftsSinceLegendaryOffered.ShouldBe(0);
    }

    /// <summary>A negative value is refused by the mutator.</summary>
    [Fact]
    public void A_negative_counter_is_refused_by_the_mutator()
    {
        var run = Rehydrate(RunSnapshots.Valid);

        Should.Throw<ArgumentOutOfRangeException>(() => run.SetDraftCounters(-1, 0, 0));
        Should.Throw<ArgumentOutOfRangeException>(() => run.SetDraftCounters(0, -1, 0));
        Should.Throw<ArgumentOutOfRangeException>(() => run.SetDraftCounters(0, 0, -1));
    }

    // ------------------------------------------------------------------ the seam

    /// <summary>A stored negative counter is refused at the rehydration seam, one fault per field.</summary>
    [Theory]
    [InlineData(-1, 0, 0, nameof(RunSnapshot.DraftsSinceLegendaryOffered))]
    [InlineData(0, -1, 0, nameof(RunSnapshot.DraftsWithoutAboveCommon))]
    [InlineData(0, 0, -1, nameof(RunSnapshot.DraftsWithoutOwnedUpgrade))]
    public void A_negative_stored_counter_is_refused(
        int legendary, int common, int upgrade, string field)
    {
        var result = RunAggregate.Rehydrate(RunSnapshots.With(
            draftsSinceLegendaryOffered: legendary,
            draftsWithoutAboveCommon: common,
            draftsWithoutOwnedUpgrade: upgrade));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain(field, Case.Sensitive);
    }

    /// <summary>A run that has drafted nothing carries three zeros, and that is a legal row.</summary>
    [Fact]
    public void A_run_that_has_drafted_nothing_carries_three_zeros()
    {
        var run = Rehydrate(RunSnapshots.Valid);

        run.DraftsSinceLegendaryOffered.ShouldBe(0);
        run.DraftsWithoutAboveCommon.ShouldBe(0);
        run.DraftsWithoutOwnedUpgrade.ShouldBe(0);
    }
}
