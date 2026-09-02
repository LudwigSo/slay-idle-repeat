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

    /// <summary>One of the three pet slots — 1 to 3, reserved whether they are filled or not.</summary>
    private const byte PetSlot = 2;

    /// <summary>The slot the log uses for "nobody" — 255.</summary>
    private const byte NoActorSlot = 255;

    /// <summary>The simulator's fixed tick rate, as this suite states it independently.</summary>
    private const int SimulatorTicksPerSecond = 20;

    /// <summary>The fight-length cap, in seconds.</summary>
    private const int FightLengthCapSeconds = 90;

    /// <summary>The stream whose counter says how many battles a run has started.</summary>
    private const string CombatStream = "combat";

    /// <summary>How long <see cref="ShortFight"/> runs, so a case can pin the clamp against it.</summary>
    private const int ShortFightDurationTicks = 40;

    /// <summary>How long <see cref="LongFight"/> runs.</summary>
    private const int LongFightDurationTicks = 600;

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

    /// <summary>
    /// 🔴 <b>Both rows the read answered reach the prediction, and dropping one is how the fight went
    /// missing for a whole milestone.</b>
    /// </summary>
    /// <remarks>
    /// 🔒 Stated over the rows themselves rather than over the readiness, because the readiness cannot
    /// see the difference: a prediction handed the run alone still answers, and the screen still draws
    /// whatever it answers. This is the assertion that would have gone red.
    /// </remarks>
    [Fact]
    public async Task The_screen_hands_the_prediction_the_profile_row_as_well_as_the_run()
    {
        var player = AnyPlayer();
        var run = BattleRun(runSeed: 31337);
        var simulation = StubBattleSimulation.Answering(BattleReadiness.Ready);

        await Build(RecordingGameHost.Finding(player, run), simulation)
            .StartAsync(CancellationToken.None);

        simulation.AskedAboutRun.ShouldBe(
            run, "the run is what scales the enemy and names the tile being fought.");
        simulation.AskedAboutPlayer.ShouldBe(
            player,
            "and the profile row is what the HERO is composed from — the loadout, the Legend Level, " +
            "the stock the equipped items live in. A screen that read it and then predicted the fight " +
            "without it can only animate a hero the rules invented, which is why this is asserted " +
            "against the row the read actually answered with rather than against 'not null'.");
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

    // ---- every caption comes out of the content set ---------------------------------------------

    /// <summary>
    /// 🔒 Every fixed caption on the screen, resolved through the catalogue rather than written into
    /// the screen.
    /// </summary>
    /// <remarks>
    /// 🔴 The fixture value of a key is the key with a marker in front, so a caption written as an
    /// English literal in the presenter cannot match one — which is the whole reason the fixture is
    /// built that way, and it buys nothing until something actually READS the captions. X-04 wants
    /// every user-facing string keyed in EN and DE from day one; a literal here ships a screen that
    /// stays English in German for as long as nobody looks.
    /// </remarks>
    [Fact]
    public void Every_fixed_caption_on_the_screen_is_resolved_from_the_content_set()
    {
        var presenter = Build(RecordingGameHost.Finding(AnyPlayer(), BattleRun()));

        string[] drawn =
        [
            presenter.Title,
            presenter.HeroLabel,
            presenter.EnemyLabel,
            presenter.SpeedLabel,
            presenter.SpeedSingleText,
            presenter.SpeedDoubleText,
            presenter.SpeedTripleText,
            presenter.SkipText,
        ];

        drawn.ShouldBe(
            [.. FixedCaptionKeys.Select(BattleContent.EnglishValueOf)],
            "every one of these is authored, named by the battle content document and paid for in " +
            "two locales. A caption that came from anywhere else is a string the translator never " +
            "sees and the player reads in the wrong language — and the speed and skip captions are " +
            "the labels on the only four controls this screen has.");
    }

    /// <summary>The keys behind the captions above, in the order they are read.</summary>
    private static readonly string[] FixedCaptionKeys =
    [
        BattleContent.TitleNameKey,
        BattleContent.HeroLabelKey,
        BattleContent.EnemyLabelKey,
        BattleContent.SpeedLabelKey,
        BattleContent.SpeedSingleActionKey,
        BattleContent.SpeedDoubleActionKey,
        BattleContent.SpeedTripleActionKey,
        BattleContent.SkipActionKey,
    ];

    /// <summary>
    /// 🔒 A caption the content set does not carry still reads as something a player can report.
    /// </summary>
    /// <remarks>
    /// The skip is the one control on this screen an accessibility clause requires, so a build whose
    /// locale has lost its caption must still draw a labelled button. Falling back to the key leaves
    /// an ugly control; falling back to blank leaves an invisible one, on the only way off a screen
    /// a player may not be able to watch.
    /// </remarks>
    [Fact]
    public void A_caption_the_content_set_is_missing_falls_back_to_its_key_rather_than_to_nothing()
    {
        var presenter = Build(
            RecordingGameHost.Finding(AnyPlayer(), BattleRun()),
            content: BattleContent.Authoring(BattleContent.SkipActionKey));

        presenter.SkipText.ShouldBe(
            BattleContent.SkipActionKey,
            "an unauthored caption has to degrade to its own key: a blank skip button is a control " +
            "a player cannot see on the one screen an accessibility clause promises they can always " +
            "leave, and it is invisible to whoever would otherwise report the missing string.");
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
    /// 🔒 The four sentences are four DIFFERENT sentences, <b>as authored</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 Stated over the shipped locale, not over a fixture. Every fixture string in this suite is
    /// derived from its own key, so four distinct keys give four distinct values by construction and
    /// a fixture-based version of this case could never fail whatever anyone wrote in
    /// <c>en.json</c>. The claim is about what a player reads.
    /// </remarks>
    /// <para>
    /// 🔒 There were five. The fifth said the hero's power was not implemented — the one sentence in the
    /// set that promised a player nothing they did could help, and the one the shipped build showed for
    /// every fight in the game. It is retired with its state.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_four_stall_sentences_are_four_different_authored_sentences()
    {
        var authored = BattleContent.StallCauses.Select(cause =>
        {
            BattleContent.ShippedEnglish.TryGetValue(cause.SentenceKey, out var sentence).ShouldBeTrue(
                $"'{cause.SentenceKey}' is not in the shipped English locale, so the cause it names " +
                "has no sentence and a player meeting it is shown its key.");

            return sentence;
        }).ToArray();

        authored.Length.ShouldBe(4);
        authored.ShouldAllBe(sentence => sentence.Length > 0);
        authored.Distinct(StringComparer.Ordinal).Count().ShouldBe(
            authored.Length,
            "two of the four stall causes are AUTHORED with the same sentence, so a player meeting " +
            "one of them is told about the other. The whole reason these are four states rather than " +
            "one is that they are escaped four different ways — finishing the tile, reporting a " +
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

        var attempt = Predicting().Simulate(AnyRehydratablePlayer(), run);

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
    /// <remarks>
    /// 🔴 The two runs are deliberately NOT identical. A revive pays to stand the hero back up, so
    /// the row that comes back differs in exactly the fields a revive touches — health and the gold
    /// or ad use it cost. Handing the derivation two byte-identical rows would assert only that a
    /// pure function is pure, which every implementation that reads either field satisfies; varying
    /// what a revive varies is what makes this a statement about the seed depending on the run's own
    /// seed and its battle counter and on nothing else the row happens to carry.
    /// </remarks>
    [Fact]
    public void A_revived_battle_re_derives_the_seed_the_first_attempt_was_fought_under()
    {
        var simulation = Predicting();
        var firstAttempt = BattleRun(runSeed: 991, battlesStarted: 2, currentHp: 0, gold: 400);
        var afterRevive = BattleRun(runSeed: 991, battlesStarted: 2, currentHp: 50, gold: 150);

        var before = simulation.Simulate(AnyRehydratablePlayer(), firstAttempt);
        var after = simulation.Simulate(AnyRehydratablePlayer(), afterRevive);

        after.BattleSeed.ShouldBe(
            before.BattleSeed,
            "a revive re-enters the battle rather than beginning one, so a second seed here " +
            "would put the player back into a DIFFERENT fight than the one they just lost — with the " +
            "enemy's rolls reshuffled by the act of paying to continue.");
    }

    [Fact]
    public void A_further_battle_in_the_same_run_is_a_different_fight()
    {
        var simulation = Predicting();

        var first = simulation.Simulate(AnyRehydratablePlayer(), BattleRun(runSeed: 991, battlesStarted: 2));
        var second = simulation.Simulate(AnyRehydratablePlayer(), BattleRun(runSeed: 991, battlesStarted: 3));

        second.BattleSeed.ShouldNotBe(
            first.BattleSeed,
            "without the counter in the derivation every fight of a run would play out " +
            "identically, which the previous case would happily agree with — it only proves the seed " +
            "is STABLE, not that it depends on which battle this is.");
    }

    // ---- 🔒 the fight itself, over the shipped content ------------------------------------------

    /// <summary>
    /// 🔴 <b>The case the whole screen exists for, and the one that was missing.</b>
    /// </summary>
    /// <remarks>
    /// 🔴 This suite used to assert the opposite — that the production prediction refuses at the hero's
    /// stat block — and quoted the refusal's own reasoning as its justification. That reasoning had been
    /// false since <c>M7-06b</c> made <c>HeroBuild</c> and <c>RunBattle</c> public, so four green cases
    /// were pinning a client that could not fight. Stated over the <b>shipped</b> content and the
    /// <b>production</b> prediction, because that pair is the thing that was broken: every other case
    /// on this screen drives a double.
    /// </remarks>
    [Fact]
    public void The_prediction_fights_the_battle_the_run_is_standing_in()
    {
        var attempt = Predicting().Simulate(AnyRehydratablePlayer(), OnAFightTile());

        attempt.Readiness.ShouldBe(
            BattleReadiness.Ready,
            "a run standing on an enemy tile with a drawn combat counter is a fight the rules can " +
            "compose from the two persisted rows, and every piece of that composition is public. A " +
            "refusal here is a client that cannot fight — which is exactly what shipped, and what a " +
            "player met as a status line telling them their hero's power was not implemented.");
        attempt.Result.ShouldNotBeNull(
            "the readiness says a fight is in hand, so a null result would leave the screen " +
            "reporting a fight it cannot animate and submitting nothing — the same dead end under a " +
            "different name.");
        attempt.Result.Log.ShouldNotBeEmpty(
            "an empty log is its own readiness. A fight reported as ready with no events would " +
            "play in a single frame and confirm a result nobody watched.");
        attempt.Result.DurationTicks.ShouldBeGreaterThan(
            0,
            "a fight of zero length is not a fight, and the playhead would land on the end of it " +
            "before the first frame was drawn.");
    }

    /// <summary>
    /// 🔒 <b>The whole client half, end to end: a real fight is predicted, watched, and confirmed.</b>
    /// </summary>
    /// <remarks>
    /// 🔒 Every other playback case on this screen drives a fixture fight through a double, which is
    /// right — they are about timing, bars and captions. None of them could see a prediction that never
    /// produces a fight, and that is precisely what shipped. This one runs the <b>production</b>
    /// prediction over the <b>shipped</b> content and asserts the confirming command comes out the far
    /// end carrying that fight's own hash.
    /// </remarks>
    [Fact]
    public async Task A_really_predicted_fight_is_watched_to_its_end_and_confirmed()
    {
        var run = OnAFightTile();
        var host = RecordingGameHost.Finding(AnyRehydratablePlayer(), run);
        var fight = Predicting().Simulate(AnyRehydratablePlayer(), run).Result.ShouldNotBeNull();
        var presenter = Build(host, Predicting());

        await presenter.StartAsync(CancellationToken.None);
        var submission = await presenter.SkipAsync(CancellationToken.None);

        submission.ShouldBe(
            BattleSubmission.Submitted,
            "a screen holding a real fight has a result to report, and a refusal here is the dead " +
            "end this change exists to close: nothing submitted, the run parked in the battle phase, " +
            "and every other command refused while it stands there.");
        host.SubmitCommand.ShouldBeOfType<ConfirmBattleResultCommand>()
            .LogHash.ShouldBe(
                fight.LogHash.ToString(CultureInfo.InvariantCulture),
                "and the hash it reports has to be the hash of the fight the prediction actually " +
                "produced — the server recomputes the same fight from the same seed and compares, so a " +
                "number from anywhere else reads as a forged log by an honest player.");
    }

    /// <summary>
    /// 🔴 <b>The probe that discriminates: the hero's row reaches the fight.</b>
    /// </summary>
    /// <remarks>
    /// 🔒 Two Legend Levels over one battle seed. A prediction that composed the hero from the run
    /// alone — or from anything but the profile row it was handed — answers both of these with the
    /// same log, and every assertion in the case above would still pass. That is the shape of the
    /// defect this whole change is about: the rows were both in hand and one was dropped.
    /// </remarks>
    [Fact]
    public void The_players_own_row_decides_the_fight_and_not_the_run_alone()
    {
        var simulation = Predicting();
        var run = OnAFightTile(runSeed: 5150);

        var novice = simulation.Simulate(PlayerState.Rehydratable(Player, legendLevel: 1), run).Result;
        var veteran = simulation.Simulate(PlayerState.Rehydratable(Player, legendLevel: 60), run).Result;

        novice.ShouldNotBeNull();
        veteran.ShouldNotBeNull();
        veteran.LogHash.ShouldNotBe(
            novice.LogHash,
            "the same battle seed fought by a level-1 hero and a level-60 one has to be two " +
            "different fights, or the hero this screen is animating is not the player's hero. The " +
            "seed, the tile and the chapter are identical here — the Legend Level is the only thing " +
            "that moved, and it moves through the row this prediction spent a milestone ignoring.");
    }

    [Fact]
    public void A_run_whose_counter_does_not_name_a_battle_is_refused_at_the_seed_and_says_so()
    {
        var attempt = Predicting()
            .Simulate(AnyRehydratablePlayer(), BattleRun(runSeed: 7, battlesStarted: 0));

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

        var attempt = Predicting().Simulate(AnyRehydratablePlayer(), run);

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

        var attempt = Predicting().Simulate(AnyRehydratablePlayer(), run);

        attempt.Readiness.ShouldBe(
            BattleReadiness.PhaseNotBattle,
            "a run that is walking the board has no open fight, and predicting one anyway " +
            "would let this screen submit a battle result for a battle nobody entered.");
        attempt.SeedDerived.ShouldBeFalse(
            "the counter of a run with no open battle names the LAST fight, not a current " +
            "one, so a seed reported here would be a real number for the wrong fight.");
    }

    /// <summary>
    /// 🔒 The seed the prediction derived reaches the SCREEN, and so a bug report.
    /// </summary>
    /// <remarks>
    /// 🔴 The split between the two properties is the load-bearing finding made visible: reporting
    /// a seed of zero with no flag beside it is indistinguishable from deriving zero, and zero is a
    /// legal seed. A screen that dropped either would leave "the fight did not play" as the whole of
    /// what anybody can say about it.
    /// </remarks>
    [Fact]
    public async Task The_screen_reports_the_seed_the_prediction_derived_and_that_it_derived_one()
    {
        var attempt = new BattleSimulationAttempt(
            BattleReadiness.SimulatorFailed, BattleSeed: 0, SeedDerived: true, Result: null);
        var presenter = Build(
            RecordingGameHost.Finding(AnyPlayer(), BattleRun()),
            StubBattleSimulation.Attempting(attempt));

        await presenter.StartAsync(CancellationToken.None);

        presenter.BattleSeed.ShouldBe(
            0UL,
            "the seed is what turns 'the fight did not play' into a report somebody can act on, " +
            "because it is the one number the server and the client can compare afterwards.");
        presenter.SeedDerived.ShouldBeTrue(
            "and zero is a legal seed, so the flag beside it is the only thing separating a real " +
            "derivation of zero from an attempt that never reached the derivation at all — which is " +
            "exactly the difference between filing this against the simulator and against the run.");
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

        presenter.TotalTicks.ShouldBe(
            ShortFightDurationTicks,
            "the fight's own length is what the playhead is clamped to, so a screen that never " +
            "learned it would satisfy the assertion below by holding both numbers at zero.");
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

        beforeCycle.ShouldBe(
            SimulatorTicksPerSecond,
            "the playhead has to have MOVED before a case can say a speed change does not move it " +
            "— a screen frozen at zero would agree with both assertions below and prove neither.");

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
    /// 🔒 The floor under both enum-driven sweeps, by NAMED MEMBER and never by count. A rule
    /// quantified over an enum passes vacuously the day the enum is emptied or its members are
    /// renamed away — and the second sweep it floors is the S6 negative control, where a member
    /// quietly leaving the set is a readiness in which nobody ever again checks that the screen
    /// submits nothing.
    /// </summary>
    /// <remarks>
    /// 🔴 Every member is named, not the three the skip rule leans on hardest. A count would be no
    /// floor at all — a rename keeps the count — and a partial list is a floor with a hole in it
    /// exactly where the enum is most likely to be reorganised.
    /// </remarks>
    [Fact]
    public void The_readiness_vocabulary_still_names_every_state_the_two_sweeps_are_quantified_over()
    {
        var members = Enum.GetValues<BattleReadiness>();

        members.ShouldContain(
            BattleReadiness.Ready,
            "without it the skip sweep never once exercises a screen that HAS a fight, so 'skip is " +
            "always offered' would be proven only over screens where skipping does nothing.");
        members.ShouldContain(
            BattleReadiness.NoRun,
            "a player who reached a battle screen with no run at all is the one stall a navigation " +
            "bug produces, and it dropping out of the sweeps takes the S6 guard with it.");
        members.ShouldContain(
            BattleReadiness.PhaseNotBattle,
            "this is the state a screen opened over the wrong run lands in, and it is the one where " +
            "submitting a fabricated result would confirm a battle the run never entered.");
        members.ShouldContain(
            BattleReadiness.SeedUnavailable,
            "a run that no longer names its own battle is the corrupt row of the set, and a screen " +
            "that invented a hash for it would report a result for a fight nothing can identify.");
        members.Select(member => member.ToString()).ShouldNotContain(
            "HeroStatsUnavailable",
            "this state told players their hero's power was unimplemented after it had been " +
            "implemented, and it was the answer to EVERY fight. Reintroducing it — or reusing its " +
            "value 5 — would put that sentence back in front of a player, so the sweep asserts its " +
            "absence rather than merely not mentioning it.");
        members.ShouldContain(
            BattleReadiness.SimulatorFailed,
            "a simulator that threw is the state most tempting to treat as a transient nothing, " +
            "which is exactly how a screen learns to submit a result it never computed.");
        members.ShouldContain(
            BattleReadiness.LogEmpty,
            "and this is the state where a skip is most tempting to withdraw — there is nothing to " +
            "skip to — which is precisely why it has to stay in the sweep.");
        members.ShouldContain(
            BattleReadiness.ReadUnavailable,
            "a read that never answered is the one stall whose answer is retry, and a screen that " +
            "cannot report it is a screen a player is stranded on with no instruction.");
    }

    [Fact]
    public async Task Skipping_jumps_to_the_end_without_replaying_what_was_skipped()
    {
        var presenter = await Playing(LongFight());

        await presenter.SkipAsync(CancellationToken.None);

        presenter.TotalTicks.ShouldBe(
            LongFightDurationTicks,
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

    /// <summary>
    /// 🔒 The other half of "confirmed once": a fight still playing has not been confirmed at all.
    /// </summary>
    [Fact]
    public async Task A_fight_still_playing_has_not_confirmed_anything_yet()
    {
        var host = RecordingGameHost.Finding(AnyPlayer(), BattleRun());
        var presenter = await Playing(LongFight(), host);

        var submission = await presenter.AdvanceAsync(1.0, CancellationToken.None);

        presenter.CurrentTick.ShouldBe(
            SimulatorTicksPerSecond,
            "the playhead has to have MOVED and stopped short of the end, or this says nothing " +
            "about a fight in progress — a screen frozen at zero submits nothing either, and would " +
            "satisfy every assertion below while animating no fight at all.");
        presenter.TotalTicks.ShouldBe(
            LongFightDurationTicks,
            "and it is short of the end only if the screen knows where the end is.");
        presenter.Complete.ShouldBeFalse(
            "the fight has to still be running for this to be a statement about a running fight.");
        submission.ShouldBe(
            BattleSubmission.NothingToSubmit,
            "an advance mid-fight owes nothing, and reporting it as a submission would have the " +
            "scene tear the screen down and hand the run on while the player is still watching.");
        host.SubmitCallCount.ShouldBe(
            0,
            "and confirming a result before the log has finished would close the battle on an " +
            "outcome the player has not been shown, from a frame that has no idea how it ended.");
    }

    /// <summary>
    /// 🔒 The status line closes the fight by saying which way it went.
    /// </summary>
    /// <remarks>
    /// Both keys are authored and named by the battle content document, so a screen that never
    /// resolved either would leave two paid-for translations unreachable and the player looking at
    /// a finished fight with no word on whether they won it.
    /// </remarks>
    [Theory]
    [InlineData(true, BattleContent.VictoryStatusKey)]
    [InlineData(false, BattleContent.DefeatStatusKey)]
    public async Task A_finished_replay_says_which_way_the_fight_went(bool heroWon, string sentenceKey)
    {
        var presenter = await Playing(ShortFight(heroWon: heroWon));

        await presenter.AdvanceAsync(30, CancellationToken.None);

        presenter.HeroWon.ShouldBe(
            heroWon,
            "the outcome is the one thing the whole screen exists to deliver, and a screen that " +
            "did not carry it could not tell the two sentences below apart either.");
        presenter.StatusText.ShouldBe(
            BattleContent.EnglishValueOf(sentenceKey),
            "a defeat announced as a victory is the worst sentence this screen could print: the " +
            "player is about to be offered a revive for a fight they were just told they won.");
    }

    /// <summary>
    /// 🔒 A confirmation the rules layer refuses is reported, not swallowed.
    /// </summary>
    /// <remarks>
    /// 🔴 The run is parked in the battle phase and every command but this one is refused there, so
    /// a refused confirmation leaves the run stuck for good — the board will not roll and the revive
    /// is itself one of the commands the phase blocks. A screen that dropped the refusal on the
    /// floor would look exactly like one that succeeded.
    /// </remarks>
    [Fact]
    public async Task A_result_the_rules_layer_refuses_is_reported_rather_than_swallowed()
    {
        var host = RecordingGameHost
            .Finding(AnyPlayer(), BattleRun())
            .RefusingCommands(RejectionReason.ILLEGAL_STATE);
        var presenter = await Playing(ShortFight(), host);

        var submission = await presenter.AdvanceAsync(30, CancellationToken.None);

        submission.ShouldBe(
            BattleSubmission.RefusedByRules,
            "a refusal is an answer rather than a success, and a caller that could not tell them " +
            "apart would hand the run on to a board that is still standing in the battle phase.");
        presenter.RulesRejection.ShouldBe(
            RejectionReason.ILLEGAL_STATE,
            "and the reason has to survive to the screen: it is the only thing distinguishing a " +
            "run whose battle was already confirmed from a malformed hash, and the two are fixed by " +
            "completely different people.");
        presenter.StatusText.ShouldBe(
            BattleContent.EnglishValueOf(BattleContent.RefusedStatusKey),
            "a refusal the player is never told about is a screen that appears to have worked and " +
            "a run that has silently stopped — the failure mode a support ticket cannot describe.");
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

        presenter.CurrentTick.ShouldBe(
            SimulatorTicksPerSecond,
            "the playhead has to be standing between two of the fight's phase changes for the " +
            "assertion below to be about which one it reads.");
        presenter.CurrentBossPhase.ShouldBe(
            2,
            "the fight's third phase is in the log already and the playhead has not reached it, " +
            "so a band folded over the WHOLE log rather than over the part that has played would " +
            "announce a transition the player has not watched happen — a full-width flash for a " +
            "phase the boss is still a third of its health away from.");
    }

    /// <summary>
    /// 🔒 The band reads the phase-change event's own VALUE rather than counting the events crossed.
    /// </summary>
    /// <remarks>
    /// The two agree in every log the simulator emits today — see <see cref="ElevatedPhaseFight"/>
    /// — so this is the only case that separates them, and it is worth separating because the thing
    /// that makes them agree is the rules layer's private emission order and not any promise made
    /// to a client.
    /// </remarks>
    [Fact]
    public async Task The_phase_band_reads_the_phase_the_event_names_rather_than_counting_events()
    {
        var presenter = await Playing(ElevatedPhaseFight());

        await presenter.AdvanceAsync(1.0, CancellationToken.None);

        presenter.CurrentBossPhase.ShouldBe(
            3,
            "one phase change has been crossed and it names the third phase, so a screen that " +
            "counted them would put the band on phase ONE — announcing the opening of a fight at " +
            "the moment it enters its final phase, with the drums of the wrong transition.");
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
        presenter.PhaseBandText.ShouldBe(
            BattleContent.EnglishValueOf(BattleContent.PhaseTwoNameKey),
            "and it announces it by NAME. The log reports a phase as a bare number and the table " +
            "that gives that number a meaning is inside the rules assembly, so a band that drew the " +
            "number would flash '2' across the screen at the moment a boss changes what it does.");

        await presenter.AdvanceAsync(1.0, CancellationToken.None);
        presenter.PhaseBandVisible.ShouldBeFalse(
            "and a band that never came down would cover the fight it was announcing for " +
            "the rest of the battle.");
        presenter.PhaseBandText.ShouldBeEmpty(
            "a band that is down has nothing to say, and a caption left standing behind it is a " +
            "phase name a scene can still draw once the band that framed it has gone.");
    }

    /// <summary>
    /// 🔒 The accessibility clause, stated as behaviour rather than as the number itself: reduced
    /// motion shortens every animation on this screen to a tenth of a second.
    /// </summary>
    /// <remarks>
    /// 🔒 The playhead is deliberately parked BETWEEN the two dwells — four tenths of a second past
    /// the phase change, which is past the reduced-motion tenth and short of the ordinary six
    /// tenths. A fixture whose phase change sat on the playhead itself would put zero elapsed time
    /// behind both dwells, leaving the band up under each and the two assertions below in flat
    /// contradiction.
    /// </remarks>
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
            "reduced motion shortens every animation here to a tenth of a second, and the " +
            "playhead stands four tenths past the transition — four times that. A player who set it " +
            "because full-width flashes make them ill still gets the flash otherwise.");
    }

    // ---- health bars: a denominator from the log, both ends derived against it -------------------

    /// <summary>
    /// 🔒 The case that pins which of the two answers wins for the hero. Its spawned maximum and
    /// the health it opens on are different numbers, so a screen that took the maximum as the opening
    /// value fails here.
    /// </summary>
    [Fact]
    public async Task The_heros_starting_health_is_recovered_from_what_it_lost_and_regained()
    {
        var presenter = await Playing(ShortFight());

        await presenter.AdvanceAsync(30, CancellationToken.None);

        var hero = Actor(presenter, HeroSlot);

        hero.MaxHp!.Value.ShouldBe(
            HeroMaxHp,
            tolerance: HealthTolerance,
            customMessage:
            "the bar is scaled to the maximum the log spawned the actor with, which is the whole " +
            "reason the log carries one.");
        hero.StartingHp!.Value.ShouldBe(
            HeroHpAtTheStart,
            tolerance: HealthTolerance,
            customMessage:
            "a hero opens on the health its run persisted, not on full, and that number is only " +
            "recoverable by reconstruction: what was left, plus every point taken off, minus every " +
            "point healed back. Drawing it at its maximum instead would hide every point of damage " +
            "the hero carried in from an earlier fight.");
        hero.StartingHp.Value.ShouldBeLessThan(
            hero.MaxHp.Value,
            "and the fixture's two numbers differ on purpose, so a screen that confused them fails " +
            "here rather than passing on a hero that happened to be at full health.");
        hero.EndingHp!.Value.ShouldBe(
            HeroHpAtTheEnd,
            tolerance: HealthTolerance,
            customMessage: "and the finishing value is the one number the result states outright.");
    }

    [Fact]
    public async Task An_actor_the_log_records_a_death_for_is_anchored_at_zero()
    {
        var presenter = await Playing(ShortFight());

        await presenter.AdvanceAsync(30, CancellationToken.None);

        var slain = Actor(presenter, FirstEnemySlot);

        slain.EndingHp!.Value.ShouldBe(
            0,
            tolerance: HealthTolerance,
            customMessage:
            "a death is the one thing that fixes an actor's final health outright, and it must win " +
            "over the walk — a walk that had drifted a fraction above zero would leave a sliver of " +
            "bar standing under an actor the log has just killed.");
        slain.StartingHp!.Value.ShouldBe(
            DamageTakenByTheDyingEnemy,
            tolerance: HealthTolerance,
            customMessage: "an actor that ended at zero started at exactly the damage it absorbed.");
        slain.MaxHp!.Value.ShouldBe(
            DyingEnemyMaxHp,
            tolerance: HealthTolerance,
            customMessage: "and its bar is scaled to the maximum it was spawned with.");
    }

    /// <summary>
    /// 🔴 <b>The case this screen was broken by, asserted as the presence it is now.</b> An enemy
    /// that survives anchors neither the hero's equation nor a death's, and for as long as the log
    /// carried no maximum it was reported as unknown and drawn with no bar at all — which, since a
    /// losing hero kills nothing, was every enemy of every fight a player lost.
    /// </summary>
    [Fact]
    public async Task A_surviving_enemys_bar_is_drawn_from_the_maximum_the_log_spawned_it_with()
    {
        var presenter = await Playing(ShortFight());

        await presenter.AdvanceAsync(30, CancellationToken.None);

        var survivor = Actor(presenter, SecondEnemySlot);

        survivor.MaxHp!.Value.ShouldBe(
            SurvivingEnemyMaxHp,
            tolerance: HealthTolerance,
            customMessage:
            "the surviving enemy's maximum comes from its spawn event and from nothing else — no " +
            "derivation over this log can reach it, which is why the log states it.");
        survivor.StartingHp!.Value.ShouldBe(
            SurvivingEnemyMaxHp,
            tolerance: HealthTolerance,
            customMessage:
            "nothing but the hero carries health between fights, so an enemy opens on its maximum.");
        survivor.EndingHp!.Value.ShouldBe(
            SurvivingEnemyMaxHp - DamageDealtToTheSurvivingEnemy,
            tolerance: HealthTolerance,
            customMessage:
            "and its finish is its opening less what it absorbed — which is what lets a SKIPPED fight " +
            "leave its bar where a watched one would have walked it, rather than at full.");
    }

    /// <summary>
    /// 🔒 The absence that is still an absence: an actor some event names and no spawn ever
    /// introduced.
    /// </summary>
    /// <remarks>
    /// Not a log the simulator emits — every actor is spawned before anything happens to it — so this
    /// pins that a malformed log reads as malformed rather than as an actor at full health. A bar
    /// invented here would be wrong by exactly however far the guess was off, and a player watching a
    /// bar is reading the fraction, not the number.
    /// </remarks>
    [Fact]
    public async Task An_actor_the_log_never_spawned_is_reported_as_unknown_rather_than_guessed()
    {
        var presenter = await Playing(FightWithAnUnspawnedActor());

        await presenter.AdvanceAsync(30, CancellationToken.None);

        var unspawned = Actor(presenter, SecondEnemySlot);

        unspawned.MaxHp.ShouldBeNull(
            "no spawn event names this slot, so nothing states its maximum.");
        unspawned.StartingHp.ShouldBeNull(
            "and with neither a spawn nor a death nor a reported remainder, no arithmetic reaches " +
            "either end of its bar.");
        unspawned.EndingHp.ShouldBeNull();

        Actor(presenter, HeroSlot).MaxHp.ShouldNotBeNull(
            "while the hero of the same log still has its own — one malformed row does not cost the " +
            "rest of the roster their bars.");
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

    // ---- 🔒 what one event of the log asks the screen to draw ----------------------------------

    /// <summary>
    /// 🔒 The crit↔hit pairing, which is a reconstruction of the rules layer's private per-attack
    /// emission order and the only way a client can colour the number.
    /// </summary>
    /// <remarks>
    /// A critical hit is its own event rather than a flag on the blow, and it is emitted BEFORE the
    /// blow it describes. So the announcement has to be held across the events between the two and
    /// then spent — and the spending is what this case is about: an implementation that set the flag
    /// and never cleared it would colour every number in the fight gold from the first crit onward,
    /// which is a screen that shouts about every blow and therefore about none.
    /// </remarks>
    [Fact]
    public async Task A_blow_the_log_announced_as_critical_is_drawn_as_one_and_the_next_blow_is_not()
    {
        var presenter = await Playing(CritFight());

        await presenter.AdvanceAsync(0.5, CancellationToken.None);

        TheBlowIn(presenter).Floater.ShouldBe(
            ReplayFloater.Crit,
            "the crit event carries no value of its own and the hit that follows carries no " +
            "flag, so the only thing that can tell the screen to draw this number yellow and larger " +
            "is the announcement held over from the event before it.");

        await presenter.AdvanceAsync(0.5, CancellationToken.None);

        TheBlowIn(presenter).Floater.ShouldBe(
            ReplayFloater.Hit,
            "and the announcement belongs to ONE blow. A flag that survived its own hit " +
            "would paint every later number in the fight as a critical one, which tells the player " +
            "nothing at the exact moment the design wants them told something.");
    }

    /// <summary>
    /// 🔒 The case the pairing is most likely to get wrong: a crit whose blow never lands.
    /// </summary>
    /// <remarks>
    /// 🔴 The per-attack sequence is <c>Attack → Miss → Crit → Block → WardBroken → Hit</c>, and a
    /// ward that swallows the whole blow ends it with no <c>Hit</c> at all. The announcement is then
    /// about a number that is never drawn, and an implementation that only cleared the flag on a hit
    /// would carry it forward and gild the next unrelated blow — a critical hit the player is shown
    /// for an attack that was never critical, several ticks after the one that was.
    /// </remarks>
    [Fact]
    public async Task A_critical_announcement_whose_blow_never_lands_does_not_colour_the_next_one()
    {
        var presenter = await Playing(AbsorbedCritFight());

        await presenter.AdvanceAsync(1.5, CancellationToken.None);

        presenter.StepCues.Count(cue => cue.Burst == ReplayBurst.CritPop).ShouldBe(
            1,
            "the announcement itself is still drawn — something did happen — so this case is " +
            "about the number that follows it rather than about the announcement going missing.");
        TheBlowIn(presenter).Floater.ShouldBe(
            ReplayFloater.Hit,
            "the announced blow was absorbed whole and emitted no number at all, so the next " +
            "attack's ordinary hit is the first number the screen draws. Colouring it as the critical " +
            "one attributes a blow to an attack that never landed it.");
    }

    /// <summary>
    /// 🔒 The announcement bursts before the number, because that is the order the log puts them in.
    /// </summary>
    [Fact]
    public async Task A_critical_announcement_bursts_ahead_of_the_number_it_belongs_to()
    {
        var presenter = await Playing(CritFight());

        await presenter.AdvanceAsync(0.5, CancellationToken.None);

        presenter.StepCues.Select(cue => cue.Burst).ShouldBe(
            [ReplayBurst.CritPop, ReplayBurst.HitSpark],
            "the two are one blow told in two events, and a screen that reordered them would " +
            "pop the announcement after the number it was announcing.");
    }

    /// <summary>
    /// 🔒 Each kind of number is told apart, because the design draws each of them differently.
    /// </summary>
    /// <remarks>
    /// 🔴 A KIND rather than a colour. The half that draws owns the palette; this half owns the
    /// reading of the log that decides which entry of it applies — including that a damage-over-time
    /// tick carries a SIGNED delta and is drawn unsigned, because a minus sign in front of a floating
    /// damage number reads as healing.
    /// </remarks>
    [Fact]
    public async Task Every_kind_of_floating_number_the_design_draws_is_told_apart_by_its_own_event()
    {
        var presenter = await Playing(CueFight());

        await presenter.AdvanceAsync(CueFightSeconds, CancellationToken.None);

        var floaters = presenter.StepCues
                                .Where(cue => cue.Floater != ReplayFloater.None)
                                .Select(cue => (cue.Floater, cue.FloaterAmount))
                                .ToArray();

        floaters.ShouldBe(
            [
                (ReplayFloater.Crit, CueFightBlow),
                (ReplayFloater.DamageOverTime, CueFightTick),
                (ReplayFloater.Heal, CueFightHeal),
            ],
            "the design draws four kinds of number and pays for the difference in the one " +
            "moment a player is reading the screen fastest: an ordinary blow, a critical one, healing " +
            "and a tick of something already on them. Collapsing any two of them leaves the player " +
            "unable to tell being healed from being burned — and the tick's size is drawn unsigned, " +
            "because a minus sign in front of a damage number reads as the opposite of what happened.");
    }

    /// <summary>🔒 The bursts, likewise, and the actor each one belongs to.</summary>
    [Fact]
    public async Task A_pets_ability_bursts_over_the_pet_that_used_it_rather_than_over_its_target()
    {
        var presenter = await Playing(CueFight());

        await presenter.AdvanceAsync(CueFightSeconds, CancellationToken.None);

        var acted = presenter.StepCues.Single(cue => cue.ActorId == PetSlot);

        acted.Burst.ShouldBe(
            ReplayBurst.HitSpark,
            "a pet doing something is the one moment a player sees that their pets are in " +
            "the fight at all, since nothing else on the screen names them.");
        acted.Side.ShouldBe(
            ReplaySide.Pet,
            "and it belongs to the SOURCE of the event rather than its target — every other " +
            "event on this screen is drawn over the actor it happened TO, so a pet ability read the " +
            "same way would flash over the enemy the pet was helping against.");
    }

    /// <summary>🔒 A death, which is the one event that fixes an actor's final health.</summary>
    [Fact]
    public async Task A_death_takes_its_actor_to_nothing_and_puffs_once()
    {
        var presenter = await Playing(CueFight());

        await presenter.AdvanceAsync(CueFightSeconds, CancellationToken.None);

        var fell = presenter.StepCues.Single(cue => cue.Died);

        fell.ActorId.ShouldBe(
            FirstEnemySlot,
            "a death names the actor that DIED in its target and whatever killed it in its " +
            "source, so a screen reading the wrong end of it would grey out the winner.");
        fell.Health.ShouldNotBeNull(
            "the death is what fixes this actor's final health, so a cue that carried none " +
            "would leave the bar wherever the last blow happened to stop.");
        fell.Health.Value.ShouldBe(
            0,
            tolerance: HealthTolerance,
            customMessage:
            "a bar with a sliver left under an actor the log has just killed is the one " +
            "thing on this screen a player can prove wrong by looking at it.");
        fell.Burst.ShouldBe(ReplayBurst.DeathPuff);
    }

    /// <summary>🔒 A status arrives with a STACK COUNT rather than a potency, and leaves with none.</summary>
    [Fact]
    public async Task A_status_arrives_with_its_stack_count_and_leaves_with_none()
    {
        var presenter = await Playing(CueFight());

        await presenter.AdvanceAsync(CueFightSeconds, CancellationToken.None);

        var stacked = presenter.StepCues.Where(cue => cue.StatusId is not null).ToArray();

        stacked.ShouldAllBe(cue => cue.StatusId == CueFightStatus);
        stacked.Select(cue => cue.StatusStacks).ShouldBe(
            [CueFightStacks, 0],
            "an applied status carries how many are stacked rather than how hard it bites, " +
            "and the design puts that number on the chip under the bar — so a screen reading it as a " +
            "potency would write the wrong integer beside every stacking effect in the game. An " +
            "expiry carries none at all, and the chip has to come off rather than freeze at its last " +
            "count for the rest of the fight.");
    }

    /// <summary>
    /// 🔒 The negative control: most of the log asks the screen to draw nothing whatever.
    /// </summary>
    /// <remarks>
    /// 🔴 The events are still crossed and still handed over — a screen that dropped them would lose
    /// the opening of every fight — but none of them puts a number in the air or fires a burst. A
    /// projection that emitted something for each of these would spray the screen with numbers for a
    /// telegraph, a queued run effect and the fight's own end.
    /// </remarks>
    [Fact]
    public async Task The_events_that_ask_for_no_drawing_produce_no_instruction_at_all()
    {
        var presenter = await Playing(QuietFight());

        await presenter.AdvanceAsync(CueFightSeconds, CancellationToken.None);

        presenter.StepEvents.Count.ShouldBe(
            QuietFightEventCount,
            "every one of them still has to be crossed, or this case is stating the emptiness " +
            "of a step that consumed nothing rather than the quietness of a step that consumed a lot.");
        presenter.StepCues.ShouldBeEmpty(
            "an opening, an attack, a miss, a block, a broken ward, a ward granted, a phase " +
            "change, a telegraph, a queued run effect and the fight's own end are all things that " +
            "happened and none of them is a number over an actor's head. A screen drawing one for " +
            "each would put ten floating numbers on a tick where nothing was hit.");
    }

    // ---- 🔒 one reading of the actor roster, and it is this one ---------------------------------

    /// <summary>
    /// 🔒 The transcribed roster — slot 0 the hero, 1 to 3 the pets, 4 upward the enemies — read
    /// once, here, where a case can hold it.
    /// </summary>
    /// <remarks>
    /// 🔴 The roster is internal to the rules assembly and has no public restatement, so a client has
    /// to transcribe it. A second transcription in whatever draws the screen is a second thing to
    /// keep in step with the first, and this repository has no harness that could ever check the
    /// second one — the day the rules layer widens the pet block, one copy learns and the other
    /// silently draws a pet standing among the enemies.
    /// </remarks>
    [Theory]
    [InlineData(HeroSlot, ReplaySide.Hero, 0)]
    [InlineData(PetSlot, ReplaySide.Pet, PetSlot)]
    [InlineData(FirstEnemySlot, ReplaySide.Enemy, 1)]
    [InlineData(SecondEnemySlot, ReplaySide.Enemy, 2)]
    public async Task Each_actor_stands_on_the_side_its_own_slot_names(
        byte slot, ReplaySide side, int index)
    {
        var presenter = await Playing(RosterFight());

        var actor = Actor(presenter, slot);

        actor.Side.ShouldBe(
            side,
            $"slot {slot} is the log's own way of saying which side of the fight this actor is " +
            "on, and a screen that read it wrong would stand the hero among the enemies — or, worse, " +
            "aim the hero's own healing at the actor trying to kill them.");
        actor.SideIndex.ShouldBe(
            index,
            "and the index is what tells two enemies of the same fight apart. The first of a " +
            "side is the first rather than the fourth, because the player is not reading slot numbers.");
    }

    [Fact]
    public async Task An_actor_is_captioned_by_the_side_it_stands_on_and_which_one_of_it_it_is()
    {
        var presenter = await Playing(RosterFight());

        presenter.CaptionOf(HeroSlot).ShouldBe(
            BattleContent.EnglishValueOf(BattleContent.HeroLabelKey),
            "there is only ever one hero, so numbering it would put a '1' beside the player's " +
            "own character for no reason a player could work out.");
        presenter.CaptionOf(PetSlot).ShouldBe(
            BattleContent.EnglishValueOf(BattleContent.HeroLabelKey) + " " + PetSlot,
            "a pet has no caption of its own in either locale, so it borrows the hero's side " +
            "rather than putting an untranslated English word in front of a German player.");
        presenter.CaptionOf(SecondEnemySlot).ShouldBe(
            BattleContent.EnglishValueOf(BattleContent.EnemyLabelKey) + " 2",
            "and both halves come out of the content set, so a caption a player reads is a " +
            "caption a translator was paid for.");
    }

    [Fact]
    public async Task The_banner_names_every_enemy_the_fight_carries_and_no_one_else()
    {
        var presenter = await Playing(RosterFight());

        presenter.OpponentLabel.ShouldBe(
            BattleContent.EnglishValueOf(BattleContent.EnemyLabelKey) + " 1 · " +
            BattleContent.EnglishValueOf(BattleContent.EnemyLabelKey) + " 2",
            "the banner is what the player is told they are fighting, and no enemy's real " +
            "name is reachable from a client at all — so it names them by side and index. A banner " +
            "that swept in the hero and its pets would list the player among their own opponents.");
    }

    [Fact]
    public void A_fight_with_no_enemy_in_its_roster_still_has_a_banner()
    {
        var presenter = Build(RecordingGameHost.Finding(AnyPlayer(), BattleRun()));

        presenter.OpponentLabel.ShouldBe(
            BattleContent.EnglishValueOf(BattleContent.EnemyLabelKey),
            "the banner is drawn before the read answers and in every state where the read " +
            "answered with no fight, and a blank one is a heading a player can see room for and " +
            "cannot read.");
    }

    // ---- 🔒 one arithmetic for one health bar --------------------------------------------------

    /// <summary>
    /// 🔒 The load-bearing claim: watching a fight and skipping it leave the health on the SAME
    /// number, to the last decimal.
    /// </summary>
    /// <remarks>
    /// 🔴 Exact equality on purpose. A watched fight walks the health one blow at a time and a
    /// skipped one is put straight onto what the fight ended with, so the two are different
    /// arithmetics over the same log — and an unrounded walk over enough small blows drifts off the
    /// value the result reports by whatever the doubles lost on the way. The player never sees a
    /// tolerance; they see two screenshots of one boss kill that disagree.
    /// </remarks>
    [Fact]
    public async Task A_watched_fight_and_a_skipped_one_leave_the_health_on_the_same_number()
    {
        var fight = FractionFight();

        var watched = await Playing(fight);
        var skipped = await Playing(fight);

        await watched.AdvanceAsync(30, CancellationToken.None);
        await skipped.SkipAsync(CancellationToken.None);

        watched.Complete.ShouldBeTrue(
            "the watched fight has to have actually reached its end, or the two are being " +
            "compared at different points of the same log.");
        watched.HealthOf(HeroSlot).ShouldBe(
            fight.HeroHpRemaining,
            "the fight itself reports what the hero was left with, so a walk that finished " +
            "anywhere else has accumulated an error over the fight — and it is the walk, not the " +
            "result, that the player watched arrive.");
        skipped.HealthOf(HeroSlot).ShouldBe(
            watched.HealthOf(HeroSlot),
            "and the outcome was fixed before the first frame was drawn, so patience must " +
            "not change a number. Two arithmetics over one health bar is two answers to the same " +
            "fight, differing in exactly the decimal nobody thinks to check.");
    }

    [Fact]
    public async Task A_skipped_fight_puts_every_actor_on_the_health_it_ended_the_fight_with()
    {
        var presenter = await Playing(ShortFight());

        await presenter.SkipAsync(CancellationToken.None);

        presenter.HealthOf(HeroSlot).ShouldBe(
            HeroHpAtTheEnd,
            "a skip draws none of the blows, so the bars cannot be walked to the end — they " +
            "are put there. A skipped fight that left every bar full would end on a screen showing a " +
            "hero at full health beside the news that they lost.");
        presenter.HealthOf(FirstEnemySlot).ShouldBe(
            0,
            "and the actor the log records a death for ended at nothing, however little of " +
            "the fight was watched.");
        presenter.HealthOf(SecondEnemySlot).ShouldBe(
            SurvivingEnemyMaxHp - DamageDealtToTheSurvivingEnemy,
            "and an enemy still standing lands exactly where a watched fight would have walked " +
            "it — its spawned maximum less what it absorbed. This is the assertion the fix is visible " +
            "in: it read ShouldBeNull for as long as a surviving enemy had no bar to put anywhere, so a " +
            "skipped fight ended on a screen whose enemy bars were simply absent.");
    }

    /// <summary>
    /// 🔴 <b>The symptom, in the middle of a fight.</b> A blow against an enemy that survives
    /// moves that enemy's bar.
    /// </summary>
    /// <remarks>
    /// This case read <c>Health.ShouldBeNull</c> and was named for it. The floater was drawn and the bar
    /// was not, so a player watched damage numbers rise off an enemy whose health never moved — which
    /// reads as an enemy taking no damage rather than as a screen missing a denominator.
    /// </remarks>
    [Fact]
    public async Task A_blow_against_a_surviving_enemy_moves_its_bar()
    {
        var presenter = await Playing(ShortFight());

        await presenter.AdvanceAsync(1.0, CancellationToken.None);

        var struck = presenter.StepCues.Single(cue => cue.ActorId == SecondEnemySlot);

        struck.Floater.ShouldBe(
            ReplayFloater.Hit,
            "the blow itself is real and its number is drawn.");
        struck.FloaterAmount.ShouldBe(
            DamageDealtToTheSurvivingEnemy,
            tolerance: HealthTolerance,
            customMessage: "and the number drawn is the health the blow actually removed.");
        struck.Health!.Value.ShouldBe(
            SurvivingEnemyMaxHp - DamageDealtToTheSurvivingEnemy,
            tolerance: HealthTolerance,
            customMessage:
            "and the bar moves with it, off the maximum the log spawned the enemy with. A floater " +
            "with no bar under it is the exact shape of the bug: the damage was always in the log and " +
            "always drawn, and the enemy still looked invulnerable.");
    }

    /// <summary>🔒 The absence, where it is still an absence: a slot no spawn introduced.</summary>
    [Fact]
    public async Task A_blow_against_an_actor_the_log_never_spawned_moves_no_bar()
    {
        var presenter = await Playing(FightWithAnUnspawnedActor());

        await presenter.AdvanceAsync(1.0, CancellationToken.None);

        var struck = presenter.StepCues.Single(cue => cue.ActorId == SecondEnemySlot);

        struck.Floater.ShouldBe(
            ReplayFloater.Hit,
            "the blow is in the log, so its number is drawn — what is missing is the bar to take it " +
            "off, not the hit.");
        struck.Health.ShouldBeNull(
            "with no spawn event for this slot there is no maximum to move a bar against. Moving one " +
            "from a starting point that was guessed would draw a fraction of a fight that is fiction, " +
            "on the actor the player is most anxiously watching.");
    }

    /// <summary>
    /// 🔒 A tick of a damage-over-time is health lost like any other, and the derivation knows it.
    /// </summary>
    /// <remarks>
    /// 🔴 The derivation runs the walk backwards. If it counted one fewer kind of event than the walk
    /// applies, a fight with a burn on the hero would walk past the health the result reports and
    /// stop somewhere below it — the bar would end lower than the number printed beside it.
    /// </remarks>
    [Fact]
    public async Task A_fight_whose_hero_burns_still_ends_on_the_health_the_result_reports()
    {
        var fight = CueFight();

        var presenter = await Playing(fight);

        await presenter.AdvanceAsync(CueFightSeconds, CancellationToken.None);

        presenter.Complete.ShouldBeTrue(
            "the whole fight has to have played for its end to be the thing being compared.");
        presenter.HealthOf(HeroSlot).ShouldBe(
            fight.HeroHpRemaining,
            "a tick takes health off exactly as a blow does, so a derivation blind to ticks " +
            "would start the hero's bar too high and walk it to a value the result contradicts — two " +
            "numbers for the same hero, on the same screen, at the same moment.");
    }

    // ---- fixture -------------------------------------------------------------------------------

    private static PlayerSnapshot AnyPlayer() => PlayerState.Player(Player);

    /// <summary>
    /// The production prediction over the checkout's own content — what the shipped game runs.
    /// </summary>
    private static LocalBattleSimulation Predicting() => new(BootContent.Shipped);

    /// <summary>
    /// A profile row the domain will actually rehydrate, for the cases that reach the real simulator.
    /// </summary>
    /// <remarks>
    /// 🔒 <see cref="AnyPlayer"/> is deliberately NOT this: it leaves the inventory, loadout and
    /// presets at their null defaults, which the domain refuses. Every case that only reads the
    /// snapshot's fields is fine with that row; a case that composes a hero from it is not, and the
    /// difference is worth two fixtures rather than one that quietly satisfies both.
    /// </remarks>
    private static PlayerSnapshot AnyRehydratablePlayer() => PlayerState.Rehydratable(Player);

    /// <summary>
    /// A run parked in the battle phase <b>on an enemy tile</b> — a fight the rules can compose.
    /// </summary>
    /// <remarks>
    /// 🔒 The tile is stated on the row rather than rolled onto, exactly as the application suite's
    /// own battle fixture does and for the same reason: the board a run generates is a function of its
    /// seed, so waiting for a fight tile would tie this to the generator's draw order and would
    /// silently stop producing a battle the day a tile weight moved. The tile KIND is read off the
    /// screen's own public constant, never transcribed as a number.
    /// </remarks>
    /// <param name="runSeed">The run's committed seed.</param>
    /// <param name="battlesStarted">The combat counter — the battle now open is the one before it.</param>
    /// <param name="currentHp">The health the hero enters the fight on.</param>
    private static RunSnapshot OnAFightTile(
        ulong runSeed = 4242, int battlesStarted = 1, int currentHp = 100) =>
        PlayerState.Run(
            Run,
            Player,
            RunPhase.BattlePending,
            position: 3,
            currentHp: currentHp,
            runSeed: runSeed,
            pendingTileKind: BattleReplayPresenter.EnemyTileKind,
            pendingTileLinearIndex: 3,
            pendingTileStage: 1,
            rngStreamPositions: Counters(battlesStarted));

    /// <summary>The per-stream counters of a run that has started the given number of battles.</summary>
    private static IReadOnlyDictionary<string, ulong> Counters(int battlesStarted) =>
        new Dictionary<string, ulong>(StringComparer.Ordinal) { [CombatStream] = (ulong)battlesStarted };

    /// <summary>A run parked in the battle phase, which is the only state this screen opens over.</summary>
    /// <param name="runSeed">The run's committed seed, half of the battle seed's derivation.</param>
    /// <param name="battlesStarted">The combat counter, the other half.</param>
    /// <param name="currentHp">The hero's health — a field a revive changes and the seed must not read.</param>
    /// <param name="gold">The run's Gold — likewise.</param>
    private static RunSnapshot BattleRun(
        ulong runSeed = 4242, int battlesStarted = 1, int currentHp = 100, long gold = 0) =>
        PlayerState.Run(
            Run,
            Player,
            RunPhase.BattlePending,
            currentHp: currentHp,
            gold: gold,
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
    /// The second enemy is alive at the end on purpose — it is the actor whose bar was undrawable
    /// before the log carried a maximum, and a fixture where every enemy dies could not state that its
    /// bar is drawn now.
    /// </remarks>
    /// <summary>What the hero has left when <see cref="ShortFight"/> ends.</summary>
    private const double HeroHpAtTheEnd = 40.0;

    /// <summary>The one blow the hero takes in <see cref="ShortFight"/>.</summary>
    private const double DamageTakenByTheHero = 12.3456;

    /// <summary>The one heal the hero receives in <see cref="ShortFight"/>.</summary>
    private const double HealingReceivedByTheHero = 5.1234;

    /// <summary>What the hero must therefore have started <see cref="ShortFight"/> with.</summary>
    private const double HeroHpAtTheStart =
        HeroHpAtTheEnd + DamageTakenByTheHero - HealingReceivedByTheHero;

    /// <summary>The whole of the damage the first enemy absorbs before it dies.</summary>
    private const double DamageTakenByTheDyingEnemy = 30.8765;

    /// <summary>
    /// The hero's Max HP in <see cref="ShortFight"/> — deliberately far above the health it opens on.
    /// </summary>
    /// <remarks>
    /// 🔒 The two numbers are different on purpose. A run carries health between fights, so the hero
    /// opens part-way along its bar, and a screen that took the spawned maximum as the opening value
    /// would draw it at full — the one thing the bar exists to contradict. Equal numbers here would let
    /// that bug pass.
    /// </remarks>
    private const double HeroMaxHp = 200.0;

    /// <summary>
    /// The dying enemy's Max HP, which is exactly what it absorbs: it opens full and dies.
    /// </summary>
    /// <remarks>
    /// ⚠️ The two derivations agree for this actor, necessarily — an enemy that opened full and ended
    /// at zero absorbed its whole maximum — so it is the hero that pins which one wins, not this one.
    /// </remarks>
    private const double DyingEnemyMaxHp = DamageTakenByTheDyingEnemy;

    /// <summary>The surviving enemy's Max HP in <see cref="ShortFight"/>.</summary>
    private const double SurvivingEnemyMaxHp = 60.0;


    /// <summary>
    /// The width a health derivation may be wrong by. Four decimals is what the rules layer rounds
    /// its own values to, so anything wider than a rounding artefact is a real disagreement.
    /// </summary>
    private const double HealthTolerance = 1e-9;

    private static SimulationResult ShortFight(bool heroWon = true) =>
        new(
            heroWon,
            DurationTicks: ShortFightDurationTicks,
            HeroHpRemaining: HeroHpAtTheEnd,
            Log:
            [
                At(0, CombatEventType.ActorSpawned, NoActorSlot, HeroSlot, value: HeroMaxHp),
                At(0, CombatEventType.ActorSpawned, NoActorSlot, FirstEnemySlot, value: DyingEnemyMaxHp),
                At(0, CombatEventType.ActorSpawned, NoActorSlot, SecondEnemySlot, value: SurvivingEnemyMaxHp),
                At(0, CombatEventType.BattleStart, NoActorSlot, NoActorSlot),
                At(10, CombatEventType.Attack),
                At(10, CombatEventType.Hit, HeroSlot, FirstEnemySlot, value: DamageTakenByTheDyingEnemy),
                At(20, CombatEventType.Hit, FirstEnemySlot, HeroSlot, value: DamageTakenByTheHero),
                At(20, CombatEventType.Hit, HeroSlot, SecondEnemySlot, value: DamageDealtToTheSurvivingEnemy),
                At(30, CombatEventType.Heal, HeroSlot, HeroSlot, value: HealingReceivedByTheHero),
                At(ShortFightDurationTicks, CombatEventType.ActorDeath, HeroSlot, FirstEnemySlot),
                At(ShortFightDurationTicks, CombatEventType.BattleEnd, NoActorSlot, NoActorSlot),
            ],
            LogHash: 17278238499121983245UL);

    /// <summary>
    /// A log that lands a blow on a slot no <c>ActorSpawned</c> ever introduced.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>Not a log the simulator emits, deliberately.</b> The pre-tick spawns every actor of
    /// the opening roster before anything happens to any of them, and <c>AdmitSummon</c> spawns a
    /// newcomer on the tick it enters, so a slot that takes a hit without a spawn is malformed. It is
    /// constructed here because the screen still has to answer for one: the reachable way to get this
    /// log is a client and a server that disagree about the roster, and the honest answer is "no bar"
    /// rather than a bar at whatever fullness a guess produced.
    /// </remarks>
    private static SimulationResult FightWithAnUnspawnedActor() =>
        new(
            HeroWon: false,
            DurationTicks: ShortFightDurationTicks,
            HeroHpRemaining: HeroHpAtTheEnd,
            Log:
            [
                At(0, CombatEventType.ActorSpawned, NoActorSlot, HeroSlot, value: HeroMaxHp),
                At(0, CombatEventType.BattleStart, NoActorSlot, NoActorSlot),
                At(20, CombatEventType.Hit, HeroSlot, SecondEnemySlot, value: DamageDealtToTheSurvivingEnemy),
                At(ShortFightDurationTicks, CombatEventType.BattleEnd, NoActorSlot, NoActorSlot),
            ],
            LogHash: 313131UL);

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

    /// <summary>A boss fight that walks all three phases, shaped the way a real one is.</summary>
    /// <remarks>
    /// 🔒 The phase-1 entry sits on tick zero because that is where the rules layer puts it: a boss
    /// enters its first phase as the fight opens, and that entry emits a phase change like any
    /// other. The later two are spaced so that a case can stand the playhead well inside a band's
    /// dwell, well outside it, and between the ordinary dwell and the reduced-motion one — three
    /// positions a fixture whose changes land exactly on the playhead cannot offer at all.
    /// </remarks>
    private static SimulationResult BossFight() =>
        new(
            HeroWon: true,
            DurationTicks: 100,
            HeroHpRemaining: 30,
            Log:
            [
                At(0, CombatEventType.BattleStart, NoActorSlot, NoActorSlot),
                At(0, CombatEventType.PhaseChange, FirstEnemySlot, FirstEnemySlot, value: 1),
                At(2, CombatEventType.PhaseChange, FirstEnemySlot, FirstEnemySlot, value: 2),
                At(35, CombatEventType.PhaseChange, FirstEnemySlot, FirstEnemySlot, value: 3),
                At(100, CombatEventType.BattleEnd, NoActorSlot, NoActorSlot),
            ],
            LogHash: 424242UL);

    /// <summary>
    /// A boss fight whose one phase change carries a phase the count of events does not.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>Not a log the simulator emits today, and that is the point.</b> The rules layer walks a
    /// boss upward one phase at a time from an entry on tick zero, so in every log it currently
    /// produces the number of phase changes crossed happens to equal the phase entered — which
    /// means no realistic fixture can tell a screen that READS the payload apart from one that
    /// COUNTS the events. The payload is the contract the client is given; the emission sequence is
    /// internal to the rules assembly and can change without a single client test noticing. So the
    /// discrimination is stated over a log built to state it.
    /// </remarks>
    private static SimulationResult ElevatedPhaseFight() =>
        new(
            HeroWon: true,
            DurationTicks: 100,
            HeroHpRemaining: 30,
            Log:
            [
                At(0, CombatEventType.BattleStart, NoActorSlot, NoActorSlot),
                At(10, CombatEventType.PhaseChange, FirstEnemySlot, FirstEnemySlot, value: 3),
                At(100, CombatEventType.BattleEnd, NoActorSlot, NoActorSlot),
            ],
            LogHash: 424243UL);

    /// <summary>How much the hero takes off the enemy that survives <see cref="ShortFight"/>.</summary>
    private const double DamageDealtToTheSurvivingEnemy = 8.25;

    /// <summary>Long enough to consume the whole of <see cref="CueFight"/> in one advance.</summary>
    private const double CueFightSeconds = 5.0;

    /// <summary>The one blow of <see cref="CueFight"/>, which the log announces as a critical one.</summary>
    private const double CueFightBlow = 12.0;

    /// <summary>The size of its one damage-over-time tick, which the log carries as a negative.</summary>
    private const double CueFightTick = 4.5;

    /// <summary>And its one heal.</summary>
    private const double CueFightHeal = 2.25;

    /// <summary>What the hero is left with when <see cref="CueFight"/> ends.</summary>
    private const double CueFightHeroHpRemaining = 50.0;

    /// <summary>The status <see cref="CueFight"/> applies and later expires.</summary>
    private const ushort CueFightStatus = 7;

    /// <summary>How many of it are stacked — a count, not a potency.</summary>
    private const int CueFightStacks = 3;

    /// <summary>
    /// A fight carrying one of every event that asks the screen to draw something.
    /// </summary>
    /// <remarks>
    /// 🔒 The hero is burned and then healed, which is the pair the health derivation has to account
    /// for as carefully as it accounts for blows: a tick removes health exactly as a hit does, and a
    /// derivation blind to one of them starts the bar in a place its own end cannot be reached from.
    /// </remarks>
    private static SimulationResult CueFight() =>
        new(
            HeroWon: true,
            DurationTicks: 80,
            HeroHpRemaining: CueFightHeroHpRemaining,
            Log:
            [
                At(0, CombatEventType.BattleStart, NoActorSlot, NoActorSlot),
                At(10, CombatEventType.Attack),
                At(10, CombatEventType.Crit),
                At(10, CombatEventType.Hit, HeroSlot, FirstEnemySlot, value: CueFightBlow),
                At(20, CombatEventType.StatusApplied, HeroSlot, FirstEnemySlot,
                    value: CueFightStacks, dataId: CueFightStatus),
                At(30, CombatEventType.StatusTick, FirstEnemySlot, HeroSlot, value: -CueFightTick),
                At(40, CombatEventType.Heal, HeroSlot, HeroSlot, value: CueFightHeal),
                At(50, CombatEventType.PetAbility, PetSlot, FirstEnemySlot),
                At(60, CombatEventType.StatusExpired, HeroSlot, FirstEnemySlot, dataId: CueFightStatus),
                At(70, CombatEventType.ActorDeath, HeroSlot, FirstEnemySlot),
                At(80, CombatEventType.BattleEnd, NoActorSlot, NoActorSlot),
            ],
            LogHash: 515151UL);

    /// <summary>
    /// Two attacks, the first of them announced as a critical hit and the second not.
    /// </summary>
    /// <remarks>
    /// 🔒 Shaped the way the rules layer emits an attack: the attack opens the sequence and the crit
    /// is its own event, emitted BEFORE the blow it describes and carrying no value of its own.
    /// </remarks>
    private static SimulationResult CritFight() =>
        new(
            HeroWon: true,
            DurationTicks: 40,
            HeroHpRemaining: 30,
            Log:
            [
                At(0, CombatEventType.BattleStart, NoActorSlot, NoActorSlot),
                At(10, CombatEventType.Attack),
                At(10, CombatEventType.Crit),
                At(10, CombatEventType.Hit, HeroSlot, FirstEnemySlot, value: 9),
                At(20, CombatEventType.Attack),
                At(20, CombatEventType.Hit, HeroSlot, FirstEnemySlot, value: 4),
                At(40, CombatEventType.BattleEnd, NoActorSlot, NoActorSlot),
            ],
            LogHash: 606060UL);

    /// <summary>
    /// A critical hit a ward swallows whole, followed by an ordinary one.
    /// </summary>
    /// <remarks>
    /// 🔴 The announced attack emits no <c>Hit</c> at all — a block and a broken ward end it — so the
    /// announcement belongs to a number that is never drawn. The ordinary blow that follows is the
    /// one a mispaired implementation gilds.
    /// </remarks>
    private static SimulationResult AbsorbedCritFight() =>
        new(
            HeroWon: true,
            DurationTicks: 40,
            HeroHpRemaining: 30,
            Log:
            [
                At(0, CombatEventType.BattleStart, NoActorSlot, NoActorSlot),
                At(10, CombatEventType.Attack),
                At(10, CombatEventType.Crit),
                At(10, CombatEventType.Block),
                At(10, CombatEventType.WardBroken),
                At(20, CombatEventType.Attack),
                At(20, CombatEventType.Hit, HeroSlot, FirstEnemySlot, value: 6),
                At(40, CombatEventType.BattleEnd, NoActorSlot, NoActorSlot),
            ],
            LogHash: 707070UL);

    /// <summary>How many events <see cref="QuietFight"/> carries, none of which is drawn.</summary>
    private const int QuietFightEventCount = 10;

    /// <summary>A fight made entirely of events that put nothing on the screen.</summary>
    private static SimulationResult QuietFight() =>
        new(
            HeroWon: true,
            DurationTicks: 40,
            HeroHpRemaining: 30,
            Log:
            [
                At(0, CombatEventType.BattleStart, NoActorSlot, NoActorSlot),
                At(5, CombatEventType.Attack),
                At(5, CombatEventType.Miss),
                At(10, CombatEventType.Attack),
                At(10, CombatEventType.Block),
                At(10, CombatEventType.WardBroken),
                At(15, CombatEventType.Shield, HeroSlot, HeroSlot, value: 20),
                At(20, CombatEventType.Telegraph, FirstEnemySlot, FirstEnemySlot, value: 1.25),
                At(25, CombatEventType.RunEffectQueued, NoActorSlot, NoActorSlot),
                At(40, CombatEventType.BattleEnd, NoActorSlot, NoActorSlot),
            ],
            LogHash: 808080UL);

    /// <summary>A fight whose roster reaches the hero, a pet and two enemies.</summary>
    private static SimulationResult RosterFight() =>
        new(
            HeroWon: true,
            DurationTicks: 40,
            HeroHpRemaining: 30,
            Log:
            [
                At(0, CombatEventType.BattleStart, NoActorSlot, NoActorSlot),
                At(10, CombatEventType.PetAbility, PetSlot, FirstEnemySlot),
                At(20, CombatEventType.Hit, HeroSlot, SecondEnemySlot, value: 5),
                At(30, CombatEventType.Hit, FirstEnemySlot, HeroSlot, value: 5),
                At(40, CombatEventType.BattleEnd, NoActorSlot, NoActorSlot),
            ],
            LogHash: 909090UL);

    /// <summary>What the hero is left with when <see cref="FractionFight"/> ends.</summary>
    private const double FractionFightHeroHpRemaining = 1.5;

    /// <summary>
    /// A fight of many small blows, whose health cannot be walked without rounding it.
    /// </summary>
    /// <remarks>
    /// 🔴 Seven tenths, taken one at a time. A tenth has no exact binary form, so a walk that does
    /// not hold each step to the rounding the rules layer holds its own values to arrives at
    /// something that is not the health the result reports — which is precisely the disagreement
    /// between a fight watched and the same fight skipped. A fixture of round numbers could not
    /// state this at all.
    /// </remarks>
    private static SimulationResult FractionFight() =>
        new(
            HeroWon: false,
            DurationTicks: 20,
            HeroHpRemaining: FractionFightHeroHpRemaining,
            Log:
            [
                At(0, CombatEventType.BattleStart, NoActorSlot, NoActorSlot),
                At(1, CombatEventType.Hit, FirstEnemySlot, HeroSlot, value: 0.1),
                At(2, CombatEventType.Hit, FirstEnemySlot, HeroSlot, value: 0.1),
                At(3, CombatEventType.Hit, FirstEnemySlot, HeroSlot, value: 0.1),
                At(4, CombatEventType.Hit, FirstEnemySlot, HeroSlot, value: 0.1),
                At(5, CombatEventType.Hit, FirstEnemySlot, HeroSlot, value: 0.1),
                At(6, CombatEventType.Hit, FirstEnemySlot, HeroSlot, value: 0.1),
                At(7, CombatEventType.Hit, FirstEnemySlot, HeroSlot, value: 0.1),
                At(20, CombatEventType.BattleEnd, NoActorSlot, NoActorSlot),
            ],
            LogHash: 121212UL);

    /// <summary>The one damage number a step drew, failing legibly when it drew any other count.</summary>
    private static ReplayCue TheBlowIn(BattleReplayPresenter presenter)
    {
        var blows = presenter.StepCues
                             .Where(cue => cue.Floater is ReplayFloater.Hit or ReplayFloater.Crit)
                             .ToArray();

        return blows.Length == 1
            ? blows[0]
            : throw new InvalidOperationException(
                $"The step asked for {blows.Length} damage numbers rather than one, so there is no " +
                "single blow to read a kind off — a stronger failure than reading the wrong kind.");
    }

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
            [StatId.DMG_PCT] = 1,
            [StatId.DR_PCT] = 1,
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
    /// Client-local, like the interface it stands in for. Every stall the screen can be in is reached
    /// through this rather than by arranging real rows that produce it, because what these cases are
    /// about is what the SCREEN does with an answer — the production prediction's own answers are
    /// pinned separately, against the shipped content, further up this file.
    /// </remarks>
    private sealed class StubBattleSimulation : IBattleSimulationSource
    {
        private readonly BattleSimulationAttempt _attempt;

        private StubBattleSimulation(BattleSimulationAttempt attempt) => _attempt = attempt;

        /// <summary>A prediction that answers with exactly the attempt a case built.</summary>
        internal static StubBattleSimulation Attempting(BattleSimulationAttempt attempt) =>
            new(attempt);

        /// <summary>A prediction that hands back the given fight.</summary>
        internal static StubBattleSimulation Playing(SimulationResult fight) =>
            new(new BattleSimulationAttempt(BattleReadiness.Ready, BattleSeed: 88, SeedDerived: true, fight));

        /// <summary>A prediction answering one named readiness, with a fight only when it is ready.</summary>
        internal static StubBattleSimulation Answering(BattleReadiness readiness) =>
            readiness == BattleReadiness.Ready
                ? Playing(ShortFight())
                : new(new BattleSimulationAttempt(readiness, BattleSeed: 0, SeedDerived: false, Result: null));

        /// <summary>The rows the screen handed over, so a case can check it handed over both.</summary>
        internal PlayerSnapshot? AskedAboutPlayer { get; private set; }

        internal RunSnapshot? AskedAboutRun { get; private set; }

        /// <inheritdoc/>
        public BattleSimulationAttempt Simulate(PlayerSnapshot player, RunSnapshot run)
        {
            ArgumentNullException.ThrowIfNull(player);
            ArgumentNullException.ThrowIfNull(run);

            AskedAboutPlayer = player;
            AskedAboutRun = run;

            return _attempt;
        }
    }
}
