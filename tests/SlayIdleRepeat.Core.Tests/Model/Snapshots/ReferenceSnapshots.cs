namespace SlayIdleRepeat.Core.Tests.Model.Snapshots;

/// <summary>The representative snapshot-shaped records the canonical-encoding suite is written against.</summary>
/// <remarks>
/// These live in the <b>test</b> project on purpose: the real DTOs need the aggregates' real fields,
/// and inventing them here would be a design decision made in the wrong milestone.
/// <c>CanonicalStateWriter</c> is specified against the <i>encoding rules</i>, not two particular types.
/// <para>
/// Every record is <b>positional</b>, because the writer defines "declaration order" as the primary
/// constructor's parameter order — the one order reflection guarantees. Each is mirrored field for
/// field by the independent generator that produced the reference-vector table, so changing a field
/// here without regenerating is a break the table will announce.
/// </para>
/// </remarks>
internal static class ReferenceSnapshots
{
    /// <summary>The canonical instance behind every committed reference-vector row.</summary>
    /// <remarks>
    /// A lookup rather than a per-row test, so the row ids in the JSON and the instances here
    /// cannot drift apart silently: an id with no instance throws, and an instance whose bytes
    /// differ from the committed ones fails its row.
    /// </remarks>
    internal static object Instance(string rowId) => rowId switch
    {
        "scalars-all" => Scalars,
        "int-negative-one" => new OneIntSnapshot(-1),
        "int-zero" => new OneIntSnapshot(0),
        "bool-true" => new OneBoolSnapshot(true),
        "bool-false" => new OneBoolSnapshot(false),
        "string-multibyte" => new OneStringSnapshot(MultiByteText),
        "string-empty" => new OneStringSnapshot(""),
        "string-null" => new OneStringSnapshot(null),
        "double-rounded" => new OneDoubleSnapshot(1234.5678),
        "double-positive-zero" => new OneDoubleSnapshot(0.0),
        "timestamp-epoch" => new OneTimestampSnapshot(DateTimeOffset.FromUnixTimeMilliseconds(0)),
        "timestamp-2024" => new OneTimestampSnapshot(DateTimeOffset.FromUnixTimeMilliseconds(StampMilliseconds)),
        "timestamp-pre-epoch" => new OneTimestampSnapshot(DateTimeOffset.FromUnixTimeMilliseconds(-1000)),
        "optional-present" => new OptionalsSnapshot(42, "here", new InnerSnapshot(3, "deep")),
        "optional-absent" => new OptionalsSnapshot(null, null, null),
        "optional-present-zero" => new OptionalsSnapshot(0, null, null),
        "nested-depth-three" => NestedDepthThree,
        "list-stored-order" => new ListSnapshot(new[] { 3, 1, 2 }),
        "list-reversed" => new ListSnapshot(new[] { 2, 1, 3 }),
        "list-empty" => new ListSnapshot(Array.Empty<int>()),
        "list-of-records" => new InnerListSnapshot(new[] { new InnerSnapshot(1, "a"), new InnerSnapshot(2, "b") }),
        "nested-lists-ambiguity-a" => new NestedListSnapshot(new IReadOnlyList<int>[] { new[] { 1 }, new[] { 2, 3 } }),
        "nested-lists-ambiguity-b" => new NestedListSnapshot(new IReadOnlyList<int>[] { new[] { 1, 2 }, new[] { 3 } }),
        "dictionary-string-keys" => new StringMapSnapshot(MixedCaseMap),
        "dictionary-numeric-keys" => new NumberMapSnapshot(UnorderedNumberMap),
        "dictionary-empty" => new StringMapSnapshot(new Dictionary<string, int>()),
        "meta-command-player-only" => Player,
        "run-command-player-then-run" => Player,
        "run-command-swapped" => Run,
        "wire-leading-zero-nibbles" => new OneIntSnapshot(LeadingZeroHashValue),
        _ => throw new InvalidOperationException(
            $"The reference table row '{rowId}' names no instance in {nameof(ReferenceSnapshots)}."),
    };

    /// <summary>The second argument of a two-snapshot (run-mode) row.</summary>
    internal static object SecondInstance(string rowId) => rowId switch
    {
        "run-command-player-then-run" => Run,
        "run-command-swapped" => Player,
        _ => throw new InvalidOperationException(
            $"The reference table row '{rowId}' is not a run-mode row and has no second instance."),
    };

    /// <summary>9 UTF-16 characters, 15 UTF-8 bytes — the two counts a naive encoder confuses.</summary>
    internal const string MultiByteText = "Grüße, 世界";

    /// <summary>2024-06-01T12:04:56.789Z, as Unix milliseconds UTC.</summary>
    internal const long StampMilliseconds = 1717243496789L;

    /// <summary>The <see cref="OneIntSnapshot"/> value whose hash has two leading zero nibbles.</summary>
    /// <remarks>Found by search in the generator; it is what pins zero-padding of the wire form.</remarks>
    internal const int LeadingZeroHashValue = 174;

    /// <summary>Keys that sort differently ordinally than they do under a culture-aware comparer.</summary>
    /// <remarks>
    /// Ordinal: <c>A</c>(0x41) <c>B</c>(0x42) <c>a</c>(0x61) <c>b</c>(0x62). A culture-aware
    /// comparer gives <c>a A b B</c> — a different byte stream, and a client/server split the
    /// day the two ends run under different cultures.
    /// </remarks>
    internal static IReadOnlyDictionary<string, int> MixedCaseMap { get; } = new Dictionary<string, int>
    {
        ["a"] = 1,
        ["B"] = 2,
        ["b"] = 3,
        ["A"] = 4,
    };

    /// <summary>Numeric ids inserted out of order, including a negative one.</summary>
    internal static IReadOnlyDictionary<long, string> UnorderedNumberMap { get; } = new Dictionary<long, string>
    {
        [30L] = "thirty",
        [-5L] = "minus five",
        [2L] = "two",
    };

    /// <summary>Every scalar encoding rule, in one record.</summary>
    internal static ScalarsSnapshot Scalars { get; } = new(
        SchemaVersion: 1,
        Flag: true,
        NegativeCount: -1,
        Big: -9007199254740993L,
        Unsigned: ulong.MaxValue,
        Kind: Element.Frost,
        Rank: Rarity.Cursed,
        Mask: BigFlag.All,
        Name: MultiByteText,
        Value: 1234.5678,
        Stamp: DateTimeOffset.FromUnixTimeMilliseconds(StampMilliseconds));

    /// <summary>Three record levels, so depth-first descent is pinned rather than assumed.</summary>
    internal static OuterSnapshot NestedDepthThree { get; } = new(
        Head: "outer",
        Middle: new MiddleSnapshot("middle", new InnerSnapshot(3, "leaf")),
        Tail: 9);

    /// <summary>A <c>PlayerSnapshot</c>-shaped stand-in for the two hashing modes.</summary>
    internal static PlayerLikeSnapshot Player { get; } = new(1, "PL_0001", 12345L);

    /// <summary>A <c>RunSnapshot</c>-shaped stand-in for the two hashing modes.</summary>
    internal static RunLikeSnapshot Run { get; } = new(1, "RUN_0001", 7);
}

/// <summary>An <c>int</c>-backed enum, the ordinary case.</summary>
internal enum Element
{
    None = 0,
    Frost = 7,
}

/// <summary>An <c>sbyte</c>-backed enum with a negative member, so sign extension is exercised.</summary>
internal enum Rarity : sbyte
{
    Cursed = -3,
    Common = 1,
}

/// <summary>A <c>ulong</c>-backed enum holding a value no <c>long</c> can, so widening is exercised.</summary>
internal enum BigFlag : ulong
{
    None = 0,
    All = ulong.MaxValue,
}

/// <summary>Every scalar encoding rule, in declaration order.</summary>
internal sealed record ScalarsSnapshot(
    int SchemaVersion,
    bool Flag,
    int NegativeCount,
    long Big,
    ulong Unsigned,
    Element Kind,
    Rarity Rank,
    BigFlag Mask,
    string Name,
    double Value,
    DateTimeOffset Stamp);

/// <summary>One <see cref="int"/>, for the widening rules in isolation.</summary>
internal sealed record OneIntSnapshot(int Value);

/// <summary>
/// A one-field record over any <typeparamref name="T"/>, so a scalar rule can be exercised for
/// every declared type without a named record per type.
/// </summary>
/// <typeparam name="T">The declared type of the single field.</typeparam>
/// <remarks>
/// The constructor parameter's type is the closed <typeparamref name="T"/>, which is exactly
/// what the writer dispatches on — the generic is a fixture convenience, not a special case in
/// the encoding. The committed reference-vector rows deliberately use the named records instead,
/// so the table reads as data rather than as generic instantiations.
/// </remarks>
internal sealed record OneValueSnapshot<T>(T Value);

/// <summary>One <see cref="bool"/>, for the one-byte rule in isolation.</summary>
internal sealed record OneBoolSnapshot(bool Value);

/// <summary>One nullable <see cref="string"/>, for the length-prefix and presence rules.</summary>
internal sealed record OneStringSnapshot(string? Value);

/// <summary>One <see cref="double"/>, for the IEEE-754 bit-pattern rule.</summary>
internal sealed record OneDoubleSnapshot(double Value);

/// <summary>One <see cref="DateTimeOffset"/>, for the Unix-milliseconds rule.</summary>
internal sealed record OneTimestampSnapshot(DateTimeOffset Value);

/// <summary>One UTC <see cref="DateTime"/> — the other admissible timestamp shape.</summary>
internal sealed record OneUtcDateTimeSnapshot(DateTime Value);

/// <summary>The presence byte for a value-type optional, a reference type and a nested record.</summary>
internal sealed record OptionalsSnapshot(int? MaybeNumber, string? MaybeText, InnerSnapshot? MaybeInner);

/// <summary>The leaf of the nesting fixtures.</summary>
internal sealed record InnerSnapshot(int Depth, string Label);

/// <summary>The middle level of the nesting fixtures.</summary>
internal sealed record MiddleSnapshot(string Label, InnerSnapshot Leaf);

/// <summary>The root of the nesting fixtures — three levels deep.</summary>
internal sealed record OuterSnapshot(string Head, MiddleSnapshot Middle, int Tail);

/// <summary>
/// <see cref="OuterSnapshot"/>'s five leaf values with the nesting removed, so a test can pin
/// that shape is part of the encoding and not merely the sequence of leaves.
/// </summary>
internal sealed record FlattenedSnapshot(string Head, string Label, int Depth, string LeafLabel, int Tail);

/// <summary>A list of scalars, for stored order and the element count.</summary>
internal sealed record ListSnapshot(IReadOnlyList<int> Numbers);

/// <summary>A list of records, so depth-first descent per element is pinned.</summary>
internal sealed record InnerListSnapshot(IReadOnlyList<InnerSnapshot> Items);

/// <summary>A list of lists — the shape that collides without a count prefix.</summary>
internal sealed record NestedListSnapshot(IReadOnlyList<IReadOnlyList<int>> Rows);

/// <summary>A string-keyed map, for the ascending-ordinal rule.</summary>
internal sealed record StringMapSnapshot(IReadOnlyDictionary<string, int> ByName);

/// <summary>A numeric-keyed map, for the ascending-numeric rule.</summary>
internal sealed record NumberMapSnapshot(IReadOnlyDictionary<long, string> ById);

/// <summary>A <see cref="ulong"/>-keyed map, so unsigned key ordering is pinned above <see cref="long.MaxValue"/>.</summary>
internal sealed record UnsignedMapSnapshot(IReadOnlyDictionary<ulong, int> ById);

/// <summary>An enum-keyed map, so enum keys are pinned to sort as the numeric ids they are.</summary>
internal sealed record EnumMapSnapshot(IReadOnlyDictionary<Element, int> ByKind);

/// <summary>A <c>PlayerSnapshot</c>-shaped stand-in.</summary>
internal sealed record PlayerLikeSnapshot(int SchemaVersion, string PlayerId, long Gold);

/// <summary>A <c>RunSnapshot</c>-shaped stand-in.</summary>
internal sealed record RunLikeSnapshot(int SchemaVersion, string RunId, int Turn);

/// <summary>
/// <see cref="PlayerLikeSnapshot"/> with its last field widened, so the field-order pin can be
/// shown to catch a type change at an unchanged name and position.
/// </summary>
internal sealed record WidenedPlayerLikeSnapshot(int SchemaVersion, string PlayerId, double Gold);
