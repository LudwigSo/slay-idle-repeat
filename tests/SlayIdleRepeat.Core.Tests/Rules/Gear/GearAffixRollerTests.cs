using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Gear;
using SlayIdleRepeat.Core.Tests.Content;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Gear;

/// <summary>
/// The affix draw: without replacement, one draw for the affix and one for its value, and a refusal
/// rather than a quiet top-down when the pool cannot fill the band.
/// </summary>
/// <remarks>
/// The roller takes the eligible pool rather than working it out. Both restrictions — the slot
/// restriction that keeps lifesteal off boots and the rarity floor that keeps the reroll charge off
/// the lower bands — are applied where the pool is read, so a roller that had to remember either
/// would eventually forget one.
/// </remarks>
public sealed class GearAffixRollerTests
{
    /// <summary>An arbitrary but fixed seed. Nothing here depends on which draw it produces.</summary>
    private const ulong Seed = 0x5A1D_5EED_0AFFUL;

    /// <summary>A band that rolls no affixes draws nothing and consumes no draw index.</summary>
    /// <remarks>
    /// The bottom band rolls none, and it is the commonest drop in the game. A roller that consumed a
    /// draw for a count of zero would shift every later draw on the stream by one per C item.
    /// </remarks>
    [Fact]
    public void A_count_of_zero_rolls_nothing_and_consumes_no_draw_index()
    {
        var draws = Rng();

        GearAffixRoller.Roll(Pool(), 0, draws).ShouldBeEmpty();
        draws.Position.ShouldBe(0UL);
    }

    /// <summary>Each affix costs two draw indices: one for which, one for how much.</summary>
    /// <remarks>
    /// So the stream position after a roll is a function of the count alone, and a replay lands in
    /// the same place whatever the pool happened to contain.
    /// </remarks>
    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(4, 8)]
    public void Each_affix_costs_one_draw_for_the_affix_and_one_for_its_value(int count, ulong consumed)
    {
        var draws = Rng();

        GearAffixRoller.Roll(Pool(), count, draws).Count.ShouldBe(count);
        draws.Position.ShouldBe(consumed);
    }

    /// <summary>The pool is drawn without replacement, so no affix repeats on one item.</summary>
    /// <remarks>
    /// Over a sweep of seeds rather than one: a roller that drew with replacement would produce a
    /// repeat only some of the time, and a single seed that happened to miss it would prove nothing.
    /// An item carrying the same affix twice would double one stat and read to a player as a single
    /// unusually strong roll.
    /// </remarks>
    [Fact]
    public void The_pool_is_drawn_without_replacement()
    {
        for (var seed = 1UL; seed <= 64UL; seed++)
        {
            var rolled = GearAffixRoller.Roll(
                Pool(), 4, DeterministicRng.OpenAt(seed, RngStreams.Drops, 0));

            rolled.Count.ShouldBe(4);
            rolled.Select(affix => affix.AffixId).ShouldBeUnique();
        }
    }

    /// <summary>Drawing the whole pool answers the whole pool, in some order.</summary>
    /// <remarks>
    /// The exhaustive case: with the count equal to the pool size, without replacement means every
    /// affix appears exactly once, which a sampler could not fake.
    /// </remarks>
    [Fact]
    public void Drawing_the_whole_pool_answers_every_affix_in_it()
    {
        GearAffixRoller.Roll(Pool(), Pool().Count, Rng())
            .Select(affix => affix.AffixId)
            .ShouldBe(Pool().Select(affix => affix.AffixId), ignoreOrder: true);
    }

    /// <summary>Every rolled value lands inside its own affix's authored range.</summary>
    [Fact]
    public void Every_rolled_value_lands_inside_its_affixes_authored_range()
    {
        var pool = Pool();
        var seen = 0;

        for (var seed = 1UL; seed <= 64UL; seed++)
        {
            foreach (var rolled in GearAffixRoller.Roll(
                         pool, 3, DeterministicRng.OpenAt(seed, RngStreams.Drops, 0)))
            {
                var authored = pool.Single(affix =>
                    string.Equals(affix.AffixId, rolled.AffixId, StringComparison.Ordinal));

                rolled.Value.ShouldBeInRange(authored.Minimum, authored.Maximum);
                seen++;
            }
        }

        seen.ShouldBe(
            192, "the range assertion above is inside a loop, and a roller answering nothing would " +
                 "satisfy it by quantifying over an empty sequence.");
    }

    /// <summary>Rolled values are rounded once, at the roll.</summary>
    /// <remarks>
    /// The record refuses an unrounded value precisely so the rounding cannot quietly move somewhere
    /// the draw is no longer visible — this is the side that shows the rounding actually happens here.
    /// </remarks>
    [Fact]
    public void Rolled_values_are_rounded_at_the_roll()
    {
        for (var seed = 1UL; seed <= 32UL; seed++)
        {
            foreach (var rolled in GearAffixRoller.Roll(
                         Pool(), 3, DeterministicRng.OpenAt(seed, RngStreams.Drops, 0)))
            {
                DeterminismRounding.IsRounded(rolled.Value).ShouldBeTrue(
                    $"'{rolled.AffixId}' rolled {rolled.Value}, which persisted state cannot carry");
            }
        }
    }

    /// <summary>A zero-width range still consumes its value draw.</summary>
    /// <remarks>
    /// So a re-tune that widens one affix's range does not shift every later affix on the same item.
    /// The shipped pool has such an affix — the reroll charge, authored at exactly one.
    /// </remarks>
    [Fact]
    public void A_zero_width_range_still_consumes_its_value_draw()
    {
        var draws = Rng();

        var rolled = GearAffixRoller.Roll(
            [new GearAffixDefinition("AFX_REROLL_CHARGE", 1.0, 1.0, [GearSlot.RING])], 1, draws);

        rolled[0].Value.ShouldBe(1.0);
        draws.Position.ShouldBe(
            2UL,
            "one draw for the affix and one for its value. Skipping the value draw on a zero-width " +
            "range would make the stream position depend on the authored range rather than the count.");
    }

    /// <summary>A pool smaller than the count is refused, never quietly topped down.</summary>
    /// <remarks>
    /// A band asking for more affixes than its slot's pool can supply is a content error, and
    /// answering with fewer would hide it behind an item that merely looks unlucky.
    /// </remarks>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(2, 3)]
    [InlineData(3, 4)]
    public void A_pool_smaller_than_the_count_is_refused(int poolSize, int count)
    {
        var draws = Rng();

        var thrown = Should.Throw<InvalidTunableException>(
            () => GearAffixRoller.Roll(Pool().Take(poolSize).ToArray(), count, draws));

        thrown.Reference.ShouldBe(DropsTuning.AffixesReference);
        thrown.Message.ShouldContain("looks unlucky", Case.Sensitive);
        draws.Position.ShouldBe(0UL, "a refusal is decided before the first draw");
    }

    /// <summary>A negative count is refused rather than read as none.</summary>
    [Fact]
    public void A_negative_count_is_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => GearAffixRoller.Roll(Pool(), -1, Rng()))
            .ParamName.ShouldBe("count");
    }

    /// <summary>Both reference arguments are required, and each refusal names its own.</summary>
    [Fact]
    public void A_null_argument_is_refused()
    {
        Should.Throw<ArgumentNullException>(() => GearAffixRoller.Roll(null!, 1, Rng()))
            .ParamName.ShouldBe("pool");
        Should.Throw<ArgumentNullException>(() => GearAffixRoller.Roll(Pool(), 1, null!))
            .ParamName.ShouldBe("draws");
    }

    /// <summary>The caller's pool is never mutated, however many affixes are drawn out of it.</summary>
    /// <remarks>
    /// The roller removes drawn affixes from its own working copy. Removing them from the caller's
    /// list instead would empty the tuning reader's answer one item at a time.
    /// </remarks>
    [Fact]
    public void The_callers_pool_is_never_mutated()
    {
        var pool = new List<GearAffixDefinition>(Pool());

        GearAffixRoller.Roll(pool, 4, Rng());

        pool.Count.ShouldBe(Pool().Count);
    }

    /// <summary>The same seed at the same position rolls the same affixes at the same values.</summary>
    [Fact]
    public void The_same_seed_at_the_same_position_rolls_the_same_affixes()
    {
        GearAffixRoller.Roll(Pool(), 3, Rng()).ShouldBe(GearAffixRoller.Roll(Pool(), 3, Rng()));
    }

    /// <summary>…and the roll is not simply a constant across seeds.</summary>
    /// <remarks>The floor under the case above, which a roller answering a fixed list would pass.</remarks>
    [Fact]
    public void The_roll_depends_on_the_draw_rather_than_being_a_constant()
    {
        Enumerable.Range(1, 32)
            .Select(seed => string.Join(
                ",",
                GearAffixRoller.Roll(
                        Pool(), 2, DeterministicRng.OpenAt((ulong)seed, RngStreams.Drops, 0))
                    .Select(affix => affix.AffixId)))
            .Distinct(StringComparer.Ordinal)
            .Count()
            .ShouldBeGreaterThan(1);
    }

    private static DeterministicRng Rng() => DeterministicRng.OpenAt(Seed, RngStreams.Drops, 0);

    /// <summary>The shipped ring pool at the top band — six affixes with genuinely different ranges.</summary>
    private static IReadOnlyList<GearAffixDefinition> Pool() =>
        DropsTuning.Read(GearDocuments.Shipped).EligibleAffixes(GearSlot.RING, Rarity.A);
}
