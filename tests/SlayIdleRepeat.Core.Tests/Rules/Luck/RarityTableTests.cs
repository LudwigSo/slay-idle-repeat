using Shouldly;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Luck;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Luck;

/// <summary>
/// The weighted rarity table and the two operations the luck rules perform on one: flooring it at a
/// rarity — proportionally, one rule for every source class — and scaling a single row.
/// </summary>
/// <remarks>
/// The cases use <c>24</c> §4.0a's premium-chest table (B 20 · A 45 · S 30 · SS 5) because its
/// numbers <em>discriminate</em>: floored at A, proportional renormalisation answers
/// <c>0.5625 / 0.375 / 0.0625</c>, redistributing the freed 20 equally would answer
/// <c>0.5167 / 0.3667 / 0.1167</c>, and giving the remainder to the floor would answer
/// <c>0.65 / 0.30 / 0.05</c>. A table whose three readings agreed would prove nothing.
/// </remarks>
public sealed class RarityTableTests
{
    // ---------------------------------------------------------------- flooring

    /// <summary>Every rarity below the floor is zeroed, and its row stays in the table.</summary>
    /// <remarks>
    /// Kept rather than removed: the row order is the walk order, and a table whose shape changed
    /// with the floor would make a floored draw and an unfloored one two different walks.
    /// </remarks>
    [Fact]
    public void Flooring_zeroes_every_rarity_below_the_floor_and_keeps_its_row()
    {
        var floored = LuckTables.ChestPremium().FloorAt(Rarity.A);

        Weight(floored, Rarity.B).ShouldBe(
            0.0,
            "24 §4.0a rule 3: a floored source 'draws its class table renormalised at/above the " +
            "floor'. DeterministicRng.WeightedPick skips a zero-weight row on the walk, so zeroing " +
            "is what makes a below-floor rarity unreachable.");
        floored.Rows.Select(row => row.Rarity).ShouldBe(
            LuckTables.ChestPremium().Rows.Select(row => row.Rarity));
    }

    /// <summary>
    /// The survivors keep their relative ratios — the claim that tells proportional renormalisation
    /// apart from every other way of spending the freed weight.
    /// </summary>
    [Fact]
    public void Flooring_preserves_the_surviving_rarities_relative_ratios()
    {
        var floored = LuckTables.ChestPremium().FloorAt(Rarity.A);

        DeterminismRounding.Round(Weight(floored, Rarity.A) / Weight(floored, Rarity.S)).ShouldBe(
            1.5,
            "A:S is 45:30 before the floor and must still be 45:30 after it. Redistributing the " +
            "freed 20 equally would answer 1.409; giving it all to the floor would answer 2.167.");
        DeterminismRounding.Round(Weight(floored, Rarity.S) / Weight(floored, Rarity.SS)).ShouldBe(
            6.0,
            "S:SS is 30:5 before the floor and must still be 30:5 after it. Redistributing equally " +
            "would answer 3.143.");
    }

    /// <summary>A floored table sums to one, and each survivor holds its own share of it.</summary>
    [Fact]
    public void Flooring_renormalises_the_survivors_to_sum_to_one()
    {
        var floored = LuckTables.ChestPremium().FloorAt(Rarity.A);

        Weight(floored, Rarity.A).ShouldBe(0.5625, "45 of the surviving 80");
        Weight(floored, Rarity.S).ShouldBe(0.375, "30 of the surviving 80");
        Weight(floored, Rarity.SS).ShouldBe(0.0625, "5 of the surviving 80");
        DeterminismRounding.Round(floored.TotalWeight).ShouldBe(1.0);
    }

    /// <summary>
    /// A floor at the bottom of the ladder renormalises without reweighting: nothing is below it, so
    /// every share survives and every ratio is unchanged.
    /// </summary>
    /// <remarks>
    /// The negative control on flooring. An implementation that always gave the floor rarity the
    /// freed weight would pass every case above and would visibly break here, where there is no
    /// freed weight to give.
    /// </remarks>
    [Fact]
    public void A_floor_below_every_authored_rarity_renormalises_without_moving_a_share()
    {
        var floored = LuckTables.ChestPremium().FloorAt(Rarity.C);

        Weight(floored, Rarity.B).ShouldBe(0.2);
        Weight(floored, Rarity.A).ShouldBe(0.45);
        Weight(floored, Rarity.S).ShouldBe(0.3);
        Weight(floored, Rarity.SS).ShouldBe(0.05);
        DeterminismRounding.Round(floored.TotalWeight).ShouldBe(1.0);
    }

    /// <summary>A zero-weight row below the floor changes nothing — there is nothing there to free.</summary>
    /// <remarks>
    /// The second negative control: an implementation that spent "the number of zeroed rows" rather
    /// than "the zeroed weight" would answer differently for these two tables.
    /// </remarks>
    [Fact]
    public void A_zero_weight_row_below_the_floor_does_not_change_the_result()
    {
        var withEmptyRow = RarityTable.Of(
        [
            new RarityWeight(Rarity.C, 0.0),
            new RarityWeight(Rarity.B, 20.0),
            new RarityWeight(Rarity.A, 45.0),
            new RarityWeight(Rarity.S, 30.0),
            new RarityWeight(Rarity.SS, 5.0),
        ]).FloorAt(Rarity.A);

        Weight(withEmptyRow, Rarity.A).ShouldBe(0.5625);
        Weight(withEmptyRow, Rarity.S).ShouldBe(0.375);
        Weight(withEmptyRow, Rarity.SS).ShouldBe(0.0625);
    }

    /// <summary>Flooring answers a new table and leaves the one it was asked of alone.</summary>
    /// <remarks>
    /// Load-bearing: a resolution floors the class table for one draw, and a mutation would leave the
    /// next opener of the same chest class drawing against the previous player's guarantee.
    /// </remarks>
    [Fact]
    public void Flooring_leaves_the_original_table_unchanged()
    {
        var table = LuckTables.ChestPremium();

        table.FloorAt(Rarity.SS);

        Weight(table, Rarity.B).ShouldBe(20.0);
        table.TotalWeight.ShouldBe(100.0);
    }

    /// <summary>A floor the table cannot satisfy is refused rather than answered as an empty table.</summary>
    /// <remarks>
    /// Refused rather than drawn, and refused <em>before</em> the draw: a resolution that consumed a
    /// draw index on its way to an exception would shift every later draw on that stream.
    /// </remarks>
    [Fact]
    public void A_floor_no_row_can_satisfy_is_refused()
    {
        Should.Throw<InvalidOperationException>(() => LuckTables.BelowA().FloorAt(Rarity.A))
            .Message.ShouldContain(
                "floor",
                Case.Insensitive,
                "the identity of the refusal, not merely that something threw: LuckService throws " +
                "InvalidOperationException for an unserved source class as well, and a case that " +
                "accepted either would go green on a table that was refused for the wrong reason.");
    }

    /// <summary>The top of the ladder is a satisfiable floor wherever the table carries weight there.</summary>
    /// <remarks>The positive control on the refusal above — otherwise "always throws" would pass it.</remarks>
    [Fact]
    public void A_floor_the_table_can_satisfy_is_answered()
    {
        var floored = LuckTables.ChestApex().FloorAt(Rarity.SS);

        Weight(floored, Rarity.SS).ShouldBe(1.0);
        Weight(floored, Rarity.S).ShouldBe(0.0);
    }

    /// <summary>An undeclared rarity is not a floor.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public void An_undeclared_rarity_is_refused_as_a_floor(int rarity)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => LuckTables.ChestPremium().FloorAt((Rarity)rarity))
            .ParamName.ShouldBe(
                "floor",
                "the argument that was wrong, not merely that some argument was — FloorAt has one " +
                "parameter today and will not always, and a case that took any range refusal would " +
                "then pass on the wrong one.");
    }

    // ---------------------------------------------------------------- scaling

    /// <summary>Scaling multiplies one row and leaves every other where it was.</summary>
    [Fact]
    public void Scaling_multiplies_one_row_and_leaves_the_rest_alone()
    {
        var scaled = LuckTables.ChestPremium().Scale(Rarity.SS, 2.0);

        Weight(scaled, Rarity.SS).ShouldBe(10.0);
        Weight(scaled, Rarity.B).ShouldBe(20.0);
        Weight(scaled, Rarity.A).ShouldBe(45.0);
        Weight(scaled, Rarity.S).ShouldBe(30.0);
        scaled.TotalWeight.ShouldBe(
            105.0,
            "24 §4.0a rule 2: the multiplier applies before normalisation, so the total grows");
    }

    /// <summary>
    /// A rarity the table does not carry is left alone rather than added: a curve targeting a rarity
    /// a class cannot drop must not conjure it.
    /// </summary>
    /// <remarks>
    /// Reachable: the wheel's curve targets <c>JACKPOT</c>, and two of the five ladders are authored
    /// against tables that do not carry every band. An added row would be a rarity the class's own
    /// odds table says it never grants.
    /// </remarks>
    [Fact]
    public void Scaling_a_rarity_the_table_does_not_carry_adds_nothing()
    {
        var scaled = LuckTables.CrateMount().Scale(Rarity.C, 5.0);

        scaled.Rows.Select(row => row.Rarity).ShouldBe(
            new[] { Rarity.A, Rarity.S, Rarity.SS },
            "24 §4.5 authors the crate as A/S/SS. A C row appearing here is a mount rarity the " +
            "document does not have.");
        scaled.TotalWeight.ShouldBe(100.0);
    }

    /// <summary>A multiplier of zero closes the band; the row stays, unreachable.</summary>
    [Fact]
    public void Scaling_to_zero_closes_the_band_without_removing_the_row()
    {
        var scaled = LuckTables.CrateMount().Scale(Rarity.SS, 0.0);

        Weight(scaled, Rarity.SS).ShouldBe(0.0);
        scaled.Rows.Count.ShouldBe(3);
        scaled.HasPositiveWeight.ShouldBeTrue();
    }

    /// <summary>Scaling answers a new table and leaves the one it was asked of alone.</summary>
    [Fact]
    public void Scaling_leaves_the_original_table_unchanged()
    {
        var table = LuckTables.CrateMount();

        table.Scale(Rarity.SS, 10.0);

        Weight(table, Rarity.SS).ShouldBe(4.0);
    }

    /// <summary>A multiplier must be finite and non-negative, and the rarity must be declared.</summary>
    /// <remarks>
    /// The expected parameter is part of each case: <c>Scale</c> takes two arguments and both have a
    /// stated range, so a refusal that named the other one would be a guard on the wrong argument
    /// passing for the right one.
    /// </remarks>
    [Theory]
    [InlineData(4, -1.0, "multiplier")]
    [InlineData(4, double.NaN, "multiplier")]
    [InlineData(4, double.PositiveInfinity, "multiplier")]
    [InlineData(0, 2.0, "rarity")]
    [InlineData(6, 2.0, "rarity")]
    public void A_scale_outside_its_stated_ranges_is_refused(int rarity, double multiplier, string parameter)
    {
        Should.Throw<ArgumentOutOfRangeException>(
                () => LuckTables.ChestPremium().Scale((Rarity)rarity, multiplier))
            .ParamName.ShouldBe(parameter);
    }

    // ---------------------------------------------------------------- construction

    /// <summary>Rows come back in ascending rarity order however they were supplied.</summary>
    /// <remarks>
    /// The walk is ordered, so the order is part of the answer: two tables with the same weights in
    /// different orders must draw identically or a floored draw and an unfloored one diverge.
    /// </remarks>
    [Fact]
    public void Rows_come_back_in_ascending_rarity_order()
    {
        RarityTable.Of(
        [
            new RarityWeight(Rarity.SS, 5.0),
            new RarityWeight(Rarity.B, 20.0),
            new RarityWeight(Rarity.S, 30.0),
            new RarityWeight(Rarity.A, 45.0),
        ]).Rows.Select(row => row.Rarity).ShouldBe(new[] { Rarity.B, Rarity.A, Rarity.S, Rarity.SS });
    }

    /// <summary>The total is the sum of the rows, unnormalised until something normalises it.</summary>
    [Fact]
    public void The_total_weight_is_the_sum_of_the_rows()
    {
        LuckTables.ChestPremium().TotalWeight.ShouldBe(100.0);
        LuckTables.ChestApex().TotalWeight.ShouldBe(100.0);
    }

    /// <summary>A table with any positive row can be drawn from; one with none cannot.</summary>
    /// <remarks>
    /// The false arm carries the case. Construction refuses an all-zero table and <c>FloorAt</c>
    /// refuses a floor it cannot satisfy, so scaling the last positive row to zero is the one way a
    /// weightless table is reachable at all — and without it <c>HasPositiveWeight =&gt; true</c>
    /// satisfies every assertion here, which would make the check <c>Resolve</c> runs before drawing
    /// (and the "a refusal consumes no draw index" property that rests on it) dead code.
    /// </remarks>
    [Fact]
    public void A_table_reports_whether_anything_can_be_drawn_from_it()
    {
        LuckTables.ChestPremium().HasPositiveWeight.ShouldBeTrue();
        LuckTables.ChestPremium().FloorAt(Rarity.SS).HasPositiveWeight.ShouldBeTrue();

        LuckTables.Only(Rarity.A).Scale(Rarity.A, 0.0).HasPositiveWeight.ShouldBeFalse(
            "a table whose only band has been closed carries no weight, and answering true here " +
            "would send it to the weighted walk with nothing to land on.");
    }

    /// <summary>A rarity supplied twice is refused: one of the two weights would vanish silently.</summary>
    [Fact]
    public void A_rarity_supplied_twice_is_refused()
    {
        Should.Throw<ArgumentException>(() => RarityTable.Of(
        [
            new RarityWeight(Rarity.A, 45.0),
            new RarityWeight(Rarity.A, 30.0),
        ])).ParamName.ShouldBe(Rows);
    }

    /// <summary>Every weight is finite and non-negative, and at least one is positive.</summary>
    [Theory]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void A_row_that_is_not_a_finite_non_negative_weight_is_refused(double weight)
    {
        Should.Throw<ArgumentException>(() => RarityTable.Of(
        [
            new RarityWeight(Rarity.A, 45.0),
            new RarityWeight(Rarity.S, weight),
        ])).ParamName.ShouldBe(Rows);
    }

    /// <summary>A table nothing can be drawn from is refused at construction.</summary>
    [Fact]
    public void A_table_with_no_positive_weight_is_refused_at_construction()
    {
        Should.Throw<ArgumentException>(() => RarityTable.Of(
        [
            new RarityWeight(Rarity.A, 0.0),
            new RarityWeight(Rarity.S, 0.0),
        ])).ParamName.ShouldBe(Rows);

        Should.Throw<ArgumentException>(() => RarityTable.Of([])).ParamName.ShouldBe(Rows);
    }

    /// <summary>The builder refuses a null sequence rather than dereferencing it.</summary>
    [Fact]
    public void A_null_row_sequence_is_refused()
    {
        Should.Throw<ArgumentNullException>(() => RarityTable.Of(null!)).ParamName.ShouldBe(Rows);
    }

    /// <summary>
    /// The one argument every construction refusal is about, named once so each case pins which
    /// guard fired rather than merely that an <see cref="ArgumentException"/> came out.
    /// </summary>
    private const string Rows = "rows";

    /// <summary>One row's weight, through the project's own 4-decimal rule.</summary>
    /// <remarks>
    /// Renormalisation divides, and <c>45 × (1 / 80)</c> and <c>45 / 80</c> are not the same double
    /// even though they are the same number. Comparing the raw value would pin which of the two the
    /// implementation happens to write; <c>DeterminismRounding</c> is the repository's own answer to
    /// that, and the three readings this file discriminates between are 0.5625 / 0.5167 / 0.65 —
    /// nowhere near each other at four decimal places.
    /// </remarks>
    private static double Weight(RarityTable table, Rarity rarity) =>
        DeterminismRounding.Round(table.Rows.Single(row => row.Rarity == rarity).Weight);
}
