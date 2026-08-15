using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Tests.Model;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary>
/// 🔒 `02` §2 / M3-15 — <c>START_RUN</c>: the run-less-slice guard's one-row exemption
/// (<c>CommandRegistration.OpensRun</c>), the <c>runSeed</c> derivation, the new <c>Run</c>'s shape,
/// and the two ways a request is refused before anything is spent.
/// </summary>
/// <remarks>
/// Driven over the <b>production</b> dispatch table through <c>GameRules.Apply</c> wherever possible,
/// so this suite proves the real row rather than a stand-in shaped like it. Steering <b>S1</b> is the
/// organising rule: the guard's exemption is probed on the row it exempts, on two differently-shaped
/// rows it does not, and on the negative control of a meta command the change never touches.
/// </remarks>
public sealed class StartRunTests
{
    // ------------------------------------------------------------------ acceptance, the new Run

    /// <summary>
    /// 🔒 The positive claim: <c>START_RUN</c> succeeds on the run-less slice <c>WorldSlice(player,
    /// null)</c> is the natural one for, and the resulting <c>Run</c> carries exactly what `02` §2
    /// authorises — nothing invented past it.
    /// </summary>
    [Fact]
    public void START_RUN_succeeds_on_a_run_less_slice_and_attaches_a_Run()
    {
        var state = Worlds.OutsideARun();
        var runsStartedBefore = state.Player.RunsStarted;

        var result = SlayIdleRepeat.Core.GameRules.Apply(
            state, new StartRunCommand(3, DifficultyTier.HEROIC), Worlds.Context);

        result.Accepted.ShouldBeTrue();
        result.Events.ShouldBeEmpty("30 §7 names no event for a run's own creation.");

        var run = result.NewState.Run;
        run.ShouldNotBeNull();
        run!.ChapterId.ShouldBe(3);
        run.Tier.ShouldBe(DifficultyTier.HEROIC);
        run.Position.ShouldBe(-1, "03 §1.1's virtual trailhead — one step before node 0.");
        run.Gold.ShouldBe(0L);
        run.RngStreamPositions.ShouldBeEmpty("a run that has drawn nothing stands at draw 0 everywhere.");
        run.AdUses.ShouldBeEmpty();
        run.LastAppliedAtUtc.ShouldBe(Worlds.Context.NowUtc);
        run.PlayerId.ShouldBe(result.NewState.Player.Id);

        result.NewState.Player.RunsStarted.ShouldBe(
            runsStartedBefore + 1,
            "02 §2's runCounter is Player.BeginRun()'s return, so a successful START_RUN spends the " +
            "lifetime counter exactly once.");
    }

    /// <summary>
    /// 🔒 The seed is exactly `02` §2's formula, over the values this very command call used —
    /// not a plausible-looking number, the actual <c>Hash64</c>.
    /// </summary>
    [Fact]
    public void The_runSeed_is_SeedDerivations_formula_over_this_calls_own_values()
    {
        var priorRuns = 5L;
        var state = new WorldSlice(
            Worlds.Rehydrated(PlayerSnapshots.With(runsStarted: priorRuns)), null);

        var result = SlayIdleRepeat.Core.GameRules.Apply(
            state, new StartRunCommand(2, DifficultyTier.MYTHIC), Worlds.Context);

        var expectedCounter = priorRuns + 1;
        var expectedSeed = SeedDerivation.RunSeed(
            state.Player.Id, 2, DifficultyTier.MYTHIC, Worlds.Context.NowUtc, expectedCounter);

        result.NewState.Run!.RunSeed.ShouldBe(expectedSeed);
        result.NewState.Player.RunsStarted.ShouldBe(expectedCounter);
    }

    /// <summary>
    /// 🔒 The anti-reroll rule's own mechanism: two runs the same player starts at the SAME
    /// <c>NowUtc</c> get different seeds, because <c>runCounter</c> — not the clock — is what tells
    /// them apart (`02` §2's own worked case: "two runs begun in the same second still differ").
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
    /// 🔒 S1's required negative case: <c>START_RUN</c> on a slice that already carries an active
    /// <c>Run</c> is <b>rejected</b>, not silently overwritten — and the counter it would otherwise
    /// spend is untouched, because the rejection runs before <c>Player.BeginRun()</c> is ever called.
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

        // 🔒 P4: a rejected command's NewState is the CALLER'S OWN slice, unchanged — so the run in
        // it is still the SAME object, not a fresh one wearing the same values.
        result.NewState.ShouldBeSameAs(state);
        result.NewState.Run.ShouldBeSameAs(originalRun);

        result.NewState.Player.RunsStarted.ShouldBe(
            runsStartedBefore,
            "the already-active-run check runs BEFORE Player.BeginRun() — a rejected START_RUN must " +
            "not spend the lifetime counter it would have seeded the (refused) run from.");
    }

    /// <summary>
    /// 🔒 A chapter <c>SeedDerivation.RunSeed</c> cannot hash is a REJECTION, not the
    /// <c>ArgumentOutOfRangeException</c> that function itself would throw — `30` §2.1's P3: the
    /// client's illegal request is data, never an exception out of <c>Apply</c>.
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

    /// <summary>
    /// 🔒 The negative control both refusals above need: chapter 1 on a defined tier is legal, so the
    /// guards above are refusing the SPECIFIC bad values, not every request.
    /// </summary>
    [Fact]
    public void Chapter_1_on_a_defined_tier_is_accepted()
    {
        var result = SlayIdleRepeat.Core.GameRules.Apply(
            Worlds.OutsideARun(), new StartRunCommand(1, DifficultyTier.NORMAL), Worlds.Context);

        result.Accepted.ShouldBeTrue();
    }

    // ------------------------------------------------------------------ S1: two other run rows, differently shaped

    /// <summary>
    /// 🔒 S1 — the guard's exemption is <c>START_RUN</c>'s alone. A no-payload run row still throws
    /// on the identical run-less slice.
    /// </summary>
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

    /// <summary>
    /// 🔒 S1's negative control: a <c>CommandKind.Meta</c> command is entirely untouched by this
    /// guard, on the identical run-less slice — it was always sendable there.
    /// </summary>
    [Fact]
    public void A_meta_command_is_unaffected_by_the_guard_on_the_identical_slice()
    {
        var result = SlayIdleRepeat.Core.GameRules.Apply(
            Worlds.OutsideARun(), Harnesses.BeginSession, Worlds.Drawing(0xFEED));

        result.Accepted.ShouldBeTrue();
    }

    // ------------------------------------------------------------------ composition with the next run command

    /// <summary>
    /// 🔒 The RNG write-back machinery <c>GameRules.Execute</c> built for already-handled run
    /// commands (M1) composes correctly with a handler that CREATES the <c>Run</c> rather than
    /// mutating an existing one: the very next run command against the resulting slice opens a real
    /// <c>RunRngScope</c> over the seed <c>START_RUN</c> just committed and folds its draws back.
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

        // 🔒 Rooted at the COMMITTED seed, not a fresh one — the same seed START_RUN derived and
        // wrote to the Run, never recomputed (Run.RunSeed's own remarks: "derived once … and never
        // recomputed").
        drawn.NewState.Run.RunSeed.ShouldBe(committedSeed);
    }
}
