using System.Globalization;
using System.Text.RegularExpressions;
using Shouldly;
using SlayIdleRepeat.TestSupport;
using SlayIdleRepeat.Core.Model.Snapshots;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Model.Snapshots;

/// <summary>
/// 🔒 The public surface of `14` §16.6: the wire form, the two hashing modes, and determinism.
/// </summary>
/// <remarks>
/// §16.6 opens with the reason this class exists — *"A second serialiser producing 'almost the
/// same bytes' is how parity tests rot; there is exactly one."* The two named modes here are that
/// one writer's only doors, so no caller ever hand-rolls the run-command concatenation.
/// </remarks>
public sealed class CanonicalStateWriterTests
{
    /// <summary>Exactly <c>"fnv1a:"</c> plus 16 lowercase hexadecimal characters, and nothing else.</summary>
    private static readonly Regex WireForm = new("^fnv1a:[0-9a-f]{16}$", RegexOptions.None, TimeSpan.FromSeconds(1));

    /// <summary>🔒 The wire form names its algorithm, so it can only be rotated visibly.</summary>
    [Fact]
    public void HashMetaCommandState_prefixes_the_hash_with_the_algorithm_name()
    {
        var hash = CanonicalStateWriter.HashMetaCommandState(ReferenceSnapshots.Player);

        hash.ShouldStartWith("fnv1a:", Case.Sensitive);
    }

    /// <summary>The wire form is the prefix plus exactly 16 hex characters — 22 in all.</summary>
    [Fact]
    public void HashMetaCommandState_produces_sixteen_hex_characters_after_the_prefix()
    {
        var hash = CanonicalStateWriter.HashMetaCommandState(ReferenceSnapshots.Player);

        hash.Length.ShouldBe(22);
        WireForm.IsMatch(hash).ShouldBeTrue($"'{hash}' is not of the form {WireForm}");
    }

    /// <summary>🔒 The hex is lowercase. A mixed-case wire form is two wire forms.</summary>
    /// <remarks>
    /// Asserted against a value whose hash actually <b>has</b> letters in it, and checked for at
    /// least one. The obvious form — <c>hash.ShouldBe(hash.ToLowerInvariant())</c> — is vacuous
    /// for any hash made only of digits, and is subsumed by the wire-form regex besides: both would
    /// pass an uppercase formatter roughly whenever the digits happened to fall that way.
    /// </remarks>
    [Fact]
    public void HashMetaCommandState_produces_lowercase_hexadecimal()
    {
        var hash = CanonicalStateWriter.HashMetaCommandState(ReferenceSnapshots.Scalars);
        var digits = hash[CanonicalStateWriter.AlgorithmPrefix.Length..];

        var lowercaseHexLetters = new[] { "a", "b", "c", "d", "e", "f" };

        lowercaseHexLetters.Any(digits.Contains)
            .ShouldBeTrue($"'{digits}' has no hex letter at all, so it cannot show the case of one");
        lowercaseHexLetters.Select(letter => letter.ToUpperInvariant()).Any(digits.Contains)
            .ShouldBeFalse($"'{digits}' contains an uppercase hex letter; the wire form is lowercase");
    }

    /// <summary>
    /// 🔒 A hash with leading zero nibbles is <b>zero-padded</b> to 16 characters, never truncated.
    /// A trimming formatter produces a shorter string roughly once every sixteen states and is
    /// invisible until a client compares one against a padded one.
    /// </summary>
    [Fact]
    public void HashMetaCommandState_zero_pads_a_hash_with_leading_zero_nibbles()
    {
        var snapshot = new OneIntSnapshot(ReferenceSnapshots.LeadingZeroHashValue);

        var hash = CanonicalStateWriter.HashMetaCommandState(snapshot);

        hash.ShouldStartWith("fnv1a:00", Case.Sensitive);
        WireForm.IsMatch(hash).ShouldBeTrue($"'{hash}' is not of the form {WireForm}");
    }

    /// <summary>The wire form's hex is the raw 64-bit hash of the same bytes, restated.</summary>
    [Fact]
    public void HashMetaCommandState_renders_the_hash_of_the_canonical_bytes()
    {
        var expected = CanonicalStateWriter.Fnv1a64(
            CanonicalStateWriter.CanonicalBytes(ReferenceSnapshots.Player));

        var hash = CanonicalStateWriter.HashMetaCommandState(ReferenceSnapshots.Player);

        hash.ShouldBe("fnv1a:" + expected.ToString("x16", CultureInfo.InvariantCulture));
    }

    /// <summary>The prefix is a public constant, so a consumer checks it rather than retyping it.</summary>
    [Fact]
    public void AlgorithmPrefix_is_the_literal_prefix_of_every_hash_the_writer_produces()
    {
        CanonicalStateWriter.AlgorithmPrefix.ShouldBe("fnv1a:");
        CanonicalStateWriter.HashMetaCommandState(ReferenceSnapshots.Player)
            .ShouldStartWith(CanonicalStateWriter.AlgorithmPrefix, Case.Sensitive);
    }

    /// <summary>🔒 A run command hashes <c>PlayerSnapshot</c> then <c>RunSnapshot</c>, concatenated.</summary>
    [Fact]
    public void HashRunCommandState_hashes_the_player_bytes_followed_by_the_run_bytes()
    {
        var concatenated = CanonicalStateWriter.CanonicalBytes(ReferenceSnapshots.Player)
            .Concat(CanonicalStateWriter.CanonicalBytes(ReferenceSnapshots.Run))
            .ToArray();
        var expected = CanonicalStateWriter.Fnv1a64(concatenated);

        var hash = CanonicalStateWriter.HashRunCommandState(ReferenceSnapshots.Player, ReferenceSnapshots.Run);

        hash.ShouldBe("fnv1a:" + expected.ToString("x16", CultureInfo.InvariantCulture));
    }

    /// <summary>🔒 A meta command hashes <c>PlayerSnapshot</c> alone.</summary>
    [Fact]
    public void HashMetaCommandState_hashes_the_player_bytes_alone()
    {
        var expected = CanonicalStateWriter.Fnv1a64(
            CanonicalStateWriter.CanonicalBytes(ReferenceSnapshots.Player));

        var hash = CanonicalStateWriter.HashMetaCommandState(ReferenceSnapshots.Player);

        hash.ShouldBe("fnv1a:" + expected.ToString("x16", CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// 🔒 The run-mode hash of (player, run) is <b>not</b> the meta-mode hash of the player.
    /// Two commands that touched different state must not report the same <c>stateHash</c>.
    /// </summary>
    [Fact]
    public void HashRunCommandState_differs_from_the_meta_hash_of_the_same_player()
    {
        var run = CanonicalStateWriter.HashRunCommandState(ReferenceSnapshots.Player, ReferenceSnapshots.Run);
        var meta = CanonicalStateWriter.HashMetaCommandState(ReferenceSnapshots.Player);

        run.ShouldNotBe(meta);
    }

    /// <summary>
    /// 🔒 The two arguments are <b>ordered</b>. Swapping them is a different state and a different
    /// hash — the concatenation is not a commutative combine.
    /// </summary>
    [Fact]
    public void HashRunCommandState_is_not_symmetric_in_its_two_arguments()
    {
        var forwards = CanonicalStateWriter.HashRunCommandState(ReferenceSnapshots.Player, ReferenceSnapshots.Run);
        var backwards = CanonicalStateWriter.HashRunCommandState(ReferenceSnapshots.Run, ReferenceSnapshots.Player);

        forwards.ShouldNotBe(backwards);
    }

    /// <summary>
    /// The run mode is a hash of the concatenated <b>bytes</b>, not a combination of two hashes.
    /// Combining hashes would let a caller reproduce the run hash from two meta hashes and hash
    /// the halves independently — a second serialisation path by the back door.
    /// </summary>
    [Fact]
    public void HashRunCommandState_hashes_concatenated_bytes_rather_than_combining_two_hashes()
    {
        var playerHash = CanonicalStateWriter.Fnv1a64(CanonicalStateWriter.CanonicalBytes(ReferenceSnapshots.Player));
        var runHash = CanonicalStateWriter.Fnv1a64(CanonicalStateWriter.CanonicalBytes(ReferenceSnapshots.Run));

        var hash = CanonicalStateWriter.HashRunCommandState(ReferenceSnapshots.Player, ReferenceSnapshots.Run);

        hash.ShouldNotBe("fnv1a:" + (playerHash ^ runHash).ToString("x16", CultureInfo.InvariantCulture));
    }

    /// <summary>A run command with no player snapshot has no canonical state to hash.</summary>
    [Fact]
    public void HashRunCommandState_refuses_a_missing_player_snapshot()
    {
        var act = () => CanonicalStateWriter.HashRunCommandState(null!, ReferenceSnapshots.Run);

        Should.Throw<ArgumentNullException>(act);
    }

    /// <summary>And a run command with no run snapshot is not a meta command in disguise.</summary>
    [Fact]
    public void HashRunCommandState_refuses_a_missing_run_snapshot()
    {
        var act = () => CanonicalStateWriter.HashRunCommandState(ReferenceSnapshots.Player, null!);

        Should.Throw<ArgumentNullException>(act);
    }

    /// <summary>The meta mode's single snapshot is required for the same reason.</summary>
    [Fact]
    public void HashMetaCommandState_refuses_a_missing_snapshot()
    {
        var act = () => CanonicalStateWriter.HashMetaCommandState(null!);

        Should.Throw<ArgumentNullException>(act);
    }

    /// <summary>🔒 The same input hashes identically twice — nothing is consumed or advanced.</summary>
    [Fact]
    public void HashMetaCommandState_returns_the_same_hash_when_called_twice()
    {
        var first = CanonicalStateWriter.HashMetaCommandState(ReferenceSnapshots.Scalars);
        var second = CanonicalStateWriter.HashMetaCommandState(ReferenceSnapshots.Scalars);

        second.ShouldBe(first);
    }

    /// <summary>
    /// 🔒 Two separately-constructed values with equal contents hash identically. There is no
    /// per-instance identity, cache or reference in the encoding — only the state.
    /// </summary>
    [Fact]
    public void HashMetaCommandState_returns_the_same_hash_for_two_separately_constructed_equal_values()
    {
        var first = CanonicalStateWriter.HashMetaCommandState(new PlayerLikeSnapshot(1, "PL_0001", 12345L));
        var second = CanonicalStateWriter.HashMetaCommandState(new PlayerLikeSnapshot(1, "PL_0001", 12345L));

        second.ShouldBe(first);
    }

    /// <summary>
    /// Hashing an unrelated value in between changes nothing. A writer that carried state across
    /// calls — an accumulator, a memoised plan, a reused buffer read past its length — would make
    /// a command's <c>stateHash</c> depend on which command preceded it.
    /// </summary>
    [Fact]
    public void HashMetaCommandState_is_unaffected_by_an_intervening_call_with_different_input()
    {
        var before = CanonicalStateWriter.HashMetaCommandState(ReferenceSnapshots.Player);
        CanonicalStateWriter.HashMetaCommandState(ReferenceSnapshots.Scalars);
        var after = CanonicalStateWriter.HashMetaCommandState(ReferenceSnapshots.Player);

        after.ShouldBe(before);
    }

    /// <summary>
    /// A shorter value hashed after a longer one does not inherit the longer one's tail — the
    /// classic reused-buffer bug, which only ever shows up in this exact ordering.
    /// </summary>
    [Fact]
    public void HashMetaCommandState_does_not_leak_a_previous_longer_value_into_a_shorter_one()
    {
        var alone = CanonicalStateWriter.HashMetaCommandState(new OneValueSnapshot<string>("a"));

        CanonicalStateWriter.HashMetaCommandState(new OneValueSnapshot<string>(new string('x', 4096)));
        var afterLong = CanonicalStateWriter.HashMetaCommandState(new OneValueSnapshot<string>("a"));

        afterLong.ShouldBe(alone);
    }

    /// <summary>
    /// A value larger than any stack buffer still encodes to exactly its presence byte, its
    /// 4-byte count and its bytes. The size where an implementation switches from a stack buffer
    /// to the heap is exactly where an off-by-one hides.
    /// </summary>
    [Theory]
    [MemberData(nameof(BufferBoundaryLengths))]
    public void CanonicalBytes_encodes_a_payload_of_any_size(int length)
    {
        var snapshot = new OneValueSnapshot<string>(new string('x', length));

        var bytes = CanonicalStateWriter.CanonicalBytes(snapshot);

        bytes.Length.ShouldBe(1 + 4 + length);
    }

    /// <summary>The wire form is well formed at those same buffer boundaries.</summary>
    [Theory]
    [MemberData(nameof(BufferBoundaryLengths))]
    public void HashMetaCommandState_produces_a_well_formed_wire_form_for_a_payload_of_any_size(int length)
    {
        var snapshot = new OneValueSnapshot<string>(new string('x', length));

        var hash = CanonicalStateWriter.HashMetaCommandState(snapshot);

        WireForm.IsMatch(hash).ShouldBeTrue($"'{hash}' is not of the form {WireForm}");
    }

    /// <summary>
    /// A record that changes in exactly one field produces a different hash. Trivial, and the
    /// thing a writer that silently skipped an unrecognised field would fail.
    /// </summary>
    [Fact]
    public void HashMetaCommandState_changes_when_any_single_field_changes()
    {
        var baseline = CanonicalStateWriter.HashMetaCommandState(ReferenceSnapshots.Player);

        var changed = CanonicalStateWriter.HashMetaCommandState(new PlayerLikeSnapshot(1, "PL_0001", 12346L));

        changed.ShouldNotBe(baseline);
    }

    /// <summary>
    /// 🔒 A record whose shape the writer cannot pin — no single positional constructor — is
    /// refused rather than encoded in whatever order reflection happened to hand back. Reflection
    /// does not guarantee property order; constructor parameter order is declaration order.
    /// </summary>
    [Theory]
    [MemberData(nameof(UnpinnableShapes))]
    public void CanonicalBytes_refuses_a_type_whose_declaration_order_is_not_defined(object snapshot)
    {
        var act = () => CanonicalStateWriter.CanonicalBytes(snapshot);

        Should.Throw<NotSupportedException>(act).Message.ShouldMatchWildcard("*16.6*");
    }

    /// <summary>
    /// 🔒 A value stored in a base-typed slot but constructed as a subclass is refused: its extra
    /// fields are outside the pinned field list, so encoding it would write state the
    /// <c>SchemaVersion</c> pin never saw.
    /// </summary>
    [Fact]
    public void CanonicalBytes_refuses_a_value_whose_runtime_type_is_not_its_declared_type()
    {
        var snapshot = new UnsupportedSnapshots.WithPolymorphicSlot(
            new UnsupportedSnapshots.OpenDerived(1, 2));

        var act = () => CanonicalStateWriter.CanonicalBytes(snapshot);

        Should.Throw<NotSupportedException>(act).Message.ShouldMatchWildcard("*16.6*");
    }

    /// <summary>
    /// 🔒 A record carrying a public property that is <b>not</b> a primary-constructor parameter is
    /// refused. The field list is the parameter list, so such a property would be hashed as zero
    /// bytes — the one defect a state hash may never have, and the shape an optional snapshot
    /// member (<c>record</c> + <c>{ get; init; }</c>) reaches for by default.
    /// </summary>
    [Fact]
    public void CanonicalBytes_refuses_a_record_property_declared_outside_the_primary_constructor()
    {
        var act = () => CanonicalStateWriter.CanonicalBytes(
            new UnsupportedSnapshots.WithPropertyOutsideTheConstructor(1, 3) { RevivesUsed = 99 });

        var thrown = Should.Throw<NotSupportedException>(act);

        thrown.Message.ShouldMatchWildcard("*16.6*");
        thrown.Message.ShouldMatchWildcard("*ZERO BYTES*");
        thrown.Message.ShouldMatchWildcard("*primary constructor*");
    }

    /// <summary>
    /// 🔒 And it is refused rather than silently dropped. Record equality sees the extra field, so
    /// an encoder that skipped it would hand two demonstrably different states the same
    /// <c>stateHash</c> — §2.4's client-mirror check and §13's parity tests would both report
    /// agreement on genuinely divergent state.
    /// </summary>
    [Fact]
    public void HashMetaCommandState_refuses_the_shape_whose_extra_field_record_equality_can_see()
    {
        var quiet = new UnsupportedSnapshots.WithPropertyOutsideTheConstructor(1, 3) { RevivesUsed = 0 };
        var busy = quiet with { RevivesUsed = 99 };

        busy.ShouldNotBe(quiet);

        var hashQuiet = () => CanonicalStateWriter.HashMetaCommandState(quiet);
        var hashBusy = () => CanonicalStateWriter.HashMetaCommandState(busy);

        Should.Throw<NotSupportedException>(hashQuiet);
        Should.Throw<NotSupportedException>(hashBusy);
    }

    /// <summary>
    /// 🔒 A record carrying a public <b>field</b> is refused — the same zero-byte defect as the two
    /// tests above, reached by the door the property check cannot watch.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A field is neither a primary-constructor parameter nor a property, so it falls through both
    /// halves of the shape check. Before this rule, <c>CombatEventAsDocumented</c> hashed
    /// <b>only its constructor parameter</b> and its two fields contributed nothing.
    /// </para>
    /// <para>
    /// ⚠️ The fixture is named after `05` §7's <c>CombatEvent</c> because that is where the shape
    /// actually appears: the battle <c>LogHash</c> is defined over exactly such a record, and
    /// M2-15 would have shipped a hash over one sixth of its own event had this stayed open.
    /// </para>
    /// </remarks>
    [Fact]
    public void CanonicalBytes_refuses_a_record_carrying_a_public_field()
    {
        var act = () => CanonicalStateWriter.CanonicalBytes(
            new UnsupportedSnapshots.CombatEventAsDocumented(3) { SourceId = 4, Value = 41.2536 });

        var thrown = Should.Throw<NotSupportedException>(act);

        thrown.Message.ShouldContain("16.6", Case.Sensitive);
        thrown.Message.ShouldContain("ZERO BYTES", Case.Sensitive);

        // S2 — the generic rule list names BOTH the property-shape and the field-shape reason, so a
        // fragment of it cannot tell the two branches apart. This sentence is emitted only by the
        // field branch, and it names the offending fields.
        thrown.Message.ShouldContain(
            "SPECIFICALLY: CombatEventAsDocumented declares the public instance field(s) [SourceId, Value]",
            Case.Sensitive);
    }

    /// <summary>
    /// 🔒 And it is refused rather than silently hashed to a fraction of itself: two instances that
    /// record equality calls different, differing <b>only</b> in their fields, must not both reach a
    /// hash.
    /// </summary>
    /// <remarks>
    /// This is the assertion that fails loudly if the field rule is ever removed. Without the rule
    /// both calls succeed and return the <i>same</i> value — which is why the test is written as two
    /// refusals over a demonstrated inequality rather than as a hash comparison: a hash comparison
    /// would have to spell out the collision it is trying to prevent.
    /// </remarks>
    [Fact]
    public void HashMetaCommandState_refuses_the_shape_whose_fields_it_cannot_see()
    {
        var quiet = new UnsupportedSnapshots.CombatEventAsDocumented(3) { SourceId = 0, Value = 0.0 };
        var busy = new UnsupportedSnapshots.CombatEventAsDocumented(3) { SourceId = 99, Value = 41.2536 };

        busy.ShouldNotBe(quiet, "record equality sees the fields — that is what makes a shared hash a defect");

        Should.Throw<NotSupportedException>(() => CanonicalStateWriter.HashMetaCommandState(quiet));
        Should.Throw<NotSupportedException>(() => CanonicalStateWriter.HashMetaCommandState(busy));
    }

    /// <summary>
    /// A <c>readonly</c> public field is refused too — <c>readonly</c> changes nothing about
    /// visibility to the encoding, and it is the only form a <c>readonly struct</c> could take.
    /// </summary>
    [Fact]
    public void CanonicalBytes_refuses_a_record_carrying_a_readonly_public_field()
    {
        var act = () => CanonicalStateWriter.CanonicalBytes(
            UnsupportedSnapshots.WithReadonlyPublicField.With(3, 4));

        Should.Throw<NotSupportedException>(act).Message.ShouldContain(
            "SPECIFICALLY: WithReadonlyPublicField declares the public instance field(s) [SourceId]",
            Case.Sensitive);
    }

    /// <summary>
    /// And the property-shape refusal does <b>not</b> claim a field problem — the two branches are
    /// distinguishable in both directions (S2).
    /// </summary>
    [Fact]
    public void The_property_shape_refusal_does_not_name_a_field()
    {
        var thrown = Should.Throw<NotSupportedException>(
            () => CanonicalStateWriter.CanonicalBytes(
                new UnsupportedSnapshots.WithPropertyOutsideTheConstructor(1, 3) { RevivesUsed = 99 }));

        thrown.Message.ShouldNotContain("SPECIFICALLY", Case.Sensitive);
    }

    /// <summary>
    /// The field rule refuses the shape rather than the <i>type</i>: the same record with its fields
    /// promoted into the primary constructor hashes normally.
    /// </summary>
    /// <remarks>
    /// S2 — without this, the two tests above would pass just as well against a writer that had
    /// started refusing every record for some unrelated reason.
    /// </remarks>
    [Fact]
    public void A_record_whose_fields_are_constructor_parameters_still_hashes()
    {
        var promoted = new PromotedFieldsSnapshot(3, 4, 41.2536);

        Should.NotThrow(() => CanonicalStateWriter.CanonicalBytes(promoted));

        CanonicalStateWriter.HashMetaCommandState(promoted)
            .ShouldNotBe(CanonicalStateWriter.HashMetaCommandState(promoted with { SourceId = 99 }));
    }

    /// <summary>
    /// A snapshot nested deeper than the writer's descent limit terminates with a diagnosable
    /// failure rather than a stack overflow. Snapshots are shallow trees by construction; runaway
    /// depth is a bug in the snapshot, and it must be sayable rather than fatal to the process.
    /// </summary>
    /// <remarks>
    /// A true reference cycle is not constructible from immutable positional records, so the
    /// shape that stands in for one is a 200-level self-referencing chain — the same unbounded
    /// descent, reached the only way a snapshot can actually reach it.
    /// </remarks>
    [Fact]
    public void CanonicalBytes_refuses_a_snapshot_nested_deeper_than_the_descent_limit()
    {
        var deep = Enumerable.Range(0, 200).Aggregate(
            (UnsupportedSnapshots.SelfReferencing?)null,
            (next, depth) => new UnsupportedSnapshots.SelfReferencing(depth, next));

        var act = () => CanonicalStateWriter.CanonicalBytes(deep!);

        Should.Throw<NotSupportedException>(act).Message.ShouldMatchWildcard("*depth*");
    }

    /// <summary>The shapes with no reflection-defined declaration order, one fixture per shape.</summary>
    public static TheoryData<object> UnpinnableShapes() => new()
    {
        new UnsupportedSnapshots.WithPlainClass(new UnsupportedSnapshots.NotARecord()),
        new UnsupportedSnapshots.AmbiguousConstructors(1),
        new UnsupportedSnapshots.Empty(),
    };

    /// <summary>Payload sizes straddling the buffer boundaries an encoder is likely to pick.</summary>
    public static TheoryData<int> BufferBoundaryLengths() => new() { 1, 255, 256, 257, 65536 };

    /// <summary>
    /// <see cref="UnsupportedSnapshots.CombatEventAsDocumented"/> with its fields promoted into the
    /// primary constructor — the shape the field rule is steering authors toward.
    /// </summary>
    private sealed record PromotedFieldsSnapshot(int Tick, byte SourceId, double Value);
}
