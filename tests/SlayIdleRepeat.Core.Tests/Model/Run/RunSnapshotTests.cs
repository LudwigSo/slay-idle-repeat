using Shouldly;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.TestSupport;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Model;

/// <summary>
/// 🔒 `30` §11.3 / `14` §16.6 — <c>Run.ToSnapshot</c> and <c>Run.Rehydrate</c> as a pair, and the
/// properties the persistence and <c>stateHash</c> machinery depends on.
/// </summary>
public sealed class RunSnapshotTests
{
    private static RunSnapshot Populated => RunSnapshots.With(
        runSeed: 0xFEEDFACECAFEBEEFUL,
        chapterId: 5,
        tier: DifficultyTier.MYTHIC,
        lastAppliedAtUtc: RunSnapshots.Midmorning.AddSeconds(4),
        position: 19,
        currentHp: 61,
        maxHp: 140,
        gold: 1_450,
        rngStreamPositions: RunSnapshots.Streams(
            (RngStreams.Dice, 12UL), (RngStreams.Board, 8UL), (RngStreams.Combat, 3UL)),
        adUses: RunSnapshots.AdUses(("AD_REVIVE", 1), ("AD_REROLL_DICE", 2)));

    /// <summary>
    /// 🔒 The <c>stateHash</c> of a <b>run</b> command (`14` §16.6): the player snapshot then the
    /// run snapshot, through <c>CanonicalStateWriter</c>'s own named mode.
    /// </summary>
    /// <remarks>
    /// <c>HashRunCommandState</c> rather than <c>HashMetaCommandState</c>, because a run snapshot is
    /// never hashed alone — §16.6 concatenates the two, and the concatenation lives in the writer
    /// precisely so no caller performs it. The player half is held at
    /// <c>PlayerSnapshots.Valid</c> throughout this file, so every difference the cases below measure
    /// is a difference in the run.
    /// </remarks>
    private static string Hash(RunSnapshot run) =>
        CanonicalStateWriter.HashRunCommandState(PlayerSnapshots.Valid, run);

    /// <summary>
    /// 🔒 A snapshot round-trips: rehydrate then snapshot again is the row you started with, value
    /// for value.
    /// </summary>
    /// <remarks>
    /// Record equality compares the two dictionaries by reference, so this is asserted field by field
    /// rather than with a single <c>ShouldBe</c> — which would pass for two rows whose maps differed
    /// and fail for two whose maps were equal but not the same object.
    /// <para>
    /// ⚠️ The two maps are compared with <c>ignoreOrder</c>. Shouldly's <c>ShouldBe</c> over an
    /// <see cref="IEnumerable{T}"/> is order-sensitive, and a dictionary's enumeration order is an
    /// <b>implementation detail of the aggregate's copy</b>, not part of the contract:
    /// <c>CanonicalStateWriter</c> imposes ascending key order itself (the case below pins exactly
    /// that), so an implementation that copied into a sorted map would round-trip and hash
    /// identically while failing an ordered comparison. Pinning the order here would be a test that
    /// breaks on a harmless refactor.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_snapshot_round_trips_through_the_aggregate()
    {
        var round = Run.Rehydrate(Populated).Value.ToSnapshot();

        round.SchemaVersion.ShouldBe(Populated.SchemaVersion);
        round.Id.ShouldBe(Populated.Id);
        round.PlayerId.ShouldBe(Populated.PlayerId);
        round.RunSeed.ShouldBe(Populated.RunSeed);
        round.ChapterId.ShouldBe(Populated.ChapterId);
        round.Tier.ShouldBe(Populated.Tier);
        round.LastAppliedAtUtc.ShouldBe(Populated.LastAppliedAtUtc);
        round.Position.ShouldBe(Populated.Position);
        round.CurrentHp.ShouldBe(Populated.CurrentHp);
        round.MaxHp.ShouldBe(Populated.MaxHp);
        round.Gold.ShouldBe(Populated.Gold);
        round.RngStreamPositions.ShouldBe(Populated.RngStreamPositions, ignoreOrder: true);
        round.AdUses.ShouldBe(Populated.AdUses, ignoreOrder: true);
    }

    /// <summary>
    /// 🔒 And the round trip is <c>stateHash</c>-stable, which is the property `14` §2.4's
    /// client-mirror check actually depends on.
    /// </summary>
    [Fact]
    public void A_round_tripped_snapshot_hashes_identically()
    {
        var round = Run.Rehydrate(Populated).Value.ToSnapshot();

        Hash(round)
            .ShouldBe(Hash(Populated));
    }

    /// <summary>
    /// 🔒 A stream map built in a different insertion order hashes identically — the writer imposes
    /// ascending key order rather than trusting the container's.
    /// </summary>
    [Fact]
    public void A_stream_map_built_in_a_different_order_hashes_identically()
    {
        var forwards = RunSnapshots.With(rngStreamPositions: RunSnapshots.Streams(
            (RngStreams.Board, 8UL), (RngStreams.Dice, 12UL)));

        var backwards = RunSnapshots.With(rngStreamPositions: RunSnapshots.Streams(
            (RngStreams.Dice, 12UL), (RngStreams.Board, 8UL)));

        Hash(forwards)
            .ShouldBe(Hash(backwards));
    }

    /// <summary>
    /// 🔒 …and a materially different map hashes differently, so the case above is not just observing
    /// that everything hashes the same.
    /// </summary>
    [Fact]
    public void A_different_stream_position_hashes_differently()
    {
        var early = RunSnapshots.With(rngStreamPositions: RunSnapshots.Streams((RngStreams.Dice, 12UL)));
        var late = RunSnapshots.With(rngStreamPositions: RunSnapshots.Streams((RngStreams.Dice, 13UL)));

        Hash(early)
            .ShouldNotBe(Hash(late));
    }

    /// <summary>
    /// 🔒 Every field of <see cref="RunSnapshot"/> reaches the bytes: changing any one of the
    /// thirteen changes the hash.
    /// </summary>
    /// <remarks>
    /// ⚠️ A snapshot field that does not move the <c>stateHash</c> is state two runs can differ in
    /// while `14` §2.4's client-mirror check reports agreement — the shape M1-04 caught on
    /// <c>PlayerSnapshot</c> when a public field outside the primary constructor was silently
    /// skipped. The probe set is floored against the record's own constructor arity so a field added
    /// without a probe makes the emptiness below quietly weaker (steering S3).
    /// </remarks>
    [Fact]
    public void Every_field_of_the_snapshot_reaches_the_hash()
    {
        var v = RunSnapshots.Valid;

        var probes = new (string Field, RunSnapshot A, RunSnapshot B)[]
        {
            // 🔒 One PAST the current version, as an expression rather than a literal — see
            // PlayerSnapshotTests' probe of the same name for what the literal cost at M1-09's bump.
            (nameof(RunSnapshot.SchemaVersion), v,
                RunSnapshots.With(schemaVersion: SnapshotSchema.SchemaVersion + 1)),
            (nameof(RunSnapshot.Id), v, RunSnapshots.With(id: new RunId("OTHER_RUN"))),
            (nameof(RunSnapshot.PlayerId), v, RunSnapshots.With(playerId: new PlayerId("OTHER_PLAYER"))),
            (nameof(RunSnapshot.RunSeed), v, RunSnapshots.With(runSeed: RunSnapshots.Seed + 1)),
            (nameof(RunSnapshot.ChapterId), v, RunSnapshots.With(chapterId: 2)),
            (nameof(RunSnapshot.Tier), v, RunSnapshots.With(tier: DifficultyTier.HEROIC)),
            (nameof(RunSnapshot.LastAppliedAtUtc), v, RunSnapshots.With(lastAppliedAtUtc: RunSnapshots.Midmorning.AddSeconds(1))),
            (nameof(RunSnapshot.Position), v, RunSnapshots.With(position: 1)),
            (nameof(RunSnapshot.CurrentHp), v, RunSnapshots.With(currentHp: 99)),
            (nameof(RunSnapshot.MaxHp), v, RunSnapshots.With(currentHp: 100, maxHp: 101)),
            (nameof(RunSnapshot.Gold), v, RunSnapshots.With(gold: 1)),
            (nameof(RunSnapshot.RngStreamPositions), v, RunSnapshots.With(rngStreamPositions: RunSnapshots.Streams((RngStreams.Dice, 1UL)))),
            (nameof(RunSnapshot.AdUses), v, RunSnapshots.With(adUses: RunSnapshots.AdUses(("AD_REVIVE", 1)))),
        };

        var invisible = probes
            .Where(p => Hash(p.A)
                        == Hash(p.B))
            .Select(p => p.Field)
            .ToArray();

        invisible.ShouldBeEmpty(
            "a snapshot field that does not move the stateHash is state two runs can differ in " +
            "while 14 §2.4's client-mirror check reports agreement.");

        probes.Select(p => p.Field).Distinct(StringComparer.Ordinal).Count()
            .ShouldBe(typeof(RunSnapshot).GetConstructors().Single().GetParameters().Length);
    }

    /// <summary>
    /// 🔒 The snapshot is a <b>copy</b>: mutating the aggregate afterwards must not rewrite a row
    /// already handed to a persistence adapter.
    /// </summary>
    [Fact]
    public void A_snapshot_taken_before_a_mutation_is_not_changed_by_it()
    {
        var run = Run.Rehydrate(Populated).Value;
        var before = run.ToSnapshot();
        var hashBefore = Hash(before);

        run.MoveCurrency(CurrencyId.GOLD, 300, "tile_kill_gold");
        run.CountAdUse("AD_REVIVE", 1);
        run.MoveTo(20);
        run.CommitStreamPositions(new Dictionary<string, ulong>(StringComparer.Ordinal)
        {
            [RngStreams.Dice] = 13,
            [RngStreams.Board] = 8,
            [RngStreams.Combat] = 4,
        });

        before.Gold.ShouldBe(1_450);
        before.AdUses["AD_REVIVE"].ShouldBe(1);
        before.Position.ShouldBe(19);
        before.RngStreamPositions[RngStreams.Dice].ShouldBe(12UL);
        Hash(before).ShouldBe(hashBefore);
    }

    /// <summary>
    /// <c>ToSnapshot</c> stamps the <b>current</b> <c>SchemaVersion</c>, whatever the row it was
    /// rehydrated from said — a snapshot describes today's layout, not yesterday's.
    /// </summary>
    [Fact]
    public void ToSnapshot_stamps_the_current_SchemaVersion()
    {
        Run.Rehydrate(RunSnapshots.Valid).Value
           .ToSnapshot().SchemaVersion.ShouldBe(SnapshotSchema.SchemaVersion);
    }

    /// <summary>
    /// 🔒 `14` §16.6 / `30` §11.3 — <c>SchemaVersion</c> is the first field of the record, so a
    /// reader knows the layout before it reads anything laid out by it.
    /// </summary>
    /// <remarks>
    /// <c>SnapshotFieldOrderPinTests</c> asserts this over the whole subject set; this is the same
    /// claim stated where a reader of <see cref="RunSnapshot"/> will look for it.
    /// </remarks>
    [Fact]
    public void SchemaVersion_is_the_first_field_of_the_record()
    {
        CanonicalStateWriter.CanonicalFieldOrder(typeof(RunSnapshot))[0]
            .ShouldBe("SchemaVersion:System.Int32");
    }

    /// <summary>
    /// 🔒 The whole snapshot has a canonical encoding — every member is on `14` §16.6's closed
    /// allowlist. A member that was not would refuse at the first <c>stateHash</c> of the run.
    /// </summary>
    [Fact]
    public void The_snapshot_has_a_canonical_encoding()
    {
        CanonicalStateWriter.IsCanonicalRecord(typeof(RunSnapshot)).ShouldBeTrue();

        Should.NotThrow(() => Hash(Populated));
    }

    /// <summary>
    /// 🔒 `14` §8.1 — <c>RunSeed</c> is <b>in</b> the snapshot, because §8.1 makes it authoritative
    /// run state: a run that lost it could not re-derive its board, its drops or its draft, and could
    /// therefore not be resumed at all.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Doc contradiction, carried forward (steering S16).</b> `02` §2 says <c>runSeed</c> never
    /// leaves the server, while `14` §16.6 makes the client-mirror <c>stateHash</c> a hash of the
    /// snapshot DTOs — and this field is in the DTO. Those cannot both be literally true.
    /// <b>M1-06 owns the ruling</b>, because it authors <c>stateHash</c> and <c>CommandResult</c> and
    /// is the first task that has to say what the client actually hashes. This case pins the half
    /// that <em>is</em> settled — the field is aggregate state — so the resolution has to be a
    /// decision about the wire rather than a quiet deletion here.
    /// </remarks>
    [Fact]
    public void The_run_seed_is_authoritative_run_state_and_travels_in_the_snapshot()
    {
        var round = Run.Rehydrate(Populated).Value.ToSnapshot();

        round.RunSeed.ShouldBe(0xFEEDFACECAFEBEEFUL);

        CanonicalStateWriter.CanonicalFieldOrder(typeof(RunSnapshot))
            .ShouldContain("RunSeed:System.UInt64");
    }
}
