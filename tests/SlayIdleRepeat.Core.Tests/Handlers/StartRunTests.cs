using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Tests.Model;
using Xunit;
using PlayerAggregate = SlayIdleRepeat.Core.Model.Player;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary>
/// START_RUN: the run-less-slice guard's one-row exemption (OpensRun), the runSeed derivation, the
/// new Run's shape, and the two ways a request is refused before anything is spent.
/// </summary>
/// <remarks>
/// Driven over the production dispatch table through GameRules.Apply wherever possible, so this
/// suite proves the real row rather than a stand-in shaped like it.
/// </remarks>
public sealed class StartRunTests
{
    /// <summary>
    /// A run-less slice whose player has already made the clear `10` §7's ladder demands before the
    /// given chapter/tier may be started.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two cases below keep their non-trivial chapter/tier pairs — chapter 3 Heroic and chapter 2
    /// Mythic — and are given the history the ladder now asks for, rather than being dropped to
    /// chapter 1 Normal. Dropping them would be the cheaper edit and the wrong one: the seed
    /// derivation takes the chapter and the tier as inputs, and a seed asserted over (1, NORMAL)
    /// cannot tell a formula that reads them from one that ignores them.
    /// </para>
    /// <para>
    /// The clear key is built with <c>Player.ChapterTierKey</c> rather than spelled out, so a change
    /// to the storage format cannot quietly turn these fixtures into empty histories.
    /// </para>
    /// </remarks>
    private static WorldSlice PastTheLadder(
        int clearedChapter, DifficultyTier clearedTier, int? legendLevel = null, long runsStarted = 0L) =>
        new(
            Worlds.Rehydrated(PlayerSnapshots.With(
                legendLevel: legendLevel,
                runsStarted: runsStarted,
                clearedChapterTiers: PlayerSnapshots.Counters(
                    (PlayerAggregate.ChapterTierKey(clearedChapter, clearedTier), 1L)))),
            null);

    /// <summary>The Legend Level the Mythic rung demands, read from the content the gate reads.</summary>
    /// <remarks>
    /// Read rather than written down: the number lives in tuning, and a C# copy of it is invisible to
    /// every architecture rule that watches for one.
    /// </remarks>
    private static readonly int MythicLegendLevel = Worlds.Context.Content.ReadInt32(
        $"{ChapterGatingTuning.GatingReference}/{DifficultyTier.MYTHIC}/requiresLegendLevel");

    // ------------------------------------------------------------------ acceptance, the new Run

    /// <summary>
    /// START_RUN succeeds on a run-less slice, and the resulting Run carries exactly what is
    /// authorised — nothing invented past it.
    /// </summary>
    // ═══════════════════════════════════════════════════ M7-06d · Max HP comes off the hero

    /// <summary>
    /// 🔒 A run opens at the hero's COMPOSED Max HP, full — not at the structural floor of 1 this
    /// handler used to write.
    /// </summary>
    /// <remarks>
    /// 🔴 The floor was not a harmless placeholder. Every <c>SetHitPoints</c> call in the game passes
    /// <c>run.MaxHp</c> unchanged, so a 1 never moved: <c>Revive</c> healed
    /// <c>MaxHp × HealPctMaxHp</c> clamped into <c>[1, MaxHp]</c> = 1, and the campfire's 40% rest and
    /// the Stage Gate's 15% heal were fractions of 1. Asserted against <c>05</c> §2's own curve rather
    /// than a copied number, so a retune of the curve moves this case with it.
    /// </remarks>
    [Fact]
    public void A_run_opens_at_the_heros_composed_max_hit_points()
    {
        var state = Worlds.OutsideARun();

        var result = SlayIdleRepeat.Core.GameRules.Apply(
            state, new StartRunCommand(1, DifficultyTier.NORMAL), Worlds.Context);

        result.Accepted.ShouldBeTrue("START_RUN was refused " + result.Rejection + ".");

        var run = result.NewState.Run!;

        run.MaxHp.ShouldBeGreaterThan(
            1,
            "the run opened at the structural floor, so M7-06d's fix is not in effect and the whole " +
            "HP economy is inert again: Revive heals 1 and the campfire rests a fraction of 1.");
        run.CurrentHp.ShouldBe(run.MaxHp, "a fresh run opens at full health.");
    }

    /// <summary>
    /// …and the number is the hero's, which is what makes it move when the hero does.
    /// </summary>
    /// <remarks>
    /// 🔒 The load-bearing case of the two (steering S2). The one above passes for any Max HP above 1 —
    /// including a fabricated constant — so on its own it would let the floor be replaced by a second
    /// invented number. This one states that the value came from <c>HeroBuild</c>, by comparing two
    /// runs whose heroes differ only in Legend Level: <c>05</c> §2's curve is
    /// <c>MaxHP = 250 + 45·L</c>, so a higher level must open a strictly larger run.
    /// </remarks>
    [Fact]
    public void The_max_hit_points_a_run_opens_at_follow_the_heros_own_curve()
    {
        var lower = OpenedBy(legendLevel: 1);
        var higher = OpenedBy(legendLevel: 30);

        higher.ShouldBeGreaterThan(
            lower,
            "two runs opened by heroes 29 Legend Levels apart share a Max HP, so the number is not " +
            "coming off 05 §2's curve at all — it is a constant that happens to be above 1.");
    }

    /// <summary>The Max HP a run opens at for a hero of the given Legend Level.</summary>
    private static int OpenedBy(int legendLevel)
    {
        var player = Worlds.Rehydrated(
            SlayIdleRepeat.Core.Tests.Model.PlayerSnapshots.With(legendLevel: legendLevel));

        var result = SlayIdleRepeat.Core.GameRules.Apply(
            new WorldSlice(player, null),
            new StartRunCommand(1, DifficultyTier.NORMAL),
            Worlds.Context);

        result.Accepted.ShouldBeTrue("START_RUN was refused " + result.Rejection + ".");

        return result.NewState.Run!.MaxHp;
    }

    [Fact]
    public void START_RUN_succeeds_on_a_run_less_slice_and_attaches_a_Run()
    {
        var state = PastTheLadder(3, DifficultyTier.NORMAL);
        var runsStartedBefore = state.Player.RunsStarted;

        var result = SlayIdleRepeat.Core.GameRules.Apply(
            state, new StartRunCommand(3, DifficultyTier.HEROIC), Worlds.Context);

        result.Accepted.ShouldBeTrue();
        result.Events.ShouldBeEmpty("no event names a run's own creation.");

        var run = result.NewState.Run;
        run.ShouldNotBeNull();
        run!.ChapterId.ShouldBe(3);
        run.Tier.ShouldBe(DifficultyTier.HEROIC);
        run.Position.ShouldBe(-1, "the virtual trailhead — one step before node 0.");
        run.Gold.ShouldBe(0L);
        run.RngStreamPositions.ShouldBeEmpty("a run that has drawn nothing stands at draw 0 everywhere.");
        run.AdUses.ShouldBeEmpty();
        run.LastAppliedAtUtc.ShouldBe(Worlds.Context.NowUtc);
        run.PlayerId.ShouldBe(result.NewState.Player.Id);

        result.NewState.Player.RunsStarted.ShouldBe(
            runsStartedBefore + 1,
            "a successful START_RUN spends the lifetime run counter exactly once.");
    }

    /// <summary>
    /// The seed is exactly SeedDerivation's formula, over the values this very command call used —
    /// the actual Hash64, not a plausible-looking number.
    /// </summary>
    [Fact]
    public void The_runSeed_is_SeedDerivations_formula_over_this_calls_own_values()
    {
        var priorRuns = 5L;
        var state = PastTheLadder(
            2, DifficultyTier.HEROIC, legendLevel: MythicLegendLevel, runsStarted: priorRuns);

        var result = SlayIdleRepeat.Core.GameRules.Apply(
            state, new StartRunCommand(2, DifficultyTier.MYTHIC), Worlds.Context);

        var expectedCounter = priorRuns + 1;
        var expectedSeed = SeedDerivation.RunSeed(
            state.Player.Id, 2, DifficultyTier.MYTHIC, Worlds.Context.NowUtc, expectedCounter);

        result.NewState.Run!.RunSeed.ShouldBe(expectedSeed);
        result.NewState.Player.RunsStarted.ShouldBe(expectedCounter);
    }

    /// <summary>
    /// The anti-reroll rule's own mechanism: two runs the same player starts at the same NowUtc get
    /// different seeds, because runCounter — not the clock — is what tells them apart.
    /// </summary>
    [Fact]
    public void Two_runCounters_at_the_identical_instant_produce_different_seeds()
    {
        var playerId = new PlayerId("PLAYER_SAME_SECOND");

        var first = SeedDerivation.RunSeed(playerId, 1, DifficultyTier.NORMAL, Worlds.NowUtc, runCounter: 1);
        var second = SeedDerivation.RunSeed(playerId, 1, DifficultyTier.NORMAL, Worlds.NowUtc, runCounter: 2);

        first.ShouldNotBe(second);
    }

    // ------------------------------------------------------------------ refusals, before anything is spent

    /// <summary>
    /// START_RUN on a slice that already carries an active Run is rejected, not silently overwritten
    /// — and the counter it would otherwise spend is untouched.
    /// </summary>
    [Fact]
    public void START_RUN_on_a_slice_with_an_active_run_is_rejected_not_overwritten()
    {
        var state = Worlds.InARun();
        var runsStartedBefore = state.Player.RunsStarted;
        var originalRun = state.Run;

        var result = SlayIdleRepeat.Core.GameRules.Apply(
            state, new StartRunCommand(1, DifficultyTier.NORMAL), Worlds.Context);

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);

        // A rejected command's NewState is the caller's own slice: the run in it is still the same
        // object, not a fresh one wearing the same values.
        result.NewState.ShouldBeSameAs(state);
        result.NewState.Run.ShouldBeSameAs(originalRun);

        result.NewState.Player.RunsStarted.ShouldBe(
            runsStartedBefore,
            "a rejected START_RUN must not spend the lifetime counter. 🔴 This line does not prove " +
            "the check runs before Player.BeginRun(), and used to say it did: Apply hands the handler " +
            "a CLONE and returns the caller's slice on a rejection, so a counter spent early moves on " +
            "an object nobody reads. Moving BeginRun() above every guard reddens nothing in this " +
            "repository — see StartRunChapterGateTests' remarks, where the mutation was run.");
    }

    /// <summary>
    /// A chapter SeedDerivation.RunSeed cannot hash is a rejection, not the ArgumentOutOfRangeException
    /// that function itself would throw: the client's illegal request is data, never an exception out
    /// of Apply.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_chapter_below_1_is_rejected_without_spending_the_counter(int chapterId)
    {
        var state = Worlds.OutsideARun();
        var runsStartedBefore = state.Player.RunsStarted;

        var result = SlayIdleRepeat.Core.GameRules.Apply(
            state, new StartRunCommand(chapterId, DifficultyTier.NORMAL), Worlds.Context);

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
        result.NewState.Player.RunsStarted.ShouldBe(runsStartedBefore);
    }

    /// <summary>An undefined <see cref="DifficultyTier"/> is refused the same way, for the same reason.</summary>
    [Fact]
    public void An_undefined_tier_is_rejected_without_spending_the_counter()
    {
        var state = Worlds.OutsideARun();
        var runsStartedBefore = state.Player.RunsStarted;

        var result = SlayIdleRepeat.Core.GameRules.Apply(
            state, new StartRunCommand(1, (DifficultyTier)0), Worlds.Context);

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
        result.NewState.Player.RunsStarted.ShouldBe(runsStartedBefore);
    }

    /// <summary>Negative control: chapter 1 on a defined tier is legal, so the guards above refuse specific bad values only.</summary>
    [Fact]
    public void Chapter_1_on_a_defined_tier_is_accepted()
    {
        var result = SlayIdleRepeat.Core.GameRules.Apply(
            Worlds.OutsideARun(), new StartRunCommand(1, DifficultyTier.NORMAL), Worlds.Context);

        result.Accepted.ShouldBeTrue();
    }

    // ------------------------------------------------------------------ S1: two other run rows, differently shaped

    /// <summary>The guard's exemption is START_RUN's alone. A no-payload run row still throws on the identical run-less slice.</summary>
    [Fact]
    public void A_no_payload_run_command_still_throws_on_a_run_less_slice()
    {
        Should.Throw<InvalidOperationException>(() =>
                SlayIdleRepeat.Core.GameRules.Apply(Worlds.OutsideARun(), new RollDiceCommand(), Worlds.Context))
            .Message.ShouldContain("ROLL_DICE", Case.Sensitive);
    }

    /// <summary>Same claim, over a run row with a payload — a different shape from <see cref="RollDiceCommand"/>.</summary>
    [Fact]
    public void A_run_command_with_a_payload_still_throws_on_a_run_less_slice()
    {
        Should.Throw<InvalidOperationException>(() =>
                SlayIdleRepeat.Core.GameRules.Apply(Worlds.OutsideARun(), new ChooseForkCommand(0), Worlds.Context))
            .Message.ShouldContain("CHOOSE_FORK", Case.Sensitive);
    }

    /// <summary>Negative control: a meta command is entirely untouched by this guard — it was always sendable there.</summary>
    [Fact]
    public void A_meta_command_is_unaffected_by_the_guard_on_the_identical_slice()
    {
        var result = SlayIdleRepeat.Core.GameRules.Apply(
            Worlds.OutsideARun(), Harnesses.BeginSession, Worlds.Drawing(0xFEED));

        result.Accepted.ShouldBeTrue();
    }

    // ------------------------------------------------------------------ composition with the next run command

    /// <summary>
    /// The RNG write-back machinery composes correctly with a handler that creates the Run rather
    /// than mutating an existing one: the very next run command opens a real RunRngScope over the
    /// seed START_RUN just committed and folds its draws back.
    /// </summary>
    [Fact]
    public void The_next_run_commands_RngScope_is_rooted_at_the_seed_START_RUN_just_committed()
    {
        var table = new CommandDispatch()
            .Handled<StartRunCommand>(
                "START_RUN", CommandKind.Run, SlayIdleRepeat.Core.Handlers.StartRun.Handle, opensRun: true)
            .Handled(Worlds.RunWireName, CommandKind.Run, (Worlds.RunFixtureCommand _, HandlerInput input) =>
            {
                input.Rng.Stream(RngStreams.Dice).Range(1, 7);
                return HandlerResult.Accept();
            });

        var opened = SlayIdleRepeat.Core.GameRules.Execute(
            table, Worlds.OutsideARun(), new StartRunCommand(1, DifficultyTier.NORMAL), Worlds.Context);

        opened.Accepted.ShouldBeTrue();
        var committedSeed = opened.NewState.Run!.RunSeed;

        var drawn = SlayIdleRepeat.Core.GameRules.Execute(
            table, opened.NewState, new Worlds.RunFixtureCommand(), Worlds.Context);

        drawn.Accepted.ShouldBeTrue();
        drawn.NewState.Run!.StreamPosition(RngStreams.Dice).ShouldBe(1UL);

        // Rooted at the committed seed, not a fresh one — the same seed START_RUN derived and wrote
        // to the Run, never recomputed.
        drawn.NewState.Run.RunSeed.ShouldBe(committedSeed);
    }
}
