using Shouldly;
using SlayIdleRepeat.Core.Rng;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rng;

/// <summary>
/// <c>WeightedPick</c>: one draw. <c>x = unit interval × Σ weights</c>; walk the table in order;
/// the first item whose cumulative weight exceeds <c>x</c>.
/// </summary>
/// <remarks>
/// Two draws would be the natural implementation (one to choose, one to break a tie) and would
/// quietly break the persisted counter for every stream a weighted pick ever touches. Strict
/// <c>&gt;</c> in the walk is what makes a zero-weight row unreachable rather than
/// reachable-only-at-exactly-zero.
/// </remarks>
public sealed class WeightedPickTests
{
    private const ulong RunSeed = 0x0123456789ABCDEFUL;

    /// <summary>The largest value <c>NextDouble</c> can produce: 1 − 2^-53.</summary>
    private const double TopOfUnitInterval = 1.0 - (1.0 / 9007199254740992.0);

    [Fact]
    public void WeightedPick_consumes_exactly_one_draw_index()
    {
        var rng = new DeterministicRng(RunSeed, RngStreams.Drops, 40UL);

        rng.WeightedPick(new[] { ("common", 90.0), ("rare", 9.0), ("epic", 1.0) });

        rng.Position.ShouldBe(41UL);
    }

    /// <summary>A single-entry table always yields its one item, and still costs its one draw.</summary>
    [Fact]
    public void WeightedPick_returns_the_only_item_of_a_single_entry_table()
    {
        var rng = new DeterministicRng(RunSeed, RngStreams.Drops);

        rng.WeightedPick(new[] { ("only", 1.0) }).ShouldBe("only");
    }

    /// <summary>
    /// The walk is in table order, so the order is part of the outcome: reversing a table re-assigns
    /// the same unit interval to different items.
    /// </summary>
    /// <remarks>
    /// The two weights are <b>equal</b> on purpose — the reversal then flips the answer for every value
    /// the draw can take. A lopsided table (1 against 99) would only differ for the outer 2% of the
    /// interval, and would call a correct implementation broken for the other 98%.
    /// </remarks>
    [Fact]
    public void WeightedPick_walks_the_table_in_order_so_reordering_it_changes_the_outcome()
    {
        var forwards = new[] { ("first", 1.0), ("second", 1.0) };
        var backwards = new[] { ("second", 1.0), ("first", 1.0) };

        var fromForwards = new DeterministicRng(RunSeed, RngStreams.Drops).WeightedPick(forwards);
        var fromBackwards = new DeterministicRng(RunSeed, RngStreams.Drops).WeightedPick(backwards);

        fromForwards.ShouldNotBe(fromBackwards);
    }

    /// <summary>
    /// A zero-weight row is unreachable, anywhere in the table, at any draw — content leaves
    /// weights at zero to disable a row.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void WeightedPick_never_returns_a_zero_weight_entry(int zeroIndex)
    {
        var table = new[] { ("a", 1.0), ("b", 1.0), ("c", 1.0) };
        table[zeroIndex] = (table[zeroIndex].Item1, 0.0);
        var rng = new DeterministicRng(RunSeed, RngStreams.Drops);

        var picks = Enumerable.Range(0, 500).Select(_ => rng.WeightedPick(table)).ToArray();

        picks.ShouldNotContain(table[zeroIndex].Item1);
    }

    /// <summary>
    /// The rule on the pure walk: <c>x</c> is the unit interval <b>scaled by Σ weights</b>, and the
    /// first cumulative weight <b>strictly greater</b> than <c>x</c> wins.
    /// </summary>
    /// <remarks>
    /// The weights sum to 4, not 1, and that is load-bearing: on a table summing to exactly 1 the
    /// scaling step is the identity, so every row would pass against a walk that never scaled at all.
    /// All values are exact in binary, so the boundary rows are boundaries and not floating-point luck.
    /// </remarks>
    [Theory]
    [InlineData(0.0, "a")]
    [InlineData(0.125, "a")]
    [InlineData(0.25, "b")]
    [InlineData(0.375, "b")]
    [InlineData(0.5, "c")]
    [InlineData(0.75, "c")]
    public void The_walk_returns_the_first_item_whose_cumulative_weight_exceeds_the_value(
        double unitInterval, string expected)
    {
        var table = new[] { ("a", 1.0), ("b", 1.0), ("c", 2.0) };

        DeterministicRng.PickAt(unitInterval, table).ShouldBe(expected);
    }

    /// <summary>
    /// The top of the range must still land on the last weighted item, never on nothing and never
    /// on an exception: whether <c>x = (1 − 2^-53) × Σ</c> stays strictly below the final
    /// cumulative weight is not guaranteed by the arithmetic, and a naive walk can run off the end.
    /// </summary>
    [Fact]
    public void The_walk_does_not_fall_off_the_end_at_the_top_of_the_unit_interval()
    {
        var table = new[] { ("a", 0.5), ("b", 0.5) };

        var act = () => DeterministicRng.PickAt(TopOfUnitInterval, table);

        Should.NotThrow(act).ShouldBe("b");
    }

    /// <summary>
    /// The same edge with a trailing zero-weight row: falling off the end must land on the last
    /// row that can actually be picked, not on the disabled one behind it.
    /// </summary>
    [Fact]
    public void The_walk_at_the_top_of_the_unit_interval_skips_a_trailing_zero_weight_entry()
    {
        var table = new[] { ("a", 0.5), ("b", 0.5), ("disabled", 0.0) };

        DeterministicRng.PickAt(TopOfUnitInterval, table).ShouldBe("b");
    }

    /// <summary>The bottom of the range picks the first weighted item.</summary>
    [Fact]
    public void The_walk_at_the_bottom_of_the_unit_interval_returns_the_first_weighted_entry()
    {
        var table = new[] { ("disabled", 0.0), ("a", 0.5), ("b", 0.5) };

        DeterministicRng.PickAt(0.0, table).ShouldBe("a");
    }

    /// <summary>
    /// Weights are relative, so scaling the whole table changes nothing. This is what lets
    /// content authors write 90/9/1 or 0.9/0.09/0.01 without shifting a single outcome.
    /// </summary>
    /// <remarks>
    /// Asserted over every committed draw rather than at one arbitrary position. A walk that
    /// forgets to scale the unit interval by Σ agrees with itself on the two tables for most of
    /// the interval and only diverges in its top tenth — at a single unpinned position this test
    /// would pass or fail on where that one draw happened to land.
    /// </remarks>
    [Theory]
    [MemberData(nameof(DrawIds))]
    public void WeightedPick_is_unchanged_by_scaling_every_weight(string rowId)
    {
        var row = ReferenceVectors.DrawRow(rowId);
        var small = new[] { ("a", 0.9), ("b", 0.09), ("c", 0.01) };
        var large = new[] { ("a", 90.0), ("b", 9.0), ("c", 1.0) };

        var fromSmall = new DeterministicRng(row.Seed, row.Stream, row.Position).WeightedPick(small);
        var fromLarge = new DeterministicRng(row.Seed, row.Stream, row.Position).WeightedPick(large);

        fromSmall.ShouldBe(fromLarge);
    }

    /// <summary>
    /// The pick is taken at this draw's unit interval — the value <c>NextDouble</c> would have
    /// returned, not some other function of the draw. Deriving <c>x</c> from <c>NextUInt</c>, or
    /// from <c>draw % Σ</c>, would satisfy every other test here.
    /// </summary>
    [Theory]
    [MemberData(nameof(DrawIds))]
    public void WeightedPick_picks_at_this_draws_unit_interval(string rowId)
    {
        var row = ReferenceVectors.DrawRow(rowId);
        var expected = DeterministicRng.PickAt(
            BitConverter.UInt64BitsToDouble(row.NextDoubleBits), UniformTenEntryTable());

        var picked = new DeterministicRng(row.Seed, row.Stream, row.Position)
            .WeightedPick(UniformTenEntryTable());

        picked.ShouldBe(expected);
    }

    /// <summary>An empty table has nothing to return; there is no sensible draw to make.</summary>
    [Fact]
    public void WeightedPick_rejects_an_empty_table()
    {
        var rng = new DeterministicRng(RunSeed, RngStreams.Drops);

        var act = () => rng.WeightedPick(Array.Empty<(string, double)>());

        Should.Throw<ArgumentException>(act);
    }

    [Fact]
    public void WeightedPick_rejects_a_null_table()
    {
        var rng = new DeterministicRng(RunSeed, RngStreams.Drops);

        var act = () => rng.WeightedPick<string>(null!);

        Should.Throw<ArgumentNullException>(act);
    }

    /// <summary>
    /// A table where every row is disabled has no pickable item — content is broken, and
    /// silently returning the last row would hide it.
    /// </summary>
    [Fact]
    public void WeightedPick_rejects_a_table_whose_weights_all_sit_at_zero()
    {
        var rng = new DeterministicRng(RunSeed, RngStreams.Drops);

        var act = () => rng.WeightedPick(new[] { ("a", 0.0), ("b", 0.0) });

        Should.Throw<ArgumentException>(act);
    }

    /// <summary>
    /// A negative weight makes the cumulative walk non-monotonic, which turns "the first item
    /// that exceeds x" into an arbitrary answer. It is a content error, not a draw.
    /// </summary>
    [Theory]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void WeightedPick_rejects_a_weight_that_is_not_a_finite_non_negative_number(double weight)
    {
        var rng = new DeterministicRng(RunSeed, RngStreams.Drops);

        var act = () => rng.WeightedPick(new[] { ("a", 1.0), ("b", weight) });

        Should.Throw<ArgumentException>(act);
    }

    /// <summary>
    /// Over many draws every weighted row is reachable. Not a distribution test — a much weaker
    /// claim, that the walk is not stuck on one row, which a broken cumulative comparison would
    /// fail outright.
    /// </summary>
    [Fact]
    public void WeightedPick_reaches_every_weighted_entry_over_many_draws()
    {
        var table = new[] { ("a", 1.0), ("b", 1.0), ("c", 1.0) };
        var rng = new DeterministicRng(RunSeed, RngStreams.Drops);

        var picks = Enumerable.Range(0, 300).Select(_ => rng.WeightedPick(table)).Distinct();

        picks.ShouldBe(new[] { "a", "b", "c" }, ignoreOrder: true);
    }

    /// <summary>Ten equally weighted rows — one row per tenth of the unit interval.</summary>
    private static (string, double)[] UniformTenEntryTable() => new[]
    {
        ("a", 1.0), ("b", 1.0), ("c", 1.0), ("d", 1.0), ("e", 1.0),
        ("f", 1.0), ("g", 1.0), ("h", 1.0), ("i", 1.0), ("j", 1.0),
    };

    public static TheoryData<string> DrawIds() => ReferenceVectors.DrawIds();
}
