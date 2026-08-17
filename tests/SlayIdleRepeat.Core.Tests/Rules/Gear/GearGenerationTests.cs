using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Gear;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Model.Gear;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Gear;
using SlayIdleRepeat.Core.Rules.Luck;
using SlayIdleRepeat.Core.Tests.Content;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Gear;

/// <summary>
/// The two in-run gear grant paths: a kill's drop, and the session floor a completed run owes.
/// </summary>
/// <remarks>
/// <b>Nothing protected is decided here.</b> The band of a drop and the size of a floor grant both
/// come back from the luck façade, which is the one place a guarantee can fire — so the cases below
/// pin that the generator <em>reports</em> the façade's decision rather than making one of its own.
/// A generator that drew its own rarity would skip the dry-streak counters, and a skipped counter is
/// invisible until a player has killed six Elites for nothing.
/// </remarks>
public sealed class GearGenerationTests
{
    /// <summary>An arbitrary but fixed seed. Nothing here depends on which item it produces.</summary>
    private const ulong Seed = 0x5A1D_5EED_0C0DUL;

    /// <summary>The drop's band is the band the façade resolved, not one the generator chose.</summary>
    /// <remarks>
    /// <para>
    /// The façade is run separately over an identical stream and the two answers compared, which is
    /// what makes this a routing claim rather than a restatement of the drop table.
    /// </para>
    /// <para>
    /// 🔴 <b>The table deals TWO bands evenly, and it has to.</b> It used to be the single-band probe
    /// the cases below still use — 100% on one band at every chapter — under which both sides of the
    /// comparison are that band whatever produced them, so a generator that ignored
    /// <c>LuckService</c> entirely and minted a rarity of its own passed. Two bands make the façade's
    /// answer seed-dependent, and the premise assertion below is what keeps it that way.
    /// </para>
    /// </remarks>
    /// <param name="lower">The lower of the two bands the probe table deals.</param>
    /// <param name="higher">The higher of them.</param>
    [Theory]
    [InlineData(Rarity.C, Rarity.B)]
    [InlineData(Rarity.B, Rarity.A)]
    [InlineData(Rarity.A, Rarity.S)]
    public void The_drops_band_is_the_one_the_facade_resolved(Rarity lower, Rarity higher)
    {
        var drops = HalfAndHalf(lower, higher);

        BandsAcrossSeeds(drops).Length.ShouldBe(
            2,
            $"the premise: at 50/50 between {lower} and {higher} the façade's answer is a draw. If " +
            "this table ever answered one band for every seed, the comparison below would hold over a " +
            "generator that never consulted the façade at all.");

        var expected = LuckService.ResolveRunDrop(
            Tuning(), DropRun(), drops, 3, RunDropTrigger.ELITE, Counters(Rarity.A, 2), Rng());

        var rolled = GearGeneration.RollRunDrop(
            new GearInstanceId("gi_0001"),
            Catalogue(),
            Tuning(),
            DropRun(),
            drops,
            3,
            RunDropTrigger.ELITE,
            Counters(Rarity.A, 2),
            Rng());

        rolled.Item.Rarity.ShouldBe(expected.Outcome);
        rolled.FromPity.ShouldBe(expected.FromPity);
        rolled.Changes.Select(change => (change.Key, change.Value)).ShouldBe(
            expected.Changes.Select(change => (change.Key, change.Value)),
            "the counter movements are the resolution's, carried through unchanged — a generator that " +
            "re-derived them would be a second place a counter can move.");
    }

    /// <summary>The item is scaled against the chapter the drop happened in.</summary>
    [Fact]
    public void The_drop_is_scaled_against_the_chapter_it_happened_in()
    {
        var rolled = RollRunDrop(chapterOrigin: 6, trigger: RunDropTrigger.NORMAL_ENEMY, priorMisses: 0);

        rolled.Item.ChapterOrigin.ShouldBe(6);
        rolled.Item.InstanceId.ShouldBe(new GearInstanceId("gi_0001"));
    }

    /// <summary>A forced Elite drop reaches at least the band the breaker guarantees.</summary>
    /// <remarks>
    /// The end-to-end shape of the guarantee: the façade floors the table and the generator mints
    /// whatever came back, so a drop that reported pity and landed below the forced band would mean
    /// the two halves had come apart.
    /// </remarks>
    [Fact]
    public void A_forced_elite_drop_reaches_at_least_the_guaranteed_band()
    {
        var rolled = RollRunDrop(chapterOrigin: 1, trigger: RunDropTrigger.ELITE, priorMisses: 5);

        rolled.FromPity.ShouldBeTrue();
        (rolled.Item.Rarity >= DropRun().EliteMercy.ForceRarityAtLeast).ShouldBeTrue(
            $"the sixth Elite kill of a dry streak is forced, and this one landed on {rolled.Item.Rarity}");
        rolled.Changes[0].Value.ShouldBe(0, "and the counter it satisfied goes back to zero");
    }

    /// <summary>An ordinary kill's drop moves no counter, and is still a whole item.</summary>
    [Fact]
    public void An_ordinary_kills_drop_moves_no_counter_and_is_still_a_whole_item()
    {
        var rolled = RollRunDrop(chapterOrigin: 1, trigger: RunDropTrigger.NORMAL_ENEMY, priorMisses: 5);

        rolled.Changes.ShouldBeEmpty();
        rolled.FromPity.ShouldBeFalse();
        rolled.Item.DefId.ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>A drop costs one draw for the band, then the mint's own.</summary>
    /// <remarks>
    /// One index for the resolution, one for the base item, one for the quality and two per affix —
    /// so a replay of the same kill lands the stream in the same place whether or not pity fired.
    /// </remarks>
    [Fact]
    public void A_drop_costs_one_draw_for_the_band_and_then_the_mints_own()
    {
        var draws = Rng();

        GearGeneration.RollRunDrop(
            new GearInstanceId("gi_0001"),
            Catalogue(),
            Tuning(),
            DropRun(),
            EveryDropIs(Rarity.A),
            1,
            RunDropTrigger.ELITE,
            PityCounters.Empty,
            draws);

        draws.Position.ShouldBe(
            7UL,
            "one for the band, one for the base item, one for the quality, and two for each of the " +
            "two affixes an A-band item rolls.");
    }

    /// <summary>The same seed and counters produce the same drop.</summary>
    [Fact]
    public void The_same_seed_and_counters_produce_the_same_drop()
    {
        var first = RollRunDrop(3, RunDropTrigger.ELITE, 2);
        var second = RollRunDrop(3, RunDropTrigger.ELITE, 2);

        second.Item.ShouldBe(first.Item);
        second.FromPity.ShouldBe(first.FromPity);
    }

    // ---------------------------------------------------------------- the session floor

    /// <summary>A qualifying run with nothing worth keeping is paid the authored band and count.</summary>
    [Fact]
    public void A_qualifying_run_is_paid_the_authored_band_and_count()
    {
        var granted = RollSessionFloor(qualified: true, itemsAtOrAboveFloor: 0, grantsAlreadyToday: 0);

        granted.Count.ShouldBe(LuckDocuments.ShippedSessionFloorGrantCount);
        granted[0].Rarity.ShouldBe(
            DropRun().SessionFloor.GrantRarity,
            "the floor grants at the band the document authors, not at the chapter's drop table — it " +
            "is a payout, not a draw.");
        granted[0].InstanceId.ShouldBe(new GearInstanceId("gi_floor_0"));
        granted[0].ChapterOrigin.ShouldBe(4);
    }

    /// <summary>A floor grant is still a real item: base, quality and affixes are all drawn.</summary>
    [Fact]
    public void A_floor_grant_is_a_fully_rolled_item()
    {
        var granted = RollSessionFloor(true, 0, 0);
        var drops = Drops();

        granted[0].Quality.ShouldBeInRange(drops.Quality.Minimum, drops.Quality.Maximum);
        granted[0].Affixes.Count.ShouldBe(drops.Band(DropRun().SessionFloor.GrantRarity).AffixCount);
        Catalogue().Definition(granted[0].Family).DefId.ShouldBe(granted[0].DefId);
    }

    /// <summary>A floor that does not fire grants nothing and draws nothing.</summary>
    /// <remarks>
    /// Three different reasons for the same answer, all of them the caller's to explain to the
    /// player — and none of them may cost a draw index, or a run that failed to qualify would shift
    /// the stream for every run after it.
    /// </remarks>
    [Theory]
    [InlineData(false, 0, 0)]
    [InlineData(true, 1, 0)]
    [InlineData(true, 0, 2)]
    public void A_floor_that_does_not_fire_grants_nothing_and_draws_nothing(
        bool qualified, int produced, int alreadyToday)
    {
        var draws = Rng();

        GearGeneration.RollSessionFloor(
                [new GearInstanceId("gi_floor_0")],
                Catalogue(),
                DropRun(),
                Drops(),
                4,
                qualified,
                produced,
                alreadyToday,
                draws)
            .ShouldBeEmpty();

        draws.Position.ShouldBe(0UL);
    }

    /// <summary>Fewer identities than the floor grants is refused rather than paid short.</summary>
    /// <remarks>
    /// An item with no id cannot be stored, equipped or salvaged, so the shortfall is refused rather
    /// than silently paying out fewer items than the floor owes.
    /// </remarks>
    [Fact]
    public void Fewer_identities_than_the_floor_grants_is_refused()
    {
        var thrown = Should.Throw<ArgumentException>(() => GearGeneration.RollSessionFloor(
            [],
            Catalogue(),
            DropRun(),
            Drops(),
            4,
            qualified: true,
            itemsAtOrAboveFloor: 0,
            grantsAlreadyToday: 0,
            Rng()));

        thrown.ParamName.ShouldBe("instanceIds");
        thrown.Message.ShouldContain("cannot be stored, equipped or salvaged", Case.Sensitive);
    }

    /// <summary>Surplus identities are left unused rather than paid out.</summary>
    /// <remarks>The other side of the shortfall: the count comes from the façade, not from the ids.</remarks>
    [Fact]
    public void Surplus_identities_are_left_unused()
    {
        GearGeneration.RollSessionFloor(
                [new GearInstanceId("gi_a"), new GearInstanceId("gi_b"), new GearInstanceId("gi_c")],
                Catalogue(),
                DropRun(),
                Drops(),
                4,
                qualified: true,
                itemsAtOrAboveFloor: 0,
                grantsAlreadyToday: 0,
                Rng())
            .Count.ShouldBe(LuckDocuments.ShippedSessionFloorGrantCount);
    }

    /// <summary>Both reference arguments the floor checks itself are required.</summary>
    [Fact]
    public void A_null_argument_is_refused()
    {
        Should.Throw<ArgumentNullException>(() => GearGeneration.RollSessionFloor(
                null!, Catalogue(), DropRun(), Drops(), 4, true, 0, 0, Rng()))
            .ParamName.ShouldBe("instanceIds");
        Should.Throw<ArgumentNullException>(() => GearGeneration.RollSessionFloor(
                [new GearInstanceId("gi_a")], Catalogue(), null!, Drops(), 4, true, 0, 0, Rng()))
            .ParamName.ShouldBe("dropRun");
        Should.Throw<ArgumentNullException>(() => GearGeneration.RollRunDrop(
                new GearInstanceId("gi_0001"),
                Catalogue(),
                null!,
                DropRun(),
                Drops(),
                1,
                RunDropTrigger.ELITE,
                PityCounters.Empty,
                Rng()))
            .ParamName.ShouldBe("tuning");
    }

    private static (GearInstance Item, bool FromPity, IReadOnlyList<PityCounterChange> Changes)
        RollRunDrop(int chapterOrigin, RunDropTrigger trigger, int priorMisses) =>
        GearGeneration.RollRunDrop(
            new GearInstanceId("gi_0001"),
            Catalogue(),
            Tuning(),
            DropRun(),
            Drops(),
            chapterOrigin,
            trigger,
            Counters(Rarity.A, priorMisses),
            Rng());

    private static IReadOnlyList<GearInstance> RollSessionFloor(
        bool qualified, int itemsAtOrAboveFloor, int grantsAlreadyToday) =>
        GearGeneration.RollSessionFloor(
            [new GearInstanceId("gi_floor_0")],
            Catalogue(),
            DropRun(),
            Drops(),
            4,
            qualified,
            itemsAtOrAboveFloor,
            grantsAlreadyToday,
            Rng());

    private static GearCatalogue Catalogue() => GearCatalogue.Read(GearDocuments.Shipped);

    private static LuckTuning Tuning() => LuckTuning.Read(LuckDocuments.Shipped);

    private static DropRunTuning DropRun() => DropRunTuning.Read(LuckDocuments.Shipped);

    private static DropsTuning Drops() => DropsTuning.Read(GearDocuments.Shipped);

    private static PityCounters Counters(Rarity forcedBand, int misses) =>
        PityCounters.Empty.With(Tuning().CounterKey(SourceClass.DROP_RUN, forcedBand), misses);

    private static DeterministicRng Rng() => DeterministicRng.OpenAt(Seed, RngStreams.Drops, 0);

    /// <summary>A drop table in which every chapter draws one band — a probe, not a balance table.</summary>
    /// <remarks>
    /// ⚠️ A single-band table makes a drop's rarity <em>independent of the draw</em>, which is what
    /// the cases about counters and draw indices want and what a case about <em>routing</em> must not
    /// have. Those use <see cref="HalfAndHalf"/>.
    /// </remarks>
    private static DropsTuning EveryDropIs(Rarity only) => DropsTuning.Read(GearDocuments.With(
        dropShares: ContentValue.Array(
        [
            GearDocuments.ShareBand(
                ContentValue.Number(1), ContentValue.Number(8), (only.ToString(), 100m)),
        ])));

    /// <summary>A drop table dealing two bands evenly at every chapter, so the band is a draw.</summary>
    /// <param name="lower">The lower band.</param>
    /// <param name="higher">The higher band.</param>
    private static DropsTuning HalfAndHalf(Rarity lower, Rarity higher) =>
        DropsTuning.Read(GearDocuments.With(
            dropShares: ContentValue.Array(
            [
                GearDocuments.ShareBand(
                    ContentValue.Number(1),
                    ContentValue.Number(8),
                    (lower.ToString(), 50m),
                    (higher.ToString(), 50m)),
            ])));

    /// <summary>
    /// The distinct bands the façade answers for a table, across a sweep of seeds — the floor that
    /// keeps a routing comparison from being satisfied by a constant.
    /// </summary>
    private static Rarity[] BandsAcrossSeeds(DropsTuning drops) =>
        Enumerable.Range(1, 32)
            .Select(seed => LuckService.ResolveRunDrop(
                    Tuning(),
                    DropRun(),
                    drops,
                    3,
                    RunDropTrigger.ELITE,
                    Counters(Rarity.A, 2),
                    DeterministicRng.OpenAt((ulong)seed, RngStreams.Drops, 0))
                .Outcome)
            .Distinct()
            .ToArray();
}
