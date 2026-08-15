using System.Collections.Immutable;
using Shouldly;
using SlayIdleRepeat.TestSupport;
using SlayIdleRepeat.Core.Model.Snapshots;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Model.Snapshots;

/// <summary>
/// Lists are written in stored order; every dictionary/map in ascending key order — ordinal for
/// strings, numeric for numeric ids. No unordered container is ever hashed as-is.
/// </summary>
/// <remarks>
/// Enforced structurally rather than by convention: the writer dispatches over a closed allowlist
/// of shapes with no <c>IEnumerable</c> fallback, so a container whose order is undefined cannot
/// be encoded at all.
/// </remarks>
public sealed class CanonicalCollectionOrderingTests
{
    [Fact]
    public void CanonicalBytes_writes_a_list_in_stored_order()
    {
        var forwards = CanonicalStateWriter.CanonicalBytes(new ListSnapshot(new[] { 1, 2, 3 }));
        var backwards = CanonicalStateWriter.CanonicalBytes(new ListSnapshot(new[] { 3, 2, 1 }));

        Hex(forwards).ShouldNotBe(Hex(backwards));
    }

    /// <summary>A list is a 4-byte little-endian element count, then each element in turn.</summary>
    [Fact]
    public void CanonicalBytes_prefixes_a_list_with_its_element_count()
    {
        var bytes = CanonicalStateWriter.CanonicalBytes(new ListSnapshot(new[] { 7 }));

        Hex(bytes).ShouldBe("01" + "01000000" + "0700000000000000");
    }

    /// <summary>An empty list is a present slot with a zero count, not an absence.</summary>
    [Fact]
    public void CanonicalBytes_writes_an_empty_list_as_a_present_zero_count_slot()
    {
        var bytes = CanonicalStateWriter.CanonicalBytes(new ListSnapshot(Array.Empty<int>()));

        Hex(bytes).ShouldBe("01" + "00000000");
    }

    /// <summary>
    /// The count prefix is what keeps nested collections apart. Without it <c>[[1],[2,3]]</c>
    /// and <c>[[1,2],[3]]</c> flatten to the same bytes, and two genuinely different states would
    /// share a <c>stateHash</c>.
    /// </summary>
    [Fact]
    public void CanonicalBytes_keeps_two_nested_lists_with_the_same_flat_elements_apart()
    {
        var first = CanonicalStateWriter.CanonicalBytes(
            new NestedListSnapshot(new IReadOnlyList<int>[] { new[] { 1 }, new[] { 2, 3 } }));
        var second = CanonicalStateWriter.CanonicalBytes(
            new NestedListSnapshot(new IReadOnlyList<int>[] { new[] { 1, 2 }, new[] { 3 } }));

        Hex(first).ShouldNotBe(Hex(second));
    }

    /// <summary>Every list-shaped declared type encodes identically — the shape is the contract.</summary>
    [Fact]
    public void CanonicalBytes_writes_an_array_a_List_and_an_ImmutableArray_identically()
    {
        var fromArray = CanonicalStateWriter.CanonicalBytes(new ListSnapshot(new[] { 1, 2 }));
        var fromList = CanonicalStateWriter.CanonicalBytes(new ListSnapshot(new List<int> { 1, 2 }));
        var fromImmutable = CanonicalStateWriter.CanonicalBytes(new ListSnapshot(ImmutableArray.Create(1, 2)));

        Hex(fromList).ShouldBe(Hex(fromArray));
        Hex(fromImmutable).ShouldBe(Hex(fromArray));
    }

    /// <summary>Each element of a list of records is descended into, depth-first, in stored order.</summary>
    [Fact]
    public void CanonicalBytes_descends_into_each_element_of_a_list_of_records()
    {
        var bytes = CanonicalStateWriter.CanonicalBytes(
            new InnerListSnapshot(new[] { new InnerSnapshot(1, "a"), new InnerSnapshot(2, "b") }));

        Hex(bytes).ShouldBe(
            "01" + "02000000" +                                   // present, 2 elements
            "01" + "0100000000000000" + "01" + "01000000" + "61" + // { Depth = 1, Label = "a" }
            "01" + "0200000000000000" + "01" + "01000000" + "62"); // { Depth = 2, Label = "b" }
    }

    /// <summary>
    /// Two dictionaries with identical contents in different insertion orders hash
    /// <b>identically</b> — the client and the server build their maps by different routes and
    /// must still agree.
    /// </summary>
    [Fact]
    public void CanonicalBytes_writes_two_dictionaries_with_the_same_contents_identically()
    {
        var oneOrder = new Dictionary<string, int> { ["a"] = 1, ["B"] = 2, ["b"] = 3, ["A"] = 4 };
        var otherOrder = new Dictionary<string, int> { ["A"] = 4, ["b"] = 3, ["B"] = 2, ["a"] = 1 };

        var first = CanonicalStateWriter.CanonicalBytes(new StringMapSnapshot(oneOrder));
        var second = CanonicalStateWriter.CanonicalBytes(new StringMapSnapshot(otherOrder));

        Hex(first).ShouldBe(Hex(second));
    }

    /// <summary>
    /// String keys sort <b>ordinally</b>, not by culture: <c>"B"</c> (0x42) precedes <c>"a"</c>
    /// (0x61), while almost every culture-aware comparer says the opposite. A culture-sensitive
    /// sort would make the byte stream depend on the machine's locale.
    /// </summary>
    [Fact]
    public void CanonicalBytes_sorts_string_keys_ordinally_rather_than_by_culture()
    {
        var bytes = CanonicalStateWriter.CanonicalBytes(new StringMapSnapshot(ReferenceSnapshots.MixedCaseMap));

        Hex(bytes).ShouldBe(
            "01" + "04000000" +                                       // present, 4 entries
            "01" + "01000000" + "41" + "0400000000000000" +           // "A" -> 4
            "01" + "01000000" + "42" + "0200000000000000" +           // "B" -> 2
            "01" + "01000000" + "61" + "0100000000000000" +           // "a" -> 1
            "01" + "01000000" + "62" + "0300000000000000");           // "b" -> 3
    }

    /// <summary>
    /// The fixture keys genuinely separate ordinal ordering from a non-ordinal one, so the test
    /// above is a real distinction. Compared against <see cref="StringComparer.OrdinalIgnoreCase"/>
    /// rather than a culture-aware comparer on purpose: a culture comparer answers differently
    /// under globalization-invariant mode.
    /// </summary>
    [Fact]
    public void The_fixture_keys_order_differently_under_ordinal_and_case_folding_comparers()
    {
        var keys = new[] { "a", "B", "b", "A" };

        var ordinal = keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
        var caseFolding = keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToArray();

        ordinal.ShouldBe(new[] { "A", "B", "a", "b" });
        ordinal.SequenceEqual(caseFolding).ShouldBeFalse();
    }

    /// <summary>
    /// Numeric keys sort <b>numerically</b>, not by their string form: <c>-5</c> before
    /// <c>2</c> before <c>30</c>, where a textual sort would give <c>-5</c>, <c>30</c>, <c>2</c>.
    /// </summary>
    [Fact]
    public void CanonicalBytes_sorts_numeric_keys_numerically_rather_than_textually()
    {
        var bytes = CanonicalStateWriter.CanonicalBytes(new NumberMapSnapshot(ReferenceSnapshots.UnorderedNumberMap));

        Hex(bytes).ShouldBe(
            "01" + "03000000" +                                                       // present, 3 entries
            "fbffffffffffffff" + "01" + "0a000000" + "6d696e75732066697665" +          // -5 -> "minus five"
            "0200000000000000" + "01" + "03000000" + "74776f" +                        //  2 -> "two"
            "1e00000000000000" + "01" + "06000000" + "746869727479");                  // 30 -> "thirty"
    }

    /// <summary>
    /// An unsigned key above <see cref="long.MaxValue"/> sorts as the large number it is, not
    /// as the negative one its bit pattern would be if compared signed.
    /// </summary>
    [Fact]
    public void CanonicalBytes_sorts_unsigned_keys_as_unsigned()
    {
        var map = new Dictionary<ulong, int> { [ulong.MaxValue] = 1, [1UL] = 2 };

        var bytes = CanonicalStateWriter.CanonicalBytes(new UnsignedMapSnapshot(map));

        Hex(bytes).ShouldBe(
            "01" + "02000000" +
            "0100000000000000" + "0200000000000000" +   // 1 first
            "ffffffffffffffff" + "0100000000000000");   // ulong.MaxValue last
    }

    [Fact]
    public void CanonicalBytes_sorts_enum_keys_by_their_numeric_value()
    {
        var map = new Dictionary<Element, int> { [Element.Frost] = 1, [Element.None] = 2 };

        var bytes = CanonicalStateWriter.CanonicalBytes(new EnumMapSnapshot(map));

        Hex(bytes).ShouldBe(
            "01" + "02000000" +
            "0000000000000000" + "0200000000000000" +   // None (0) first
            "0700000000000000" + "0100000000000000");   // Frost (7) second
    }

    /// <summary>Every dictionary-shaped declared type encodes identically.</summary>
    [Fact]
    public void CanonicalBytes_writes_a_Dictionary_a_SortedDictionary_and_an_ImmutableDictionary_identically()
    {
        var entries = new[] { KeyValuePair.Create("b", 2), KeyValuePair.Create("a", 1) };

        var fromDictionary = CanonicalStateWriter.CanonicalBytes(
            new StringMapSnapshot(new Dictionary<string, int>(entries)));
        var fromSorted = CanonicalStateWriter.CanonicalBytes(
            new StringMapSnapshot(new SortedDictionary<string, int>(new Dictionary<string, int>(entries))));
        var fromImmutable = CanonicalStateWriter.CanonicalBytes(
            new StringMapSnapshot(ImmutableDictionary.CreateRange(entries)));

        Hex(fromSorted).ShouldBe(Hex(fromDictionary));
        Hex(fromImmutable).ShouldBe(Hex(fromDictionary));
    }

    /// <summary>
    /// A <see cref="SortedDictionary{TKey, TValue}"/> built with a <b>non-ordinal</b> comparer
    /// still encodes ordinally: the writer imposes the order, it never inherits the container's.
    /// </summary>
    [Fact]
    public void CanonicalBytes_ignores_the_comparer_a_sorted_dictionary_was_built_with()
    {
        var caseFoldSorted = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["a"] = 1,
            ["B"] = 2,
        };

        var bytes = CanonicalStateWriter.CanonicalBytes(new StringMapSnapshot(caseFoldSorted));

        Hex(bytes).ShouldBe(
            "01" + "02000000" +
            "01" + "01000000" + "42" + "0200000000000000" +   // "B" (0x42) first, ordinally
            "01" + "01000000" + "61" + "0100000000000000");   // "a" (0x61) second
    }

    /// <summary>An empty map is a present slot with a zero entry count.</summary>
    [Fact]
    public void CanonicalBytes_writes_an_empty_dictionary_as_a_present_zero_count_slot()
    {
        var bytes = CanonicalStateWriter.CanonicalBytes(new StringMapSnapshot(new Dictionary<string, int>()));

        Hex(bytes).ShouldBe("01" + "00000000");
    }

    /// <summary>
    /// No unordered container is ever hashed as-is: each of these shapes has no defined order —
    /// or, for the last two, no pinned encoding at all — and the writer refuses them rather than
    /// picking one. If any of these ever starts hashing, a fallback branch grew back.
    /// </summary>
    [Theory]
    [MemberData(nameof(UnorderedContainers))]
    public void CanonicalBytes_refuses_a_container_with_no_defined_order(object snapshot)
    {
        var act = () => CanonicalStateWriter.CanonicalBytes(snapshot);

        Should.Throw<NotSupportedException>(act).Message.ShouldMatchWildcard("*16.6*");
    }

    /// <summary>
    /// Unpinned scalar shapes are refused too: an encoder that invents a byte layout for one of
    /// them has invented a second serialisation contract.
    /// </summary>
    [Theory]
    [MemberData(nameof(UnpinnedScalars))]
    public void CanonicalBytes_refuses_a_scalar_the_specification_does_not_pin(object snapshot)
    {
        var act = () => CanonicalStateWriter.CanonicalBytes(snapshot);

        Should.Throw<NotSupportedException>(act).Message.ShouldMatchWildcard("*16.6*");
    }

    public static TheoryData<object> UnorderedContainers() => new()
    {
        new UnsupportedSnapshots.WithHashSet(new HashSet<string> { "a" }),
        new UnsupportedSnapshots.WithSetInterface(new HashSet<string> { "a" }),
        new UnsupportedSnapshots.WithEnumerable(new[] { 1, 2 }),
        new UnsupportedSnapshots.WithKeyValueCollection(new List<KeyValuePair<string, int>>()),
        new UnsupportedSnapshots.WithUnorderableKey(UnsupportedSnapshots.GuidMap),
    };

    public static TheoryData<object> UnpinnedScalars() => new()
    {
        new UnsupportedSnapshots.WithSingle(1f),
        new UnsupportedSnapshots.WithDecimal(1m),
        new UnsupportedSnapshots.WithChar('a'),
        new UnsupportedSnapshots.WithGuid(Guid.Empty),
        new UnsupportedSnapshots.WithTimeSpan(TimeSpan.Zero),
        new UnsupportedSnapshots.WithObject(new object()),
    };

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();
}
