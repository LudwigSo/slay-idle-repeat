using Shouldly;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Rules.Combat;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat;

/// <summary>
/// 🔒 The committed <c>LogHash</c> reference table of `05` §7, asserted row by row.
/// </summary>
/// <remarks>
/// <b>A failure here is a determinism break, never a test fix.</b> `11` §6 compares the client-reported
/// <c>LogHash</c> against the server's to detect tampering, and M5-12 compares it across x64 and two
/// ARM64 devices. If a row moves, every duel in flight starts reporting a mismatch.
/// <para>
/// ⚠️ It proves <b>stability</b> — the encoding cannot change without a row going red — and <b>not</b> the
/// encoding <i>choice</i> against an external authority, because nobody publishes <c>event list →
/// LogHash</c> vectors for this game. What <i>is</i> externally validated is the hash function
/// underneath: the generator reproduced Noll's published FNV-1a 64 vectors before emitting a row.
/// </para>
/// </remarks>
public sealed class CombatLogReferenceVectorTests
{
    /// <summary>Every committed row: the event list encodes to exactly the committed bytes.</summary>
    [Theory]
    [MemberData(nameof(RowIds))]
    public void CanonicalBytes_matches_every_committed_reference_vector(string rowId)
    {
        var row = CombatLogReferenceVectors.Row(rowId);

        var hex = Convert.ToHexString(BytesFor(row)).ToLowerInvariant();

        hex.ShouldBe(row.EncodedHex, $"'{row.Id}' pins {row.Why}");
    }

    /// <summary>
    /// Every committed row's byte length. Asserted alongside the bytes because it localises a
    /// break: a length mismatch says the <i>shape</i> moved, a same-length mismatch says a value did.
    /// </summary>
    [Theory]
    [MemberData(nameof(RowIds))]
    public void The_canonical_encoding_matches_every_committed_byte_length(string rowId)
    {
        var row = CombatLogReferenceVectors.Row(rowId);

        BytesFor(row).Length.ShouldBe(row.EncodedByteLength, $"'{row.Id}' pins {row.Why}");
    }

    /// <summary>
    /// Every committed row, through the public door: <c>HashCombatLog</c> is what
    /// <see cref="SimulationResult.LogHash"/> is computed by, and what `11` §6 compares.
    /// </summary>
    [Theory]
    [MemberData(nameof(RowIds))]
    public void HashCombatLog_matches_every_committed_reference_vector(string rowId)
    {
        var row = CombatLogReferenceVectors.Row(rowId);

        var hash = CanonicalStateWriter.HashCombatLog(ReferenceLogs.Instance(row.Id));

        hash.ShouldBe(row.Hash, $"'{row.Id}' pins {row.Why}");
    }

    /// <summary>Every committed row carries the event count its list actually has.</summary>
    [Theory]
    [MemberData(nameof(RowIds))]
    public void Every_committed_row_carries_its_own_event_count(string rowId)
    {
        var row = CombatLogReferenceVectors.Row(rowId);

        ReferenceLogs.Instance(row.Id).Count.ShouldBe(row.EventCount, $"'{row.Id}' pins {row.Why}");
    }

    /// <summary>
    /// 🔒 The arithmetic of the encoding, stated as a rule rather than left implicit in fourteen
    /// hex strings: a log of <c>N</c> events is <c>4 + 48N</c> bytes — the 4-byte element count,
    /// then six 8-byte fields per event, with no presence byte because
    /// <see cref="CombatEvent"/> is a non-nullable value type.
    /// </summary>
    /// <remarks>
    /// This is what would catch a seventh field, a narrowed field or a presence byte creeping in —
    /// each of which moves every row at once, which a per-row assertion reports as fourteen
    /// unrelated failures rather than as the one structural change it is.
    /// </remarks>
    [Theory]
    [MemberData(nameof(RowIds))]
    public void One_event_occupies_exactly_six_widened_fields(string rowId)
    {
        var row = CombatLogReferenceVectors.Row(rowId);

        row.EncodedByteLength.ShouldBe(4 + (48 * row.EventCount), $"'{row.Id}' pins {row.Why}");
    }

    /// <summary>
    /// 🔒 The external half of S5: the writer still <b>is</b> FNV-1a 64, not merely
    /// self-consistent with a table generated beside it.
    /// </summary>
    /// <remarks>
    /// Re-asserted here, over this file's own copy of the published rows, rather than left to
    /// <c>Fnv1a64KnownAnswerTests</c> — so this table is self-validating: if the algorithm under it
    /// were rotated, the rows above would still agree with each other and only this test would
    /// notice.
    /// </remarks>
    [Theory]
    [MemberData(nameof(PublishedRows))]
    public void The_writer_still_reproduces_the_published_FNV_1a_vectors(string input, ulong expected)
    {
        CanonicalStateWriter.Fnv1a64(System.Text.Encoding.UTF8.GetBytes(input)).ShouldBe(expected);
    }

    /// <summary>The FNV-1a parameters the committed file names are the ones the writer uses.</summary>
    /// <remarks>
    /// The empty input hashes to the offset basis by definition, and <c>"a"</c> pins the prime:
    /// <c>(basis ^ 'a') * prime</c>. Two multiplications is enough to make a wrong prime visible.
    /// </remarks>
    [Fact]
    public void The_committed_FNV_1a_parameters_are_the_writers()
    {
        CanonicalStateWriter.Fnv1a64([]).ShouldBe(CombatLogReferenceVectors.OffsetBasis);

        var expected = unchecked((CombatLogReferenceVectors.OffsetBasis ^ (byte)'a') * CombatLogReferenceVectors.Prime);
        CanonicalStateWriter.Fnv1a64("a"u8).ShouldBe(expected);
    }

    /// <summary>
    /// 🔒 The property the whole log format rests on: <b>order is inside the hash</b>. `05` §3.1
    /// step 7 appends events as they happen, so a simulator that batched a tick's events and
    /// flushed them in a different order would produce a different replay — and this is what makes
    /// that visible instead of silent.
    /// </summary>
    [Fact]
    public void The_same_events_in_a_different_order_hash_differently()
    {
        var ordered = CombatLogReferenceVectors.Row("two-events-ordered");
        var swapped = CombatLogReferenceVectors.Row("two-events-swapped");

        ReferenceLogs.Instance("two-events-swapped")
            .ShouldBe(ReferenceLogs.Instance("two-events-ordered").Reverse(), ignoreOrder: false);

        ordered.Hash.ShouldNotBe(swapped.Hash);
        CanonicalStateWriter.HashCombatLog(ReferenceLogs.Instance("two-events-ordered"))
            .ShouldNotBe(CanonicalStateWriter.HashCombatLog(ReferenceLogs.Instance("two-events-swapped")));
    }

    /// <summary>
    /// 🔒 Every <see cref="CombatEventType"/> member appears in the <c>every-event-type</c> row,
    /// so the committed hash pins every ordinal.
    /// </summary>
    /// <remarks>
    /// This is what makes inserting a member mid-enum a build failure. The ordinal is what gets
    /// hashed, so an insertion silently rewrites every <c>LogHash</c> ever computed — including
    /// ones already stored against duels in flight (`11` §6).
    /// </remarks>
    [Fact]
    public void The_every_event_type_row_pins_every_enum_ordinal()
    {
        var members = Enum.GetValues<CombatEventType>();
        members.ShouldNotBeEmpty();

        var row = CombatLogReferenceVectors.Row("every-event-type");
        row.EventCount.ShouldBe(members.Length,
            "the every-event-type row is the only pin on the enum's SIZE; a member missing from it is an " +
            "ordinal nothing is watching");

        // The row's construction rule, which nothing else checks: the i-th event carries tick i and
        // DataId i, so the committed bytes pin the ordinals as NUMBERS rather than merely as a set.
        ReferenceLogs.AllEventTypes
            .Select(e => (e.Tick, (int)e.DataId, (int)e.Type))
            .ShouldBe(Enumerable.Range(0, members.Length).Select(i => (i, i, i)));
    }

    /// <summary>
    /// 🔒 The <c>completed-battle</c> row is produced by <see cref="CombatLog.Complete"/>, so the
    /// terminal <see cref="CombatEventType.BattleEnd"/>'s own six fields are inside a committed
    /// hash.
    /// </summary>
    /// <remarks>
    /// Every other row is written out by hand, which is what the table is for — it must be able to
    /// express shapes the builder forbids. The consequence was that <c>Complete</c>'s
    /// <c>BattleEnd</c> — the last event of every log in the game, and inside the <c>LogHash</c>
    /// `11` §6 compares between client and server — was pinned by nothing at all: changing its
    /// actor ids and <c>DataId</c> left the entire suite green.
    /// </remarks>
    [Fact]
    public void The_terminal_BattleEnd_is_pinned_by_a_row_built_through_Complete()
    {
        var row = CombatLogReferenceVectors.Row("completed-battle");
        var events = ReferenceLogs.Instance(row.Id);

        events[^1].ShouldBe(new CombatEvent(
            9, CombatEventType.BattleEnd, CombatActor.None, CombatActor.None, 0.0, CombatLog.NoDataId));

        CanonicalStateWriter.HashCombatLog(events).ShouldBe(row.Hash, $"'{row.Id}' pins {row.Why}");
    }

    /// <summary>
    /// A log stored as a <see cref="List{T}"/> encodes identically to one stored as an array.
    /// </summary>
    /// <remarks>
    /// Every reference row is an array, but `11` §6 has the <b>server</b> rebuild the fight and
    /// hash its own log, and nothing obliges it to reach the same container type. `14` §16.6's list
    /// rule is a count and the elements, so both shapes must agree — asserted rather than assumed,
    /// because a divergence here reads as tampering and discards a real duel.
    /// </remarks>
    [Fact]
    public void A_list_and_an_array_of_the_same_events_hash_identically()
    {
        var events = ReferenceLogs.Instance("completed-battle");

        CanonicalStateWriter.HashCombatLog(events.ToList())
            .ShouldBe(CanonicalStateWriter.HashCombatLog(events.ToArray()));
    }

    /// <summary>
    /// The table guards the code; this guards the table. A future edit that quietly dropped the
    /// only order-sensitivity pair, or the only large-value row, would leave a suite that still
    /// passes while covering less.
    /// </summary>
    [Theory]
    [InlineData("empty-log")]
    [InlineData("battle-start-only")]
    [InlineData("hit-single")]
    [InlineData("two-events-ordered")]
    [InlineData("two-events-swapped")]
    [InlineData("duplicate-events")]
    [InlineData("every-event-type")]
    [InlineData("run-effect-queued")]
    [InlineData("telegraph")]
    [InlineData("actor-ceiling")]
    [InlineData("tick-ceiling")]
    [InlineData("large-damage-value")]
    [InlineData("enrage-stack")]
    [InlineData("status-stack-then-expire")]
    [InlineData("completed-battle")]
    [InlineData("negative-value")]
    public void The_reference_table_still_covers_every_load_bearing_rule(string rowId)
    {
        CombatLogReferenceVectors.Rows.Where(row => row.Id == rowId).ShouldHaveSingleItem();
    }

    /// <summary>
    /// And the coverage list above names every row the table actually has, so a row added to the
    /// JSON without a guard here cannot be deleted again unnoticed.
    /// </summary>
    [Fact]
    public void The_coverage_list_names_every_committed_row()
    {
        var guarded = typeof(CombatLogReferenceVectorTests)
            .GetMethod(nameof(The_reference_table_still_covers_every_load_bearing_rule))!
            .GetCustomAttributes(typeof(InlineDataAttribute), inherit: false)
            .Cast<InlineDataAttribute>()
            .Select(data => (string)data.GetData(null!).Single().Single()!)
            .ToArray();

        guarded.ShouldBe(CombatLogReferenceVectors.Rows.Select(row => row.Id), ignoreOrder: true);
    }

    /// <summary>
    /// Every committed hash is distinct. Fourteen rows chosen to exercise different rules that
    /// nevertheless collided would be a table proving far less than it appears to.
    /// </summary>
    [Fact]
    public void Every_committed_row_has_its_own_hash()
    {
        CombatLogReferenceVectors.Rows.Select(row => row.Hash).ShouldBeUnique();
    }

    /// <summary>
    /// S3 — a floor under the table itself. Every assertion above is a <c>[Theory]</c> over
    /// <see cref="RowIds"/>, and xUnit reports a theory with no data as a pass.
    /// </summary>
    [Fact]
    public void The_reference_table_is_not_empty()
    {
        CombatLogReferenceVectors.Rows.Count.ShouldBeGreaterThanOrEqualTo(16);
        CombatLogReferenceVectors.Published.Count.ShouldBeGreaterThanOrEqualTo(15);
    }

    /// <summary>
    /// 🔒 <c>HashCombatLog</c> refuses a root that is not a list.
    /// </summary>
    /// <remarks>
    /// Its parameter is <see cref="object"/> — it has to be, because `30` §11.4 forbids
    /// <c>Model</c> from naming a type in <c>Rules</c> — so this is the one hashing mode whose root
    /// kind actually varies. A single event would hash its 48 record bytes with no element count
    /// and return a perfectly plausible <see cref="ulong"/> that is the <c>LogHash</c> of nothing.
    /// </remarks>
    [Fact]
    public void HashCombatLog_refuses_a_root_that_is_not_a_list()
    {
        var single = new CombatEvent(3, CombatEventType.Hit, CombatActor.Hero, CombatActor.FirstEnemy, 1.0, 0);

        Should.Throw<NotSupportedException>(() => CanonicalStateWriter.HashCombatLog(single))
            .Message.ShouldContain("must be a list of events", Case.Sensitive);

        Should.Throw<NotSupportedException>(
            () => CanonicalStateWriter.HashCombatLog(new Dictionary<int, int> { [1] = 2 }));

        // And the shape it is for still works.
        Should.NotThrow(() => CanonicalStateWriter.HashCombatLog(new[] { single }));
    }

    /// <summary>A null log is an argument error, not a hash of nothing.</summary>
    [Fact]
    public void HashCombatLog_refuses_a_null_log()
    {
        Should.Throw<ArgumentNullException>(() => CanonicalStateWriter.HashCombatLog(null!));
        Should.Throw<ArgumentNullException>(() => CombatLog.FirstIndexAtOrAfter(null!, 0));
    }

    /// <summary>An empty log hashes, and to something other than the bare FNV offset basis.</summary>
    /// <remarks>
    /// The 4-byte element count is what separates them. Without it an empty log would hash to the
    /// offset basis itself — the value every empty byte stream in the game shares.
    /// </remarks>
    [Fact]
    public void An_empty_log_hashes_to_more_than_the_offset_basis()
    {
        var empty = CanonicalStateWriter.HashCombatLog(Array.Empty<CombatEvent>());

        empty.ShouldBe(CombatLogReferenceVectors.Row("empty-log").Hash);
        empty.ShouldNotBe(CombatLogReferenceVectors.OffsetBasis);
    }

    private static byte[] BytesFor(CombatLogReferenceVectors.LogRow row) =>
        CanonicalStateWriter.CanonicalBytes(ReferenceLogs.Instance(row.Id));

    public static TheoryData<string> RowIds() => CombatLogReferenceVectors.RowIds();

    public static TheoryData<string, ulong> PublishedRows() => CombatLogReferenceVectors.PublishedRows();
}
