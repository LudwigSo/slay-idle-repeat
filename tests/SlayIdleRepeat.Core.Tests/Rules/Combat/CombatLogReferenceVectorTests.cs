using Shouldly;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Rules.Combat;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat;

/// <summary>The committed <c>LogHash</c> reference table, asserted row by row.</summary>
/// <remarks>
/// A failure here is a determinism break, never a test fix: a tamper check compares the
/// client-reported <c>LogHash</c> against the server's. It proves stability — the encoding cannot
/// change without a row going red — not an external encoding authority, since none exists for this
/// game; what is externally validated is the hash function underneath.
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
    /// <see cref="SimulationResult.LogHash"/> is computed by.
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
    /// The arithmetic of the encoding, stated as a rule rather than left implicit in the hex strings:
    /// a log of <c>N</c> events is <c>4 + 48N</c> bytes — a 4-byte element count, then six 8-byte
    /// fields per event, with no presence byte since <see cref="CombatEvent"/> is a non-nullable
    /// value type.
    /// </summary>
    [Theory]
    [MemberData(nameof(RowIds))]
    public void One_event_occupies_exactly_six_widened_fields(string rowId)
    {
        var row = CombatLogReferenceVectors.Row(rowId);

        row.EncodedByteLength.ShouldBe(4 + (48 * row.EventCount), $"'{row.Id}' pins {row.Why}");
    }

    /// <summary>
    /// The writer still is FNV-1a 64, not merely self-consistent with a table generated beside it:
    /// if the algorithm under it were rotated, the rows above would still agree with each other and
    /// only this test would notice.
    /// </summary>
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
    /// The property the whole log format rests on: order is inside the hash. A simulator that
    /// batched a tick's events and flushed them in a different order would produce a different
    /// replay, and this is what makes that visible instead of silent.
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
    /// Every <see cref="CombatEventType"/> member appears in the <c>every-event-type</c> row, so
    /// the committed hash pins every ordinal — the ordinal is what gets hashed, so inserting a
    /// member mid-enum would silently rewrite every <c>LogHash</c> ever computed.
    /// </summary>
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
    /// The <c>completed-battle</c> row is produced by <see cref="CombatLog.Complete"/>, so the
    /// terminal <see cref="CombatEventType.BattleEnd"/>'s own six fields are inside a committed
    /// hash — every other row is written by hand, which previously left <c>BattleEnd</c> pinned by
    /// nothing at all.
    /// </summary>
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
    /// A log stored as a <see cref="List{T}"/> encodes identically to one stored as an array —
    /// asserted rather than assumed, since the server rebuilds the fight independently and a
    /// divergence here reads as tampering and discards a real duel.
    /// </summary>
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
    /// A floor under the table itself: every assertion above is a <c>[Theory]</c> over
    /// <see cref="RowIds"/>, and xUnit reports a theory with no data as a pass.
    /// </summary>
    [Fact]
    public void The_reference_table_is_not_empty()
    {
        CombatLogReferenceVectors.Rows.Count.ShouldBeGreaterThanOrEqualTo(16);
        CombatLogReferenceVectors.Published.Count.ShouldBeGreaterThanOrEqualTo(15);
    }

    /// <summary>
    /// <c>HashCombatLog</c> refuses a root that is not a list. Its parameter is
    /// <see cref="object"/>, so this is the one hashing mode whose root kind actually varies: a
    /// single event would hash its 48 record bytes with no element count and return a perfectly
    /// plausible <see cref="ulong"/> that is the <c>LogHash</c> of nothing.
    /// </summary>
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

    /// <summary>
    /// An empty log hashes, and to something other than the bare FNV offset basis: the 4-byte
    /// element count is what separates them.
    /// </summary>
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
