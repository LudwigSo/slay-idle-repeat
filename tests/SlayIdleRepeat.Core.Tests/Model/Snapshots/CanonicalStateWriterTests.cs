using System.Globalization;
using System.Text.RegularExpressions;
using FluentAssertions;
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

        hash.Should().StartWith("fnv1a:");
    }

    /// <summary>The wire form is the prefix plus exactly 16 hex characters — 22 in all.</summary>
    [Fact]
    public void HashMetaCommandState_produces_sixteen_hex_characters_after_the_prefix()
    {
        var hash = CanonicalStateWriter.HashMetaCommandState(ReferenceSnapshots.Player);

        hash.Should().HaveLength(22);
        hash.Should().MatchRegex(WireForm);
    }

    /// <summary>🔒 The hex is lowercase. A mixed-case wire form is two wire forms.</summary>
    [Fact]
    public void HashMetaCommandState_produces_lowercase_hexadecimal()
    {
        var hash = CanonicalStateWriter.HashMetaCommandState(ReferenceSnapshots.Scalars);

        hash.Should().Be(hash.ToLowerInvariant());
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

        hash.Should().StartWith("fnv1a:00");
        hash.Should().MatchRegex(WireForm);
    }

    /// <summary>The wire form's hex is the raw 64-bit hash of the same bytes, restated.</summary>
    [Fact]
    public void HashMetaCommandState_renders_the_hash_of_the_canonical_bytes()
    {
        var expected = CanonicalStateWriter.Fnv1a64(
            CanonicalStateWriter.CanonicalBytes(ReferenceSnapshots.Player));

        var hash = CanonicalStateWriter.HashMetaCommandState(ReferenceSnapshots.Player);

        hash.Should().Be("fnv1a:" + expected.ToString("x16", CultureInfo.InvariantCulture));
    }

    /// <summary>The prefix is a public constant, so a consumer checks it rather than retyping it.</summary>
    [Fact]
    public void AlgorithmPrefix_is_the_literal_prefix_of_every_hash_the_writer_produces()
    {
        CanonicalStateWriter.AlgorithmPrefix.Should().Be("fnv1a:");
        CanonicalStateWriter.HashMetaCommandState(ReferenceSnapshots.Player)
            .Should().StartWith(CanonicalStateWriter.AlgorithmPrefix);
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

        hash.Should().Be("fnv1a:" + expected.ToString("x16", CultureInfo.InvariantCulture));
    }

    /// <summary>🔒 A meta command hashes <c>PlayerSnapshot</c> alone.</summary>
    [Fact]
    public void HashMetaCommandState_hashes_the_player_bytes_alone()
    {
        var expected = CanonicalStateWriter.Fnv1a64(
            CanonicalStateWriter.CanonicalBytes(ReferenceSnapshots.Player));

        var hash = CanonicalStateWriter.HashMetaCommandState(ReferenceSnapshots.Player);

        hash.Should().Be("fnv1a:" + expected.ToString("x16", CultureInfo.InvariantCulture));
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

        run.Should().NotBe(meta);
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

        forwards.Should().NotBe(backwards);
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

        hash.Should().NotBe("fnv1a:" + (playerHash ^ runHash).ToString("x16", CultureInfo.InvariantCulture));
    }

    /// <summary>A run command with no player snapshot has no canonical state to hash.</summary>
    [Fact]
    public void HashRunCommandState_refuses_a_missing_player_snapshot()
    {
        var act = () => CanonicalStateWriter.HashRunCommandState(null!, ReferenceSnapshots.Run);

        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>And a run command with no run snapshot is not a meta command in disguise.</summary>
    [Fact]
    public void HashRunCommandState_refuses_a_missing_run_snapshot()
    {
        var act = () => CanonicalStateWriter.HashRunCommandState(ReferenceSnapshots.Player, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>The meta mode's single snapshot is required for the same reason.</summary>
    [Fact]
    public void HashMetaCommandState_refuses_a_missing_snapshot()
    {
        var act = () => CanonicalStateWriter.HashMetaCommandState(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>🔒 The same input hashes identically twice — nothing is consumed or advanced.</summary>
    [Fact]
    public void HashMetaCommandState_returns_the_same_hash_when_called_twice()
    {
        var first = CanonicalStateWriter.HashMetaCommandState(ReferenceSnapshots.Scalars);
        var second = CanonicalStateWriter.HashMetaCommandState(ReferenceSnapshots.Scalars);

        second.Should().Be(first);
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

        second.Should().Be(first);
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

        after.Should().Be(before);
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

        afterLong.Should().Be(alone);
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

        bytes.Should().HaveCount(1 + 4 + length);
    }

    /// <summary>The wire form is well formed at those same buffer boundaries.</summary>
    [Theory]
    [MemberData(nameof(BufferBoundaryLengths))]
    public void HashMetaCommandState_produces_a_well_formed_wire_form_for_a_payload_of_any_size(int length)
    {
        var snapshot = new OneValueSnapshot<string>(new string('x', length));

        var hash = CanonicalStateWriter.HashMetaCommandState(snapshot);

        hash.Should().MatchRegex(WireForm);
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

        changed.Should().NotBe(baseline);
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

        act.Should().Throw<NotSupportedException>().WithMessage("*16.6*");
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

        act.Should().Throw<NotSupportedException>().WithMessage("*16.6*");
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

        act.Should().Throw<NotSupportedException>().WithMessage("*depth*");
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
}
