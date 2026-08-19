using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Tests.Content;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Model;

/// <summary>
/// The lifetime feat counters on <c>Player</c>. The ids are synthetic so a rename of a real
/// counter fails only in <c>FeatCounterProjectionTests</c>, the one place a rename must fail.
/// </summary>
public sealed class PlayerFeatCounterTests
{
    private const string Counter = "a_counter_the_mechanism_carries";
    private const string OtherCounter = "a_second_counter_the_mechanism_carries";

    /// <summary>A real projection id, used only to assert that the aggregate does not write it itself.</summary>
    private const string ProjectionOwnedCounter = "currency_earned_crowns";

    private static ContentSnapshot Content => ProgressionDocuments.Shipped;

    private static Core.Model.Player Player(PlayerSnapshot? snapshot = null) =>
        Core.Model.Player.Rehydrate(snapshot ?? PlayerSnapshots.Valid, Content).Value;

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
    /// Pinned by identity: <c>ArgumentOutOfRangeException</c> derives from <c>ArgumentException</c>
    /// and Shouldly matches by assignability, so an implementation validating the amount first
    /// would satisfy a bare type assertion for the wrong reason.
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

        var reading = Should.Throw<ArgumentException>(() => player.FeatCount(counterId!));

        reading.ShouldBeOfType<ArgumentException>();
        reading.ParamName.ShouldBe("counterId");
        reading.Message.ShouldContain("A feat counter id names the projection that owns it", Case.Sensitive);
    }

    [Fact]
    public void A_feat_counter_refuses_an_advance_that_would_overflow()
    {
        var player = Player(PlayerSnapshots.With(
            featCounters: PlayerSnapshots.Counters((Counter, long.MaxValue))));

        Should.Throw<ArgumentOutOfRangeException>(() => player.CountFeat(Counter, 1L));

        player.FeatCount(Counter).ShouldBe(long.MaxValue);
    }

    /// <summary>🔒 28 D2: a Feat is claimed retroactively against a lifetime count, so no period boundary may touch these.</summary>
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
    /// Stated over canonical bytes, never record equality: a synthesized <c>Equals</c> compares the
    /// map component by reference.
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
    /// Probed through <c>TryGetValue</c> lookups, not canonical bytes, deliberately: the writer
    /// always orders string keys ordinally and never consults the map's comparer, so a byte-level
    /// comparison would stay green against a <c>Rehydrate</c> that kept a case-insensitive map.
    /// </summary>
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
    /// <c>ClearedChapterTiers</c>: read as empty it would zero a player's history at the exact
    /// moment a Feat is claimed against it.
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

        // The asymmetry, stated where it can be checked: the sibling map IS read as empty when absent.
        Core.Model.Player.Rehydrate(PlayerSnapshots.WithNull(cleared: true), Content)
            .IsSuccess.ShouldBeTrue();
    }

    /// <summary>Both diagnostics asserted side by side so the lifetime and period sentences cannot drift into one.</summary>
    [Fact]
    public void A_negative_persisted_feat_count_is_refused_as_a_lifetime_counter()
    {
        var feats = Core.Model.Player.Rehydrate(
            PlayerSnapshots.With(featCounters: PlayerSnapshots.Counters((Counter, -1L))), Content);

        feats.IsFailure.ShouldBeTrue();
        feats.Error.ShouldContain(nameof(PlayerSnapshot.FeatCounters), Case.Sensitive);
        feats.Error.ShouldContain("is never cleared at all", Case.Sensitive);

        var daily = Core.Model.Player.Rehydrate(
            PlayerSnapshots.With(dailyCounters: PlayerSnapshots.Counters(("ad_caps", -1L))), Content);

        daily.IsFailure.ShouldBeTrue();
        daily.Error.ShouldContain("is cleared at its period boundary", Case.Sensitive);
    }

    /// <summary>
    /// ReadOnlyDictionary DOES implement IDictionary — explicitly, with throwing writers — so the
    /// property worth asserting is that the writers throw, not that the interface is absent.
    /// </summary>
    [Fact]
    public void The_feat_counter_view_cannot_be_written_through()
    {
        var player = Player();
        player.CountFeat(Counter, 1L);

        var writable = player.FeatCounters.Counts.ShouldBeAssignableTo<IDictionary<string, long>>();

        writable.IsReadOnly.ShouldBeTrue();
        Should.Throw<NotSupportedException>(() => writable[Counter] = 99L);
        Should.Throw<NotSupportedException>(() => writable.Remove(Counter));

        player.FeatCount(Counter).ShouldBe(1L);
    }

    /// <summary>The registered arm is the discriminating half: a <c>FeatCount</c> hard-wired to zero fails here.</summary>
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
    /// 🔒 The aggregate does not count for itself: <c>GameRules.Apply</c> turns events into counts,
    /// which is the difference between one projection table and a hook in every rule.
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
