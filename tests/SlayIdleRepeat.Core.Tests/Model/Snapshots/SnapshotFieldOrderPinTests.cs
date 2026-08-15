using Shouldly;
using SlayIdleRepeat.Core.Model.Snapshots;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Model.Snapshots;

/// <summary>A CI test pins the field list per <c>SchemaVersion</c>. This is that test.</summary>
/// <remarks>
/// Because a vacuous rule proves nothing about its own teeth, the second half of this file drives
/// the same comparison against deliberately wrong lists over a test-only record — an added,
/// removed and reordered field each proven caught, without ever committing a violation.
/// </remarks>
public sealed class SnapshotFieldOrderPinTests
{
    /// <summary>Every snapshot record's canonical field order is exactly the list pinned for the current <c>SchemaVersion</c>.</summary>
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

        offenders.ShouldBeEmpty();
    }

    /// <summary>
    /// A snapshot record that is not pinned at all is an <b>added</b> record, which is as much a
    /// serialisation change as an added field.
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

        offenders.ShouldBeEmpty();
    }

    /// <summary>
    /// A pinned record that no longer exists is a <b>removed</b> record. Without this half,
    /// deleting a snapshot outright would pass a pin that only ever looks forwards.
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

        offenders.ShouldBeEmpty();
    }

    /// <summary>
    /// The current <c>SchemaVersion</c> has a pin at all. A bump that forgets to add its section
    /// would otherwise leave the rule silently guarding nothing.
    /// </summary>
    [Fact]
    public void The_pin_file_carries_a_section_for_the_current_SchemaVersion()
    {
        SnapshotFieldOrderPin.PinnedVersions.ShouldContain(SnapshotSchema.SchemaVersion);
    }

    /// <summary>
    /// Every version up to the current one keeps its section. An old list describes data that
    /// already exists on real devices; deleting it deletes the migration's starting point.
    /// </summary>
    [Fact]
    public void The_pin_file_keeps_a_section_for_every_schema_version_up_to_the_current_one()
    {
        var expected = Enumerable.Range(1, SnapshotSchema.SchemaVersion);

        SnapshotFieldOrderPin.PinnedVersions.ShouldBe(expected, ignoreOrder: true);
    }

    /// <summary>
    /// The pin describes the <b>current</b> schema and no future one. A section for a version
    /// above <c>SchemaVersion</c> means someone pinned a shape without bumping the number.
    /// </summary>
    [Fact]
    public void The_pin_file_carries_no_section_above_the_current_SchemaVersion()
    {
        SnapshotFieldOrderPin.PinnedVersions.ShouldNotBeEmpty();
        SnapshotFieldOrderPin.PinnedVersions.ShouldAllBe(v => v <= SnapshotSchema.SchemaVersion);
    }

    /// <summary>
    /// The pin bites on a <b>reordered</b> field. The subtlest of the three changes: the field set
    /// is identical, every name and type is still present, and the byte stream is completely
    /// different.
    /// </summary>
    [Fact]
    public void The_pin_catches_a_reordered_field()
    {
        var actual = CanonicalStateWriter.CanonicalFieldOrder(typeof(PlayerLikeSnapshot));
        var reordered = new[] { actual[0], actual[2], actual[1] };

        var offenders = SnapshotFieldOrderPin.Violations(nameof(PlayerLikeSnapshot), 1, reordered, actual);

        offenders.ShouldNotBeEmpty();
    }

    [Fact]
    public void The_pin_catches_an_added_field()
    {
        var actual = CanonicalStateWriter.CanonicalFieldOrder(typeof(PlayerLikeSnapshot));
        var withoutTheNewField = actual.Take(actual.Count - 1).ToArray();

        var offenders = SnapshotFieldOrderPin.Violations(nameof(PlayerLikeSnapshot), 1, withoutTheNewField, actual);

        offenders.ShouldHaveSingleItem().ShouldContain("pinned <no field>", Case.Sensitive);
    }

    [Fact]
    public void The_pin_catches_a_removed_field()
    {
        var actual = CanonicalStateWriter.CanonicalFieldOrder(typeof(PlayerLikeSnapshot));
        var withAnExtraField = actual.Append("Removed:System.Int32").ToArray();

        var offenders = SnapshotFieldOrderPin.Violations(nameof(PlayerLikeSnapshot), 1, withAnExtraField, actual);

        offenders.ShouldHaveSingleItem().ShouldContain("found <no field>", Case.Sensitive);
    }

    /// <summary>
    /// The pin bites on a field whose <b>type</b> changed even though its name and position did
    /// not. Widening an id or making a field optional both move the bytes.
    /// </summary>
    [Fact]
    public void The_pin_catches_a_field_whose_type_changed()
    {
        var pinned = CanonicalStateWriter.CanonicalFieldOrder(typeof(PlayerLikeSnapshot));
        var actual = CanonicalStateWriter.CanonicalFieldOrder(typeof(WidenedPlayerLikeSnapshot));

        var offenders = SnapshotFieldOrderPin.Violations(nameof(PlayerLikeSnapshot), 1, pinned, actual);

        offenders.ShouldNotBeEmpty();
    }

    /// <summary>
    /// A failure says plainly that this is a serialisation change requiring a
    /// <c>SchemaVersion</c> bump and a migration, not a test edit. A pin whose message reads like
    /// an ordinary assertion failure gets "fixed" by editing the pin, which defeats the rule.
    /// </summary>
    [Fact]
    public void A_pin_failure_says_it_needs_a_SchemaVersion_bump_and_a_migration()
    {
        var actual = CanonicalStateWriter.CanonicalFieldOrder(typeof(PlayerLikeSnapshot));
        var reordered = new[] { actual[1], actual[0], actual[2] };

        var offenders = SnapshotFieldOrderPin.Violations(nameof(PlayerLikeSnapshot), 1, reordered, actual);

        offenders.ShouldNotBeEmpty();
        offenders[0].ShouldContain("SERIALISATION CHANGE", Case.Sensitive);
        offenders[0].ShouldContain("SnapshotSchema.SchemaVersion", Case.Sensitive);
        offenders[0].ShouldContain("migration", Case.Sensitive);
        offenders[0].ShouldContain("never by editing the pinned list", Case.Sensitive);
    }

    /// <summary>
    /// An unchanged record produces no violation. The negative half of the self-test: a
    /// comparison that flagged everything would also "prove" it has teeth.
    /// </summary>
    [Fact]
    public void The_pin_is_silent_when_the_field_order_is_unchanged()
    {
        var actual = CanonicalStateWriter.CanonicalFieldOrder(typeof(PlayerLikeSnapshot));

        var offenders = SnapshotFieldOrderPin.Violations(nameof(PlayerLikeSnapshot), 1, actual, actual);

        offenders.ShouldBeEmpty();
    }

    /// <summary>
    /// The predicate that selects this pin's subject set actually recognises a snapshot record.
    /// An <c>IsCanonicalRecord</c> that answered <c>false</c> for everything would leave the rules
    /// above vacuous forever — a pin that never bites and never says why.
    /// </summary>
    [Fact]
    public void IsCanonicalRecord_accepts_a_positional_record()
    {
        CanonicalStateWriter.IsCanonicalRecord(typeof(PlayerLikeSnapshot)).ShouldBeTrue();
    }

    /// <summary>
    /// And it rejects every shape whose declaration order reflection cannot pin, so the pin's idea
    /// of a snapshot is the same closed set <c>CanonicalBytes</c> will encode. A predicate that
    /// accepted a plain class would pin a field order reflection never promised.
    /// </summary>
    [Theory]
    [MemberData(nameof(ShapesWithNoPinnableFieldOrder))]
    public void IsCanonicalRecord_rejects_a_shape_with_no_pinnable_declaration_order(Type shape)
    {
        CanonicalStateWriter.IsCanonicalRecord(shape).ShouldBeFalse();
    }

    /// <summary>
    /// The pinned list is the writer's own depth-first traversal, so a nested record's fields
    /// appear inside their parent's, in declaration order, with a dotted path.
    /// </summary>
    [Fact]
    public void CanonicalFieldOrder_flattens_a_nested_record_depth_first()
    {
        var order = CanonicalStateWriter.CanonicalFieldOrder(typeof(OuterSnapshot));

        order.ShouldBe(new[]
        {
            "Head:System.String",
            "Middle.Label:System.String",
            "Middle.Leaf.Depth:System.Int32",
            "Middle.Leaf.Label:System.String",
            "Tail:System.Int32",
        });
    }

    /// <summary>
    /// A list's element slot and a dictionary's key and value slots are named in the path, so
    /// wrapping a field in a collection reads as the shape change it is.
    /// </summary>
    [Fact]
    public void CanonicalFieldOrder_names_the_element_key_and_value_slots_of_a_collection()
    {
        CanonicalStateWriter.CanonicalFieldOrder(typeof(InnerListSnapshot))
            .ShouldBe(new[] { "Items[].Depth:System.Int32", "Items[].Label:System.String" });

        CanonicalStateWriter.CanonicalFieldOrder(typeof(StringMapSnapshot))
            .ShouldBe(new[] { "ByName{key}:System.String", "ByName{value}:System.Int32" });
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

        optional.ShouldBe(new[] { "Value:System.Nullable<System.Int32>" });
        required.ShouldBe(new[] { "Value:System.Int32" });
    }

    /// <summary>
    /// The <b>other</b> half of the subject query is not the vacuity source either.
    /// <see cref="IsCanonicalRecord_accepts_a_positional_record"/> proves the record predicate
    /// works; nothing proved that <c>IsPublic &amp;&amp; !IsNested &amp;&amp; IsUnderSnapshots</c>
    /// reaches the namespace at all. If it did not, all four pin rules would stay green over
    /// nothing, forever.
    /// </summary>
    [Fact]
    public void The_visibility_and_namespace_filter_reaches_the_snapshots_namespace_today()
    {
        var names = SnapshotFieldOrderPin.PublicTypesUnderSnapshots.Select(type => type.Name).ToArray();

        names.ShouldContain(nameof(SnapshotSchema));
        names.ShouldContain(nameof(CanonicalStateWriter));
    }

    /// <summary>The floor that replaced the vacuity tripwire: the subject set is never empty again, and it names the record it expects.</summary>
    /// <remarks>
    /// Both halves matter: the count floor catches the selector being emptied by a move or a
    /// visibility change, and naming <c>PlayerSnapshot</c> catches a rename, which a bare count
    /// would not once <c>RunSnapshot</c> lands beside it.
    /// </remarks>
    [Fact]
    public void The_pins_subject_set_is_not_empty_and_holds_the_first_snapshot_record()
    {
        SnapshotFieldOrderPin.SnapshotRecords.ShouldNotBeEmpty(
            "the pin woke up in M1-04 and must never go back to sleep. An empty subject set is the " +
            "one failure a pin cannot announce: all four rules above would report success forever. " +
            "If PlayerSnapshot moved, was renamed, was made internal or was nested, fix that — do " +
            "not weaken the selector.");

        SnapshotFieldOrderPin.SnapshotRecords
            .Select(record => record.Name)
            .ShouldContain(nameof(PlayerSnapshot));

        // Naming the second record as well is what the remark above anticipated: with two records
        // in the set, a bare count survives one of them being renamed, made internal, nested or
        // moved out of Core/Model/Snapshots/, and the pin would go on guarding the survivor while
        // reporting success over the one that left.
        SnapshotFieldOrderPin.SnapshotRecords
            .Select(record => record.Name)
            .ShouldContain(nameof(RunSnapshot));
    }

    /// <summary>
    /// Every snapshot record carries <c>SchemaVersion</c> as its <b>first</b> field. Neither
    /// <c>CanonicalFieldOrder</c> nor the pin looked at index 0 before, so an omission would fail
    /// nothing until <c>stateHash</c> values existed in the wild.
    /// </summary>
    [Fact]
    public void Every_snapshot_record_carries_SchemaVersion_as_its_first_field()
    {
        var offenders = SnapshotFieldOrderPin.SnapshotRecords
            .Where(record => CanonicalStateWriter.CanonicalFieldOrder(record).FirstOrDefault() != SchemaVersionField)
            .Select(record =>
                $"{record.FullName} does not carry '{SchemaVersionField}' as its first field. Every " +
                "*Snapshot record does (14 §16.6, 30 §11.3) — Rehydrate validates it, and a record " +
                "whose version is not the first thing written cannot be read back before it is known.");

        offenders.ShouldBeEmpty();
    }

    /// <summary>The teeth of the rule above: it accepts the shape that complies and rejects one that does not.</summary>
    [Fact]
    public void The_first_field_rule_recognises_a_record_that_does_and_one_that_does_not()
    {
        CanonicalStateWriter.CanonicalFieldOrder(typeof(PlayerLikeSnapshot))[0].ShouldBe(SchemaVersionField);
        CanonicalStateWriter.CanonicalFieldOrder(typeof(InnerSnapshot))[0].ShouldNotBe(SchemaVersionField);
    }

    /// <summary>The pinned-field-list entry a <c>SchemaVersion</c> at index 0 produces.</summary>
    private const string SchemaVersionField = "SchemaVersion:System.Int32";

    /// <summary>The shapes with no reflection-guaranteed declaration order, one fixture per shape.</summary>
    public static TheoryData<Type> ShapesWithNoPinnableFieldOrder() => new()
    {
        typeof(UnsupportedSnapshots.NotARecord),
        typeof(UnsupportedSnapshots.AmbiguousConstructors),
        typeof(UnsupportedSnapshots.Empty),
        typeof(UnsupportedSnapshots.WithPropertyOutsideTheConstructor),
        typeof(UnsupportedSnapshots.WithPublicField),
    };
}
