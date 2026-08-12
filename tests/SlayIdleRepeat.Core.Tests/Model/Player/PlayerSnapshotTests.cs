using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Tests.Content;
using SlayIdleRepeat.TestSupport;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Model;

/// <summary>
/// 🔒 `30` §11.3 / `14` §16.6 — <c>ToSnapshot</c> and <c>Rehydrate</c> as a pair, and the
/// properties the persistence and <c>stateHash</c> machinery depends on.
/// </summary>
public sealed class PlayerSnapshotTests
{
    private static ContentSnapshot Content => ProgressionDocuments.Shipped;

    private static PlayerSnapshot Populated => PlayerSnapshots.With(
        displayName: "Ludwig",
        legendLevel: 41,
        legendXp: 987_654,
        runsStarted: 613,
        wallet: PlayerSnapshots.Wallet(
            (CurrencyId.CROWNS, 1_200),
            (CurrencyId.SOUL_SHARDS, 30),
            (CurrencyId.HONOR, 77)),
        energy: new EnergyBanks(118, 42),
        energyAnchorUtc: PlayerSnapshots.Midmorning,
        lastAppliedAtUtc: PlayerSnapshots.Midmorning.AddSeconds(4),
        ftueBeatId: FtueBeat.B10,
        ftueCompletedAtUtc: PlayerSnapshots.Midmorning.AddSeconds(-30),
        dailyCounters: PlayerSnapshots.Counters(("ad_caps", 3), ("dungeon_entries", 1)),
        weeklyCounters: PlayerSnapshots.Counters(("guild_quest_contributions", 12)));

    /// <summary>
    /// 🔒 A snapshot round-trips: rehydrate then snapshot again is the row you started with,
    /// value for value.
    /// </summary>
    /// <remarks>
    /// Record equality compares the two dictionaries by reference, so this is asserted field by
    /// field rather than with a single <c>ShouldBe</c> — which would pass for two rows whose
    /// counters differed and fail for two whose counters were equal but not the same object.
    /// </remarks>
    [Fact]
    public void A_snapshot_round_trips_through_the_aggregate()
    {
        var round = Core.Model.Player.Rehydrate(Populated, Content).Value.ToSnapshot();

        round.SchemaVersion.ShouldBe(Populated.SchemaVersion);
        round.Id.ShouldBe(Populated.Id);
        round.DisplayName.ShouldBe(Populated.DisplayName);
        round.LegendLevel.ShouldBe(Populated.LegendLevel);
        round.LegendXp.ShouldBe(Populated.LegendXp);
        round.RunsStarted.ShouldBe(Populated.RunsStarted);
        round.Wallet.ShouldBe(Populated.Wallet);
        round.Energy.ShouldBe(Populated.Energy);
        round.EnergyAnchorUtc.ShouldBe(Populated.EnergyAnchorUtc);
        round.LastAppliedAtUtc.ShouldBe(Populated.LastAppliedAtUtc);
        round.FtueBeatId.ShouldBe(Populated.FtueBeatId);
        round.FtueCompletedAtUtc.ShouldBe(Populated.FtueCompletedAtUtc);
        round.DailyPeriodStartUtc.ShouldBe(Populated.DailyPeriodStartUtc);
        round.DailyCounters.ShouldBe(Populated.DailyCounters);
        round.WeeklyPeriodStartUtc.ShouldBe(Populated.WeeklyPeriodStartUtc);
        round.WeeklyCounters.ShouldBe(Populated.WeeklyCounters);
    }

    /// <summary>
    /// 🔒 And the round trip is <c>stateHash</c>-stable, which is the property `14` §2.4's
    /// client-mirror check actually depends on.
    /// </summary>
    /// <remarks>
    /// Field-by-field equality is not the same claim: two rows can be equal field by field and
    /// still hash differently if a dictionary's iteration order leaked into the bytes, which is
    /// exactly the failure the writer's imposed key order exists to prevent.
    /// </remarks>
    [Fact]
    public void A_round_tripped_snapshot_hashes_identically()
    {
        var round = Core.Model.Player.Rehydrate(Populated, Content).Value.ToSnapshot();

        CanonicalStateWriter.HashMetaCommandState(round)
            .ShouldBe(CanonicalStateWriter.HashMetaCommandState(Populated));
    }

    /// <summary>
    /// 🔒 A wallet built in a different insertion order hashes identically — the writer imposes
    /// ascending key order rather than trusting the container's.
    /// </summary>
    [Fact]
    public void A_wallet_built_in_a_different_order_hashes_identically()
    {
        var forwards = PlayerSnapshots.With(wallet: PlayerSnapshots.Wallet(
            (CurrencyId.CROWNS, 5), (CurrencyId.HONOR, 7)));

        var backwards = PlayerSnapshots.With(wallet: PlayerSnapshots.Wallet(
            (CurrencyId.HONOR, 7), (CurrencyId.CROWNS, 5)));

        CanonicalStateWriter.HashMetaCommandState(forwards)
            .ShouldBe(CanonicalStateWriter.HashMetaCommandState(backwards));
    }

    /// <summary>
    /// 🔒 …and a materially different wallet hashes differently, so the test above is not just
    /// observing that everything hashes the same.
    /// </summary>
    [Fact]
    public void A_different_balance_hashes_differently()
    {
        var poor = PlayerSnapshots.With(wallet: PlayerSnapshots.Wallet((CurrencyId.CROWNS, 5)));
        var rich = PlayerSnapshots.With(wallet: PlayerSnapshots.Wallet((CurrencyId.CROWNS, 6)));

        CanonicalStateWriter.HashMetaCommandState(poor)
            .ShouldNotBe(CanonicalStateWriter.HashMetaCommandState(rich));
    }

    /// <summary>
    /// 🔒 Every field of <c>PlayerSnapshot</c> reaches the bytes: changing any one of the fifteen
    /// changes the hash.
    /// </summary>
    /// <remarks>
    /// ⚠️ This is the assertion that would have caught the public-field defect on real state
    /// rather than on a fixture. A field the writer silently skipped — which is what a public field
    /// outside the primary constructor used to be — would show up here as two materially different
    /// players sharing a <c>stateHash</c>, and nothing else in the suite is looking.
    /// </remarks>
    [Fact]
    public void Every_field_of_the_snapshot_reaches_the_hash()
    {
        var v = PlayerSnapshots.Valid;
        var atBeatTen = PlayerSnapshots.With(ftueBeatId: FtueBeat.B10);

        // ⚠️ Stated as PAIRS rather than as fifteen variants against one baseline. The
        // FtueCompletedAtUtc probe has to move FtueBeatId too — the seam refuses a completed
        // tutorial anywhere but B10 — so measured against the shared baseline it would report a
        // hash change even if FtueCompletedAtUtc contributed nothing at all.
        var probes = new (string Field, PlayerSnapshot A, PlayerSnapshot B)[]
        {
            (nameof(PlayerSnapshot.SchemaVersion), v, PlayerSnapshots.With(schemaVersion: 2)),
            (nameof(PlayerSnapshot.Id), v, PlayerSnapshots.With(id: new PlayerId("OTHER"))),
            (nameof(PlayerSnapshot.DisplayName), v, PlayerSnapshots.With(displayName: "Someone Else")),
            (nameof(PlayerSnapshot.LegendLevel), v, PlayerSnapshots.With(legendLevel: 2)),
            (nameof(PlayerSnapshot.LegendXp), v, PlayerSnapshots.With(legendXp: 1)),
            (nameof(PlayerSnapshot.RunsStarted), v, PlayerSnapshots.With(runsStarted: 1)),
            (nameof(PlayerSnapshot.Wallet), v, PlayerSnapshots.With(wallet: PlayerSnapshots.Wallet((CurrencyId.CROWNS, 1)))),
            ("Energy.Energy", v, PlayerSnapshots.With(energy: new EnergyBanks(1, 0))),
            ("Energy.Reserve", v, PlayerSnapshots.With(energy: new EnergyBanks(0, 1))),
            (nameof(PlayerSnapshot.EnergyAnchorUtc), v, PlayerSnapshots.With(energyAnchorUtc: PlayerSnapshots.Midmorning.AddSeconds(1))),
            (nameof(PlayerSnapshot.LastAppliedAtUtc), v, PlayerSnapshots.With(lastAppliedAtUtc: PlayerSnapshots.Midmorning.AddSeconds(1))),
            (nameof(PlayerSnapshot.FtueBeatId), v, PlayerSnapshots.With(ftueBeatId: FtueBeat.B1)),
            (nameof(PlayerSnapshot.FtueCompletedAtUtc), atBeatTen,
                PlayerSnapshots.With(ftueBeatId: FtueBeat.B10, ftueCompletedAtUtc: PlayerSnapshots.Midmorning)),
            (nameof(PlayerSnapshot.DailyPeriodStartUtc), v, PlayerSnapshots.With(dailyPeriodStartUtc: PlayerSnapshots.Wednesday.AddDays(1))),
            (nameof(PlayerSnapshot.DailyCounters), v, PlayerSnapshots.With(dailyCounters: PlayerSnapshots.Counters(("ad_caps", 1)))),
            (nameof(PlayerSnapshot.WeeklyPeriodStartUtc), v, PlayerSnapshots.With(weeklyPeriodStartUtc: PlayerSnapshots.Monday.AddDays(7))),
            (nameof(PlayerSnapshot.WeeklyCounters), v, PlayerSnapshots.With(weeklyCounters: PlayerSnapshots.Counters(("ad_caps", 1)))),
        };

        var invisible = probes
            .Where(p => CanonicalStateWriter.HashMetaCommandState(p.A)
                        == CanonicalStateWriter.HashMetaCommandState(p.B))
            .Select(p => p.Field)
            .ToArray();

        invisible.ShouldBeEmpty(
            "a snapshot field that does not move the stateHash is state two players can differ in " +
            "while 14 §2.4's client-mirror check reports agreement.");

        // 🔒 A floor on the probe set itself: one probe per constructor parameter, plus a second
        // for the nested Energy record's other bank. A probe silently dropped — or a field added
        // without one — would make the emptiness above quietly weaker, which is steering S3's
        // failure mode inside a test rather than inside a rule.
        probes.Select(p => p.Field).Distinct(StringComparer.Ordinal).Count()
            .ShouldBe(typeof(PlayerSnapshot).GetConstructors().Single().GetParameters().Length + 1);
    }

    /// <summary>
    /// 🔒 The snapshot is a <b>copy</b>: mutating the aggregate afterwards must not rewrite a row
    /// already handed to a persistence adapter.
    /// </summary>
    [Fact]
    public void A_snapshot_taken_before_a_mutation_is_not_changed_by_it()
    {
        var player = Core.Model.Player.Rehydrate(Populated, Content).Value;
        var before = player.ToSnapshot();
        var hashBefore = CanonicalStateWriter.HashMetaCommandState(before);

        player.MoveCurrency(CurrencyId.CROWNS, 300, "quest_reward");
        player.CountDaily("ad_caps", 1);
        player.ResetWeeklyCounters(PlayerSnapshots.Monday.AddDays(7));

        before.Wallet[CurrencyId.CROWNS].ShouldBe(1_200);
        before.DailyCounters["ad_caps"].ShouldBe(3);
        before.WeeklyCounters.Count.ShouldBe(1);
        CanonicalStateWriter.HashMetaCommandState(before).ShouldBe(hashBefore);
    }

    /// <summary>
    /// …and the converse: mutating a dictionary the caller handed to <c>Rehydrate</c> must not
    /// reach inside the aggregate.
    /// </summary>
    [Fact]
    public void A_map_the_caller_still_holds_cannot_reach_inside_the_aggregate()
    {
        var counters = new Dictionary<string, long>(StringComparer.Ordinal) { ["ad_caps"] = 1 };
        var player = Core.Model.Player
            .Rehydrate(PlayerSnapshots.With(dailyCounters: counters), Content).Value;

        counters["ad_caps"] = 99;
        counters["smuggled"] = 1;

        player.DailyCount("ad_caps").ShouldBe(1);
        player.DailyCount("smuggled").ShouldBe(0);
    }

    /// <summary>
    /// <c>ToSnapshot</c> stamps the <b>current</b> <c>SchemaVersion</c>, whatever the row it was
    /// rehydrated from said — a snapshot describes today's layout, not yesterday's.
    /// </summary>
    [Fact]
    public void ToSnapshot_stamps_the_current_SchemaVersion()
    {
        Core.Model.Player.Rehydrate(PlayerSnapshots.Valid, Content).Value
            .ToSnapshot().SchemaVersion.ShouldBe(SnapshotSchema.SchemaVersion);
    }

    /// <summary>
    /// 🔒 `14` §16.6 / `30` §11.3 — <c>SchemaVersion</c> is the first field of the record, so a
    /// reader knows the layout before it reads anything laid out by it.
    /// </summary>
    /// <remarks>
    /// <c>SnapshotFieldOrderPinTests</c> asserts this over the whole subject set; this is the same
    /// claim stated where a reader of <c>PlayerSnapshot</c> will look for it.
    /// </remarks>
    [Fact]
    public void SchemaVersion_is_the_first_field_of_the_record()
    {
        CanonicalStateWriter.CanonicalFieldOrder(typeof(PlayerSnapshot))[0]
            .ShouldBe("SchemaVersion:System.Int32");
    }

    /// <summary>
    /// 🔒 The whole snapshot has a canonical encoding — every member is on `14` §16.6's closed
    /// allowlist. A member that was not would refuse at the first <c>stateHash</c> of the game.
    /// </summary>
    [Fact]
    public void The_snapshot_has_a_canonical_encoding()
    {
        CanonicalStateWriter.IsCanonicalRecord(typeof(PlayerSnapshot)).ShouldBeTrue();

        Should.NotThrow(() => CanonicalStateWriter.HashMetaCommandState(Populated));
    }

    /// <summary>
    /// 🔒 The counter key is a bare <c>string</c> rather than a wrapper id, and that is mechanical
    /// rather than stylistic: <c>KeyOrderFor</c> defines an ascending order for strings and numeric
    /// ids <b>only</b>.
    /// </summary>
    /// <remarks>
    /// Demonstrated against <see cref="PlayerId"/>, which is the exact shape a <c>CounterKey</c>
    /// would have taken — a positional <c>readonly record struct</c> over one validated string.
    /// A map keyed by one has no canonical encoding at all, so it could never have travelled in a
    /// snapshot.
    /// </remarks>
    [Fact]
    public void A_map_keyed_by_a_string_wrapper_would_have_had_no_canonical_encoding()
    {
        var act = () => CanonicalStateWriter.CanonicalFieldOrder(typeof(WrapperKeyedSnapshot));

        Should.Throw<NotSupportedException>(act)
              .Message.ShouldMatchWildcard("*has no ascending key order*");

        // …while the string-keyed shape the snapshot actually uses is fine.
        CanonicalStateWriter.CanonicalFieldOrder(typeof(PlayerSnapshot))
            .ShouldContain("DailyCounters{key}:System.String");
    }

    /// <summary>The shape the counter map would have had if its key were a wrapper id.</summary>
    private sealed record WrapperKeyedSnapshot(int SchemaVersion, IReadOnlyDictionary<PlayerId, long> Counters);
}
