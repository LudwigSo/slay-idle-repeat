using System.Globalization;
using Shouldly;
using SlayIdleRepeat.Application.UseCases;
using SlayIdleRepeat.Client.Game.Presenters;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Stats;
using Xunit;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// `13` §5 / `05` §8 — the Battle Replay screen (S06): what it animates, how fast, what it says when
/// it has nothing to animate, and the one command that closes the battle a run is standing in.
/// </summary>
public sealed class BattleReplayPresenterTests
{
    /// <summary>The log's own slot for the hero — 0, transcribed from the rules layer's roster.</summary>
    private const byte HeroSlot = 0;

    /// <summary>The log's own slot for the first enemy — 4, after the hero and three pet slots.</summary>
    private const byte FirstEnemySlot = 4;

    /// <summary>The log's slot for a second enemy.</summary>
    private const byte SecondEnemySlot = 5;

    /// <summary>The slot the log uses for "nobody" — 255.</summary>
    private const byte NoActorSlot = 255;

    /// <summary>The simulator's fixed tick rate, as this suite states it independently.</summary>
    private const int SimulatorTicksPerSecond = 20;

    /// <summary>The fight-length cap, in seconds.</summary>
    private const int FightLengthCapSeconds = 90;

    /// <summary>The stream whose counter says how many battles a run has started.</summary>
    private const string CombatStream = "combat";

    private static readonly PlayerId Player = new("PLAYER_battle_5d21");
    private static readonly RunId Run = new("RUN_battle_9b04");

    // ---- the read, and the states it can leave the screen in ------------------------------------

    [Fact]
    public void A_freshly_built_presenter_has_read_nothing_and_says_so()
    {
        var presenter = Build(RecordingGameHost.FindingNoSuchPlayer());

        presenter.Readiness.ShouldBeNull(
            "a screen that reported a readiness before its read answered would be reporting " +
            "a fight it has not looked for. Null is the one honest answer while the question is open.");
        presenter.CurrentTick.ShouldBe(
            0,
            "a playhead that started anywhere but the beginning would drop the opening of " +
            "every fight in the game before a frame was drawn.");
        presenter.StatusText.ShouldBe(
            BattleContent.EnglishValueOf(BattleContent.LoadingStatusKey),
            "a still frame with no caption is indistinguishable from a screen that has " +
            "crashed, and the player is looking at it for as long as the read takes.");
    }

    [Fact]
    public async Task The_read_is_addressed_to_this_run_and_not_to_the_player_alone()
    {
        var host = RecordingGameHost.Finding(AnyPlayer(), BattleRun());
        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        host.ReadRun.ShouldBe(
            Run,
            "a read naming no run answers with whatever run the player happens to be in, " +
            "which for a battle screen means animating a fight from a different run than the one the " +
            "player is watching.");
        host.ReadPlayer.ShouldBe(Player, "and the answer is only this player's to read.");
    }

    [Fact]
    public async Task A_read_that_finds_no_run_is_named_as_having_no_run_rather_than_as_an_empty_fight()
    {
        var presenter = Build(RecordingGameHost.Reading(
            new OwnStateResult(OwnStateLookup.NoSuchRun, View: null)));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Readiness.ShouldBe(
            BattleReadiness.NoRun,
            "a player who reached this screen with no run has hit a navigation bug, and a " +
            "screen that reported it as an empty log would send whoever reads the report hunting " +
            "through the simulator instead.");
        presenter.StatusText.ShouldBe(BattleContent.EnglishValueOf(BattleContent.NoRunStatusKey));
    }

    [Fact]
    public async Task A_read_that_faults_is_a_state_and_not_an_escape()
    {
        var presenter = Build(RecordingGameHost.FaultingItsRead(new TimeoutException("no answer")));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Readiness.ShouldBe(
            BattleReadiness.ReadUnavailable,
            "a read that never answered is a different failure from a simulator that threw " +
            "and from a fight that produced nothing, and an exception escaping here would take the " +
            "scene down mid-run rather than putting a sentence on the screen.");
        presenter.StatusText.ShouldBe(
            BattleContent.EnglishValueOf(BattleContent.ReadUnavailableStatusKey));
    }

    // ---- 🔒 S2: the five ways a replay can have nothing to animate, told apart ------------------

    /// <summary>
    /// 🔒 The case this screen has a readiness vocabulary for. All five of these are the same still
    /// frame, and each is escaped by something completely different.
    /// </summary>
    [Theory]
    [MemberData(nameof(StallCauses))]
    public async Task Each_way_a_replay_can_have_nothing_to_animate_is_named_from_its_own_cause(
        BattleReadiness readiness, string sentenceKey)
    {
        var presenter = Build(
            RecordingGameHost.Finding(AnyPlayer(), BattleRun()),
            StubBattleSimulation.Answering(readiness));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Readiness.ShouldBe(
            readiness,
            "collapsing these into one 'the fight did not play' would leave a player, and " +
            "whoever reads their report, with no way to tell an unbuilt feature from a crash from a " +
            "run whose battle counter is gone — three problems with three different owners.");
        presenter.StatusText.ShouldBe(
            BattleContent.EnglishValueOf(sentenceKey),
            "and each cause has to be SAID, not merely recorded: the enum is invisible to " +
            "the person looking at the still frame.");
    }

    public static TheoryData<BattleReadiness, string> StallCauses()
    {
        var data = new TheoryData<BattleReadiness, string>();

        foreach (var (readiness, sentenceKey) in BattleContent.StallCauses)
        {
            data.Add(readiness, sentenceKey);
        }

        return data;
    }

    /// <summary>
    /// 🔒 The five sentences are five DIFFERENT sentences, <b>as authored</b>.
    /// </summary>
    /// <remarks>
    /// 🔴 Stated over the shipped locale, not over a fixture. Every fixture string in this suite is
    /// derived from its own key, so five distinct keys give five distinct values by construction and
    /// a fixture-based version of this case could never fail whatever anyone wrote in
    /// <c>en.json</c>. The claim is about what a player reads.
    /// </remarks>
    [Fact]
    public void The_five_stall_sentences_are_five_different_authored_sentences()
    {
        var authored = BattleContent.StallCauses.Select(cause =>
        {
            BattleContent.ShippedEnglish.TryGetValue(cause.SentenceKey, out var sentence).ShouldBeTrue(
                $"'{cause.SentenceKey}' is not in the shipped English locale, so the cause it names " +
                "has no sentence and a player meeting it is shown its key.");

            return sentence!;
        }).ToArray();

        authored.Length.ShouldBe(5);
        authored.ShouldAllBe(sentence => sentence.Length > 0);
        authored.Distinct(StringComparer.Ordinal).Count().ShouldBe(
            authored.Length,
            "two of the five stall causes are AUTHORED with the same sentence, so a player meeting " +
            "one of them is told about the other. The whole reason these are five states rather than " +
            "one is that they are escaped five different ways — waiting for a feature, reporting a " +
            "crash, reporting a run, leaving the screen — and that is undone the moment two of the " +
            $"strings say the same thing: [{string.Join(" | ", authored)}]");
    }

    // ---- 🔒 the seed half, which is real -------------------------------------------------------

    /// <summary>
    /// 🔒 Checked against an independent derivation over the same two numbers, not against a
    /// constant: a hard-coded expectation would agree with a transposed argument list forever.
    /// </summary>
    [Fact]
    public void The_battle_seed_is_derived_from_the_runs_own_seed_and_its_battle_counter()
    {
        var run = BattleRun(runSeed: 0xC0FFEE_1234_5678UL, battlesStarted: 4);

        var attempt = new LocalBattleSimulation(BootContent.Shipped).Simulate(run);

        attempt.SeedDerived.ShouldBeTrue(
            "the seed half of local prediction is the half that IS reachable, and a screen " +
            "reporting no seed at all would say the wrong thing about which half is missing.");
        attempt.BattleSeed.ShouldBe(
            SeedDerivation.BattleSeed(0xC0FFEE_1234_5678UL, 3),
            "the counter records battles STARTED, so the battle now open is the one before " +
            "it. Deriving from the counter itself would predict the NEXT fight — a different fight, " +
            "reported under this one's result.");
    }

    /// <summary>
    /// 🔒 A revive re-enters the same battle without starting a new one, so the counter does not
    /// move and the same seed must come back out.
    /// </summary>
    [Fact]
    public void A_revived_battle_re_derives_the_seed_the_first_attempt_was_fought_under()
    {
        var simulation = new LocalBattleSimulation(BootContent.Shipped);
        var firstAttempt = BattleRun(runSeed: 991, battlesStarted: 2);
        var afterRevive = BattleRun(runSeed: 991, battlesStarted: 2);

        var before = simulation.Simulate(firstAttempt);
        var after = simulation.Simulate(afterRevive);

        after.BattleSeed.ShouldBe(
            before.BattleSeed,
            "a revive re-enters the battle rather than beginning one, so a second seed here " +
            "would put the player back into a DIFFERENT fight than the one they just lost — with the " +
            "enemy's rolls reshuffled by the act of paying to continue.");
    }

    [Fact]
    public void A_further_battle_in_the_same_run_is_a_different_fight()
    {
        var simulation = new LocalBattleSimulation(BootContent.Shipped);

        var first = simulation.Simulate(BattleRun(runSeed: 991, battlesStarted: 2));
        var second = simulation.Simulate(BattleRun(runSeed: 991, battlesStarted: 3));

        second.BattleSeed.ShouldNotBe(
            first.BattleSeed,
            "without the counter in the derivation every fight of a run would play out " +
            "identically, which the previous case would happily agree with — it only proves the seed " +
            "is STABLE, not that it depends on which battle this is.");
    }

    /// <summary>
    /// 🔴 The load-bearing finding, stated as behaviour: the block is at the stat block, not at the
    /// seed.
    /// </summary>
    [Fact]
    public void The_seed_is_derived_even_though_the_prediction_then_refuses_at_the_heros_stat_block()
    {
        var attempt = new LocalBattleSimulation(BootContent.Shipped)
            .Simulate(BattleRun(runSeed: 7, battlesStarted: 1));

        attempt.Readiness.ShouldBe(
            BattleReadiness.HeroStatsUnavailable,
            "nothing reachable from a client can turn a stored hero into the stat block the " +
            "simulator takes — there is no affix-to-stat mapping anywhere and no effect source that " +
            "reads gear — so a prediction that answered anything else here has invented one, and an " +
            "invented block produces a fight the server will not agree with.");
        attempt.SeedDerived.ShouldBeTrue(
            "and this is the whole point of reporting the seed separately from the result: " +
            "'local prediction does not work' sends the next reader to the seed derivation, which is " +
            "finished and correct. The refusal is at the stat block and the report has to say so.");
        attempt.Result.ShouldBeNull(
            "a refusal carrying a result would be a fabricated fight, and a fabricated fight " +
            "carries a fabricated hash — which the rules layer accepts without recomputing.");
    }

    [Fact]
    public void A_run_whose_counter_does_not_name_a_battle_is_refused_at_the_seed_and_says_so()
    {
        var attempt = new LocalBattleSimulation(BootContent.Shipped)
            .Simulate(BattleRun(runSeed: 7, battlesStarted: 0));

        attempt.Readiness.ShouldBe(
            BattleReadiness.SeedUnavailable,
            "a run standing in a battle it never started is a corrupt row, and reporting it " +
            "as the hero-stats gap would file it against a feature nobody is going to build for it.");
        attempt.SeedDerived.ShouldBeFalse(
            "and the seed it did not derive must not read as one it did — zero is a legal " +
            "seed value, so the flag is the only thing telling the two apart.");
    }

    [Fact]
    public void A_run_with_no_combat_counter_at_all_is_refused_at_the_seed()
    {
        var run = PlayerState.Run(Run, Player, RunPhase.BattlePending, runSeed: 7);

        var attempt = new LocalBattleSimulation(BootContent.Shipped).Simulate(run);

        attempt.Readiness.ShouldBe(
            BattleReadiness.SeedUnavailable,
            "the counters are a sparse map, so an absent combat row and a zero one are the " +
            "same fact and must not be two different behaviours — one of which would be an index of " +
            "minus one handed to a derivation that refuses negatives by throwing.");
    }

    [Fact]
    public void A_run_that_is_not_standing_in_a_battle_is_refused_before_any_seed_is_derived()
    {
        var run = PlayerState.Run(
            Run, Player, RunPhase.InProgress, runSeed: 7, rngStreamPositions: Counters(battlesStarted: 1));

        var attempt = new LocalBattleSimulation(BootContent.Shipped).Simulate(run);

        attempt.Readiness.ShouldBe(
            BattleReadiness.PhaseNotBattle,
            "a run that is walking the board has no open fight, and predicting one anyway " +
            "would let this screen submit a battle result for a battle nobody entered.");
        attempt.SeedDerived.ShouldBeFalse(
            "the counter of a run with no open battle names the LAST fight, not a current " +
            "one, so a seed reported here would be a real number for the wrong fight.");
    }

    // ---- what it animates ----------------------------------------------------------------------

    [Fact]
    public async Task The_first_advance_emits_the_opening_of_the_fight_rather_than_skipping_past_it()
    {
        var presenter = await Playing(ShortFight());

        await presenter.AdvanceAsync(0.5, CancellationToken.None);

        presenter.CurrentTick.ShouldBe(10);
        presenter.StepEvents.Select(e => e.Type).ShouldContain(
            CombatEventType.BattleStart,
            "the fight's opening event sits on tick zero, and a window that opened after " +
            "the playhead's starting position would drop it from every fight in the game — the one " +
            "event a replay needs in order to know a fight has begun at all.");
    }

    /// <summary>
    /// 🔒 The window is half-open: an event on the previous playhead was already drawn, an event on
    /// the new one has just happened.
    /// </summary>
    [Fact]
    public async Task An_advance_emits_the_events_after_the_previous_playhead_and_up_to_the_new_one()
    {
        var presenter = await Playing(ShortFight());

        await presenter.AdvanceAsync(0.5, CancellationToken.None);
        await presenter.AdvanceAsync(0.5, CancellationToken.None);

        presenter.CurrentTick.ShouldBe(20);
        presenter.StepEvents.Select(e => e.Tick).ShouldNotContain(
            10,
            "tick 10 was drawn by the advance that reached it, and re-emitting it would " +
            "double every spark, every crit pop and every damage number on any frame that lands on " +
            "an event — which is most of them at triple speed.");
        presenter.StepEvents.Select(e => e.Tick).ShouldContain(
            20,
            "and an event sitting exactly on the new playhead has just happened. Excluding " +
            "it would delay every blow by one frame and leave the last one undrawn forever.");
    }

    [Fact]
    public async Task Advancing_past_the_end_of_the_log_stops_at_the_last_tick()
    {
        var presenter = await Playing(ShortFight());

        await presenter.AdvanceAsync(30, CancellationToken.None);

        presenter.CurrentTick.ShouldBe(
            presenter.TotalTicks,
            "the log is the whole fight, so a playhead that ran past its end would report a " +
            "battle still in progress after the death blow and leave the result screen waiting.");
        presenter.Complete.ShouldBeTrue(
            "and reaching the end IS the completion — nothing else in the log announces it.");
    }

    // ---- speed ---------------------------------------------------------------------------------

    /// <summary>
    /// 🔒 The arithmetic, stated as arithmetic: one second of real time is twenty log ticks times
    /// the multiplier the setting IS.
    /// </summary>
    [Theory]
    [InlineData(BattleSpeed.Single, 1)]
    [InlineData(BattleSpeed.Double, 2)]
    [InlineData(BattleSpeed.Triple, 3)]
    public async Task Each_speed_consumes_the_log_at_its_own_multiple_of_real_time(
        BattleSpeed speed, int multiplier)
    {
        var presenter = await Playing(LongFight(), speed: speed);

        await presenter.AdvanceAsync(1.0, CancellationToken.None);

        presenter.CurrentTick.ShouldBe(
            SimulatorTicksPerSecond * multiplier,
            "the design's whole promise about speed is that the replay simply consumes the " +
            "log faster — the fight is already decided, so a setting that consumed it at any other " +
            "rate would be showing a different fight rather than the same one sooner.");
    }

    [Fact]
    public async Task Cycling_the_speed_walks_the_three_settings_and_wraps_back_to_real_time()
    {
        var presenter = await Playing(LongFight(), speed: BattleSpeed.Single);

        presenter.CycleSpeed();
        presenter.Speed.ShouldBe(BattleSpeed.Double);

        presenter.CycleSpeed();
        presenter.Speed.ShouldBe(BattleSpeed.Triple);

        presenter.CycleSpeed();
        presenter.Speed.ShouldBe(
            BattleSpeed.Single,
            "one control cycles all three, so a player who overshoots has to be able to get " +
            "back by pressing it again. Stopping at the fastest would strand them there for the rest " +
            "of the fight.");
    }

    [Fact]
    public async Task Changing_speed_mid_replay_changes_the_rate_and_not_the_playhead()
    {
        var presenter = await Playing(LongFight(), speed: BattleSpeed.Single);

        await presenter.AdvanceAsync(1.0, CancellationToken.None);
        var beforeCycle = presenter.CurrentTick;

        presenter.CycleSpeed();

        presenter.CurrentTick.ShouldBe(
            beforeCycle,
            "the speed control changes how fast the rest of the fight is watched, not which " +
            "part of it is being watched. A playhead that moved on the press would jump the fight " +
            "forward or replay blows already seen, every time a player touched the control.");

        await presenter.AdvanceAsync(1.0, CancellationToken.None);

        presenter.CurrentTick.ShouldBe(
            beforeCycle + (SimulatorTicksPerSecond * 2),
            "and from there on the new rate applies to the time that has not happened yet — " +
            "a rate change that retroactively rescaled the elapsed time would move the playhead by " +
            "the very jump the assertion above rules out.");
    }

    // ---- skip: always offered, and never a fabricated result ------------------------------------

    /// <summary>
    /// 🔒 Driven off the enum rather than a list, so a readiness added later is covered the day it
    /// is added rather than the day somebody remembers this case.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryReadiness))]
    public async Task Skip_is_offered_in_every_state_the_screen_can_report(BattleReadiness readiness)
    {
        var presenter = Build(
            RecordingGameHost.Finding(AnyPlayer(), BattleRun()),
            StubBattleSimulation.Answering(readiness));

        await presenter.StartAsync(CancellationToken.None);

        presenter.SkipAvailable.ShouldBeTrue(
            "the skip is an accessibility clause rather than a convenience, so withdrawing " +
            $"it in {readiness} — a state the screen is by definition least sure of itself in — is " +
            "exactly how a player ends up on a screen with a still frame and no way off it.");
    }

    public static TheoryData<BattleReadiness> EveryReadiness()
    {
        var data = new TheoryData<BattleReadiness>();

        foreach (var readiness in Enum.GetValues<BattleReadiness>())
        {
            data.Add(readiness);
        }

        return data;
    }

    /// <summary>
    /// 🔒 The floor under the case above, by NAMED MEMBER and never by count. A rule quantified over
    /// an enum passes vacuously the day the enum is emptied or its members are renamed away.
    /// </summary>
    [Fact]
    public void The_readiness_vocabulary_still_names_the_states_the_skip_rule_depends_on()
    {
        var members = Enum.GetValues<BattleReadiness>();

        members.ShouldContain(
            BattleReadiness.Ready,
            "without it the sweep above never once exercises a screen that HAS a fight, so 'skip is " +
            "always offered' would be proven only over screens where skipping does nothing.");
        members.ShouldContain(
            BattleReadiness.HeroStatsUnavailable,
            "this is the state every real player is in today, so a sweep that stopped covering it " +
            "would stop covering the shipped behaviour entirely.");
        members.ShouldContain(
            BattleReadiness.LogEmpty,
            "and this is the state where a skip is most tempting to withdraw — there is nothing to " +
            "skip to — which is precisely why it has to stay in the sweep.");
    }

    [Fact]
    public async Task Skipping_jumps_to_the_end_without_replaying_what_was_skipped()
    {
        var presenter = await Playing(LongFight());

        await presenter.SkipAsync(CancellationToken.None);

        presenter.TotalTicks.ShouldBe(
            600,
            "the fight's own length is what a skip lands on, so a screen that never learned it " +
            "would satisfy the assertion below by leaving both numbers at zero.");
        presenter.CurrentTick.ShouldBe(
            presenter.TotalTicks,
            "a skip that landed anywhere but the end would leave the battle unfinished and " +
            "the run unable to move on.");
        presenter.StepEvents.ShouldBeEmpty(
            "the whole point of skipping is not watching the fight. Handing the scene every " +
            "event of a ninety-second log in one frame would spray eighteen hundred ticks of floating " +
            "damage numbers across the screen a player asked to be spared.");
    }

    // ---- 🔒 S24: BattlePending closes from every path the replay can end in ---------------------

    [Fact]
    public async Task A_replay_that_runs_to_the_end_confirms_the_battles_result()
    {
        var host = RecordingGameHost.Finding(AnyPlayer(), BattleRun());
        var presenter = await Playing(ShortFight(), host);

        var submission = await presenter.AdvanceAsync(30, CancellationToken.None);

        submission.ShouldBe(BattleSubmission.Submitted);
        host.SubmitCommand.ShouldBeOfType<ConfirmBattleResultCommand>(
            "the run is parked in the battle phase and the rules layer refuses every command except " +
            "this one — including the revive. A replay that ended without confirming leaves the run " +
            "permanently stuck, with the board refusing to roll and no screen able to unstick it.");
    }

    [Fact]
    public async Task Skipping_confirms_the_very_same_result_the_replay_would_have()
    {
        var watched = RecordingGameHost.Finding(AnyPlayer(), BattleRun());
        var skipped = RecordingGameHost.Finding(AnyPlayer(), BattleRun());

        await (await Playing(ShortFight(), watched)).AdvanceAsync(30, CancellationToken.None);
        await (await Playing(ShortFight(), skipped)).SkipAsync(CancellationToken.None);

        skipped.SubmitCommand.ShouldBeOfType<ConfirmBattleResultCommand>(
            "both paths have to have submitted SOMETHING first — two screens that submitted nothing " +
            "at all agree with each other perfectly and prove nothing whatever.");
        skipped.SubmitCommand.ShouldBe(
            watched.SubmitCommand,
            "the outcome was fixed before the first frame was drawn, so watching and " +
            "skipping are the same battle. Two different commands here would mean the reward a " +
            "player receives depends on whether they had the patience to sit through the animation.");
    }

    /// <summary>
    /// 🔒 The hash's SHAPE, not merely its value: the rules layer parses it with no number styles
    /// permitted at all, so anything but bare decimal digits is refused on arrival.
    /// </summary>
    [Fact]
    public async Task The_confirmed_hash_is_the_simulators_own_hash_in_the_form_the_rules_layer_parses()
    {
        var host = RecordingGameHost.Finding(AnyPlayer(), BattleRun());
        var fight = ShortFight();
        var presenter = await Playing(fight, host);

        await presenter.AdvanceAsync(30, CancellationToken.None);

        var confirmed = host.SubmitCommand.ShouldBeOfType<ConfirmBattleResultCommand>();

        confirmed.LogHash.ShouldBe(
            fight.LogHash.ToString(CultureInfo.InvariantCulture),
            "the hash is the only evidence the client offers that it replayed the fight the " +
            "server issued, so it has to be the simulator's own number rather than anything derived " +
            "from it.");
        ulong.TryParse(confirmed.LogHash, NumberStyles.None, CultureInfo.InvariantCulture, out _)
             .ShouldBeTrue(
                 "the handler parses with NumberStyles.None, so a hexadecimal form, a 'fnv1a:' prefix, " +
                 "a sign or a thousands separator is refused as a malformed command — and a refused " +
                 "confirmation leaves the run stuck in the battle phase for good.");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_confirmation_carries_the_outcome_the_simulation_actually_reached(bool heroWon)
    {
        var host = RecordingGameHost.Finding(AnyPlayer(), BattleRun());
        var presenter = await Playing(ShortFight(heroWon: heroWon), host);

        await presenter.AdvanceAsync(30, CancellationToken.None);

        host.SubmitCommand.ShouldBeOfType<ConfirmBattleResultCommand>().Won.ShouldBe(
            heroWon,
            "the loss path has to close the battle exactly as the win path does — the run " +
            "leaves the battle phase on both — and a screen that only reported wins would leave every " +
            "defeated player unable to revive, since the revive itself is one of the commands the " +
            "battle phase refuses.");
    }

    [Fact]
    public async Task The_result_is_confirmed_once_however_many_frames_run_past_the_end()
    {
        var host = RecordingGameHost.Finding(AnyPlayer(), BattleRun());
        var presenter = await Playing(ShortFight(), host);

        await presenter.AdvanceAsync(30, CancellationToken.None);
        await presenter.AdvanceAsync(30, CancellationToken.None);
        await presenter.SkipAsync(CancellationToken.None);

        host.SubmitCallCount.ShouldBe(
            1,
            "the scene drives this from a per-frame callback that keeps firing after the " +
            "fight ends, so a confirmation per frame would submit sixty duplicate battle results a " +
            "second until something else took the screen away.");
    }

    // ---- 🔒 S6: the negative control. No fight, no command, no invented hash --------------------

    /// <summary>
    /// 🔒 The load-bearing guard. The rules layer checks the hash's SHAPE and never recomputes it,
    /// so a well-formed number with <c>Won: true</c> is a full kill payout for a fight that never
    /// happened.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryUnreadyReadiness))]
    public async Task Pressing_skip_with_no_simulated_fight_submits_nothing_at_all(
        BattleReadiness readiness)
    {
        var host = RecordingGameHost.Finding(AnyPlayer(), BattleRun());
        var presenter = Build(host, StubBattleSimulation.Answering(readiness));

        await presenter.StartAsync(CancellationToken.None);
        var submission = await presenter.SkipAsync(CancellationToken.None);

        submission.ShouldBe(BattleSubmission.RefusedNotAvailable);
        host.SubmitCallCount.ShouldBe(
            0,
            $"in {readiness} there is no simulated fight, so any hash this screen could submit would " +
            "be one it made up. That is worse than dishonest: the handler validates the hash's shape " +
            "and never recomputes it, so any well-formed number paired with Won: true buys a full " +
            "kill payout for a battle nobody fought — and this screen would be the exploit, shipped.");
    }

    public static TheoryData<BattleReadiness> EveryUnreadyReadiness()
    {
        var data = new TheoryData<BattleReadiness>();

        foreach (var readiness in Enum.GetValues<BattleReadiness>().Where(r => r != BattleReadiness.Ready))
        {
            data.Add(readiness);
        }

        return data;
    }

    // ---- the boss phase band -------------------------------------------------------------------

    [Fact]
    public async Task The_phase_band_shows_the_phase_last_crossed_at_or_before_the_playhead()
    {
        var presenter = await Playing(BossFight());

        await presenter.AdvanceAsync(1.0, CancellationToken.None);

        presenter.CurrentTick.ShouldBe(20);
        presenter.CurrentBossPhase.ShouldBe(
            2,
            "a phase-change event carries the phase ENTERED as its value, so the band reads " +
            "the value itself. Counting the events instead would agree here by coincidence and " +
            "disagree the first time a fight opens at a phase above the first.");
    }

    [Fact]
    public async Task A_phase_band_never_reverts_to_a_phase_the_fight_has_already_left()
    {
        var presenter = await Playing(BossFight());

        await presenter.AdvanceAsync(2.0, CancellationToken.None);

        presenter.CurrentBossPhase.ShouldBe(
            3,
            "boss phases only ever walk upward — a heal never emits a phase change — so a " +
            "band showing anything but the highest phase crossed would announce a phase transition " +
            "that never happened, with the drums and the shockwave of a real one.");
    }

    [Fact]
    public async Task A_fight_with_no_phase_change_has_no_phase_band_at_all()
    {
        var presenter = await Playing(ShortFight());

        await presenter.AdvanceAsync(30, CancellationToken.None);

        presenter.Complete.ShouldBeTrue(
            "the whole fight has to have actually played for the absence below to mean anything — a " +
            "replay that never started shows no band either, and would agree with both assertions.");
        presenter.CurrentBossPhase.ShouldBeNull(
            "most fights in the game are not boss fights, and defaulting an ordinary enemy " +
            "to phase one would put a boss's full-width phase banner across every trash encounter a " +
            "run walks through.");
        presenter.PhaseBandVisible.ShouldBeFalse(
            "and a band with nothing to say must not be drawn at all.");
    }

    [Fact]
    public async Task The_phase_band_comes_down_once_its_dwell_has_passed()
    {
        var presenter = await Playing(BossFight());

        await presenter.AdvanceAsync(0.5, CancellationToken.None);
        presenter.PhaseBandVisible.ShouldBeTrue(
            "the band's whole job is to announce the transition at the moment it happens.");

        await presenter.AdvanceAsync(1.0, CancellationToken.None);
        presenter.PhaseBandVisible.ShouldBeFalse(
            "and a band that never came down would cover the fight it was announcing for " +
            "the rest of the battle.");
    }

    /// <summary>
    /// 🔒 The accessibility clause, stated as behaviour rather than as the number itself: reduced
    /// motion shortens every animation on this screen to a tenth of a second.
    /// </summary>
    [Fact]
    public async Task Reduced_motion_takes_the_phase_band_down_within_a_tenth_of_a_second()
    {
        var reduced = await Playing(BossFight(), reducedMotion: true);
        var ordinary = await Playing(BossFight(), reducedMotion: false);

        await reduced.AdvanceAsync(0.5, CancellationToken.None);
        await ordinary.AdvanceAsync(0.5, CancellationToken.None);

        ordinary.PhaseBandVisible.ShouldBeTrue(
            "the ordinary dwell is the control — without it a band that was simply never " +
            "drawn would satisfy the reduced-motion claim below and prove nothing.");
        reduced.PhaseBandVisible.ShouldBeFalse(
            "reduced motion shortens every animation here to a tenth of a second, and half " +
            "a second of playback is five times that. A player who set it because full-width flashes " +
            "make them ill still gets the flash otherwise.");
    }

    // ---- health bars: derived where the log fixes it, unknown where it does not ------------------

    [Fact]
    public async Task The_heros_starting_health_is_recovered_from_what_it_lost_and_regained()
    {
        var presenter = await Playing(ShortFight());

        await presenter.AdvanceAsync(30, CancellationToken.None);

        Actor(presenter, HeroSlot).StartingHp.ShouldBe(
            47,
            "no maximum HP is anywhere in the log, so a hero's bar has no denominator unless " +
            "it is reconstructed: what was left, plus every point taken off, minus every point healed " +
            "back. A bar drawn against a guess is a bar that lies about how close the fight was.");
        Actor(presenter, HeroSlot).EndingHp.ShouldBe(
            40,
            "and the finishing value is the one number the result states outright.");
    }

    [Fact]
    public async Task An_actor_the_log_records_a_death_for_is_anchored_at_zero()
    {
        var presenter = await Playing(ShortFight());

        await presenter.AdvanceAsync(30, CancellationToken.None);

        Actor(presenter, FirstEnemySlot).EndingHp.ShouldBe(
            0,
            "a death event is the one thing that fixes an enemy's final health, and it is " +
            "what lets its bar be drawn at all.");
        Actor(presenter, FirstEnemySlot).StartingHp.ShouldBe(
            30,
            "an actor that ended at zero started at exactly the damage it absorbed, so the " +
            "one death event turns an unmeasurable bar into a measured one.");
    }

    /// <summary>🔴 The absence, asserted as an absence.</summary>
    [Fact]
    public async Task A_surviving_enemys_starting_health_is_reported_as_unknown_rather_than_guessed()
    {
        var presenter = await Playing(ShortFight());

        await presenter.AdvanceAsync(30, CancellationToken.None);

        Actor(presenter, SecondEnemySlot).StartingHp.ShouldBeNull(
            "an enemy that neither died nor reported its own health gives the arithmetic no " +
            "anchor at all. A plausible number here would draw a health bar whose fullness is a " +
            "fabrication, and a player watching a boss they cannot beat would be reading a fiction " +
            "about how close they came.");
    }

    // ---- 🔒 the real simulator, and the tick rate this screen transcribes -----------------------

    /// <summary>
    /// 🔒 The one case that drives the REAL simulator, so the replay is proven against a genuine log
    /// — real event ordering, real tick spacing — rather than against a fixture shaped to agree.
    /// </summary>
    [Fact]
    public async Task The_replay_consumes_a_log_the_real_simulator_produced()
    {
        var fight = StalemateFight();

        var presenter = await Playing(fight);
        await presenter.AdvanceAsync(0.05, CancellationToken.None);

        fight.Log[^1].Type.ShouldBe(
            CombatEventType.BattleEnd,
            "every log ends with its own end event, and a replay built against a fixture " +
            "that happened not to would be a replay with no way of knowing a fight had finished.");
        presenter.StepEvents.Select(e => e.Type).ShouldContain(
            CombatEventType.BattleStart,
            "and the first tick of a genuine log is what proves the window's lower bound is " +
            "open at the true beginning of a fight rather than at the beginning of a fixture.");
    }

    /// <summary>
    /// 🔒 The transcribed tick rate, pinned NON-VACUOUSLY: a real fight is driven to the length cap,
    /// and the screen must consume exactly that fight in exactly the capped number of seconds.
    /// </summary>
    /// <remarks>
    /// The rate is a private constant, so it is pinned through the behaviour it decides rather than
    /// by reading it. Were it transcribed as ten, the replay would sit at half the fight after the
    /// full ninety seconds and this case would go red — which is what a comparison of two constants
    /// could never do.
    /// </remarks>
    [Fact]
    public async Task A_fight_that_runs_to_the_length_cap_is_consumed_in_exactly_the_capped_time()
    {
        var fight = StalemateFight();

        fight.DurationTicks.ShouldBe(
            FightLengthCapSeconds * SimulatorTicksPerSecond,
            "the fixture stat block has to actually reach the ninety-second cap for this " +
            "case to say anything. If this is red the block was tuned into a decisive fight and the " +
            "pin below has become a statement about an arbitrary shorter log.");

        var presenter = await Playing(fight);

        await presenter.AdvanceAsync(FightLengthCapSeconds - 0.05, CancellationToken.None);
        presenter.Complete.ShouldBeFalse(
            "at real time the replay lasts exactly as long as the fight did, so a screen " +
            "that had already finished a moment early is consuming the log faster than the simulator " +
            "wrote it — every fight in the game would end before its last blow landed.");

        await presenter.AdvanceAsync(0.05, CancellationToken.None);
        presenter.Complete.ShouldBeTrue(
            "and a screen still playing after ninety seconds of a ninety-second fight has " +
            "transcribed the tick rate too low, leaving the player watching a frozen frame for as " +
            "long again as the fight itself took.");
    }

    // ---- fixture -------------------------------------------------------------------------------

    private static PlayerSnapshot AnyPlayer() => PlayerState.Player(Player);

    /// <summary>The per-stream counters of a run that has started the given number of battles.</summary>
    private static IReadOnlyDictionary<string, ulong> Counters(int battlesStarted) =>
        new Dictionary<string, ulong>(StringComparer.Ordinal) { [CombatStream] = (ulong)battlesStarted };

    /// <summary>A run parked in the battle phase, which is the only state this screen opens over.</summary>
    private static RunSnapshot BattleRun(ulong runSeed = 4242, int battlesStarted = 1) =>
        PlayerState.Run(
            Run,
            Player,
            RunPhase.BattlePending,
            runSeed: runSeed,
            rngStreamPositions: Counters(battlesStarted));

    /// <summary>One log entry, with the slots and payload a case actually cares about.</summary>
    private static CombatEvent At(
        int tick,
        CombatEventType type,
        byte source = HeroSlot,
        byte target = FirstEnemySlot,
        double value = 0,
        ushort dataId = 0) =>
        new(tick, type, source, target, value, dataId);

    /// <summary>
    /// A short decided fight: the hero takes a blow, heals, and kills one of two enemies.
    /// </summary>
    /// <remarks>
    /// The second enemy is alive at the end on purpose — it is the actor whose starting health is
    /// deliberately unknowable, and a fixture where every enemy dies could not state that.
    /// </remarks>
    private static SimulationResult ShortFight(bool heroWon = true) =>
        new(
            heroWon,
            DurationTicks: 40,
            HeroHpRemaining: 40,
            Log:
            [
                At(0, CombatEventType.BattleStart, NoActorSlot, NoActorSlot),
                At(10, CombatEventType.Attack),
                At(10, CombatEventType.Hit, HeroSlot, FirstEnemySlot, value: 30),
                At(20, CombatEventType.Hit, FirstEnemySlot, HeroSlot, value: 12),
                At(20, CombatEventType.Hit, HeroSlot, SecondEnemySlot, value: 8),
                At(30, CombatEventType.Heal, HeroSlot, HeroSlot, value: 5),
                At(40, CombatEventType.ActorDeath, HeroSlot, FirstEnemySlot),
                At(40, CombatEventType.BattleEnd, NoActorSlot, NoActorSlot),
            ],
            LogHash: 17278238499121983245UL);

    /// <summary>A fight long enough that a case can advance several seconds without finishing it.</summary>
    private static SimulationResult LongFight() =>
        new(
            HeroWon: true,
            DurationTicks: 600,
            HeroHpRemaining: 50,
            Log:
            [
                At(0, CombatEventType.BattleStart, NoActorSlot, NoActorSlot),
                At(300, CombatEventType.Hit, HeroSlot, FirstEnemySlot, value: 11),
                At(600, CombatEventType.BattleEnd, NoActorSlot, NoActorSlot),
            ],
            LogHash: 991UL);

    /// <summary>A boss fight that walks all three phases, one every half second.</summary>
    private static SimulationResult BossFight() =>
        new(
            HeroWon: true,
            DurationTicks: 100,
            HeroHpRemaining: 30,
            Log:
            [
                At(0, CombatEventType.BattleStart, NoActorSlot, NoActorSlot),
                At(10, CombatEventType.PhaseChange, FirstEnemySlot, FirstEnemySlot, value: 1),
                At(20, CombatEventType.PhaseChange, FirstEnemySlot, FirstEnemySlot, value: 2),
                At(30, CombatEventType.PhaseChange, FirstEnemySlot, FirstEnemySlot, value: 3),
                At(100, CombatEventType.BattleEnd, NoActorSlot, NoActorSlot),
            ],
            LogHash: 424242UL);

    /// <summary>
    /// A REAL fight, built through the public stat factory and run by the real simulator, tuned so
    /// that neither side can finish the other and the fight reaches the length cap.
    /// </summary>
    /// <remarks>
    /// 🔒 Both sides are given an enormous health pool and negligible damage on purpose. The claim
    /// this fixture exists for is about the CAP, so the fight has to actually reach it — a block
    /// that resolved early would leave the case pinning an arbitrary log length instead.
    /// </remarks>
    private static SimulationResult StalemateFight() =>
        CombatSimulator.Simulate(
            SeedDerivation.BattleSeed(20260817, 0),
            Unkillable(),
            heroLevel: 1,
            [Unkillable()],
            enemyLevel: 1,
            BootContent.Shipped);

    /// <summary>A stat block with far too much health and far too little bite to end a fight.</summary>
    private static ActorStats Unkillable() =>
        ActorStats.From(new Dictionary<StatId, double>
        {
            [StatId.MAX_HP] = 100_000_000,
            [StatId.ATK] = 1,
            [StatId.DEF] = 100_000,
            [StatId.ASPD] = 0.5,
            [StatId.CRIT] = 0,
            [StatId.CDMG] = 1.5,
            [StatId.LIFESTEAL] = 0,
            [StatId.DODGE] = 0,
            [StatId.BLOCK] = 0,
            [StatId.PEN] = 0,
            [StatId.DMG_PCT] = 0,
            [StatId.DR_PCT] = 0,
            [StatId.HEAL_PCT] = 1,
            [StatId.THORNS] = 0,
        });

    /// <summary>The actor row for one slot, failing legibly when the screen reports none.</summary>
    private static ReplayActor Actor(BattleReplayPresenter presenter, byte slot) =>
        presenter.Actors.FirstOrDefault(actor => actor.ActorId == slot)
        ?? throw new InvalidOperationException(
            $"The replay reports no actor in slot {slot}, so there is no health bar for it at all — " +
            "which is a stronger failure than reporting the wrong number for one.");

    /// <summary>A presenter that has already read a run and holds the given fight.</summary>
    private static async Task<BattleReplayPresenter> Playing(
        SimulationResult fight,
        RecordingGameHost? host = null,
        BattleSpeed speed = BattleSpeed.Single,
        bool reducedMotion = false)
    {
        var presenter = Build(
            host ?? RecordingGameHost.Finding(AnyPlayer(), BattleRun()),
            StubBattleSimulation.Playing(fight),
            speed,
            reducedMotion);

        await presenter.StartAsync(CancellationToken.None);

        return presenter;
    }

    private static BattleReplayPresenter Build(
        RecordingGameHost host,
        IBattleSimulationSource? simulations = null,
        BattleSpeed speed = BattleSpeed.Single,
        bool reducedMotion = false,
        ContentSnapshot? content = null) =>
        new(host,
            BattleContent.Catalogue(content ?? BattleContent.Strings()),
            simulations ?? StubBattleSimulation.Playing(ShortFight()),
            Player,
            Run,
            speed,
            reducedMotion);

    /// <summary>
    /// A prediction that answers whatever a case asked it to, without touching the real simulator.
    /// </summary>
    /// <remarks>
    /// Client-local, like the interface it stands in for. The states the production prediction
    /// cannot reach today — a simulator that threw, a log that came back empty, a fight that is
    /// actually ready — exist only through this, and they are the states the screen's behaviour is
    /// mostly about.
    /// </remarks>
    private sealed class StubBattleSimulation : IBattleSimulationSource
    {
        private readonly BattleSimulationAttempt _attempt;

        private StubBattleSimulation(BattleSimulationAttempt attempt) => _attempt = attempt;

        /// <summary>A prediction that hands back the given fight.</summary>
        internal static StubBattleSimulation Playing(SimulationResult fight) =>
            new(new BattleSimulationAttempt(BattleReadiness.Ready, BattleSeed: 88, SeedDerived: true, fight));

        /// <summary>A prediction answering one named readiness, with a fight only when it is ready.</summary>
        internal static StubBattleSimulation Answering(BattleReadiness readiness) =>
            readiness == BattleReadiness.Ready
                ? Playing(ShortFight())
                : new(new BattleSimulationAttempt(readiness, BattleSeed: 0, SeedDerived: false, Result: null));

        /// <inheritdoc/>
        public BattleSimulationAttempt Simulate(RunSnapshot run)
        {
            ArgumentNullException.ThrowIfNull(run);

            return _attempt;
        }
    }
}
