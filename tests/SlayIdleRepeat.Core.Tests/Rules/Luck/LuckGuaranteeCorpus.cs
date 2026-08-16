using System.Diagnostics;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Luck;
using SlayIdleRepeat.Core.Tests.Content;

namespace SlayIdleRepeat.Core.Tests.Rules.Luck;

/// <summary>
/// One hundred thousand seeded draw sequences, each walked until its guarantees are satisfied —
/// built once and shared by every case in <see cref="LuckGuaranteePropertyTests"/>.
/// </summary>
/// <remarks>
/// Two chest classes are walked per seed: the apex chest, whose single rung at 3 is the densest
/// guarantee in the game, and the premium chest, whose two rungs at 5 and 25 run simultaneously over
/// the same draws. Both have a base rarity table <c>24</c> §4.0a authors as literals, so the natural
/// and the forced path are both exercised without inventing odds.
/// <para>
/// 🔒 <b><c>DROP_RUN</c>'s two dry-streak breakers are walked beside them</b>, and they are a
/// different shape rather than two more rungs: the class authors no ladder at all, so the façade's
/// ladder entry point refuses it by name and <see cref="WalkRunDrop"/> goes through
/// <c>ResolveRunDrop</c> instead. They belong in this corpus rather than in one of their own for the
/// reason the two chests share one: the seeds, the stream, the satisfaction test and the counter
/// threading are the same claim, and a second corpus would be a second statement of them that could
/// drift.
/// </para>
/// <para>
/// All four walks take disjoint slices of one stream rather than the same slice, so an accidental
/// correlation between two classes' results cannot make one of them look protected because another
/// was.
/// </para>
/// </remarks>
internal sealed class LuckGuaranteeCorpus
{
    /// <summary><c>24</c> §11: <em>"a property test … across 100,000 seeded sequences"</em>.</summary>
    internal const int SeedCount = 100_000;

    /// <summary>The apex chest's single rung: SS every 3rd.</summary>
    internal const int ApexSsRung = LuckDocuments.ShippedChestApexSsRung;

    /// <summary>The premium chest's lower rung: S or better every 5th.</summary>
    internal const int PremiumSRung = LuckDocuments.ShippedChestPremiumSRung;

    /// <summary>The premium chest's upper rung: SS every 25th.</summary>
    internal const int PremiumSsRung = LuckDocuments.ShippedChestPremiumSsRung;

    /// <summary>
    /// `24` §4.3 D1's elite dry-streak breaker: the 6th elite kill of a streak is the forced one.
    /// </summary>
    /// <remarks>
    /// An <b>ordinal</b>, not a miss count — five misses precede it. The authored key is spelled
    /// <c>consecutiveMissesBeforeForce</c> and reads as the other thing; the reader calls it
    /// <c>ForceOnNthKill</c>, which is what the design text says.
    /// </remarks>
    internal const int EliteMercyRung = LuckDocuments.ShippedDropRunEliteMercyN;

    /// <summary>`24` §4.3 D2's boss breaker: the 4th boss kill of a streak is the forced one.</summary>
    internal const int BossMercyRung = LuckDocuments.ShippedDropRunBossMercyN;

    private const string ApexSsKey = "chest.apex:SS";
    private const string PremiumSKey = "chest.premium:S";
    private const string PremiumSsKey = "chest.premium:SS";

    /// <summary>Where the premium walk starts on the stream, so the two walks never share a draw.</summary>
    private const ulong PremiumStreamOffset = 1_000;

    /// <summary>Where the elite dry-streak walk starts, for the same reason.</summary>
    /// <remarks>
    /// Derived from the offset before it rather than written as a round number: each walk draws at
    /// most its own cap, so starting the next one a whole offset later keeps the four slices disjoint
    /// by construction. Four unrelated literals would be disjoint only by arithmetic nobody re-checks
    /// when a cap changes.
    /// </remarks>
    private const ulong EliteStreamOffset = PremiumStreamOffset * 2;

    /// <summary>And the boss one.</summary>
    private const ulong BossStreamOffset = PremiumStreamOffset * 3;

    /// <summary>
    /// The chapter the two <c>DROP_RUN</c> walks draw their band table from.
    /// </summary>
    /// <remarks>
    /// 🔒 Chosen so that <b>both</b> paths are exercised for <b>both</b> breakers, which is what
    /// <c>Both_the_natural_path_and_the_forced_path_are_exercised</c> asks of the chest walks. Unlike
    /// a chest, a run drop draws against a chapter-banded table, so the chapter <em>is</em> the odds:
    /// chapters 1–2 author A at 10 % and S at 2.7 %, where the boss breaker would force almost every
    /// sequence and prove nothing about the natural path, and chapters 7–8 author A at 44 %, where the
    /// elite breaker would almost never fire. This band sits between them.
    /// </remarks>
    private const int DropChapter = 5;

    private static readonly Lazy<LuckGuaranteeCorpus> Lazy = new(Build);

    /// <summary>
    /// The two documents a run drop reads, resolved once for the whole sweep.
    /// </summary>
    /// <remarks>
    /// 🔴 Hoisted deliberately, and measured. Reading them inside the walk is one document parse per
    /// kill — two hundred thousand of them across the corpus — which took the build from about twelve
    /// seconds to <b>52.4</b> and blew this file's own budget on the first run. That is the exact
    /// shape <c>The_hundred_thousand_sequence_sweep_stays_inside_the_unit_tier_budget</c> exists to
    /// catch, and it caught its own author.
    /// </remarks>
    private static readonly Lazy<DropRunTuning> DropRun = new(() => DropRunTuning.Read(LuckDocuments.Shipped));

    /// <summary>The chapter-banded drop table, resolved once for the same reason.</summary>
    private static readonly Lazy<DropsTuning> Drops = new(() => DropsTuning.Read(GearDocuments.Shipped));

    private LuckGuaranteeCorpus(IReadOnlyList<GuaranteeWalk> walks, TimeSpan elapsed)
    {
        Walks = walks;
        Elapsed = elapsed;
    }

    /// <summary>The corpus, built on first use and reused by every case.</summary>
    internal static LuckGuaranteeCorpus Instance => Lazy.Value;

    /// <summary>One walk per seed.</summary>
    internal IReadOnlyList<GuaranteeWalk> Walks { get; }

    /// <summary>What building the whole corpus cost.</summary>
    internal TimeSpan Elapsed { get; }

    /// <summary>Walks one seed on its own — the re-runnability check calls this directly.</summary>
    /// <param name="seed">The seed to walk.</param>
    internal static GuaranteeWalk Walk(ulong seed) => Walk(seed, LuckTuning.Read(LuckDocuments.Shipped));

    private static LuckGuaranteeCorpus Build()
    {
        var tuning = LuckTuning.Read(LuckDocuments.Shipped);
        var walks = new GuaranteeWalk[SeedCount];
        var clock = Stopwatch.StartNew();

        for (var index = 0; index < SeedCount; index++)
        {
            walks[index] = Walk((ulong)index + 1, tuning);
        }

        clock.Stop();

        return new LuckGuaranteeCorpus(walks, clock.Elapsed);
    }

    private static GuaranteeWalk Walk(ulong seed, LuckTuning tuning)
    {
        var apex = WalkClass(
            seed,
            0,
            SourceClass.CHEST_APEX,
            LuckTables.ChestApex(),
            tuning,
            ApexSsKey,
            null,
            ApexSsRung);

        var premium = WalkClass(
            seed,
            PremiumStreamOffset,
            SourceClass.CHEST_PREMIUM,
            LuckTables.ChestPremium(),
            tuning,
            PremiumSsKey,
            PremiumSKey,
            PremiumSsRung);

        var elite = WalkRunDrop(seed, EliteStreamOffset, RunDropTrigger.ELITE, tuning);
        var boss = WalkRunDrop(seed, BossStreamOffset, RunDropTrigger.BOSS, tuning);

        return new GuaranteeWalk(
            seed,
            apex.UpperAt,
            apex.UpperForced,
            premium.LowerAt,
            premium.LowerForced,
            premium.UpperAt,
            premium.UpperForced,
            elite.At,
            elite.Forced,
            boss.At,
            boss.Forced);
    }

    /// <summary>
    /// Kills one enemy kind over and over until its dry-streak breaker has been satisfied, recording
    /// the kill it first reset on and whether that resolution reported pity.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>A separate walk from <see cref="WalkClass"/>, and it has to be.</b> The façade's ladder
    /// entry point <em>refuses</em> <c>DROP_RUN</c> by name — the class authors no
    /// <c>hardPity[]</c>/<c>softPity</c> shape, its rule is a dry-streak breaker over a
    /// chapter-banded table — so the chest walk cannot be pointed at it. What is shared is the thing
    /// worth sharing: the seeds, the stream, the satisfaction test and the counter threading.
    /// </para>
    /// <para>
    /// 🔒 The counter key is asked of the tuning reader rather than spelled. A hand-composed
    /// <c>"drop.run:A"</c> reads the right counter today and a counter nobody writes the day the key
    /// is re-authored, which is the failure `24` §3's one formation point exists to prevent.
    /// </para>
    /// <para>
    /// Like the chest walk, it stops at the breaker's own <c>N</c>: "never later than <c>N</c>" is the
    /// claim, so drawing past it would be measuring something else.
    /// </para>
    /// </remarks>
    private static (int At, bool Forced) WalkRunDrop(
        ulong seed, ulong position, RunDropTrigger trigger, LuckTuning tuning)
    {
        var dropRun = DropRun.Value;
        var drops = Drops.Value;
        var breaker = trigger == RunDropTrigger.ELITE ? dropRun.EliteMercy : dropRun.BossMercy;
        var key = tuning.CounterKey(SourceClass.DROP_RUN, breaker.ForceRarityAtLeast);

        var counters = PityCounters.Empty;
        var draws = DeterministicRng.OpenAt(seed, RngStreams.Drops, position);

        for (var ordinal = 1; ordinal <= breaker.ForceOnNthKill; ordinal++)
        {
            var resolution = LuckService.ResolveRunDrop(
                tuning, dropRun, drops, DropChapter, trigger, counters, draws);

            if (Satisfied(resolution, key))
            {
                return (ordinal, resolution.FromPity);
            }

            counters = Apply(counters, resolution);
        }

        return (GuaranteeWalk.Unsatisfied, false);
    }

    /// <summary>
    /// Draws one class until its upper rung has been satisfied, recording the ordinal each tracked
    /// rung first reset at and whether that resolution reported pity.
    /// </summary>
    /// <remarks>
    /// The walk stops at the upper rung's own <c>N</c>. A guarantee that had not fired by then is
    /// recorded as <see cref="GuaranteeWalk.Unsatisfied"/> rather than searched for further: "never
    /// later than N" is the claim, so drawing past N would be measuring something else.
    /// </remarks>
    private static (int UpperAt, bool UpperForced, int LowerAt, bool LowerForced) WalkClass(
        ulong seed,
        ulong position,
        SourceClass source,
        RarityTable table,
        LuckTuning tuning,
        string upperKey,
        string? lowerKey,
        int cap)
    {
        var counters = PityCounters.Empty;
        var draws = DeterministicRng.OpenAt(seed, RngStreams.Drops, position);

        var upperAt = GuaranteeWalk.Unsatisfied;
        var upperForced = false;
        var lowerAt = GuaranteeWalk.Unsatisfied;
        var lowerForced = false;

        for (var ordinal = 1; ordinal <= cap; ordinal++)
        {
            var resolution = LuckService.Resolve(source, tuning, table, counters, draws);

            if (upperAt == GuaranteeWalk.Unsatisfied && Satisfied(resolution, upperKey))
            {
                upperAt = ordinal;
                upperForced = resolution.FromPity;
            }

            if (lowerKey is not null && lowerAt == GuaranteeWalk.Unsatisfied && Satisfied(resolution, lowerKey))
            {
                lowerAt = ordinal;
                lowerForced = resolution.FromPity;
            }

            counters = Apply(counters, resolution);

            if (upperAt != GuaranteeWalk.Unsatisfied &&
                (lowerKey is null || lowerAt != GuaranteeWalk.Unsatisfied))
            {
                break;
            }
        }

        return (upperAt, upperForced, lowerAt, lowerForced);
    }

    /// <summary>A rung was satisfied on this draw exactly when its counter was set back to unstarted.</summary>
    private static bool Satisfied(LuckResolution resolution, string key)
    {
        foreach (var change in resolution.Changes)
        {
            if (string.Equals(change.Key, key, StringComparison.Ordinal))
            {
                return change.Value == PityCounters.Unstarted;
            }
        }

        return false;
    }

    private static PityCounters Apply(PityCounters counters, LuckResolution resolution)
    {
        foreach (var change in resolution.Changes)
        {
            counters = counters.With(change.Key, change.Value);
        }

        return counters;
    }
}

/// <summary>What one seed's walk found.</summary>
/// <param name="Seed">The seed the streams were opened over.</param>
/// <param name="ApexSsAt">The draw the apex SS guarantee was first satisfied on.</param>
/// <param name="ApexSsForced">Whether that draw was the forced one.</param>
/// <param name="PremiumSAt">The draw the premium S guarantee was first satisfied on.</param>
/// <param name="PremiumSForced">Whether that draw was the forced one.</param>
/// <param name="PremiumSsAt">The draw the premium SS guarantee was first satisfied on.</param>
/// <param name="PremiumSsForced">Whether that draw was the forced one.</param>
/// <param name="EliteMercyAt">The elite kill the dry-streak breaker was first satisfied on.</param>
/// <param name="EliteMercyForced">Whether that kill was the forced one.</param>
/// <param name="BossMercyAt">The boss kill the dry-streak breaker was first satisfied on.</param>
/// <param name="BossMercyForced">Whether that kill was the forced one.</param>
internal readonly record struct GuaranteeWalk(
    ulong Seed,
    int ApexSsAt,
    bool ApexSsForced,
    int PremiumSAt,
    bool PremiumSForced,
    int PremiumSsAt,
    bool PremiumSsForced,
    int EliteMercyAt,
    bool EliteMercyForced,
    int BossMercyAt,
    bool BossMercyForced)
{
    /// <summary>The ordinal recorded when a guarantee did not fire within its own <c>N</c> draws.</summary>
    internal const int Unsatisfied = 0;
}
