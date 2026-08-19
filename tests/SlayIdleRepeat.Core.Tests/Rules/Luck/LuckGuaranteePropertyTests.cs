using System.Globalization;
using Shouldly;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Luck;

/// <summary>
/// <c>24</c> §11's property test: across one hundred thousand seeded sequences, no guarantee ever
/// fires <em>later</em> than its own <c>N</c>.
/// </summary>
/// <remarks>
/// The exact-<c>N</c> cases pin the predicate; this pins the resolution loop around it — a service
/// that advanced the wrong counter, reset one it should not have, or read the ladder off the wrong
/// class would satisfy the predicate on every rung and still starve a player past <c>N</c>. Every
/// failure message carries the offending seed, so a sweep failure is reproducible.
/// </remarks>
public sealed class LuckGuaranteePropertyTests
{
    private static LuckGuaranteeCorpus Corpus => LuckGuaranteeCorpus.Instance;

    // ══════════════════════════════════════════════════════ never later than N

    [Fact]
    public void No_apex_guarantee_is_satisfied_later_than_the_third_chest()
    {
        Late(Corpus.Walks.Select(walk => (walk.Seed, walk.ApexSsAt)), LuckGuaranteeCorpus.ApexSsRung)
            .ShouldBeEmpty(
                "24 §4.2: 'CHEST_APEX — every 3rd chest: SS'. A sequence that got past the 3rd chest " +
                "without an SS is a player the founding rule says cannot exist.");
    }

    [Fact]
    public void No_premium_S_guarantee_is_satisfied_later_than_the_fifth_chest()
    {
        Late(Corpus.Walks.Select(walk => (walk.Seed, walk.PremiumSAt)), LuckGuaranteeCorpus.PremiumSRung)
            .ShouldBeEmpty("24 §4.2: 'CHEST_PREMIUM — every 5th chest: S or better'");
    }

    /// <summary>
    /// The rung that would break first if the two premium counters were not independent: a service
    /// that reset the 25-counter along with the 5-counter would keep the SS guarantee permanently out
    /// of reach while every exact-<c>N</c> case still passed.
    /// </summary>
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
    /// over the whole sweep. Not asserted as a biconditional for the premium SS rung: a draw the
    /// 5-rung forced can overshoot into SS, so a genuine implementation reports pity there at an
    /// ordinal that is not 25.
    /// </summary>
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

    // ══════════════════════════════════════════════════════ DROP_RUN's two breakers

    [Fact]
    public void No_elite_dry_streak_is_broken_later_than_the_sixth_elite_kill()
    {
        Late(
            Corpus.Walks.Select(walk => (walk.Seed, walk.EliteMercyAt)),
            LuckGuaranteeCorpus.EliteMercyRung)
            .ShouldBeEmpty(
                "24 §4.3 D1: 'on the 6th elite kill without an A or better, force one'. A sequence " +
                "that got past the 6th elite without one is a player the rule says cannot exist.");
    }

    /// <summary>
    /// ⚠️ The two breakers are walked over separate counters on separate stream slices, so a
    /// resolution that collapsed both keys into one would leave both cases green. Key distinctness is
    /// pinned where the counters interleave — <c>RunDropResolutionTests</c>; this is the ordinal only.
    /// </summary>
    [Fact]
    public void No_boss_dry_streak_is_broken_later_than_the_fourth_boss_kill()
    {
        Late(
            Corpus.Walks.Select(walk => (walk.Seed, walk.BossMercyAt)),
            LuckGuaranteeCorpus.BossMercyRung)
            .ShouldBeEmpty("24 §4.3 D2: 'on the 4th boss kill without an S or better, force one'");
    }

    /// <summary>
    /// A biconditional, unlike the chest case: each breaker is the only rule that can force a run
    /// drop of its own band, so this also catches a breaker firing <em>early</em> — which "never
    /// later" cannot see, and which would quietly make pity the drop rate `24` §10 E3 caps.
    /// </summary>
    [Fact]
    public void A_dry_streak_that_ran_the_full_N_kills_was_broken_by_the_forced_one()
    {
        Corpus.Walks
            .Where(walk => (walk.EliteMercyAt == LuckGuaranteeCorpus.EliteMercyRung) != walk.EliteMercyForced)
            .Select(Describe)
            .ShouldBeEmpty(
                "the elite breaker is the only rule that forces an A on a run drop and its counter " +
                "starts unstarted, so reaching the 6th elite kill and being forced on it are the " +
                "same event");

        Corpus.Walks
            .Where(walk => (walk.BossMercyAt == LuckGuaranteeCorpus.BossMercyRung) != walk.BossMercyForced)
            .Select(Describe)
            .ShouldBeEmpty("and the same holds of the boss breaker on the 4th boss kill");
    }

    /// <summary>
    /// Both breakers reach their band naturally in some sequences and by force in others — the floor
    /// that stops the cases above being satisfied by a degenerate implementation. The bounds are what
    /// <c>DropChapter</c> actually buys (measured: 5 050 elite-forced, 65 877 boss-forced), because a
    /// <c>&gt; 0</c> floor is satisfied by every authored chapter band and would leave the chapter
    /// choice unpinned.
    /// </summary>
    [Fact]
    public void Both_dry_streak_breakers_are_reached_naturally_and_by_force()
    {
        Forced(walk => walk.EliteMercyForced).ShouldBeInRange(
            1_000, 20_000,
            "24 §4.3 D1's elite breaker forces about 5% of sequences at DropChapter. Outside this " +
            "band the corpus is walking a different chapter — at 1–2 it forces about half, at 7–8 " +
            "almost never — and the natural and forced paths stop being exercised together.");

        Natural(walk => walk.EliteMercyAt, walk => walk.EliteMercyForced).ShouldBeGreaterThan(
            80_000, "and the rest roll an A or better before the sixth elite, which is the tail the " +
            "breaker bounds.");

        Forced(walk => walk.BossMercyForced).ShouldBeInRange(
            55_000, 80_000,
            "24 §4.3 D2's boss breaker forces about two thirds of sequences at DropChapter — S is a " +
            "rare band, so the boss streak runs its four kills far more often than the elite one " +
            "runs its six. Every other authored chapter band sits outside this range.");

        Natural(walk => walk.BossMercyAt, walk => walk.BossMercyForced).ShouldBeGreaterThan(
            10_000, "and the rest roll an S or better inside four boss kills.");
    }

    /// <summary>How many sequences ended on the forced draw.</summary>
    private static int Forced(Func<GuaranteeWalk, bool> forced) => Corpus.Walks.Count(forced);

    /// <summary>
    /// How many sequences reached the band on their own — satisfied, and not by the forced draw. A
    /// bare "not forced" would count a walk whose guarantee never fired as one that arrived
    /// naturally.
    /// </summary>
    private static int Natural(Func<GuaranteeWalk, int> at, Func<GuaranteeWalk, bool> forced) =>
        Corpus.Walks.Count(walk => at(walk) != GuaranteeWalk.Unsatisfied && !forced(walk));

    // ══════════════════════════════════════════════════════ the sweep's own shape

    /// <summary>
    /// Both silent failure modes are opposite: a service that forced every draw would satisfy every
    /// "never later than N" case while making pity the drop rate (24 §10 E3 caps a guarantee at 30%
    /// of grants in its class); one that never forced would satisfy them too, on these tables. The
    /// bounds are measured over the shipped tables (34 269 premium-SS forced, 24 956 apex-SS forced)
    /// so that a near-always-forced or near-never-forced service cannot pass.
    /// </summary>
    [Fact]
    public void Both_the_natural_path_and_the_forced_path_are_exercised()
    {
        Forced(walk => walk.PremiumSsForced).ShouldBeInRange(
            30_000, 40_000,
            "24 §4.2 authors SS at 5% and the rung at 25, so about a third of the windows end on the " +
            "forced draw. Outside this band the share or the rung has moved, and the two paths stop " +
            "being exercised together.");

        Natural(walk => walk.PremiumSsAt, walk => walk.PremiumSsForced).ShouldBeInRange(
            60_000, 70_000,
            "and the other two thirds reach SS naturally inside the window — pity bounds the tail " +
            "(24 §2) rather than being the drop rate (24 §10 E3).");

        Forced(walk => walk.ApexSsForced).ShouldBeInRange(
            20_000, 30_000,
            "24 §4.2's apex rung is 3, so a quarter of the sequences run all three draws and are " +
            "carried onto the SS by the guarantee.");

        Natural(walk => walk.ApexSsAt, walk => walk.ApexSsForced).ShouldBeInRange(
            70_000, 80_000,
            "and three quarters draw an SS inside the first three apex chests on their own.");
    }

    /// <summary>
    /// The floor under every case above: a shrunken corpus would satisfy all of them by quantifying
    /// over nothing.
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
        ", premiumSS=" + Pair(walk.PremiumSsAt, walk.PremiumSsForced) +
        ", elite=" + Pair(walk.EliteMercyAt, walk.EliteMercyForced) +
        ", boss=" + Pair(walk.BossMercyAt, walk.BossMercyForced);

    private static string Pair(int at, bool forced) =>
        at.ToString(CultureInfo.InvariantCulture) + (forced ? "/forced" : "/natural");
}
