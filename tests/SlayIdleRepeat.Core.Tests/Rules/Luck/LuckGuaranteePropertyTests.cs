using System.Globalization;
using Shouldly;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Luck;

/// <summary>
/// <c>24</c> §11's property test: across one hundred thousand seeded sequences, no guarantee ever
/// fires <em>later</em> than its own <c>N</c>.
/// </summary>
/// <remarks>
/// The exact-<c>N</c> cases in <c>HardPityTests</c> pin the predicate; this pins the whole
/// resolution loop around it. They are different claims — a service that advanced the wrong counter,
/// reset one it should not have, or read the ladder off the wrong class would satisfy the predicate
/// on every rung and still starve a player past <c>N</c>.
/// <para>
/// Every failure message carries the offending seed. A sweep failure that cannot be reproduced is
/// worth nothing, and one hundred thousand walks is far too many to bisect by hand.
/// </para>
/// </remarks>
public sealed class LuckGuaranteePropertyTests
{
    /// <summary>
    /// A wall-clock guard against an algorithmic regression, not a performance target.
    /// </summary>
    /// <remarks>
    /// ⚠️ The figure is a <b>proxy measurement</b>, not a measurement of this corpus: the tests were
    /// written before <c>LuckService</c> had a body, so the equivalent draw-and-counter workload was
    /// measured instead — 1 621 403 weighted picks over these same two tables, each followed by the
    /// counter-map copies a resolution produces, in <b>1.28 s</b> on a Debug build of this checkout.
    /// The budget is ~15× that, which leaves room for the service's own per-draw work and for a slow
    /// CI agent without letting an accidental rescan of the ladder through.
    /// 🔴 <b>Re-measure and tighten this once the service is implemented.</b>
    /// <para>
    /// This repository has no integration tier and no nightly lane — everything in
    /// <c>Core.Tests</c> runs on every PR — so if one hundred thousand sequences stop fitting the
    /// unit tier, the answer is to say so, not to add a tier.
    /// </para>
    /// </remarks>
    private const int BudgetSeconds = 20;

    private static LuckGuaranteeCorpus Corpus => LuckGuaranteeCorpus.Instance;

    // ══════════════════════════════════════════════════════ never later than N

    /// <summary>The apex chest's SS guarantee is satisfied by the 3rd chest, in every sequence.</summary>
    [Fact]
    public void No_apex_guarantee_is_satisfied_later_than_the_third_chest()
    {
        Late(Corpus.Walks.Select(walk => (walk.Seed, walk.ApexSsAt)), LuckGuaranteeCorpus.ApexSsRung)
            .ShouldBeEmpty(
                "24 §4.2: 'CHEST_APEX — every 3rd chest: SS'. A sequence that got past the 3rd chest " +
                "without an SS is a player the founding rule says cannot exist.");
    }

    /// <summary>The premium chest's S guarantee is satisfied by the 5th chest, in every sequence.</summary>
    [Fact]
    public void No_premium_S_guarantee_is_satisfied_later_than_the_fifth_chest()
    {
        Late(Corpus.Walks.Select(walk => (walk.Seed, walk.PremiumSAt)), LuckGuaranteeCorpus.PremiumSRung)
            .ShouldBeEmpty("24 §4.2: 'CHEST_PREMIUM — every 5th chest: S or better'");
    }

    /// <summary>
    /// The premium chest's SS guarantee is satisfied by the 25th chest, in every sequence — while the
    /// 5-rung is resetting underneath it on every fifth draw at the latest.
    /// </summary>
    /// <remarks>
    /// The rung that would break first if the two counters were not independent: a service that reset
    /// the 25-counter along with the 5-counter would keep the SS guarantee permanently out of reach,
    /// and every exact-<c>N</c> case would still pass.
    /// </remarks>
    [Fact]
    public void No_premium_SS_guarantee_is_satisfied_later_than_the_twenty_fifth_chest()
    {
        Late(Corpus.Walks.Select(walk => (walk.Seed, walk.PremiumSsAt)), LuckGuaranteeCorpus.PremiumSsRung)
            .ShouldBeEmpty(
                "24 §4.2: 'CHEST_PREMIUM — every 25th: SS'. The 5-counter resets underneath this one " +
                "at least every fifth draw; 24 §4.1 says the counters run independently and " +
                "simultaneously, and this is where a shared reset would show.");
    }

    /// <summary>
    /// A sequence that ran the full <c>N</c> draws ended on the forced one — the "at exactly N" half,
    /// over the whole sweep.
    /// </summary>
    /// <remarks>
    /// For a class's <em>lowest</em> rung the converse holds too, and is asserted: nothing else can
    /// have forced that draw, so a forced first satisfaction is the rung firing. It deliberately is
    /// <b>not</b> asserted for the premium SS rung — a draw the 5-rung forced can overshoot into SS,
    /// and <c>FromPity</c> describes the resolution rather than one of its rungs, so a genuine
    /// implementation reports pity there at an ordinal that is not 25.
    /// </remarks>
    [Fact]
    public void A_sequence_that_ran_the_full_N_draws_ended_on_the_forced_draw()
    {
        Corpus.Walks
            .Where(walk => (walk.ApexSsAt == LuckGuaranteeCorpus.ApexSsRung) != walk.ApexSsForced)
            .Select(Describe)
            .ShouldBeEmpty(
                "the apex chest has one rung and its counter starts at zero, so reaching the 3rd " +
                "draw and being forced on it are the same event");

        Corpus.Walks
            .Where(walk => (walk.PremiumSAt == LuckGuaranteeCorpus.PremiumSRung) != walk.PremiumSForced)
            .Select(Describe)
            .ShouldBeEmpty(
                "the 25-rung cannot fire inside the first five draws, so the first satisfaction of " +
                "the 5-rung is forced exactly when it lands on the 5th draw");

        Corpus.Walks
            .Where(walk => walk.PremiumSsAt == LuckGuaranteeCorpus.PremiumSsRung && !walk.PremiumSsForced)
            .Select(Describe)
            .ShouldBeEmpty(
                "a sequence whose first SS arrived on the 25th premium chest arrived there because " +
                "24 §4.2's guarantee put it there — 'every 25th: SS'");
    }

    // ══════════════════════════════════════════════════════ the sweep's own shape

    /// <summary>
    /// Some sequences reach the guarantee naturally, and some are carried to it by the forced draw.
    /// </summary>
    /// <remarks>
    /// Both failure modes are silent and opposite. A service that forced every draw would satisfy
    /// every "never later than N" case above while making pity the drop rate — <c>24</c> §10 E3 caps
    /// a guarantee at 30% of grants in its class. A service that never forced would satisfy them too,
    /// on these tables, and would starve the tail the sweep exists to bound.
    /// </remarks>
    [Fact]
    public void Both_the_natural_path_and_the_forced_path_are_exercised()
    {
        Corpus.Walks.Count(walk => walk.PremiumSsForced).ShouldBeGreaterThan(
            0, "24 §4.2 authors SS at 5%, so a 25-draw window ends on the forced draw about a quarter of the time");
        Corpus.Walks.Count(walk => !walk.PremiumSsForced).ShouldBeGreaterThan(
            0, "and the other three quarters reach SS naturally — pity bounds the tail (24 §2)");

        Corpus.Walks.Count(walk => walk.ApexSsForced).ShouldBeGreaterThan(0);
        Corpus.Walks.Count(walk => !walk.ApexSsForced).ShouldBeGreaterThan(0);
    }

    /// <summary>
    /// The floor under every case above: an empty or shrunken corpus would satisfy all of them by
    /// quantifying over nothing.
    /// </summary>
    [Fact]
    public void The_sweep_covers_a_hundred_thousand_distinct_seeds()
    {
        Corpus.Walks.Count.ShouldBe(
            LuckGuaranteeCorpus.SeedCount,
            "24 §11: 'a property test asserting it never fires later than N across 100,000 seeded " +
            "sequences'");
        Corpus.Walks.Select(walk => walk.Seed).Distinct().Count().ShouldBe(
            LuckGuaranteeCorpus.SeedCount,
            "a hundred thousand copies of one sequence is one sequence");

        Corpus.Walks.ShouldContain(walk => walk.Seed == 1UL, "the first seed is in the corpus");
        Corpus.Walks.ShouldContain(
            walk => walk.Seed == (ulong)LuckGuaranteeCorpus.SeedCount, "and so is the last");
    }

    /// <summary>
    /// A named seed re-walks to the same answer. Without this, "one hundred thousand sequences" would
    /// mean "whatever this process happened to produce once".
    /// </summary>
    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(4_242UL)]
    [InlineData(99_999UL)]
    [InlineData(100_000UL)]
    public void A_named_seed_re_walks_to_the_same_answer(ulong seed)
    {
        var again = LuckGuaranteeCorpus.Walk(seed);
        var recorded = Corpus.Walks.Single(walk => walk.Seed == seed);

        Describe(again).ShouldBe(
            Describe(recorded),
            $"seed {seed.ToString(CultureInfo.InvariantCulture)} is a pure function of itself — the " +
            "service holds no state and reads no clock (24 §11).");
    }

    /// <summary>A hundred thousand sequences belong to the unit tier, and there is no other tier.</summary>
    [Fact]
    public void The_hundred_thousand_sequence_sweep_stays_inside_the_unit_tier_budget()
    {
        Corpus.Elapsed.TotalSeconds.ShouldBeLessThan(
            BudgetSeconds,
            $"building the corpus took " +
            $"{Corpus.Elapsed.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture)} s. This " +
            "repository has no integration tier and is not getting one — if the sweep stops fitting " +
            "the unit tier, the answer is to say so, not to add a tier.");
    }

    /// <summary>Every sequence whose guarantee landed outside <c>1..N</c>, seed first.</summary>
    private static IEnumerable<string> Late(IEnumerable<(ulong Seed, int At)> found, int everyNth) =>
        found
            .Where(row => row.At < 1 || row.At > everyNth)
            .Select(row =>
                $"seed {row.Seed.ToString(CultureInfo.InvariantCulture)}: satisfied at draw " +
                $"{row.At.ToString(CultureInfo.InvariantCulture)} (0 = never), N = " +
                $"{everyNth.ToString(CultureInfo.InvariantCulture)}");

    /// <summary>One walk as reproducible text — the seed first, so a failure can be re-run.</summary>
    private static string Describe(GuaranteeWalk walk) =>
        "seed " + walk.Seed.ToString(CultureInfo.InvariantCulture) +
        ": apexSS=" + Pair(walk.ApexSsAt, walk.ApexSsForced) +
        ", premiumS=" + Pair(walk.PremiumSAt, walk.PremiumSForced) +
        ", premiumSS=" + Pair(walk.PremiumSsAt, walk.PremiumSsForced);

    private static string Pair(int at, bool forced) =>
        at.ToString(CultureInfo.InvariantCulture) + (forced ? "/forced" : "/natural");
}
