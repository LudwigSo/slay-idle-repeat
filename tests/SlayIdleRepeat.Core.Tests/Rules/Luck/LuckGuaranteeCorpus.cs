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
/// Two classes are walked per seed: the apex chest, whose single rung at 3 is the densest guarantee
/// in the game, and the premium chest, whose two rungs at 5 and 25 run simultaneously over the same
/// draws. Both have a base rarity table <c>24</c> §4.0a authors as literals, so the natural and the
/// forced path are both exercised without inventing odds.
/// <para>
/// The two walks take disjoint slices of one stream rather than the same slice, so an accidental
/// correlation between the two classes' results cannot make one of them look protected because the
/// other was.
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

    private const string ApexSsKey = "chest.apex:SS";
    private const string PremiumSKey = "chest.premium:S";
    private const string PremiumSsKey = "chest.premium:SS";

    /// <summary>Where the premium walk starts on the stream, so the two walks never share a draw.</summary>
    private const ulong PremiumStreamOffset = 1_000;

    private static readonly Lazy<LuckGuaranteeCorpus> Lazy = new(Build);

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

        return new GuaranteeWalk(
            seed,
            apex.UpperAt,
            apex.UpperForced,
            premium.LowerAt,
            premium.LowerForced,
            premium.UpperAt,
            premium.UpperForced);
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
internal readonly record struct GuaranteeWalk(
    ulong Seed,
    int ApexSsAt,
    bool ApexSsForced,
    int PremiumSAt,
    bool PremiumSForced,
    int PremiumSsAt,
    bool PremiumSsForced)
{
    /// <summary>The ordinal recorded when a guarantee did not fire within its own <c>N</c> draws.</summary>
    internal const int Unsatisfied = 0;
}
