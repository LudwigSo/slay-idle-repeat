using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Rules.Perks;
using SlayIdleRepeat.Core.Content.Perks;
using SlayIdleRepeat.Core.Tests.Model;
using SlayIdleRepeat.Core.Tests.TestSupport;
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
    /// The seed cases keep non-trivial chapter/tier pairs: a seed asserted over (1, NORMAL) cannot
    /// tell a formula that reads chapter and tier from one that ignores them.
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
    /// 🔒 A run opens at the hero's COMPOSED Max HP, full — not at the structural floor of 1, which
    /// left every heal in the game a fraction of 1.
    /// </summary>
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
    /// …and the number is the hero's own: the case above passes for any constant above 1, so this
    /// compares two runs whose heroes differ only in Legend Level — `05` §2's curve is
    /// <c>MaxHP = 250 + 45·L</c>, so a higher level must open a strictly larger run.
    /// </summary>
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
        run.ChapterId.ShouldBe(3);
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
            runsStartedBefore, "a rejected START_RUN must not spend the lifetime counter.");
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

    // ------------------------------------------------------------------ the opening draft

    /// <summary>A fresh run opens with a perk draft already waiting.</summary>
    /// <remarks>
    /// 🔒 The run's first decision is which perk to take, on the same three-option draft the rest
    /// of the run uses. It is opened here rather than by a command of its own because this is the
    /// only moment at which the run exists and nothing has happened in it — see the handler's
    /// remarks.
    /// </remarks>
    [Fact]
    public void A_fresh_run_opens_with_a_perk_draft_already_waiting()
    {
        var result = SlayIdleRepeat.Core.GameRules.Apply(
            Worlds.OutsideARun(), new StartRunCommand(1, DifficultyTier.NORMAL), Worlds.Context);

        result.Accepted.ShouldBeTrue("START_RUN was refused " + result.Rejection + ".");

        result.NewState.Run!.DraftPending.ShouldBeTrue(
            "a run that opens with no draft pending gives the player their first perk only after " +
            "their first won battle, which is the behaviour this replaced");
    }

    /// <summary>…and it draws against stage 1, naming no battle tile, because no battle caused it.</summary>
    /// <remarks>
    /// Both halves matter and they fail differently. The stage is what the rarity table is keyed on,
    /// so a run that opened its draft against the boss stage would hand out its strongest offer
    /// before the player had fought anything. The tile kind is read only for "is this Elite or
    /// Boss", and <c>Empty</c> answers both with no — writing <c>Enemy</c> there would key the same
    /// band off a battle the run never fought.
    /// </remarks>
    [Fact]
    public void The_opening_draft_draws_against_stage_1_and_names_no_battle_tile()
    {
        var result = SlayIdleRepeat.Core.GameRules.Apply(
            Worlds.OutsideARun(), new StartRunCommand(1, DifficultyTier.NORMAL), Worlds.Context);

        var run = result.NewState.Run!;

        run.DraftBattleStage.ShouldBe(1, "the run opens at the trailhead, one step before stage 1's node 0");
        run.DraftBattleKindValue.ShouldBe(
            (int)TileKind.Empty,
            "Empty is neither Elite nor Boss, which is the only question the draft asks of the kind");
    }

    /// <summary>🔒 …and what it offers is three of the categories' base perks — the choice of element.</summary>
    /// <remarks>
    /// The payoff of opening the draft here at all, and it falls out of the category gate rather
    /// than out of anything this handler says: a run owning nothing satisfies no perk's
    /// prerequisites, so the draftable pool is exactly the nine bases. The player's first decision
    /// is therefore which element the run is going to be about.
    /// <para>
    /// Swept over several openings rather than asserted on one, because the three options are drawn:
    /// a single run that happened to be offered three bases would pass over a gate doing nothing.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_opening_draft_offers_three_of_the_categories_base_perks()
    {
        // The projection reads the draft economy for the reroll price and skip reward it draws
        // beside the cards, which this suite's currencies fixture does not carry.
        var content = ContextThatCanPayASkip.Content;
        var catalogue = PerkCatalogue.Read(content);

        for (var runsStarted = 0L; runsStarted < 16L; runsStarted++)
        {
            var opened = SlayIdleRepeat.Core.GameRules.Apply(
                PastTheLadder(1, DifficultyTier.NORMAL, runsStarted: runsStarted),
                new StartRunCommand(1, DifficultyTier.NORMAL),
                ContextThatCanPayASkip);

            opened.Accepted.ShouldBeTrue("START_RUN was refused " + opened.Rejection + ".");

            var draft = DraftView.Project(opened.NewState.Run!.ToSnapshot(), content);

            draft.ShouldNotBeNull("the run opened with a draft pending, so the screen has one to draw");
            draft.Options.Count.ShouldBe(3, "06 §1 shows three options");

            foreach (var option in draft.Options)
            {
                catalogue.Find(option.PerkId).Requires.ShouldBeEmpty(
                    $"run {runsStarted} was offered '{option.PerkId}', which is gated behind a perk " +
                    "a run that has drafted nothing cannot hold");
            }
        }
    }

    /// <summary>🔒 …and nothing but a draft command may be applied until it is answered.</summary>
    /// <remarks>
    /// This is what makes the opening draft the run's FIRST decision rather than one the player can
    /// walk past. The gate is <c>GameRules.Apply</c>'s and predates this handler; what is asserted
    /// here is that opening the run trips it.
    /// </remarks>
    [Fact]
    public void The_first_roll_is_refused_until_the_opening_draft_is_answered()
    {
        var opened = SlayIdleRepeat.Core.GameRules.Apply(
            Worlds.OutsideARun(), new StartRunCommand(1, DifficultyTier.NORMAL), Worlds.Context);

        var rolled = SlayIdleRepeat.Core.GameRules.Apply(
            opened.NewState, new RollDiceCommand(), Worlds.Drawing(commandSeed: 1UL));

        rolled.Accepted.ShouldBeFalse("the player rolled past their opening perk choice");
        rolled.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    // ------------------------------------------------------------------ composition with the next run command

    /// <summary>
    /// The RNG write-back machinery composes correctly with a handler that creates the Run rather
    /// than mutating an existing one: the next run command opens a real RunRngScope over the seed
    /// START_RUN just committed and folds its draws back.
    /// </summary>
    /// <remarks>
    /// The opening draft is resolved in between, because it has to be: a fresh run opens with a
    /// draft pending and <c>GameRules.Apply</c> admits only the three draft commands until it is
    /// answered. Skipping is the shortest of the three and draws nothing, so the seed the fixture
    /// command is rooted at is still the one START_RUN committed.
    /// </remarks>
    [Fact]
    public void The_next_run_commands_RngScope_is_rooted_at_the_seed_START_RUN_just_committed()
    {
        var table = new CommandDispatch()
            .Handled<StartRunCommand>(
                "START_RUN", CommandKind.Run, SlayIdleRepeat.Core.Handlers.StartRun.Handle, opensRun: true)
            .Handled<SkipDraftCommand>(
                "SKIP_DRAFT", CommandKind.Run, SlayIdleRepeat.Core.Handlers.SkipDraft.Handle)
            .Handled(Worlds.RunWireName, CommandKind.Run, (Worlds.RunFixtureCommand _, HandlerInput input) =>
            {
                input.Rng.Stream(RngStreams.Dice).Range(1, 7);
                return HandlerResult.Accept();
            });

        var opened = SlayIdleRepeat.Core.GameRules.Execute(
            table, Worlds.OutsideARun(), new StartRunCommand(1, DifficultyTier.NORMAL), ContextThatCanPayASkip);

        opened.Accepted.ShouldBeTrue();
        var committedSeed = opened.NewState.Run!.RunSeed;

        var skipped = SlayIdleRepeat.Core.GameRules.Execute(
            table, opened.NewState, new SkipDraftCommand(), ContextThatCanPayASkip);

        skipped.Accepted.ShouldBeTrue(
            "the opening draft was refused (" + skipped.Rejection + "), so the fixture command below " +
            "would be measuring the draft gate rather than the RNG scope");

        var drawn = SlayIdleRepeat.Core.GameRules.Execute(
            table, skipped.NewState, new Worlds.RunFixtureCommand(), ContextThatCanPayASkip);

        drawn.Accepted.ShouldBeTrue();
        drawn.NewState.Run!.StreamPosition(RngStreams.Dice).ShouldBe(1UL);

        // Rooted at the committed seed, not a fresh one — the same seed START_RUN derived and wrote
        // to the Run, never recomputed.
        drawn.NewState.Run.RunSeed.ShouldBe(committedSeed);
    }

    /// <summary>
    /// <see cref="Worlds.Context"/> with the SHIPPED <c>currencies.json</c> rather than the suite's
    /// calendar-only fixture of it.
    /// </summary>
    /// <remarks>
    /// Needed only because resolving the opening draft costs a read this suite's own currencies
    /// fixture does not carry: <c>SKIP_DRAFT</c> pays the skip reward out of <c>draftEconomy</c>,
    /// and that block lives in the real document. Swapping the one document rather than the whole
    /// context keeps every other reading START_RUN makes on the fixture the rest of the file uses.
    /// </remarks>
    private static GameContext ContextThatCanPayASkip { get; } = Worlds.Context with
    {
        Content = new Core.Content.ContentSnapshot(
            Worlds.Context.Content.Version,
            Worlds.Context.Content.DocumentPaths
                .Where(path => !string.Equals(path, CurrenciesPath, StringComparison.Ordinal))
                .Select(Worlds.Context.Content.GetDocument)
                .Append(ShippedHarness.Content.GetDocument(CurrenciesPath))),
    };

    private const string CurrenciesPath = "tuning/currencies.json";
}
