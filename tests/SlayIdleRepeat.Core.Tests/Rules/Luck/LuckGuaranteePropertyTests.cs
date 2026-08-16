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
    /// Measured against the real implementation on a Debug build of this checkout, by lowering this
    /// constant until the assertion reported the corpus's own elapsed time: <b>5.25 / 5.36 / 5.68 s</b>
    /// with this case filtered to run alone, and <b>11.91 / 12.60 s</b> when the whole
    /// <c>Core.Tests</c> suite runs and xunit's collection parallelism contends for the same cores.
    /// The second figure is the one that matters, because that is how the sweep actually runs.
    /// <para>
    /// 🔒 <b>Re-measured when <c>DROP_RUN</c>'s two dry-streak walks joined the corpus</b>, rather
    /// than carried over: <b>6.19 s</b> alone and <b>14.13 s</b> contended. Ten more resolutions per
    /// seed cost about a second isolated and about a second and a half contended, which leaves this
    /// budget at ~3.2× the contended figure — the same framing it had before, not a margin quietly
    /// eaten. 🔴 The first measurement was <b>52.4 s</b>, and the cause was the new walk reading its
    /// two tuning documents per kill instead of once for the sweep. This case caught it on the run
    /// that introduced it, which is exactly what it is for.
    /// </para>
    /// <para>
    /// ⚠️ <b>The budget was raised rather than tightened, and the earlier number was the reason.</b>
    /// The 20 s it replaces was extrapolated from a proxy workload measured before the service had a
    /// body — 1.28 s for 1 621 403 weighted picks — and described itself as ~15× headroom. Against
    /// the real 12.6 s that was 1.6×, so a CI agent under twice this machine's speed would have gone
    /// red on a sweep that had not regressed at all. This is ~3.5× the contended figure and ~8× the
    /// isolated one, matching <c>DslDeterminismBaselineTests</c>' ~10×-of-measured shape, and it
    /// still catches any regression worse than about 3.5× — far below what an accidental per-draw
    /// re-read of the tuning or rescan of the ladder would cost.
    /// </para>
    /// <para>
    /// This repository has no integration tier and no nightly lane — everything in
    /// <c>Core.Tests</c> runs on every PR — so if one hundred thousand sequences stop fitting the
    /// unit tier, the answer is to say so, not to add a tier.
    /// </para>
    /// </remarks>
    private const int BudgetSeconds = 45;

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

    // ══════════════════════════════════════════════════════ DROP_RUN's two breakers

    /// <summary>
    /// 🔒 <c>24</c> §4.3 D1 — an elite dry streak is broken by the 6th elite kill, in every sequence.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>The half of `24` §11 that <c>DROP_RUN</c> did not have.</b> Its exact-<c>N</c> cases are
    /// present and discriminating — <c>RunDropResolutionTests</c> pins the 6th kill against the 5th
    /// and the 4th against the 3rd, and <c>HardPityTests</c> drives the predicate off the authored
    /// ordinals — but `24` §11 asks for a case per rule <em>and</em> a property test per rule, and
    /// only the chest ladders had the second. They catch different defects: the exact-<c>N</c> cases
    /// pin the <em>predicate</em>, this pins the <em>loop around it</em>. A resolution that advanced
    /// the counter on the wrong outcome, reset one it should not have, or read the elite breaker for a
    /// boss kill would satisfy the predicate on every rung and still starve a player past <c>N</c>.
    /// </para>
    /// <para>
    /// ⚠️ <c>N</c> is an ordinal: the 6th kill is the forced one, so five misses precede it.
    /// </para>
    /// </remarks>
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

    /// <summary>🔒 <c>24</c> §4.3 D2 — a boss dry streak is broken by the 4th boss kill, in every sequence.</summary>
    /// <remarks>
    /// The second breaker, and not a restatement of the first: it counts a miss below a different
    /// band, forces a different band, and — because `24` §3 scopes the counter by the band it
    /// guarantees — addresses a <em>different counter</em> over the same class. A resolution that
    /// formed one key for both would keep this rung permanently out of reach while the elite case
    /// above stayed green, which is the same shape as the premium chest's two simultaneous rungs.
    /// </remarks>
    [Fact]
    public void No_boss_dry_streak_is_broken_later_than_the_fourth_boss_kill()
    {
        Late(
            Corpus.Walks.Select(walk => (walk.Seed, walk.BossMercyAt)),
            LuckGuaranteeCorpus.BossMercyRung)
            .ShouldBeEmpty("24 §4.3 D2: 'on the 4th boss kill without an S or better, force one'");
    }

    /// <summary>
    /// A streak that ran the full <c>N</c> kills was broken by the forced one — the "at exactly N"
    /// half, over the whole sweep, for both breakers.
    /// </summary>
    /// <remarks>
    /// A biconditional, on the apex chest's argument: each breaker is the only rule that can force a
    /// run drop of its own band, and its counter starts unstarted, so reaching the <c>N</c>th kill and
    /// being forced on it are the same event. That is a stronger claim than "never later than
    /// <c>N</c>" and it is the one that catches a breaker firing <em>early</em> — which "never later"
    /// cannot see at all, and which would quietly make pity the drop rate that `24` §10 E3 caps.
    /// </remarks>
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
    /// Both breakers reach their band naturally in some sequences and are carried to it by the forced
    /// kill in others.
    /// </summary>
    /// <remarks>
    /// The floor that stops the three cases above from being satisfied by a degenerate
    /// implementation, and it is sharper here than for a chest: a run drop draws against a
    /// chapter-banded table, so a corpus walked on the wrong chapter would push either breaker to one
    /// extreme — every sequence forced, or none — and every "never later than N" case would still
    /// pass. See <c>LuckGuaranteeCorpus.DropChapter</c>.
    /// </remarks>
    [Fact]
    public void Both_dry_streak_breakers_are_reached_naturally_and_by_force()
    {
        Corpus.Walks.Count(walk => walk.EliteMercyForced).ShouldBeGreaterThan(
            0, "some elite streaks run the full six kills and are broken by 24 §4.3 D1");
        Corpus.Walks.Count(walk => !walk.EliteMercyForced).ShouldBeGreaterThan(
            0, "and some roll an A or better before the sixth, which is what the breaker bounds");

        Corpus.Walks.Count(walk => walk.BossMercyForced).ShouldBeGreaterThan(
            0, "the same, over 24 §4.3 D2's four boss kills");
        Corpus.Walks.Count(walk => !walk.BossMercyForced).ShouldBeGreaterThan(0);
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
            "building the corpus took " +
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
        ", premiumSS=" + Pair(walk.PremiumSsAt, walk.PremiumSsForced) +
        ", elite=" + Pair(walk.EliteMercyAt, walk.EliteMercyForced) +
        ", boss=" + Pair(walk.BossMercyAt, walk.BossMercyForced);

    private static string Pair(int at, bool forced) =>
        at.ToString(CultureInfo.InvariantCulture) + (forced ? "/forced" : "/natural");
}
