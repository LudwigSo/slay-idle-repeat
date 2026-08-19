using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Luck;
using SlayIdleRepeat.Core.Tests.Content;

namespace SlayIdleRepeat.Core.Tests.Rules.Luck;

/// <summary>
/// One hundred thousand seeded draw sequences — the apex chest, the premium chest's two rungs, and
/// <c>DROP_RUN</c>'s two dry-streak breakers — each walked until its guarantees are satisfied. Built
/// once and shared by every case in <see cref="LuckGuaranteePropertyTests"/>.
/// </summary>
/// <remarks>
/// The four walks take disjoint slices of one stream, so a correlation between two classes' results
/// cannot make one look protected because another was.
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
    /// `24` §4.3 D1: the 6th elite kill of a streak is the forced one — an ordinal, so five misses
    /// precede it.
    /// </summary>
    internal const int EliteMercyRung = LuckDocuments.ShippedDropRunEliteMercyN;

    /// <summary>`24` §4.3 D2: the 4th boss kill of a streak is the forced one.</summary>
    internal const int BossMercyRung = LuckDocuments.ShippedDropRunBossMercyN;

    private const string ApexSsKey = "chest.apex:SS";
    private const string PremiumSKey = "chest.premium:S";
    private const string PremiumSsKey = "chest.premium:SS";

    /// <summary>
    /// Each walk draws at most its own cap, so spacing the four starts a whole offset apart keeps
    /// the slices disjoint by construction.
    /// </summary>
    private const ulong PremiumStreamOffset = 1_000;

    private const ulong EliteStreamOffset = PremiumStreamOffset * 2;

    private const ulong BossStreamOffset = PremiumStreamOffset * 3;

    /// <summary>
    /// The chapter the two <c>DROP_RUN</c> walks draw from. A run drop's odds are its chapter band:
    /// chapters 1–2 would force the boss breaker on almost every sequence, chapters 7–8 would almost
    /// never force the elite one. Band 5 exercises the natural and the forced path for both.
    /// </summary>
    private const int DropChapter = 5;

    private static readonly Lazy<LuckGuaranteeCorpus> Lazy = new(Build);

    // Hoisted: reading the two documents per kill is two hundred thousand parses across the sweep.
    private static readonly Lazy<DropRunTuning> DropRun = new(() => DropRunTuning.Read(LuckDocuments.Shipped));

    private static readonly Lazy<DropsTuning> Drops = new(() => DropsTuning.Read(GearDocuments.Shipped));

    private LuckGuaranteeCorpus(IReadOnlyList<GuaranteeWalk> walks)
    {
        Walks = walks;
    }

    /// <summary>The corpus, built on first use and reused by every case.</summary>
    internal static LuckGuaranteeCorpus Instance => Lazy.Value;

    /// <summary>One walk per seed.</summary>
    internal IReadOnlyList<GuaranteeWalk> Walks { get; }

    /// <summary>Walks one seed on its own — the re-runnability check calls this directly.</summary>
    internal static GuaranteeWalk Walk(ulong seed) => Walk(seed, LuckTuning.Read(LuckDocuments.Shipped));

    private static LuckGuaranteeCorpus Build()
    {
        var tuning = LuckTuning.Read(LuckDocuments.Shipped);
        var walks = new GuaranteeWalk[SeedCount];

        for (var index = 0; index < SeedCount; index++)
        {
            walks[index] = Walk((ulong)index + 1, tuning);
        }

        return new LuckGuaranteeCorpus(walks);
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
    /// Kills one enemy kind until its dry-streak breaker has been satisfied. A separate walk from
    /// <see cref="WalkClass"/> because the ladder entry point refuses <c>DROP_RUN</c> by name —
    /// its rule is a breaker over a chapter-banded table, not a <c>hardPity[]</c> ladder.
    /// </summary>
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
    /// rung first reset at and whether that resolution reported pity. The walk stops at the upper
    /// rung's own <c>N</c>: "never later than N" is the claim, so drawing past N would be measuring
    /// something else.
    /// </summary>
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

/// <summary>What one seed's walk found: the draw each guarantee was first satisfied on, and
/// whether that draw was the forced one.</summary>
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
