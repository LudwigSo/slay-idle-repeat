using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Hero;
using SlayIdleRepeat.Core.Tests.Content;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Hero;

/// <summary>
/// <see cref="LegendLevelCurve"/> — `07` §1.1's <c>LegendXpForLevel(L) = 120 * L^1.05</c>, its
/// cumulative sum, and the level a lifetime total buys.
/// </summary>
/// <remarks>
/// 🔒 <b>The document prints two totals for the level cap and they disagree: the table's row says
/// ~3.04M and the prose two lines below says ~3.07M.</b> The M4 kickoff ruled the formula
/// authoritative and neither total transcribable, so what is asserted here is the formula's own
/// answer and the arithmetic that explains the discrepancy —
/// <see cref="The_prose_total_sums_one_level_up_too_many"/> reproduces the wrong figure by summing
/// 200 terms instead of 199, which is what makes the ruling checkable rather than asserted.
/// </remarks>
public sealed class LegendLevelCurveTests
{
    private static readonly LegendCurveTuning Curve =
        LegendCurveTuning.Read(ProgressionDocuments.Shipped);

    private static readonly LegendTuning Range = LegendTuning.Read(ProgressionDocuments.Shipped);

    /// <summary>The first level-up costs the coefficient exactly, because <c>1^x</c> is 1.</summary>
    [Fact]
    public void The_first_level_up_costs_the_coefficient()
    {
        LegendLevelCurve.XpForLevel(1, Curve, Range)
            .ShouldBe(ProgressionDocuments.ShippedLegendXpCoefficient);
    }

    /// <summary>The cost rises with the level, which is what an exponent above 1 means.</summary>
    /// <remarks>
    /// Sampled across the ladder rather than at one point: a curve computed with the exponent
    /// applied to the coefficient instead of the level is also monotonic, and would pass a
    /// two-point check.
    /// </remarks>
    [Theory]
    [InlineData(1, 2)]
    [InlineData(9, 10)]
    [InlineData(99, 100)]
    [InlineData(198, 199)]
    public void Each_level_up_costs_more_than_the_one_before(int lower, int higher)
    {
        LegendLevelCurve.XpForLevel(higher, Curve, Range)
            .ShouldBeGreaterThan(LegendLevelCurve.XpForLevel(lower, Curve, Range));
    }

    /// <summary>The starting level has cost nothing: a player at the floor has banked no XP.</summary>
    [Fact]
    public void The_starting_level_costs_nothing_cumulatively()
    {
        LegendLevelCurve.CumulativeXpTo(Range.Minimum, Curve, Range).ShouldBe(0d);
    }

    /// <summary>
    /// `07` §1.1's cumulative column, reproduced from the formula at every level the table prints.
    /// </summary>
    /// <remarks>
    /// The expectations are the table's own printed figures, compared against the formula's answer
    /// within HALF A UNIT OF THE LAST DIGIT THE ROW SHOWS — that tolerance is carried per row rather
    /// than derived, because it is a property of how the row is printed ("~1.3k" shows hundreds,
    /// "~3.04M" shows ten-thousands) and a single relative tolerance would be far too loose on one
    /// end and impossible on the other. Ten rows rather than one: the exponent is the shape of the
    /// whole ladder, and a single row is satisfied by a straight line through it.
    /// </remarks>
    [Theory]
    [InlineData(5, 1_300d, 50d)]
    [InlineData(8, 3_600d, 50d)]
    [InlineData(10, 5_900d, 50d)]
    [InlineData(15, 14_100d, 50d)]
    [InlineData(20, 25_800d, 50d)]
    [InlineData(30, 60_300d, 50d)]
    [InlineData(40, 109_700d, 50d)]
    [InlineData(60, 254_200d, 50d)]
    [InlineData(100, 729_400d, 50d)]
    [InlineData(200, 3_040_000d, 5_000d)]
    public void The_cumulative_column_of_07_section_1_1_falls_out_of_the_formula(
        int level, double printed, double tolerance)
    {
        var computed = LegendLevelCurve.CumulativeXpTo(level, Curve, Range);

        computed.ShouldBeInRange(
            printed - tolerance,
            printed + tolerance,
            $"07 §1.1's table prints ~{printed} at level {level}; the authored formula answers {computed}.");
    }

    /// <summary>
    /// 🔒 The level-200 total is the sum of <b>199</b> level-ups, and the document's prose figure is
    /// the sum of 200.
    /// </summary>
    /// <remarks>
    /// This is the doc contradiction, made checkable. `07` §1.1's table says ~3.04M and the sentence
    /// below it says <em>"199 level-ups and ~3.07M total"</em> — but 3.07M is what you get by adding
    /// <c>LegendXpForLevel(200)</c>, the price of going from 200 to 201, which the cap makes
    /// unreachable. The table is right and the prose is arithmetically one term long.
    /// </remarks>
    [Fact]
    public void The_prose_total_sums_one_level_up_too_many()
    {
        var toTheCap = LegendLevelCurve.CumulativeXpTo(Range.Maximum, Curve, Range);
        var levelUps = Range.Maximum - Range.Minimum;

        levelUps.ShouldBe(199, "07 §1.1: reaching the cap requires 199 level-ups.");

        toTheCap.ShouldBeInRange(
            3_030_000d, 3_042_000d, "the table's ~3.04M row is the formula's own answer.");

        var proseFigure = toTheCap +
            (ProgressionDocuments.ShippedLegendXpCoefficient *
             Math.Pow(Range.Maximum, (double)ProgressionDocuments.ShippedLegendXpExponent));

        proseFigure.ShouldBeInRange(
            3_060_000d,
            3_072_000d,
            "…and ~3.07M is that same sum plus the price of a 201st level, which is the term the " +
            "prose adds and the cap forbids. Neither total is transcribed anywhere in the codebase.");
    }

    /// <summary>The cumulative sum and the per-level costs are the same arithmetic.</summary>
    /// <remarks>
    /// <para>
    /// A cumulative implemented with its own closed form — or with a different rounding — would
    /// drift from the per-level costs the client shows on the level-up screen, and the two are the
    /// same promise to the player.
    /// </para>
    /// <para>
    /// ⚠️ <b>This case cannot see the rounding itself, and says so rather than pretending.</b> The
    /// expectation is built by calling production's own <c>XpForLevel</c> through production's own
    /// number of decimal places, so dropping <c>DeterminismRounding.Round</c> from the curve moves
    /// both sides of the comparison identically and this stays green.
    /// <see cref="Every_figure_the_curve_answers_is_rounded"/> is the half that bites there. The
    /// magic <c>4</c> is gone with it: the places are
    /// <see cref="DeterminismRounding.Decimals"/>' to state, and a test restating them as a literal
    /// would keep agreeing with a curve that had moved off them.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_cumulative_is_the_running_sum_of_the_per_level_costs()
    {
        var running = 0d;
        var compared = 0;

        for (var level = Range.Minimum; level < Range.Minimum + 30; level++)
        {
            running = Math.Round(
                running + LegendLevelCurve.XpForLevel(level, Curve, Range),
                DeterminismRounding.Decimals);

            LegendLevelCurve.CumulativeXpTo(level + 1, Curve, Range).ShouldBe(running);
            compared++;
        }

        compared.ShouldBe(30, "the assertion above lives inside a loop (steering S3).");
    }

    /// <summary>
    /// 🔒 Every figure the curve hands out is already in the form
    /// <c>DeterminismRounding.Round</c> produces.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>The claim the case above cannot make.</b> Legend XP is banked as a whole number and
    /// compared between a client's arithmetic and the server's, so an unrounded <c>double</c>
    /// escaping either half of this curve is a figure the two sides can disagree about — and it is
    /// invisible to any comparison built by calling the curve. <c>120 × L^1.05</c> is irrational at
    /// almost every level, so an unrounded answer is the DEFAULT here rather than an edge case:
    /// deleting the rounding from <c>XpForLevel</c> or from <c>CumulativeXpTo</c> fails this at the
    /// first level it reaches.
    /// </para>
    /// <para>
    /// Both members, and floored on the count: a rule quantifying over a range that could be empty
    /// passes forever (steering S3).
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_figure_the_curve_answers_is_rounded()
    {
        var checkedFigures = 0;

        for (var level = Range.Minimum; level < Range.Maximum; level++)
        {
            DeterminismRounding.IsRounded(LegendLevelCurve.XpForLevel(level, Curve, Range))
                .ShouldBeTrue(
                    $"XpForLevel({level}) answered " +
                    LegendLevelCurve.XpForLevel(level, Curve, Range) +
                    ", which persisted state and a replay comparison cannot carry.");

            DeterminismRounding.IsRounded(LegendLevelCurve.CumulativeXpTo(level, Curve, Range))
                .ShouldBeTrue(
                    $"CumulativeXpTo({level}) answered " +
                    LegendLevelCurve.CumulativeXpTo(level, Curve, Range) + ", likewise.");

            checkedFigures += 2;
        }

        checkedFigures.ShouldBe(
            (Range.Maximum - Range.Minimum) * 2,
            "the assertions above live inside a loop over a range the tuning could narrow.");
    }

    /// <summary>The exact cumulative total for a level is the boundary a player levels at.</summary>
    /// <remarks>
    /// Both sides of the boundary, at three different levels. One XP short is the level below, and
    /// exactly the total is the level itself — a comparison written <c>&gt;=</c> where it should be
    /// <c>&gt;</c> fails one of the two and passes the other.
    /// </remarks>
    [Theory]
    [InlineData(2)]
    [InlineData(11)]
    [InlineData(41)]
    public void A_level_is_reached_at_exactly_its_cumulative_total(int level)
    {
        // Rounded UP, not truncated: the curve answers a fractional total and Legend XP is banked
        // as a whole number, so the first total that BUYS the level is the ceiling of it. Truncating
        // would test the level below and pass for the wrong reason.
        var needed = (long)Math.Ceiling(LegendLevelCurve.CumulativeXpTo(level, Curve, Range));

        LegendLevelCurve.LevelFor(needed, Curve, Range).ShouldBe(level);
        LegendLevelCurve.LevelFor(needed - 1, Curve, Range).ShouldBe(level - 1);
    }

    /// <summary>No XP at all is the starting level, not level zero.</summary>
    [Fact]
    public void No_XP_is_the_starting_level()
    {
        LegendLevelCurve.LevelFor(0, Curve, Range).ShouldBe(Range.Minimum);
    }

    /// <summary>Excess XP past the cap buys nothing, which is what a cap is.</summary>
    [Fact]
    public void XP_past_the_cap_buys_no_further_level()
    {
        LegendLevelCurve.LevelFor(long.MaxValue / 2, Curve, Range).ShouldBe(Range.Maximum);
    }

    /// <summary>The cap has no next level, so pricing one is refused rather than answered.</summary>
    [Fact]
    public void The_cap_has_no_next_level_to_price()
    {
        Should.Throw<ArgumentOutOfRangeException>(
                () => LegendLevelCurve.XpForLevel(Range.Maximum, Curve, Range))
            .Message.ShouldContain("no next level to price");
    }

    /// <summary>A negative lifetime total is a corrupt row rather than a level to answer.</summary>
    [Fact]
    public void A_negative_lifetime_total_is_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => LegendLevelCurve.LevelFor(-1, Curve, Range));
    }

    /// <summary>
    /// 🔒 The exponent is read from the document rather than assumed: a steeper curve costs more.
    /// </summary>
    /// <remarks>
    /// The discriminating half is that the shipped exponent is 1.05 and the obvious literal anybody
    /// would write is 1 — so a curve that ignored the tunable and used <c>coefficient * level</c>
    /// would still pass every monotonicity case above. Driving it with a content set whose exponent
    /// is NOT the shipped one is what proves the read.
    /// </remarks>
    [Fact]
    public void The_exponent_is_read_from_the_document()
    {
        var steeper = LegendCurveTuning.Read(
            ProgressionDocuments.With(legendXpExponent: ContentValue.Number(1.15m)));

        LegendLevelCurve.CumulativeXpTo(200, steeper, Range)
            .ShouldBeGreaterThan(
                LegendLevelCurve.CumulativeXpTo(200, Curve, Range) * 1.5d,
                "07 §1.1: raising the exponent to 1.15 roughly doubles the total.");
    }
}
