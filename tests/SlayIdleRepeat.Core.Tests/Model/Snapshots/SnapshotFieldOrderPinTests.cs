using FluentAssertions;
using SlayIdleRepeat.Core.Model.Snapshots;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Model.Snapshots;

/// <summary>
/// 🔒 `14` §16.6 — *"A CI test pins the field list per <c>SchemaVersion</c>."* This is that test.
/// </summary>
/// <remarks>
/// <para>
/// The subject set — public snapshot records in <c>Core/Model/Snapshots/</c> — is empty today
/// because <c>PlayerSnapshot</c> and <c>RunSnapshot</c> are <c>M1-04</c>/<c>M1-05</c>'s. The rules
/// below are written against that final subject regardless: today they hold over nothing, and the
/// moment M1 declares the first record they become real assertions, with no <c>Skip</c>, no
/// placeholder, and nobody having to remember to switch anything on.
/// </para>
/// <para>
/// Because a vacuous rule proves nothing about its own teeth, the second half of this file drives
/// the same comparison against deliberately wrong lists over a test-only record — so an added,
/// removed and reordered field are each proven to be caught, without ever committing a violation.
/// </para>
/// </remarks>
public sealed class SnapshotFieldOrderPinTests
{
    /// <summary>
    /// 🔒 `14` §16.6 — every snapshot record's canonical field order is exactly the list pinned
    /// for the current <c>SchemaVersion</c>. Vacuous until M1; an assertion over every field of
    /// every snapshot the day it lands.
    /// </summary>
    [Fact]
    public void Every_snapshot_record_matches_the_field_order_pinned_for_the_current_SchemaVersion()
    {
        var pinned = SnapshotFieldOrderPin.For(SnapshotSchema.SchemaVersion);

        var offenders =
            from record in SnapshotFieldOrderPin.SnapshotRecords
            where pinned.ContainsKey(record.Name)
            from offender in SnapshotFieldOrderPin.Violations(
                record.Name,
                SnapshotSchema.SchemaVersion,
                pinned[record.Name],
                CanonicalStateWriter.CanonicalFieldOrder(record))
            select offender;

        offenders.Should().BeEmpty();
    }

    /// <summary>
    /// 🔒 `14` §16.6 — a snapshot record that is not pinned at all is an <b>added</b> record, which
    /// is as much a serialisation change as an added field. This is the rule that fires on the day
    /// M1 declares <c>PlayerSnapshot</c> and does not pin it, which is exactly the intent.
    /// </summary>
    [Fact]
    public void Every_snapshot_record_is_pinned_for_the_current_SchemaVersion()
    {
        var pinned = SnapshotFieldOrderPin.For(SnapshotSchema.SchemaVersion);

        var offenders = SnapshotFieldOrderPin.SnapshotRecords
            .Where(record => !pinned.ContainsKey(record.Name))
            .Select(record =>
                $"{record.FullName} is a snapshot record with no pinned field list for " +
                $"SchemaVersion {SnapshotSchema.SchemaVersion}. {SnapshotFieldOrderPin.Consequence}");

        offenders.Should().BeEmpty();
    }

    /// <summary>
    /// 🔒 `14` §16.6 — a pinned record that no longer exists is a <b>removed</b> record. Without
    /// this half, deleting a snapshot outright would pass a pin that only ever looks forwards.
    /// </summary>
    [Fact]
    public void Every_record_pinned_for_the_current_SchemaVersion_still_exists()
    {
        var declared = SnapshotFieldOrderPin.SnapshotRecords
            .Select(record => record.Name)
            .ToHashSet(StringComparer.Ordinal);

        var offenders = SnapshotFieldOrderPin.For(SnapshotSchema.SchemaVersion).Keys
            .Where(name => !declared.Contains(name))
            .Select(name =>
                $"{name} is pinned for SchemaVersion {SnapshotSchema.SchemaVersion} but no longer " +
                $"exists in Core/Model/Snapshots/. {SnapshotFieldOrderPin.Consequence}");

        offenders.Should().BeEmpty();
    }

    /// <summary>
    /// `14` §16.6 — the current <c>SchemaVersion</c> has a pin at all. A bump that forgets to add
    /// its section would otherwise leave the rule silently guarding nothing.
    /// </summary>
    [Fact]
    public void The_pin_file_carries_a_section_for_the_current_SchemaVersion()
    {
        SnapshotFieldOrderPin.PinnedVersions.Should().Contain(SnapshotSchema.SchemaVersion);
    }

    /// <summary>
    /// `14` §16.6 — every version up to the current one keeps its section. An old list describes
    /// data that already exists on real devices; deleting it deletes the migration's starting point.
    /// </summary>
    [Fact]
    public void The_pin_file_keeps_a_section_for_every_schema_version_up_to_the_current_one()
    {
        var expected = Enumerable.Range(1, SnapshotSchema.SchemaVersion);

        SnapshotFieldOrderPin.PinnedVersions.Should().BeEquivalentTo(expected);
    }

    /// <summary>
    /// `14` §16.6 — the pin describes the <b>current</b> schema and no future one. A section for a
    /// version above <c>SchemaVersion</c> means someone pinned a shape without bumping the number.
    /// </summary>
    [Fact]
    public void The_pin_file_carries_no_section_above_the_current_SchemaVersion()
    {
        SnapshotFieldOrderPin.PinnedVersions.Should().OnlyContain(v => v <= SnapshotSchema.SchemaVersion);
    }

    /// <summary>
    /// 🔒 `14` §16.6 — the pin bites on a <b>reordered</b> field. The subtlest of the three
    /// changes: the field set is identical, every name and type is still present, and the byte
    /// stream is completely different.
    /// </summary>
    [Fact]
    public void The_pin_catches_a_reordered_field()
    {
        var actual = CanonicalStateWriter.CanonicalFieldOrder(typeof(PlayerLikeSnapshot));
        var reordered = new[] { actual[0], actual[2], actual[1] };

        var offenders = SnapshotFieldOrderPin.Violations(nameof(PlayerLikeSnapshot), 1, reordered, actual);

        offenders.Should().NotBeEmpty();
    }

    /// <summary>🔒 `14` §16.6 — the pin bites on an <b>added</b> field.</summary>
    [Fact]
    public void The_pin_catches_an_added_field()
    {
        var actual = CanonicalStateWriter.CanonicalFieldOrder(typeof(PlayerLikeSnapshot));
        var withoutTheNewField = actual.Take(actual.Count - 1).ToArray();

        var offenders = SnapshotFieldOrderPin.Violations(nameof(PlayerLikeSnapshot), 1, withoutTheNewField, actual);

        offenders.Should().ContainSingle().Which.Should().Contain("pinned <no field>");
    }

    /// <summary>🔒 `14` §16.6 — the pin bites on a <b>removed</b> field.</summary>
    [Fact]
    public void The_pin_catches_a_removed_field()
    {
        var actual = CanonicalStateWriter.CanonicalFieldOrder(typeof(PlayerLikeSnapshot));
        var withAnExtraField = actual.Append("Removed:System.Int32").ToArray();

        var offenders = SnapshotFieldOrderPin.Violations(nameof(PlayerLikeSnapshot), 1, withAnExtraField, actual);

        offenders.Should().ContainSingle().Which.Should().Contain("found <no field>");
    }

    /// <summary>
    /// `14` §16.6 — the pin bites on a field whose <b>type</b> changed even though its name and
    /// position did not. Widening an id or making a field optional both move the bytes.
    /// </summary>
    [Fact]
    public void The_pin_catches_a_field_whose_type_changed()
    {
        var pinned = CanonicalStateWriter.CanonicalFieldOrder(typeof(PlayerLikeSnapshot));
        var actual = CanonicalStateWriter.CanonicalFieldOrder(typeof(WidenedPlayerLikeSnapshot));

        var offenders = SnapshotFieldOrderPin.Violations(nameof(PlayerLikeSnapshot), 1, pinned, actual);

        offenders.Should().NotBeEmpty();
    }

    /// <summary>
    /// 🔒 `14` §16.6 — a failure says plainly that this is a serialisation change requiring a
    /// <c>SchemaVersion</c> bump and a migration, not a test edit. A pin whose message reads like
    /// an ordinary assertion failure gets "fixed" by editing the pin, which defeats the rule.
    /// </summary>
    [Fact]
    public void A_pin_failure_says_it_needs_a_SchemaVersion_bump_and_a_migration()
    {
        var actual = CanonicalStateWriter.CanonicalFieldOrder(typeof(PlayerLikeSnapshot));
        var reordered = new[] { actual[1], actual[0], actual[2] };

        var offenders = SnapshotFieldOrderPin.Violations(nameof(PlayerLikeSnapshot), 1, reordered, actual);

        offenders.Should().NotBeEmpty();
        offenders[0].Should().Contain("SERIALISATION CHANGE")
            .And.Contain("SnapshotSchema.SchemaVersion")
            .And.Contain("migration")
            .And.Contain("never by editing the pinned list");
    }

    /// <summary>
    /// `14` §16.6 — an unchanged record produces no violation. The negative half of the self-test:
    /// a comparison that flagged everything would also "prove" it has teeth.
    /// </summary>
    [Fact]
    public void The_pin_is_silent_when_the_field_order_is_unchanged()
    {
        var actual = CanonicalStateWriter.CanonicalFieldOrder(typeof(PlayerLikeSnapshot));

        var offenders = SnapshotFieldOrderPin.Violations(nameof(PlayerLikeSnapshot), 1, actual, actual);

        offenders.Should().BeEmpty();
    }

    /// <summary>
    /// 🔒 `14` §16.6 — the predicate that selects this pin's subject set actually recognises a
    /// snapshot record. It is the one thing the three rules above cannot prove about themselves
    /// while the subject set is empty: an <c>IsCanonicalRecord</c> that answered <c>false</c> for
    /// everything would leave them vacuous forever, including on the day M1 lands
    /// <c>PlayerSnapshot</c> — a pin that never bites and never says why.
    /// </summary>
    [Fact]
    public void IsCanonicalRecord_accepts_a_positional_record()
    {
        CanonicalStateWriter.IsCanonicalRecord(typeof(PlayerLikeSnapshot)).Should().BeTrue();
    }

    /// <summary>
    /// 🔒 `14` §16.6 — and it rejects every shape whose declaration order reflection cannot pin,
    /// so the pin's idea of a snapshot is the same closed set <c>CanonicalBytes</c> will encode.
    /// A predicate that accepted a plain class would pin a field order reflection never promised.
    /// </summary>
    [Theory]
    [MemberData(nameof(ShapesWithNoPinnableFieldOrder))]
    public void IsCanonicalRecord_rejects_a_shape_with_no_pinnable_declaration_order(Type shape)
    {
        CanonicalStateWriter.IsCanonicalRecord(shape).Should().BeFalse();
    }

    /// <summary>
    /// `14` §16.6 — the pinned list is the writer's own depth-first traversal, so a nested record's
    /// fields appear inside their parent's, in declaration order, with a dotted path.
    /// </summary>
    [Fact]
    public void CanonicalFieldOrder_flattens_a_nested_record_depth_first()
    {
        var order = CanonicalStateWriter.CanonicalFieldOrder(typeof(OuterSnapshot));

        order.Should().Equal(
            "Head:System.String",
            "Middle.Label:System.String",
            "Middle.Leaf.Depth:System.Int32",
            "Middle.Leaf.Label:System.String",
            "Tail:System.Int32");
    }

    /// <summary>
    /// A list's element slot and a dictionary's key and value slots are named in the path, so
    /// wrapping a field in a collection reads as the shape change it is.
    /// </summary>
    [Fact]
    public void CanonicalFieldOrder_names_the_element_key_and_value_slots_of_a_collection()
    {
        CanonicalStateWriter.CanonicalFieldOrder(typeof(InnerListSnapshot))
            .Should().Equal("Items[].Depth:System.Int32", "Items[].Label:System.String");

        CanonicalStateWriter.CanonicalFieldOrder(typeof(StringMapSnapshot))
            .Should().Equal("ByName{key}:System.String", "ByName{value}:System.Int32");
    }

    /// <summary>
    /// An optional field is pinned as optional. Making a field nullable inserts a presence byte
    /// into every future byte stream, so it must read differently from the non-optional form.
    /// </summary>
    [Fact]
    public void CanonicalFieldOrder_distinguishes_an_optional_field_from_a_required_one()
    {
        var optional = CanonicalStateWriter.CanonicalFieldOrder(typeof(OneValueSnapshot<int?>));
        var required = CanonicalStateWriter.CanonicalFieldOrder(typeof(OneValueSnapshot<int>));

        optional.Should().Equal("Value:System.Nullable<System.Int32>");
        required.Should().Equal("Value:System.Int32");
    }

    /// <summary>The shapes with no reflection-guaranteed declaration order, one fixture per shape.</summary>
    public static TheoryData<Type> ShapesWithNoPinnableFieldOrder() => new()
    {
        typeof(UnsupportedSnapshots.NotARecord),
        typeof(UnsupportedSnapshots.AmbiguousConstructors),
        typeof(UnsupportedSnapshots.Empty),
        typeof(UnsupportedSnapshots.WithPropertyOutsideTheConstructor),
    };
}
