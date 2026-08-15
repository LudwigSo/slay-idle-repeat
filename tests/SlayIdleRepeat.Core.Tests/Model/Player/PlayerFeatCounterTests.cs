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
/// The ids used here are illustrative where the assertion is about the <em>mechanism</em>; the real
/// ids are pinned as literals by <c>FeatCounterProjectionTests</c>, which is where a rename must
/// fail.
/// </remarks>
public sealed class PlayerFeatCounterTests
{
    private const string Counter = "dice_rolled";
    private const string OtherCounter = "currency_earned_crowns";

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

    [Fact]
    public void A_feat_counter_refuses_a_blank_id()
    {
        var player = Player();

        Should.Throw<ArgumentException>(() => player.CountFeat("   ", 1L));
        Should.Throw<ArgumentException>(() => player.FeatCount(""));
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

        round.IsSuccess.ShouldBeTrue(round.Error);
        round.Value.FeatCount(Counter).ShouldBe(3L);
        round.Value.FeatCount(OtherCounter).ShouldBe(500L);
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
    /// 🔒 S17 — the map is re-read into an <c>Ordinal</c> dictionary on rehydration. A map that
    /// arrived under any other comparer would round-trip to a different hash than it was stored
    /// under, because the writer orders string keys ordinally.
    /// </summary>
    [Fact]
    public void A_case_insensitively_keyed_map_round_trips_to_the_same_bytes()
    {
        var insensitive = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
        {
            ["dice_rolled"] = 1L,
            ["Dice_Rolled_Star"] = 2L,
        };

        var player = Core.Model.Player.Rehydrate(
            PlayerSnapshots.With(featCounters: insensitive), Content);

        player.IsSuccess.ShouldBeTrue(player.Error);

        CanonicalStateWriter.HashMetaCommandState(player.Value.ToSnapshot()).ShouldBe(
            CanonicalStateWriter.HashMetaCommandState(
                PlayerSnapshots.With(featCounters: PlayerSnapshots.Counters(
                    ("dice_rolled", 1L), ("Dice_Rolled_Star", 2L)))),
            "the counters are copied into an Ordinal dictionary on the way in, so the stored bytes " +
            "do not depend on the comparer the caller happened to build the row with.");
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
    }

    [Fact]
    public void A_negative_persisted_feat_count_is_refused()
    {
        var result = Core.Model.Player.Rehydrate(
            PlayerSnapshots.With(featCounters: PlayerSnapshots.Counters((Counter, -1L))), Content);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain(nameof(PlayerSnapshot.FeatCounters), Case.Sensitive);
    }

    /// <summary>The view is read-only all the way down: it cannot be cast back to the aggregate's own store.</summary>
    [Fact]
    public void The_feat_counter_view_is_not_the_aggregates_own_dictionary()
    {
        var player = Player();
        player.CountFeat(Counter, 1L);

        player.FeatCounters.Counts.ShouldNotBeOfType<Dictionary<string, long>>();
    }

    /// <summary>A counter nobody has registered reads as zero, on either door.</summary>
    [Fact]
    public void An_unregistered_counter_reads_as_zero()
    {
        var player = Player();

        player.FeatCount("a_counter_no_projection_writes").ShouldBe(0L);
        player.FeatCounters.CountOf("a_counter_no_projection_writes").ShouldBe(0L);
        player.FeatCounters.CountOf("  ").ShouldBe(0L);
    }

    /// <summary>The counters are on the aggregate, so a wallet movement is not what carries them.</summary>
    [Fact]
    public void Feat_counters_are_independent_of_the_wallet()
    {
        var player = Player();

        player.MoveCurrency(CurrencyId.CROWNS, 10L, "fixture_grant");

        player.FeatCount(OtherCounter).ShouldBe(
            0L,
            "the aggregate does not count for itself — GameRules.Apply projects the event list, " +
            "which is what makes the counter set a table rather than thirty call sites.");
    }
}
