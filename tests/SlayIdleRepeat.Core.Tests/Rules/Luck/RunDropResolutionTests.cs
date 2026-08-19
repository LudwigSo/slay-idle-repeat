using System.Globalization;
using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Luck;
using SlayIdleRepeat.Core.Tests.Content;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Luck;

/// <summary>
/// The in-run drop half of the luck façade: the two dry-streak breakers, the counter each of them
/// moves, and the session floor that no draw produces.
/// </summary>
/// <remarks>
/// <para>
/// Outcomes are made deterministic by the <em>table</em> rather than by a searched-for seed, on
/// <c>LuckServiceTests</c>' precedent: a case that hunted a seed producing an A would re-break every
/// time the draw derivation changed, and the claims here are about the counter ledger.
/// </para>
/// <para>
/// 🔒 No counter key is spelled anywhere in this file. The keys are authored in <c>luck.json</c> and
/// paired with the guarantee they protect by the tuning reader — one place — so a case that wrote
/// <c>"drop.run:A"</c> by hand would read the right counter today and the wrong one, silently, the
/// day the key is re-authored.
/// </para>
/// </remarks>
public sealed class RunDropResolutionTests
{
    /// <summary>An arbitrary but fixed seed. Nothing here depends on which draw it produces.</summary>
    private const ulong Seed = 0x5A1D_5EED_0403UL;

    // ---------------------------------------------------------------- the ordinary kill

    /// <summary>An ordinary kill moves no counter at all, however long the elite streak is.</summary>
    /// <remarks>
    /// The breakers count consecutive <em>Elite</em> and <em>boss</em> kills, so a stream of ordinary
    /// drops between two Elite kills leaves the elite streak exactly where it was. A resolution that
    /// advanced the elite counter on every trash mob would reach the guarantee in a corridor.
    /// </remarks>
    [Fact]
    public void An_ordinary_kill_moves_no_counter_and_never_claims_pity()
    {
        var resolution = Resolve(RunDropTrigger.NORMAL_ENEMY, Counters(Rarity.A, 500));

        resolution.Changes.ShouldBeEmpty();
        resolution.FromPity.ShouldBeFalse(
            "an ordinary kill is protected by no dry-streak breaker at all, so there is no guarantee " +
            "for it to fire — not merely one that has not fired yet.");
    }

    /// <summary>An ordinary kill still costs exactly one draw index.</summary>
    [Fact]
    public void An_ordinary_kill_still_consumes_exactly_one_draw_index()
    {
        var draws = Rng();

        LuckService.ResolveRunDrop(
            Tuning(), DropRun(), Drops(), 1, RunDropTrigger.NORMAL_ENEMY, PityCounters.Empty, draws);

        draws.Position.ShouldBe(1UL);
    }

    // ---------------------------------------------------------------- the off-by-one

    /// <summary>
    /// The sixth Elite kill's off-by-one, stated so that only one reading can even complete: at five
    /// prior misses the table is floored at A, so a table with no weight at A or above has nothing
    /// to draw. (The exact-N behaviour itself is pinned at the Apply seam in
    /// <c>RunDropGrantTests</c>; <c>FromPity</c> is a flag a wrong implementation could still report
    /// correctly while flooring on the wrong draw — refusing to draw at all is not.)
    /// </summary>
    [Fact]
    public void At_five_prior_misses_a_table_with_no_A_weight_is_refused_and_at_four_it_is_drawn()
    {
        var bottomOnly = EveryDropIs(Rarity.C);
        var drawnAtFour = Rng();
        var refusedAtFive = Rng();

        LuckService.ResolveRunDrop(
                Tuning(), DropRun(), bottomOnly, 1, RunDropTrigger.ELITE, Counters(Rarity.A, 4), drawnAtFour)
            .Outcome.ShouldBe(Rarity.C, "the fifth Elite kill of a streak is not yet the forced one");
        drawnAtFour.Position.ShouldBe(1UL);

        Should.Throw<InvalidOperationException>(() => LuckService.ResolveRunDrop(
                Tuning(), DropRun(), bottomOnly, 1, RunDropTrigger.ELITE, Counters(Rarity.A, 5), refusedAtFive))
            .Message.ShouldContain(
                "floor",
                Case.Insensitive,
                "the sixth kill floors the table at A, and this table carries no weight there. The " +
                "refusal is the proof that the floor was applied on this draw and not on the next.");
        refusedAtFive.Position.ShouldBe(
            0UL, "a refusal is decided before the draw, so it consumes no index");
    }

    /// <summary>The boss breaker's off-by-one, stated so that only one reading can complete, exactly
    /// as the elite case is — its own ordinal and its own band.</summary>
    [Fact]
    public void At_three_prior_misses_a_table_with_no_S_weight_is_refused_and_at_two_it_is_drawn()
    {
        var belowS = EveryDropIs(Rarity.A);
        var drawnAtTwo = Rng();
        var refusedAtThree = Rng();

        LuckService.ResolveRunDrop(
                Tuning(), DropRun(), belowS, 1, RunDropTrigger.BOSS, Counters(Rarity.S, 2), drawnAtTwo)
            .Outcome.ShouldBe(Rarity.A);

        Should.Throw<InvalidOperationException>(() => LuckService.ResolveRunDrop(
            Tuning(), DropRun(), belowS, 1, RunDropTrigger.BOSS, Counters(Rarity.S, 3), refusedAtThree));

        refusedAtThree.Position.ShouldBe(0UL);
    }

    // ---------------------------------------------------------------- the counter it moves

    /// <summary>The counter a breaker moves is the one the registry forms for its forced band.</summary>
    /// <remarks>
    /// Asked of the tuning reader rather than spelled here. The keys are authored data and a class
    /// can run several ladders at once, so a counter is addressed by pairing an authored key with the
    /// guarantee it protects — and there is exactly one place that pairing is made.
    /// </remarks>
    [Fact]
    public void The_elite_breaker_moves_the_counter_the_registry_forms_for_its_forced_band()
    {
        var tuning = Tuning();
        var dropRun = DropRun();

        var resolution = LuckService.ResolveRunDrop(
            tuning, dropRun, Drops(), 1, RunDropTrigger.ELITE, PityCounters.Empty, Rng());

        resolution.Changes.Count.ShouldBe(1, "one breaker protects a trigger, so one counter moves");
        resolution.Changes[0].Key.ShouldBe(
            tuning.CounterKey(SourceClass.DROP_RUN, dropRun.EliteMercy.ForceRarityAtLeast));
    }

    /// <summary>The boss breaker moves a different counter from the elite one.</summary>
    /// <remarks>
    /// The two streaks are counted separately — a boss kill must not fill the elite guarantee, and
    /// a shared key would let the cheaper kill kind fill the more expensive one's ladder.
    /// </remarks>
    [Fact]
    public void The_boss_breaker_moves_a_different_counter_from_the_elite_one()
    {
        var tuning = Tuning();
        var dropRun = DropRun();

        var elite = LuckService.ResolveRunDrop(
            tuning, dropRun, Drops(), 1, RunDropTrigger.ELITE, PityCounters.Empty, Rng());
        var boss = LuckService.ResolveRunDrop(
            tuning, dropRun, Drops(), 1, RunDropTrigger.BOSS, PityCounters.Empty, Rng());

        boss.Changes[0].Key.ShouldNotBe(elite.Changes[0].Key);
    }

    /// <summary>
    /// The counter follows the <em>outcome</em>, not the pity flag: a boss drop of A is a miss
    /// against the boss breaker's S band even though it is a fine drop. (Advance-on-miss and
    /// reset-on-hit are pinned at the Apply seam in <c>RunDropGrantTests</c>.)
    /// </summary>
    [Fact]
    public void The_counter_follows_the_outcome_rather_than_the_pity_flag()
    {
        var resolution = LuckService.ResolveRunDrop(
            Tuning(),
            DropRun(),
            EveryDropIs(Rarity.A),
            1,
            RunDropTrigger.BOSS,
            Counters(Rarity.S, 2),
            Rng());

        resolution.Outcome.ShouldBe(Rarity.A);
        resolution.Changes[0].Value.ShouldBe(
            3, "the boss breaker counts a drop below S as a miss, and an A is below S");
    }

    /// <summary>
    /// 🔒 The miss band and the forced band are read from their <b>own</b> fields. The shipped
    /// breakers pair the two identically (A/A, S/S), so a swapped reading passes every shipped-data
    /// case; the fixture pulls them apart upward (A-miss/SS-force — the only direction
    /// <c>DropRunTuning</c> permits) and both halves discriminate.
    /// </summary>
    [Fact]
    public void The_miss_band_and_the_forced_band_are_read_from_their_own_fields()
    {
        var asymmetric = AsymmetricElite();

        asymmetric.EliteMercy.BelowRarity.ShouldBe(Rarity.A, "the fixture's premise, from one side");
        asymmetric.EliteMercy.ForceRarityAtLeast.ShouldBe(
            Rarity.SS, "…and from the other. These two differing is the whole case.");

        var atTheMissBand = LuckService.ResolveRunDrop(
            Tuning(),
            asymmetric,
            EveryDropIs(Rarity.A),
            1,
            RunDropTrigger.ELITE,
            Counters(Rarity.SS, 4),
            Rng());

        atTheMissBand.Outcome.ShouldBe(Rarity.A);
        atTheMissBand.FromPity.ShouldBeFalse();
        atTheMissBand.Changes[0].Value.ShouldBe(
            0,
            "an A is not STRICTLY BELOW A, so it is not a miss and the counter resets — even though " +
            "it falls far short of the SS this breaker forces. A resolution reading the forced band " +
            "as the miss band would advance the counter to five here.");

        var refused = Rng();

        Should.Throw<InvalidOperationException>(() => LuckService.ResolveRunDrop(
                Tuning(),
                asymmetric,
                EveryDropIs(Rarity.A),
                1,
                RunDropTrigger.ELITE,
                Counters(Rarity.SS, 5),
                refused))
            .Message.ShouldContain(
                "floor",
                Case.Insensitive,
                "the sixth kill floors the table at SS, the FORCED band, and this table carries no " +
                "weight there. A resolution flooring at the miss band instead would draw an A and " +
                "report a guarantee it had not paid.");

        refused.Position.ShouldBe(0UL, "a refusal is decided before the draw");
    }

    // ---------------------------------------------------------------- the draw stream

    /// <summary>Exactly one draw index is consumed whether or not the guarantee fires.</summary>
    /// <remarks>
    /// The property the whole "forcing is flooring" reading exists for: a forced draw that
    /// short-circuited to a fixed rarity would consume none, and a resumed stream would land in a
    /// different place depending on counters that are not part of the seed.
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void A_resolution_consumes_exactly_one_draw_index_forced_or_not(int priorMisses)
    {
        var draws = Rng();

        LuckService.ResolveRunDrop(
            Tuning(), DropRun(), Drops(), 1, RunDropTrigger.ELITE, Counters(Rarity.A, priorMisses), draws);

        draws.Position.ShouldBe(1UL);
    }

    /// <summary>A resumed stream advances from where it was, one index per resolution.</summary>
    [Fact]
    public void A_resumed_stream_advances_by_one_index_per_resolution()
    {
        var draws = DeterministicRng.OpenAt(Seed, RngStreams.Drops, 17);

        LuckService.ResolveRunDrop(
            Tuning(), DropRun(), Drops(), 5, RunDropTrigger.ELITE, PityCounters.Empty, draws);
        LuckService.ResolveRunDrop(
            Tuning(), DropRun(), Drops(), 5, RunDropTrigger.ELITE, PityCounters.Empty, draws);

        draws.Position.ShouldBe(19UL, "the caller opens the stream; this service only ever continues it");
    }

    /// <summary>The same seed at the same position resolves to the same thing.</summary>
    [Fact]
    public void The_same_seed_at_the_same_position_resolves_identically()
    {
        var first = LuckService.ResolveRunDrop(
            Tuning(), DropRun(), Drops(), 5, RunDropTrigger.ELITE, Counters(Rarity.A, 2), Rng());
        var second = LuckService.ResolveRunDrop(
            Tuning(), DropRun(), Drops(), 5, RunDropTrigger.ELITE, Counters(Rarity.A, 2), Rng());

        Canonical(second).ShouldBe(
            Canonical(first),
            "the same seed and snapshot are byte-identical on client and server; a resolution that " +
            "varied would make every in-run drop unverifiable.");
    }

    /// <summary>…and the resolution is not simply a constant.</summary>
    /// <remarks>
    /// The floor under the case above, which an implementation returning a fixed rarity would pass.
    /// Sixty-four seeds rather than two, because two could legitimately coincide on a table whose
    /// commonest band is forty per cent.
    /// </remarks>
    [Fact]
    public void The_resolution_depends_on_the_draw_rather_than_being_a_constant()
    {
        Outcomes(chapter: 5, seeds: 64).Length.ShouldBeGreaterThan(1);
    }

    /// <summary>A chapter that prices the bottom band at zero never drops it.</summary>
    /// <remarks>
    /// A zero-weight row is unreachable wherever it sits in the table, which is how content disables
    /// a band. The distinct-count floor comes with it: a sweep answering one band for every seed would
    /// satisfy "never C" by never drawing anything else either.
    /// </remarks>
    [Fact]
    public void A_chapter_that_prices_the_bottom_band_at_zero_never_drops_it()
    {
        var outcomes = Outcomes(chapter: 8, seeds: 64);

        outcomes.ShouldNotContain(
            Rarity.C,
            "the last chapter band authors C at zero per cent. A zero-weight row that could still be " +
            "picked is a bug that surfaces once in ten thousand runs.");
        outcomes.Length.ShouldBeGreaterThan(1);
        outcomes.ShouldContain(Rarity.A, "A is the commonest band the chapter-8 table authors");
    }

    // ---------------------------------------------------------------- refusals

    /// <summary>Every reference argument is required, and each refusal names its own.</summary>
    /// <remarks>
    /// A single guard on the first argument would satisfy all five bare type assertions while four
    /// null dereferences went unchecked.
    /// </remarks>
    [Fact]
    public void A_null_argument_is_refused()
    {
        Should.Throw<ArgumentNullException>(() => LuckService.ResolveRunDrop(
                null!, DropRun(), Drops(), 1, RunDropTrigger.ELITE, PityCounters.Empty, Rng()))
            .ParamName.ShouldBe("tuning");
        Should.Throw<ArgumentNullException>(() => LuckService.ResolveRunDrop(
                Tuning(), null!, Drops(), 1, RunDropTrigger.ELITE, PityCounters.Empty, Rng()))
            .ParamName.ShouldBe("dropRun");
        Should.Throw<ArgumentNullException>(() => LuckService.ResolveRunDrop(
                Tuning(), DropRun(), null!, 1, RunDropTrigger.ELITE, PityCounters.Empty, Rng()))
            .ParamName.ShouldBe("drops");
        Should.Throw<ArgumentNullException>(() => LuckService.ResolveRunDrop(
                Tuning(), DropRun(), Drops(), 1, RunDropTrigger.ELITE, null!, Rng()))
            .ParamName.ShouldBe("counters");
        Should.Throw<ArgumentNullException>(() => LuckService.ResolveRunDrop(
                Tuning(), DropRun(), Drops(), 1, RunDropTrigger.ELITE, PityCounters.Empty, null!))
            .ParamName.ShouldBe("draws");
    }

    /// <summary>A chapter below one is refused before the draw.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_chapter_below_one_is_refused_before_the_draw(int chapter)
    {
        var draws = Rng();

        Should.Throw<ArgumentOutOfRangeException>(() => LuckService.ResolveRunDrop(
                Tuning(), DropRun(), Drops(), chapter, RunDropTrigger.ELITE, PityCounters.Empty, draws))
            .ParamName.ShouldBe("chapter");

        draws.Position.ShouldBe(0UL);
    }

    /// <summary>A trigger the vocabulary does not declare is refused before the draw.</summary>
    /// <remarks>
    /// Treasure tiles are deliberately absent from the vocabulary — they never drop gear — so an
    /// undeclared trigger is a caller inventing a source rather than a member somebody forgot.
    /// </remarks>
    [Theory]
    [InlineData(3)]
    [InlineData(-1)]
    public void An_undeclared_trigger_is_refused_before_the_draw(int trigger)
    {
        var draws = Rng();

        Should.Throw<ArgumentOutOfRangeException>(() => LuckService.ResolveRunDrop(
                Tuning(),
                DropRun(),
                Drops(),
                1,
                (RunDropTrigger)trigger,
                PityCounters.Empty,
                draws))
            .ParamName.ShouldBe("trigger");

        draws.Position.ShouldBe(0UL);
    }

    /// <summary>A chapter the drop table has no band for is refused before the draw.</summary>
    [Fact]
    public void A_chapter_the_drop_table_has_no_band_for_is_refused_before_the_draw()
    {
        var draws = Rng();

        Should.Throw<InvalidTunableException>(() => LuckService.ResolveRunDrop(
            Tuning(), DropRun(), Drops(), 9, RunDropTrigger.ELITE, PityCounters.Empty, draws));

        draws.Position.ShouldBe(0UL);
    }

    // ---------------------------------------------------------------- the session floor
    //
    // The floor's payable cases run at the Apply seam in Handlers/SessionFloorGrantTests. What stays
    // here is what shipped content cannot express: the data-driven qualifier, the two return units,
    // and the argument refusals.

    /// <summary>
    /// The qualifier is read from the document rather than assumed: with the requirement authored
    /// off, an unqualified run is owed the floor. A rule that hard-coded the requirement would
    /// ignore the data edit.
    /// </summary>
    [Fact]
    public void A_floor_that_requires_no_qualifying_end_pays_an_unqualified_run()
    {
        var unconditional = DropRunTuning.Read(LuckDocuments.LuckOnly(
            dropRun: LuckDocuments.DropRun(sessionFloor: LuckDocuments.Floor(
                ContentValue.Text(LuckDocuments.ShippedSessionFloorGrantRarity),
                ContentValue.Number(LuckDocuments.ShippedSessionFloorGrantCount),
                ContentValue.Number(LuckDocuments.ShippedSessionFloorMaxPerDay),
                ContentValue.False))));

        ItemsFromSessionFloor(
                unconditional, qualified: false, itemsAtOrAboveFloor: 0, grantsAlreadyToday: 0)
            .ShouldBe(LuckDocuments.ShippedSessionFloorGrantCount);
    }

    /// <summary>A negative count is refused rather than read as none.</summary>
    [Fact]
    public void A_negative_count_is_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => ItemsFromSessionFloor(
                DropRun(), qualified: true, itemsAtOrAboveFloor: -1, grantsAlreadyToday: 0))
            .ParamName.ShouldBe("itemsAtOrAboveFloor");
        Should.Throw<ArgumentOutOfRangeException>(() => ItemsFromSessionFloor(
                DropRun(), qualified: true, itemsAtOrAboveFloor: 0, grantsAlreadyToday: -1))
            .ParamName.ShouldBe("grantsAlreadyToday");
    }

    /// <summary>A null tuning is refused rather than dereferenced.</summary>
    [Fact]
    public void SessionFloorGrant_refuses_a_null_tuning()
    {
        Should.Throw<ArgumentNullException>(() => ItemsFromSessionFloor(
                null!, qualified: true, itemsAtOrAboveFloor: 0, grantsAlreadyToday: 0))
            .ParamName.ShouldBe("dropRun");
    }

    // ---------------------------------------------------------------- fixtures

    private static LuckTuning Tuning() => LuckTuning.Read(LuckDocuments.Shipped);

    private static DropRunTuning DropRun() => DropRunTuning.Read(LuckDocuments.Shipped);

    private static DropsTuning Drops() => DropsTuning.Read(GearDocuments.Shipped);

    private static DeterministicRng Rng() => DeterministicRng.OpenAt(Seed, RngStreams.Drops, 0);

    /// <summary>The counters with the breaker's own counter standing at a given streak length.</summary>
    /// <remarks>
    /// The key is asked of the registry rather than spelled: the counter a breaker reads is the one
    /// the reader forms for its forced band, and a hand-composed key would silently address another.
    /// </remarks>
    private static PityCounters Counters(Rarity forcedBand, int misses) =>
        PityCounters.Empty.With(Tuning().CounterKey(SourceClass.DROP_RUN, forcedBand), misses);

    /// <summary>
    /// The shipped block with the elite breaker's two bands pulled apart — a miss below <c>A</c>, a
    /// forced draw at <c>SS</c>.
    /// </summary>
    /// <remarks>
    /// The boss breaker is left as shipped, so the two breakers still form different counter keys and
    /// the case reads one ladder rather than two ladders that happen to share a key.
    /// </remarks>
    private static DropRunTuning AsymmetricElite() => DropRunTuning.Read(LuckDocuments.LuckOnly(
        dropRun: LuckDocuments.DropRun(eliteMercy: LuckDocuments.Breaker(
            ContentValue.Number(LuckDocuments.ShippedDropRunEliteMercyN),
            ContentValue.Text(nameof(Rarity.A)),
            ContentValue.Text(nameof(Rarity.SS))))));

    private static LuckResolution Resolve(RunDropTrigger trigger, PityCounters counters) =>
        LuckService.ResolveRunDrop(Tuning(), DropRun(), Drops(), 1, trigger, counters, Rng());

    /// <summary>
    /// A drop table in which every chapter draws one band — a probe, not a balance table.
    /// </summary>
    /// <remarks>
    /// Proving what a counter does to a known outcome needs a draw whose result is known without pity
    /// having fired, and searching a seed for one would tie the case to a hash the rules are free to
    /// change. A single-band table answers the same rarity for every seed.
    /// </remarks>
    private static DropsTuning EveryDropIs(Rarity only) => DropsTuning.Read(GearDocuments.With(
        dropShares: ContentValue.Array(
        [
            GearDocuments.ShareBand(
                ContentValue.Number(1), ContentValue.Number(8), (only.ToString(), 100m)),
        ])));

    /// <summary>The distinct outcomes a chapter's shipped table produces across a sweep of seeds.</summary>
    private static Rarity[] Outcomes(int chapter, int seeds) =>
        Enumerable.Range(1, seeds)
            .Select(seed => LuckService.ResolveRunDrop(
                    Tuning(),
                    DropRun(),
                    Drops(),
                    chapter,
                    RunDropTrigger.NORMAL_ENEMY,
                    PityCounters.Empty,
                    DeterministicRng.OpenAt((ulong)seed, RngStreams.Drops, 0))
                .Outcome)
            .Distinct()
            .ToArray();

    /// <summary>One whole resolution as canonical text: outcome, pity flag, then the changes.</summary>
    private static string Canonical(LuckResolution resolution) =>
        $"outcome={resolution.Outcome};fromPity={(resolution.FromPity ? "1" : "0")};" +
        string.Join(
            ";",
            resolution.Changes
                .OrderBy(change => change.Key, StringComparer.Ordinal)
                .Select(change =>
                    $"{change.Key}={change.Value.ToString(CultureInfo.InvariantCulture)}"));

    /// <summary>
    /// The item half of the floor's answer. The rule returns items AND grants, in different units;
    /// every case below is about how many items the floor pays, so the grant half is asserted once,
    /// on its own, rather than threaded through every assertion here.
    /// </summary>
    private static int ItemsFromSessionFloor(
        DropRunTuning dropRun, bool qualified, int itemsAtOrAboveFloor, int grantsAlreadyToday) =>
        LuckService.SessionFloorGrant(dropRun, qualified, itemsAtOrAboveFloor, grantsAlreadyToday).Items;

    /// <summary>
    /// A floor that pays spends exactly one of the day's grants, and one that does not spends none —
    /// the two units the return type keeps apart. Without this the item count and the grant count
    /// are indistinguishable while the authored grantCount is 1, and a caller feeding one back as the
    /// other would halve the day''s allowance the moment it moved off 1.
    /// </summary>
    [Fact]
    public void A_paying_floor_spends_exactly_one_of_the_days_grants()
    {
        var dropRun = DropRunTuning.Read(LuckDocuments.Shipped);

        LuckService.SessionFloorGrant(dropRun, qualified: true, 0, 0).Grants.ShouldBe(1);
        LuckService.SessionFloorGrant(dropRun, qualified: true, 1, 0).Grants.ShouldBe(0);
        LuckService.SessionFloorGrant(dropRun, qualified: false, 0, 0).Grants.ShouldBe(0);
    }
}
