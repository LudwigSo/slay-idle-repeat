using FluentAssertions;
using SlayIdleRepeat.Core.Rng;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rng;

/// <summary>
/// 🔒 `14` §8.0 — <c>WeightedPick</c>. <b>ONE draw</b>: <c>x = unit interval × Σ weights</c>;
/// walk the table in order; the first item whose cumulative weight <i>exceeds</i> <c>x</c>.
/// </summary>
/// <remarks>
/// Two draws would be the natural implementation (one to choose, one to break a tie) and would
/// quietly break the persisted counter for every stream a weighted pick ever touches. The
/// walk's semantics matter as much: strict <c>&gt;</c> is what makes a zero-weight row
/// unreachable rather than reachable-only-at-exactly-zero.
/// </remarks>
public sealed class WeightedPickTests
{
    private const ulong RunSeed = 0x0123456789ABCDEFUL;

    /// <summary>The largest value <c>NextDouble</c> can produce: 1 − 2^-53.</summary>
    private const double TopOfUnitInterval = 1.0 - (1.0 / 9007199254740992.0);

    /// <summary>🔒 One draw, not two.</summary>
    [Fact]
    public void WeightedPick_consumes_exactly_one_draw_index()
    {
        var rng = new DeterministicRng(RunSeed, RngStreams.Drops, 40UL);

        rng.WeightedPick(new[] { ("common", 90.0), ("rare", 9.0), ("epic", 1.0) });

        rng.Position.Should().Be(41UL);
    }

    /// <summary>A single-entry table always yields its one item, and still costs its one draw.</summary>
    [Fact]
    public void WeightedPick_returns_the_only_item_of_a_single_entry_table()
    {
        var rng = new DeterministicRng(RunSeed, RngStreams.Drops);

        rng.WeightedPick(new[] { ("only", 1.0) }).Should().Be("only");
    }

    /// <summary>
    /// The walk is in table order, so the order is part of the outcome. Reversing a table
    /// re-assigns the same unit interval to different items — that is the contract, and content
    /// authors need it to be true and stated.
    /// </summary>
    [Fact]
    public void WeightedPick_walks_the_table_in_order_so_reordering_it_changes_the_outcome()
    {
        var forwards = new[] { ("first", 1.0), ("second", 99.0) };
        var backwards = new[] { ("second", 99.0), ("first", 1.0) };

        var fromForwards = new DeterministicRng(RunSeed, RngStreams.Drops).WeightedPick(forwards);
        var fromBackwards = new DeterministicRng(RunSeed, RngStreams.Drops).WeightedPick(backwards);

        fromForwards.Should().NotBe(fromBackwards);
    }

    /// <summary>
    /// 🔒 A zero-weight row is unreachable — anywhere in the table, at any draw. Content leaves
    /// weights at zero to disable a row, and a disabled row that can still be picked is a bug
    /// that surfaces once in ten thousand runs.
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

        picks.Should().NotContain(table[zeroIndex].Item1);
    }

    /// <summary>
    /// The rule stated directly, on the pure walk rather than through a draw: the first
    /// cumulative weight <b>strictly greater</b> than <c>x</c> wins. The weights sum to exactly
    /// 1 and are exact in binary, so the boundary rows below are boundaries and not
    /// floating-point luck — at <c>x = 0.25</c> item "a" (cumulative 0.25) does <i>not</i> win.
    /// </summary>
    [Theory]
    [InlineData(0.0, "a")]
    [InlineData(0.125, "a")]
    [InlineData(0.25, "b")]
    [InlineData(0.375, "b")]
    [InlineData(0.5, "c")]
    [InlineData(0.75, "c")]
    public void The_walk_returns_the_first_item_whose_cumulative_weight_exceeds_the_value(double x, string expected)
    {
        var table = new[] { ("a", 0.25), ("b", 0.25), ("c", 0.5) };

        DeterministicRng.PickAt(x, table).Should().Be(expected);
    }

    /// <summary>
    /// 🔒 The top of the range. <c>x = (1 − 2^-53) × Σ weights</c> can round <i>up</i> to
    /// exactly <c>Σ weights</c> when the total is a power of two, so a naive walk finds no
    /// cumulative weight greater than <c>x</c> and falls off the end. It must return the last
    /// weighted item instead of throwing or returning nothing.
    /// </summary>
    [Fact]
    public void The_walk_does_not_fall_off_the_end_at_the_top_of_the_unit_interval()
    {
        var table = new[] { ("a", 0.5), ("b", 0.5) };

        var act = () => DeterministicRng.PickAt(TopOfUnitInterval, table);

        act.Should().NotThrow().Which.Should().Be("b");
    }

    /// <summary>
    /// The same edge with a trailing zero-weight row: falling off the end must land on the last
    /// row that can actually be picked, not on the disabled one behind it.
    /// </summary>
    [Fact]
    public void The_walk_at_the_top_of_the_unit_interval_skips_a_trailing_zero_weight_entry()
    {
        var table = new[] { ("a", 0.5), ("b", 0.5), ("disabled", 0.0) };

        DeterministicRng.PickAt(TopOfUnitInterval, table).Should().Be("b");
    }

    /// <summary>The bottom of the range picks the first weighted item.</summary>
    [Fact]
    public void The_walk_at_the_bottom_of_the_unit_interval_returns_the_first_weighted_entry()
    {
        var table = new[] { ("disabled", 0.0), ("a", 0.5), ("b", 0.5) };

        DeterministicRng.PickAt(0.0, table).Should().Be("a");
    }

    /// <summary>
    /// Weights are relative, so scaling the whole table changes nothing. This is what lets
    /// content authors write 90/9/1 or 0.9/0.09/0.01 without shifting a single outcome.
    /// </summary>
    [Fact]
    public void WeightedPick_is_unchanged_by_scaling_every_weight()
    {
        var small = new[] { ("a", 0.9), ("b", 0.09), ("c", 0.01) };
        var large = new[] { ("a", 90.0), ("b", 9.0), ("c", 1.0) };

        var fromSmall = new DeterministicRng(RunSeed, RngStreams.Drops).WeightedPick(small);
        var fromLarge = new DeterministicRng(RunSeed, RngStreams.Drops).WeightedPick(large);

        fromSmall.Should().Be(fromLarge);
    }

    /// <summary>An empty table has nothing to return; there is no sensible draw to make.</summary>
    [Fact]
    public void WeightedPick_rejects_an_empty_table()
    {
        var rng = new DeterministicRng(RunSeed, RngStreams.Drops);

        var act = () => rng.WeightedPick(Array.Empty<(string, double)>());

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void WeightedPick_rejects_a_null_table()
    {
        var rng = new DeterministicRng(RunSeed, RngStreams.Drops);

        var act = () => rng.WeightedPick<string>(null!);

        act.Should().Throw<ArgumentNullException>();
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

        act.Should().Throw<ArgumentException>();
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

        act.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// Two picks over the same table at the same position agree — the pick is a pure function
    /// of the draw, with nothing carried between calls.
    /// </summary>
    [Fact]
    public void WeightedPick_is_reproducible_from_the_position_alone()
    {
        var table = new[] { ("a", 1.0), ("b", 2.0), ("c", 3.0) };

        var first = new DeterministicRng(RunSeed, RngStreams.Treasure, 17UL).WeightedPick(table);
        var second = new DeterministicRng(RunSeed, RngStreams.Treasure, 17UL).WeightedPick(table);

        first.Should().Be(second);
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

        picks.Should().BeEquivalentTo(new[] { "a", "b", "c" });
    }
}
