using Shouldly;
using SlayIdleRepeat.Core.Model.Gear;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Inventory;
using SlayIdleRepeat.Core.Tests.Model.Gear;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Inventory;

/// <summary>
/// The five orderings an inventory can be read in: slot, rarity, power, quality and newest.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Every ordering here is total and stable, and the fixture is twenty-four items on purpose.</b>
/// .NET's introsort falls back to an insertion sort below a threshold in the mid-teens, and an
/// insertion sort happens to be stable — so a comparer that leaves two items tied looks perfectly
/// well-behaved on a list of ten and scrambles on a list of thirty. Every fixture below is over
/// twenty-four items carrying deliberate duplicate keys, so a tie the comparer does not break is
/// visible rather than latent.
/// </para>
/// <para>
/// <b>Direction.</b> Rarity, power and quality sort <em>best first</em> and "newest" sorts
/// <em>newest first</em>: these keys exist so a player looking for the item worth acting on finds it
/// at the top. Slot sorts by the declared slot order instead — it is a grouping, not a ranking, and
/// the vocabulary's own order is the only one that is not an invention. Every tie falls back to
/// grant order, which is the order the stored list is already in.
/// </para>
/// </remarks>
public sealed class InventorySortingTests
{
    /// <summary>How many items every fixture in this file carries. Comfortably over the introsort threshold.</summary>
    private const int FixtureSize = 24;

    /// <summary>Grouped by slot in the declared slot order; within a slot, grant order.</summary>
    /// <remarks>
    /// The fixture deals slots round-robin, so the sorted order is a real permutation rather than the
    /// input — a sort that did nothing at all would fail here, which is the point of dealing them
    /// that way.
    /// </remarks>
    [Fact]
    public void Sorting_by_slot_groups_the_declared_slot_order_and_keeps_grant_order_inside_it()
    {
        Sorted(InventorySortKey.SLOT, Mixed()).ShouldBe(
            Ids(
                0, 6, 12, 18,
                1, 7, 13, 19,
                2, 8, 14, 20,
                3, 9, 15, 21,
                4, 10, 16, 22,
                5, 11, 17, 23),
            "six slots, four items each, each group in the order the items were granted.");
    }

    /// <summary>Best band first; within a band, grant order.</summary>
    [Fact]
    public void Sorting_by_rarity_puts_the_best_band_first_and_keeps_grant_order_inside_it()
    {
        var mixed = Mixed();

        Sorted(InventorySortKey.RARITY, mixed).ShouldBe(
            Ids(
                4, 9, 14, 19,
                3, 8, 13, 18, 23,
                2, 7, 12, 17, 22,
                1, 6, 11, 16, 21,
                0, 5, 10, 15, 20),
            "SS, then S, A, B and C — and five items share every band except the top, so a comparer " +
            "that left them tied would scramble here rather than at some later size.");

        // The floor under the sequence: the fixture has to actually carry every band, or the claim
        // above is about four of them.
        mixed.Select(item => item.Rarity).Distinct().Count().ShouldBe(5);
    }

    /// <summary>Highest quality first; within one quality, grant order.</summary>
    /// <remarks>
    /// Quality is authored in quarters here rather than at whatever a mint would roll: four distinct
    /// values across twenty-four items means six items share each one, which is the duplication the
    /// stability claim needs. Rolled qualities would almost all be distinct and prove nothing.
    /// </remarks>
    [Fact]
    public void Sorting_by_quality_puts_the_best_roll_first_and_keeps_grant_order_inside_it()
    {
        Sorted(InventorySortKey.QUALITY, Mixed()).ShouldBe(
            Ids(
                3, 7, 11, 15, 19, 23,
                2, 6, 10, 14, 18, 22,
                1, 5, 9, 13, 17, 21,
                0, 4, 8, 12, 16, 20));
    }

    /// <summary>Newest first — the exact reverse of grant order.</summary>
    /// <remarks>
    /// Stated as the whole reversal rather than "the last item is first": a sort that moved only the
    /// newest item to the front would pass the weaker claim.
    /// </remarks>
    [Fact]
    public void Sorting_by_newest_reverses_grant_order()
    {
        Sorted(InventorySortKey.NEWEST, Mixed()).ShouldBe(
            Ids([.. Enumerable.Range(0, FixtureSize).Reverse()]));
    }

    /// <summary>
    /// Strongest first: the band dominates, quality orders within a band, and grant order breaks
    /// what is left.
    /// </summary>
    /// <remarks>
    /// One slot throughout, so the comparison is between items that are genuinely alternatives to
    /// each other — a weapon and a ring derive different stats, and ordering the two by "power" would
    /// be comparing two different quantities.
    /// <para>
    /// 🔴 <b>A literal permutation, like every other ordering case in this file, and for exactly the
    /// reason they are.</b> The expectation used to be built by re-running the ordering rule in
    /// LINQ — <c>OrderByDescending(Rarity).ThenByDescending(Quality).ThenBy(arrival)</c> — which is
    /// the claim restated as its own evidence: a comparer that ranked quality before band would have
    /// been matched by a LINQ chain written the same wrong way round, and only a hand-written
    /// sequence can disagree with the code under test. The fixture is deterministic
    /// (<see cref="OneSlot"/> deals band on a four-cycle and quality on a three-cycle), so the
    /// sequence below is derivable by hand and is written out.
    /// </para>
    /// </remarks>
    [Fact]
    public void Sorting_by_power_ranks_the_band_first_then_the_roll()
    {
        var oneSlot = OneSlot();

        Sorted(InventorySortKey.POWER, oneSlot).ShouldBe(
            Ids(
                11, 23, 7, 19, 3, 15,
                2, 14, 10, 22, 6, 18,
                5, 17, 1, 13, 9, 21,
                8, 20, 4, 16, 0, 12),
            "S first, then A, B and C; inside a band the best roll first, and arrival order breaks " +
            "what is left. Six items share every band and two share every band-and-roll pair, so a " +
            "tie the comparer does not break scrambles here.");

        // The claim the expectation above encodes, said once in plain terms so a future reader does
        // not have to derive it: the worst item of a band still outranks the best of the band below.
        var sorted = Sorted(InventorySortKey.POWER, oneSlot).ToArray();
        var bands = sorted.Select(id => oneSlot.Single(item => item.InstanceId.Equals(id)).Rarity).ToArray();

        bands.ShouldBe(
            bands.OrderByDescending(band => band).ToArray(),
            "at one chapter of origin, a perfect roll on a low band must not outrank a poor roll on " +
            "a high one — the band's stat multiplier spans more than the quality range ever can.");
    }

    /// <summary>
    /// 🔒 Power is the item's real power scalar, so the chapter it dropped in moves it — an
    /// otherwise identical item from a later chapter sorts first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The claim that separates a genuine item-power ranking from a rarity ranking wearing a
    /// different name. An item keeps the power it dropped with, and the chapter curve spans far more
    /// than the band ladder does — a chapter-8 item of a middling band really is stronger than a
    /// chapter-1 item of a high one, which is why this key exists beside RARITY at all.
    /// </para>
    /// <para>
    /// Stated over two items that differ in <em>nothing else</em>, rather than folded into the
    /// fixture above: mixing chapter into that sweep would make "band dominates" false and the exact
    /// sequence unreadable.
    /// </para>
    /// </remarks>
    [Fact]
    public void Sorting_by_power_reads_the_chapter_the_item_dropped_in()
    {
        var early = Inventories.Item("early", rarity: Rarity.A, chapterOrigin: 1, quality: 0.5);
        var late = Inventories.Item("late", rarity: Rarity.A, chapterOrigin: 8, quality: 0.5);

        Sorted(InventorySortKey.POWER, [early, late]).ShouldBe(
            new[] { late.InstanceId, early.InstanceId },
            "same band, same slot, same roll — only the chapter of origin differs, so a POWER key " +
            "that answered the band alone would leave these two in arrival order.");

        Sorted(InventorySortKey.RARITY, [early, late]).ShouldBe(
            new[] { early.InstanceId, late.InstanceId },
            "…and RARITY cannot tell them apart at all, which is what makes the two keys different " +
            "keys rather than one comparer wired twice.");
    }

    // ---------------------------------------------------------------------------- the invariants

    /// <summary>Every key returns every item, once — a sort loses nothing and invents nothing.</summary>
    /// <remarks>
    /// The floor under all five orderings: a comparer that threw items away would still produce a
    /// correctly ordered prefix, and the sequence assertions above would fail with a diff nobody
    /// could read.
    /// </remarks>
    /// <remarks>
    /// A <c>[Fact]</c> over every key rather than a <c>[Theory]</c> with one case each: the sort key
    /// is <c>internal</c>, and an <c>internal</c> type cannot appear in the signature of the
    /// <c>public</c> method xUnit requires. The loop is floored on the vocabulary's own length so it
    /// cannot silently cover fewer keys than exist.
    /// </remarks>
    [Fact]
    public void Every_ordering_returns_every_item_exactly_once()
    {
        var mixed = Mixed();
        var expected = mixed.Select(item => item.InstanceId).OrderBy(id => id.Value, StringComparer.Ordinal);
        var covered = 0;

        foreach (var key in Enum.GetValues<InventorySortKey>())
        {
            Sorted(key, mixed).OrderBy(id => id.Value, StringComparer.Ordinal).ShouldBe(
                expected, $"sorting by {key} lost or invented an item");
            covered++;
        }

        covered.ShouldBe(5, "the assertion above lives inside a loop");
    }

    /// <summary>Sorting a sorted list changes nothing, for every key.</summary>
    /// <remarks>
    /// Idempotence is what a total order buys: a comparer with an unbroken tie can reorder the tied
    /// items on a second pass, and a player who sorted twice would watch the list shuffle.
    /// </remarks>
    /// <remarks>A <c>[Fact]</c> over every key, for the reason
    /// <see cref="Every_ordering_returns_every_item_exactly_once"/> records.</remarks>
    [Fact]
    public void Sorting_an_already_sorted_list_changes_nothing()
    {
        var covered = 0;

        foreach (var key in Enum.GetValues<InventorySortKey>())
        {
            var once = InventorySorting.Sort(
                Mixed(), key, Inventories.Par, Inventories.Drops, Inventories.Forge, Inventories.Catalogue);
            var twice = InventorySorting.Sort(
                once, key, Inventories.Par, Inventories.Drops, Inventories.Forge, Inventories.Catalogue);

            twice.Select(item => item.InstanceId).ShouldBe(
                once.Select(item => item.InstanceId),
                $"a second pass by {key} reordered the first pass's own output, which means a tie " +
                "the comparer did not break.");
            covered++;
        }

        covered.ShouldBe(5, "the assertion above lives inside a loop");
    }

    /// <summary>The five keys are five, and no two of them order the fixture the same way.</summary>
    /// <remarks>
    /// 🔒 The discriminating claim of the whole file. Four keys quietly wired to one comparer would
    /// pass every "is it ordered" assertion that only checked monotonicity in its own key — and every
    /// sequence above would still have to be written, which is exactly why this one is stated over
    /// the five results at once.
    /// </remarks>
    [Fact]
    public void No_two_sort_keys_order_the_fixture_the_same_way()
    {
        var keys = Enum.GetValues<InventorySortKey>();

        keys.Length.ShouldBe(5);

        var results = keys
            .Select(key => string.Join(
                ",", Sorted(key, Mixed()).Select(id => id.Value)))
            .ToArray();

        results.Distinct(StringComparer.Ordinal).Count().ShouldBe(
            keys.Length,
            "two keys producing the identical order over a fixture that varies slot, band and " +
            "quality independently means one of them is not reading its own field.");
    }

    /// <summary>An empty list sorts to an empty list rather than throwing.</summary>
    [Fact]
    public void An_empty_inventory_sorts_to_nothing()
    {
        InventorySorting.Sort(
                [], InventorySortKey.RARITY, Inventories.Par, Inventories.Drops, Inventories.Forge, Inventories.Catalogue)
            .ShouldBeEmpty();
    }

    /// <summary>A key outside the vocabulary is a caller defect, never a silent fall-through to grant order.</summary>
    [Fact]
    public void A_key_outside_the_vocabulary_is_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(
                () => InventorySorting.Sort(
                    Mixed(), (InventorySortKey)99,
                    Inventories.Par, Inventories.Drops, Inventories.Forge, Inventories.Catalogue))
            .ParamName.ShouldBe("key");
    }

    /// <summary>Every reference argument is required, and each refusal names its own.</summary>
    [Fact]
    public void A_null_argument_is_refused()
    {
        Should.Throw<ArgumentNullException>(
                () => InventorySorting.Sort(
                    null!, InventorySortKey.RARITY,
                    Inventories.Par, Inventories.Drops, Inventories.Forge, Inventories.Catalogue))
            .ParamName.ShouldBe("items");

        Should.Throw<ArgumentNullException>(
                () => InventorySorting.Sort(
                    Mixed(), InventorySortKey.POWER,
                    null!, Inventories.Drops, Inventories.Forge, Inventories.Catalogue))
            .ParamName.ShouldBe("par");

        Should.Throw<ArgumentNullException>(
                () => InventorySorting.Sort(
                    Mixed(), InventorySortKey.RARITY,
                    Inventories.Par, null!, Inventories.Forge, Inventories.Catalogue))
            .ParamName.ShouldBe("drops");

        Should.Throw<ArgumentNullException>(
                () => InventorySorting.Sort(
                    Mixed(), InventorySortKey.POWER,
                    Inventories.Par, Inventories.Drops, null!, Inventories.Catalogue))
            .ParamName.ShouldBe("forge");

        Should.Throw<ArgumentNullException>(
                () => InventorySorting.Sort(
                    Mixed(), InventorySortKey.RARITY,
                    Inventories.Par, Inventories.Drops, Inventories.Forge, null!))
            .ParamName.ShouldBe("catalogue");
    }

    // ---------------------------------------------------------------------------------- fixtures

    /// <summary>
    /// Twenty-four items in grant order, varying slot, band and quality on three different cycles so
    /// no two keys can agree by construction.
    /// </summary>
    /// <remarks>
    /// The cycle lengths — six slots, five bands, four qualities — are deliberately coprime-ish
    /// against each other and against twenty-four, so every key produces a genuinely different
    /// permutation and every key has repeats to be stable about.
    /// </remarks>
    private static IReadOnlyList<GearInstance> Mixed() =>
        Enumerable.Range(0, FixtureSize)
            .Select(i => Inventories.Item(
                Name(i),
                family: FamilyOf(i % 6, i / 6),
                rarity: (Rarity)((i % 5) + 1),
                quality: FourQualities[i % 4]))
            .ToArray();

    /// <summary>Twenty-four items of one slot, varying band and quality only.</summary>
    private static IReadOnlyList<GearInstance> OneSlot() =>
        Enumerable.Range(0, FixtureSize)
            .Select(i => Inventories.Item(
                Name(i),
                family: FamilyOf(0, i % 4),
                rarity: (Rarity)((i % 4) + 1),
                quality: ThreeQualities[i % 3]))
            .ToArray();

    /// <summary>
    /// The four quality values the mixed fixture deals, written as literals.
    /// </summary>
    /// <remarks>
    /// Literals rather than <c>0.1 * (i % 4)</c>: that product is <c>0.30000000000000004</c> at
    /// <c>i % 4 == 3</c>, which a gear instance refuses outright because persisted state carries no
    /// unrounded double. The fixture would fail to build rather than fail to sort.
    /// </remarks>
    private static readonly double[] FourQualities = [0.0, 0.1, 0.2, 0.3];

    /// <summary>The three quality values the single-slot fixture deals.</summary>
    private static readonly double[] ThreeQualities = [0.0, 0.25, 0.5];

    /// <summary>
    /// The <paramref name="familyIndex"/>th family of the <paramref name="slotIndex"/>th slot, read
    /// off the catalogue rather than transcribed — a hand-written slot-to-family table would be a
    /// second answer to what the content file already says.
    /// </summary>
    private static GearFamily FamilyOf(int slotIndex, int familyIndex) =>
        Inventories.Catalogue
            .Families(Enum.GetValues<GearSlot>()[slotIndex])[familyIndex]
            .Family;

    /// <summary>The identity of the <paramref name="arrival"/>th granted item.</summary>
    private static string Name(int arrival) => "item_" + arrival.ToString("D2");

    /// <summary>The identities of the given arrivals, in the order given.</summary>
    private static GearInstanceId[] Ids(params int[] arrivals) =>
        arrivals.Select(arrival => new GearInstanceId(Name(arrival))).ToArray();

    private static IEnumerable<GearInstanceId> Sorted(
        InventorySortKey key, IReadOnlyList<GearInstance> items) =>
        InventorySorting.Sort(items, key, Inventories.Par, Inventories.Drops, Inventories.Forge, Inventories.Catalogue)
            .Select(item => item.InstanceId);
}
