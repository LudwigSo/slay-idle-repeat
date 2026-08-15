using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Tests.Content;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Model;

/// <summary>
/// The lifetime feat counters on <c>Player</c>: additive, never reset, and part of the persisted
/// state hash.
/// </summary>
/// <remarks>
/// The ids used here are <b>synthetic</b>, deliberately: this file is about the mechanism, and an
/// id it shared with the projection would let a rename of a real counter stay green here while
/// only <c>FeatCounterProjectionTests</c> — the one place a rename must fail — went red. The one
/// real id below is <see cref="ProjectionOwnedCounter"/>, used exactly where the assertion is that
/// the aggregate does <em>not</em> write it.
/// </remarks>
public sealed class PlayerFeatCounterTests
{
    private const string Counter = "a_counter_the_mechanism_carries";
    private const string OtherCounter = "a_second_counter_the_mechanism_carries";

    /// <summary>A real projection id, used only to assert that the aggregate does not write it itself.</summary>
    private const string ProjectionOwnedCounter = "currency_earned_crowns";

    private static ContentSnapshot Content => ProgressionDocuments.Shipped;

    private static Core.Model.Player Player(PlayerSnapshot? snapshot = null) =>
        Core.Model.Player.Rehydrate(snapshot ?? PlayerSnapshots.Valid, Content).Value;

    // ------------------------------------------------------------------ the mechanism

    [Fact]
    public void A_feat_counter_comes_into_existence_on_its_first_increment()
    {
        var player = Player();

        player.FeatCount(Counter).ShouldBe(0L);
        player.FeatCounters.Counts.ShouldBeEmpty();

        player.CountFeat(Counter, 1L);

        player.FeatCount(Counter).ShouldBe(1L);
        player.FeatCounters.Counts.Keys.ShouldBe(new[] { Counter });
        player.FeatCounters.CountOf(Counter).ShouldBe(1L);
    }

    [Fact]
    public void Increments_accumulate_and_stay_independent_per_counter()
    {
        var player = Player();

        player.CountFeat(Counter, 2L);
        player.CountFeat(Counter, 3L);
        player.CountFeat(OtherCounter, 40L);

        player.FeatCount(Counter).ShouldBe(5L);
        player.FeatCount(OtherCounter).ShouldBe(40L);
    }

    /// <summary>A lifetime counter only ever grows: a negative advance is a refund, and a zero one counts nothing.</summary>
    [Theory]
    [InlineData(-1L)]
    [InlineData(0L)]
    public void A_feat_counter_refuses_a_non_positive_advance(long amount)
    {
        var player = Player();

        var thrown = Should.Throw<ArgumentOutOfRangeException>(() => player.CountFeat(Counter, amount));

        thrown.Message.ShouldContain("lifetime", Case.Insensitive);
        player.FeatCount(Counter).ShouldBe(0L);
    }

    /// <summary>
    /// The blank-id refusal, pinned by <b>identity</b>: <c>ArgumentOutOfRangeException</c> derives
    /// from <c>ArgumentException</c> and Shouldly matches by assignability, so an implementation
    /// that validated the amount first would satisfy a bare type assertion for the wrong reason.
    /// </summary>
    [Theory]
    [InlineData("   ")]
    [InlineData("")]
    [InlineData(null)]
    public void A_feat_counter_refuses_a_blank_id(string? counterId)
    {
        var player = Player();

        var thrown = Should.Throw<ArgumentException>(() => player.CountFeat(counterId!, 1L));

        thrown.ShouldBeOfType<ArgumentException>();
        thrown.ParamName.ShouldBe("counterId");
        thrown.Message.ShouldContain("A feat counter id names the projection that owns it", Case.Sensitive);

        Should.Throw<ArgumentException>(() => player.FeatCount(counterId!))
            .ShouldBeOfType<ArgumentException>();
    }

    [Fact]
    public void A_feat_counter_refuses_an_advance_that_would_overflow()
    {
        var player = Player(PlayerSnapshots.With(
            featCounters: PlayerSnapshots.Counters((Counter, long.MaxValue))));

        Should.Throw<ArgumentOutOfRangeException>(() => player.CountFeat(Counter, 1L));

        player.FeatCount(Counter).ShouldBe(long.MaxValue);
    }

    // ------------------------------------------------------------------ lifetime, and what that costs

    /// <summary>
    /// 🔒 The whole reason these counters are on the aggregate. A daily boundary clears the daily
    /// counters, a weekly boundary clears the weekly ones, and neither touches these — a Feat is
    /// claimed retroactively against a lifetime count, so a reset silently understates a history
    /// that cannot be rebuilt.
    /// </summary>
    [Fact]
    public void Feat_counters_survive_the_boundaries_that_clear_the_daily_and_weekly_counters()
    {
        var player = Player();

        player.CountDaily("some_daily_system", 7L);
        player.CountWeekly("some_weekly_system", 9L);
        player.CountFeat(Counter, 11L);

        player.ResetDailyCounters(PlayerSnapshots.Wednesday.AddDays(1));
        player.ResetWeeklyCounters(PlayerSnapshots.Monday.AddDays(7));

        player.DailyCount("some_daily_system").ShouldBe(0L, "the daily boundary clears its own counters");
        player.WeeklyCount("some_weekly_system").ShouldBe(0L, "and the weekly boundary clears its own");
        player.FeatCount(Counter).ShouldBe(
            11L,
            "a feat counter is LIFETIME: nothing on this aggregate resets it, because a Feat is " +
            "claimed retroactively against the count and a reset would pay out against a history " +
            "the player did not have.");
    }

    // ------------------------------------------------------------------ persistence

    [Fact]
    public void Feat_counters_round_trip_through_the_snapshot()
    {
        var player = Player();

        player.CountFeat(Counter, 3L);
        player.CountFeat(OtherCounter, 500L);

        var round = Core.Model.Player.Rehydrate(player.ToSnapshot(), Content);

        round.IsSuccess.ShouldBeTrue();
        round.Value.FeatCount(Counter).ShouldBe(3L);
        round.Value.FeatCount(OtherCounter).ShouldBe(500L);
    }

    /// <summary>
    /// The view is <b>live</b>, as its own remarks claim: it is built once over the aggregate's map,
    /// so a caller holding one across an increment sees the new count. An implementation that
    /// rebuilt a frozen copy per read would satisfy every other assertion in this file.
    /// </summary>
    [Fact]
    public void The_view_a_caller_already_holds_sees_a_later_increment()
    {
        var player = Player();
        var held = player.FeatCounters;

        player.CountFeat(Counter, 1L);

        held.CountOf(Counter).ShouldBe(1L);
        held.Counts[Counter].ShouldBe(1L);
        held.ShouldBeSameAs(player.FeatCounters);
    }

    /// <summary>
    /// 🔒 S17 — the snapshot copies the map rather than sharing it, so a later increment cannot
    /// rewrite a snapshot that was already handed out and hashed.
    /// </summary>
    [Fact]
    public void A_later_increment_does_not_rewrite_an_already_taken_snapshot()
    {
        var player = Player();
        player.CountFeat(Counter, 1L);

        var taken = player.ToSnapshot();
        var hashBefore = CanonicalStateWriter.HashMetaCommandState(taken);

        player.CountFeat(Counter, 1L);

        taken.FeatCounters![Counter].ShouldBe(1L);
        CanonicalStateWriter.HashMetaCommandState(taken).ShouldBe(
            hashBefore,
            "ToSnapshot() copies the counter map. A shared reference would let a later increment " +
            "change the bytes of a snapshot already written to storage and already hashed.");
    }

    /// <summary>
    /// 🔒 S17 — two states differing only in a feat counter must encode differently. Synthesized
    /// record equality compares the map component by REFERENCE, so this is stated over the
    /// canonical bytes, which is the one encoding whose contract is "two different states differ".
    /// </summary>
    [Fact]
    public void Two_players_differing_only_in_a_feat_counter_hash_differently()
    {
        var none = PlayerSnapshots.With(featCounters: PlayerSnapshots.Counters());
        var one = PlayerSnapshots.With(featCounters: PlayerSnapshots.Counters((Counter, 1L)));
        var two = PlayerSnapshots.With(featCounters: PlayerSnapshots.Counters((Counter, 2L)));
        var elsewhere = PlayerSnapshots.With(featCounters: PlayerSnapshots.Counters((OtherCounter, 1L)));

        var hashes = new[]
        {
            CanonicalStateWriter.HashMetaCommandState(none),
            CanonicalStateWriter.HashMetaCommandState(one),
            CanonicalStateWriter.HashMetaCommandState(two),
            CanonicalStateWriter.HashMetaCommandState(elsewhere),
        };

        hashes.Distinct(StringComparer.Ordinal).Count().ShouldBe(
            4,
            "an absent counter, a counter at 1, the same counter at 2 and a DIFFERENT counter at 1 " +
            "are four distinct states. If any pair collides the feat counters are outside the " +
            "state hash and a client mirror could disagree with the server about them forever.");
    }

    /// <summary>
    /// 🔒 S17 — the map is re-keyed into an <c>Ordinal</c> dictionary on rehydration.
    /// </summary>
    /// <remarks>
    /// ⚠️ Stated over the aggregate's own <b>lookup</b>, not over the canonical bytes, and that is
    /// the whole point of the test: <c>CanonicalStateWriter</c> always orders string keys with
    /// <c>string.CompareOrdinal</c> and never consults the map's comparer, so a byte-level
    /// comparison here would be green against a <c>Rehydrate</c> that kept a case-insensitive map —
    /// or one that kept the caller's dictionary uncopied. The comparer only becomes observable
    /// through <c>TryGetValue</c>, so that is where it is probed.
    /// </remarks>
    [Fact]
    public void A_map_that_arrived_under_another_comparer_is_re_keyed_ordinally()
    {
        var insensitive = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
        {
            [Counter] = 1L,
        };

        var player = Core.Model.Player.Rehydrate(
            PlayerSnapshots.With(featCounters: insensitive), Content);

        player.IsSuccess.ShouldBeTrue();
        player.Value.FeatCount(Counter).ShouldBe(1L);
        player.Value.FeatCount(Counter.ToUpperInvariant()).ShouldBe(
            0L,
            "the map is re-keyed Ordinal on the way in. Left case-insensitive it would answer for a " +
            "key it was never stored under, and the count would then round-trip to a DIFFERENT " +
            "canonical ordering than the one it was hashed under.");

        player.Value.CountFeat(Counter.ToUpperInvariant(), 5L);

        player.Value.FeatCount(Counter).ShouldBe(1L, "an Ordinal map does not merge the two keys");
        player.Value.FeatCounters.Counts.Count.ShouldBe(2);
    }

    /// <summary>The aggregate's own store is never handed to a caller, in either direction.</summary>
    [Fact]
    public void A_map_the_caller_still_holds_cannot_reach_inside_the_aggregate()
    {
        var caller = new Dictionary<string, long>(StringComparer.Ordinal) { [Counter] = 1L };

        var player = Core.Model.Player.Rehydrate(
            PlayerSnapshots.With(featCounters: caller), Content).Value;

        caller[Counter] = 999L;
        caller[OtherCounter] = 7L;

        player.FeatCount(Counter).ShouldBe(1L);
        player.FeatCount(OtherCounter).ShouldBe(0L);
    }

    /// <summary>
    /// 🔒 An absent lifetime map is a fault, not an empty one — deliberately unlike
    /// <c>ClearedChapterTiers</c>. Reading it as empty would zero a player's entire history at the
    /// exact moment a Feat is claimed against it.
    /// </summary>
    [Fact]
    public void A_null_feat_counter_map_is_refused_by_name()
    {
        var result = Core.Model.Player.Rehydrate(PlayerSnapshots.WithNull(feats: true), Content);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain(
            nameof(PlayerSnapshot.FeatCounters),
            Case.Sensitive,
            customMessage: "several maps can fail this validation; the message must say WHICH one did.");
        result.Error.ShouldContain("An absent counter map is not an empty one", Case.Sensitive);

        // The asymmetry, stated where it can be checked: the sibling map appended beside this one
        // IS read as empty when absent, so "a null map is a fault" is a claim about THIS field.
        Core.Model.Player.Rehydrate(PlayerSnapshots.WithNull(cleared: true), Content)
            .IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void A_negative_persisted_feat_count_is_refused()
    {
        var result = Core.Model.Player.Rehydrate(
            PlayerSnapshots.With(featCounters: PlayerSnapshots.Counters((Counter, -1L))), Content);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain(nameof(PlayerSnapshot.FeatCounters), Case.Sensitive);
    }

    /// <summary>The view is read-only: it cannot be cast back to a writable map.</summary>
    [Fact]
    public void The_feat_counter_view_cannot_be_written_through()
    {
        var player = Player();
        player.CountFeat(Counter, 1L);

        var counts = player.FeatCounters.Counts;

        counts.ShouldNotBeAssignableTo<IDictionary<string, long>>(
            "an IReadOnlyDictionary backed by a bare Dictionary casts straight back to a writable " +
            "one, and 30 §11.2's 'everything the outside world can see is a getter' would be a " +
            "claim nothing enforces.");

        (counts as ICollection<KeyValuePair<string, long>>)?.IsReadOnly.ShouldBe(true);
    }

    /// <summary>A counter nobody has registered reads as zero — while a registered one still reads its own count.</summary>
    /// <remarks>
    /// The registered arm is the discriminating half: without it, a <c>FeatCount</c> hard-wired to
    /// return zero would satisfy this test exactly.
    /// </remarks>
    [Fact]
    public void An_unregistered_counter_reads_as_zero_while_a_registered_one_does_not()
    {
        var player = Player();
        player.CountFeat(Counter, 4L);

        player.FeatCount(Counter).ShouldBe(4L);
        player.FeatCount("a_counter_no_projection_writes").ShouldBe(0L);
        player.FeatCounters.CountOf("a_counter_no_projection_writes").ShouldBe(0L);
        player.FeatCounters.CountOf("  ").ShouldBe(0L);
    }

    /// <summary>
    /// 🔒 The aggregate does not count for itself. A currency movement moves the wallet and returns
    /// the event; <c>GameRules.Apply</c> is what turns that event into a count — which is the
    /// difference between one projection table and a hook in every rule that could contribute.
    /// </summary>
    [Fact]
    public void A_wallet_movement_does_not_count_itself()
    {
        var player = Player();
        player.CountFeat(Counter, 1L);

        player.MoveCurrency(CurrencyId.CROWNS, 10L, "fixture_grant");

        player.FeatCount(ProjectionOwnedCounter).ShouldBe(0L);
        player.FeatCounters.Counts.Keys.ShouldBe(
            new[] { Counter },
            "the movement registered no counter of its own — the arranged one is still the only key.");
    }
}
