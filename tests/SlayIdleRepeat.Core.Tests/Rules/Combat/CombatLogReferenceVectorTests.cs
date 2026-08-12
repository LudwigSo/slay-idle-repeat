using Shouldly;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Rules.Combat;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat;

/// <summary>
/// 🔒 The committed <c>LogHash</c> reference table of `05` §7, asserted row by row.
/// </summary>
/// <remarks>
/// <para>
/// <b>A failure here is a determinism break, never a test fix.</b> `11` §6 compares the
/// client-reported <c>LogHash</c> against the server-computed one to detect tampering, and M5-12
/// compares it across x64 and two ARM64 devices as the cross-platform determinism gate. If a row
/// moves, every duel in flight starts reporting a mismatch and the gate starts comparing fiction.
/// </para>
/// <para>
/// ⚠️ <b>What this table proves, stated honestly.</b> It proves <b>stability</b> — the encoding of
/// an event list cannot change without a row going red. It does <b>not</b> prove the encoding
/// <i>choice</i> against an external authority, because nobody publishes
/// <c>event list → LogHash</c> vectors for this game. What <i>is</i> externally validated is the
/// hash function underneath: the generator reproduced Landon Curt Noll's published FNV-1a 64
/// vectors before emitting a row, and
/// <see cref="The_writer_still_reproduces_the_published_FNV_1a_vectors"/> re-asserts that the
/// writer does too. Steering rule S5.
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
            "the every-event-type row is the only pin on the enum's ordinals; a member missing from it " +
            "is an ordinal nothing is watching");

        ReferenceLogs.AllEventTypes.Select(e => e.Type).ShouldBe(members.OrderBy(m => (int)m));
    }

    /// <summary>
    /// The table guards the code; this guards the table. A future edit that quietly dropped the
    /// only order-sensitivity pair, or the only large-value row, would leave a suite that still
    /// passes while covering less.
    /// </summary>
    [Theory]
    [InlineData("empty-log")]
    [InlineData("battle-start-only")]
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
    [InlineData("negative-value")]
    public void The_reference_table_still_covers_every_load_bearing_rule(string rowId)
    {
        CombatLogReferenceVectors.Rows.Where(row => row.Id == rowId).ShouldHaveSingleItem();
    }

    /// <summary>Row ids identify a row in a failure message; duplicates make that a lie.</summary>
    [Fact]
    public void The_reference_table_ids_are_unique()
    {
        CombatLogReferenceVectors.Rows.Select(row => row.Id).ShouldBeUnique();
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

    /// <summary>Every row carries a reason, so a failing row explains what it was defending.</summary>
    [Theory]
    [MemberData(nameof(RowIds))]
    public void Every_committed_row_says_what_it_pins(string rowId)
    {
        CombatLogReferenceVectors.Row(rowId).Why.ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// S3 — a floor under the table itself. Every assertion above is a <c>[Theory]</c> over
    /// <see cref="RowIds"/>, and xUnit reports a theory with no data as a pass.
    /// </summary>
    [Fact]
    public void The_reference_table_is_not_empty()
    {
        CombatLogReferenceVectors.Rows.Count.ShouldBeGreaterThanOrEqualTo(14);
        CombatLogReferenceVectors.Published.Count.ShouldBeGreaterThanOrEqualTo(15);
    }

    private static byte[] BytesFor(CombatLogReferenceVectors.LogRow row) =>
        CanonicalStateWriter.CanonicalBytes(ReferenceLogs.Instance(row.Id));

    public static TheoryData<string> RowIds() => CombatLogReferenceVectors.RowIds();

    public static TheoryData<string, ulong> PublishedRows() => CombatLogReferenceVectors.PublishedRows();
}
