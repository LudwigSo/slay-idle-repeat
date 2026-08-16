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
    /// 🔒 The <b>sixth</b> Elite kill of a streak is the forced one, so five prior misses fire the
    /// guarantee and four do not.
    /// </summary>
    /// <remarks>
    /// The authored key is spelled <c>consecutiveMissesBeforeForce</c>, which reads as "misses
    /// tolerated before the force" and would put the guarantee on the seventh kill. The design text is
    /// unambiguous — count consecutive Elite kills whose drop was below A, and on the sixth force A or
    /// better — so five misses precede the forced draw. A case that only checked "forced eventually"
    /// would pass under either reading.
    /// </remarks>
    [Theory]
    [InlineData(4, false)]
    [InlineData(5, true)]
    [InlineData(6, true)]
    public void The_sixth_elite_kill_is_the_forced_one_so_five_prior_misses_fire_it(
        int priorMisses, bool forced)
    {
        var resolution = Resolve(RunDropTrigger.ELITE, Counters(Rarity.A, priorMisses));

        resolution.FromPity.ShouldBe(
            forced,
            $"{priorMisses} consecutive Elite kills below A have happened, so this is Elite kill " +
            $"number {priorMisses + 1} of the streak. The sixth is the forced one.");

        if (forced)
        {
            (resolution.Outcome >= Rarity.A).ShouldBeTrue(
                $"a forced Elite drop must reach A or better, and this one was {resolution.Outcome}");
        }
    }

    /// <summary>
    /// …and the same off-by-one stated so that only one reading can even complete: at five prior
    /// misses the table is floored at A, so a table with no weight at A or above has nothing to draw.
    /// </summary>
    /// <remarks>
    /// The discriminating case behind the theory above. <c>FromPity</c> is a flag a wrong
    /// implementation could still report correctly while flooring on the wrong draw; refusing to draw
    /// at all is not. The refusal happens before the draw, so the stream is left where it stood.
    /// </remarks>
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

    /// <summary>The boss breaker forces the <b>fourth</b> boss kill, so three prior misses fire it.</summary>
    /// <remarks>Its own ordinal and its own band — the two breakers are read separately.</remarks>
    [Theory]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    public void The_fourth_boss_kill_is_the_forced_one_so_three_prior_misses_fire_it(
        int priorMisses, bool forced)
    {
        var resolution = Resolve(RunDropTrigger.BOSS, Counters(Rarity.S, priorMisses));

        resolution.FromPity.ShouldBe(forced);

        if (forced)
        {
            (resolution.Outcome >= Rarity.S).ShouldBeTrue(
                "the boss breaker forces S or better, not A or better, and this one was " +
                resolution.Outcome.ToString());
        }
    }

    /// <summary>…stated so that only one reading can complete, exactly as the elite case is.</summary>
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

    /// <summary>A drop below the miss band advances the counter by exactly one.</summary>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(3, 4)]
    public void A_drop_below_the_miss_band_advances_the_counter_by_one(int before, int after)
    {
        var resolution = LuckService.ResolveRunDrop(
            Tuning(),
            DropRun(),
            EveryDropIs(Rarity.C),
            1,
            RunDropTrigger.ELITE,
            Counters(Rarity.A, before),
            Rng());

        resolution.Outcome.ShouldBe(Rarity.C);
        resolution.Changes[0].Value.ShouldBe(
            after,
            "a counter counts misses since its own last reset, so a miss adds exactly one — a " +
            "resolution that added the streak length back would reach the guarantee early.");
    }

    /// <summary>A drop that reaches the miss band resets the counter without claiming pity.</summary>
    /// <remarks>
    /// The overshoot rule: a natural draw that meets the guarantee resets it exactly as a forced one
    /// does, and <c>FromPity</c> stays false because the two are different events to the player.
    /// </remarks>
    [Theory]
    [InlineData(Rarity.A)]
    [InlineData(Rarity.S)]
    [InlineData(Rarity.SS)]
    public void A_natural_drop_at_or_above_the_miss_band_resets_the_counter_without_claiming_pity(
        Rarity outcome)
    {
        var resolution = LuckService.ResolveRunDrop(
            Tuning(),
            DropRun(),
            EveryDropIs(outcome),
            1,
            RunDropTrigger.ELITE,
            Counters(Rarity.A, 4),
            Rng());

        resolution.Outcome.ShouldBe(outcome);
        resolution.FromPity.ShouldBeFalse();
        resolution.Changes[0].Value.ShouldBe(
            0,
            "the player is never punished for good luck by having a guarantee taken away later. A " +
            "counter that kept climbing through an overshoot would fire a redundant guarantee a few " +
            "kills later.");
    }

    /// <summary>A drop the breaker forced resets the counter too.</summary>
    [Fact]
    public void A_forced_drop_resets_the_counter_it_satisfied()
    {
        var resolution = Resolve(RunDropTrigger.ELITE, Counters(Rarity.A, 5));

        resolution.FromPity.ShouldBeTrue();
        resolution.Changes[0].Value.ShouldBe(0);
    }

    /// <summary>A drop still below the miss band after a forced draw would keep advancing.</summary>
    /// <remarks>
    /// The negative control on the reset: the counter follows the <em>outcome</em>, not the pity flag.
    /// A boss drop of A is a miss against the boss breaker's S band even though it is a fine drop.
    /// </remarks>
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

    /// <summary>A qualifying run that produced nothing worth keeping is owed the authored count.</summary>
    [Fact]
    public void A_qualifying_run_that_produced_nothing_worth_keeping_is_owed_the_authored_count()
    {
        LuckService.SessionFloorGrant(
                DropRun(), qualified: true, itemsAtOrAboveFloor: 0, grantsAlreadyToday: 0)
            .ShouldBe(LuckDocuments.ShippedSessionFloorGrantCount);
    }

    /// <summary>A run that did not end the required way is owed nothing.</summary>
    [Fact]
    public void A_run_that_did_not_end_the_required_way_is_owed_nothing()
    {
        LuckService.SessionFloorGrant(
                DropRun(), qualified: false, itemsAtOrAboveFloor: 0, grantsAlreadyToday: 0)
            .ShouldBe(
                0,
                "the floor is paid on a Victory or a stage-3 death. Paying it on an abandon would " +
                "make quitting at stage 1 the cheapest way to farm the guarantee.");
    }

    /// <summary>The qualifier is read from the document rather than assumed.</summary>
    /// <remarks>
    /// The negative control: with the requirement authored off, an unqualified run is owed the floor.
    /// A rule that hard-coded the requirement would pass the case above and ignore the data edit.
    /// </remarks>
    [Fact]
    public void A_floor_that_requires_no_qualifying_end_pays_an_unqualified_run()
    {
        var unconditional = DropRunTuning.Read(LuckDocuments.LuckOnly(
            dropRun: LuckDocuments.DropRun(sessionFloor: LuckDocuments.Floor(
                ContentValue.Text(LuckDocuments.ShippedSessionFloorGrantRarity),
                ContentValue.Number(LuckDocuments.ShippedSessionFloorGrantCount),
                ContentValue.Number(LuckDocuments.ShippedSessionFloorMaxPerDay),
                ContentValue.False))));

        LuckService.SessionFloorGrant(
                unconditional, qualified: false, itemsAtOrAboveFloor: 0, grantsAlreadyToday: 0)
            .ShouldBe(LuckDocuments.ShippedSessionFloorGrantCount);
    }

    /// <summary>A run that already produced an item at or above the band is owed nothing.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    public void A_run_that_already_produced_an_item_at_the_band_is_owed_nothing(int produced)
    {
        LuckService.SessionFloorGrant(
                DropRun(), qualified: true, itemsAtOrAboveFloor: produced, grantsAlreadyToday: 0)
            .ShouldBe(0, "the floor adds an item to a run that produced none, not one to every run");
    }

    /// <summary>The day's allowance is spent at exactly the authored maximum, not one grant later.</summary>
    [Theory]
    [InlineData(0, LuckDocuments.ShippedSessionFloorGrantCount)]
    [InlineData(1, LuckDocuments.ShippedSessionFloorGrantCount)]
    [InlineData(2, 0)]
    [InlineData(3, 0)]
    public void The_days_allowance_is_spent_at_exactly_the_authored_maximum(int alreadyToday, int owed)
    {
        LuckService.SessionFloorGrant(
                DropRun(), qualified: true, itemsAtOrAboveFloor: 0, grantsAlreadyToday: alreadyToday)
            .ShouldBe(
                owed,
                $"the floor fires at most {LuckDocuments.ShippedSessionFloorMaxPerDay} times per day. " +
                "The boundary is the whole rule: paying a third grant is a daily cap that does not cap.");
    }

    /// <summary>A negative count is refused rather than read as none.</summary>
    [Fact]
    public void A_negative_count_is_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => LuckService.SessionFloorGrant(
                DropRun(), qualified: true, itemsAtOrAboveFloor: -1, grantsAlreadyToday: 0))
            .ParamName.ShouldBe("itemsAtOrAboveFloor");
        Should.Throw<ArgumentOutOfRangeException>(() => LuckService.SessionFloorGrant(
                DropRun(), qualified: true, itemsAtOrAboveFloor: 0, grantsAlreadyToday: -1))
            .ParamName.ShouldBe("grantsAlreadyToday");
    }

    /// <summary>A null tuning is refused rather than dereferenced.</summary>
    [Fact]
    public void SessionFloorGrant_refuses_a_null_tuning()
    {
        Should.Throw<ArgumentNullException>(() => LuckService.SessionFloorGrant(
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
}
