using Shouldly;
using SlayIdleRepeat.Client.Game.Presenters;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Rules.Economy;
using Xunit;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// `02` §6 / `24` §9 — the run-end moment (S13 + S14): why the run ended, what it pays, the revive
/// that is gated on an entitlement, and the one way out that is never gated on anything.
/// </summary>
/// <remarks>
/// 🔴 This screen is where a run's whole reward becomes real, so the cases that matter are the ones
/// about what it will and will not submit, and about which of two absences it names. A player told
/// they have already used a revive they never used, and a player left on a screen with no way off it,
/// are the two failures worth pinning.
/// </remarks>
public sealed class RunEndPresenterTests
{
    private static readonly PlayerId Player = new("PLAYER_runend_4a1c");
    private static readonly RunId Run = new("RUN_runend_9e02");

    /// <summary>The tile kind of the fight the hero lost — an ordinary enemy encounter.</summary>
    /// <remarks>
    /// 🔒 Read off the rules layer's own enum rather than transcribed, for the reason
    /// <c>CampfirePresenter.ShrineTileKind</c> states: the numbering is public, so a kind inserted above
    /// this one renumbers with it instead of leaving a literal pointing at whatever moved into its slot.
    /// </remarks>
    private const int EnemyBattleTileKind = (int)TileKind.Enemy;

    /// <summary>The stage that fight belonged to. Any of 1-3 would do; none of them may be 0.</summary>
    private const int FirstStage = 1;

    /// <summary>The placement `02` §6's one-per-run revive is counted under.</summary>
    /// <remarks>
    /// ⚠️ Transcribed, because <c>ReviveTuning</c> is <c>internal</c> to <c>Core</c> and this assembly
    /// cannot see it. That is why the case using it asserts on the STANDING the projection reports
    /// rather than on the count: a transcription that had gone stale would show up as a run whose revive
    /// is still offered, which is what the case fails on.
    /// </remarks>
    private const string RevivePlacementId = "AD_REVIVE";

    // ---- what the screen submits, and what it refuses on its own -------------------------------

    /// <summary>
    /// 🔒 <b>A revive this player cannot reach is refused HERE, without reaching the host.</b>
    /// </summary>
    /// <remarks>
    /// 🔴 `02` §6 gives Plus subscribers the instant no-ad revive and everyone else the ad path — and the
    /// ad path needs <c>CLAIM_AD_REWARD</c>, deferred to M15-03. So for an unentitled player there is no
    /// route at all, the screen already says so, and spending a round trip to be told what the screen
    /// knew teaches the player that the sentence beside the button is not to be trusted. Asserted on the
    /// HOST's call count, because "refused here" is a claim about a call that must not happen.
    /// </remarks>
    [Fact]
    public async Task A_revive_this_player_cannot_reach_is_refused_without_reaching_the_host()
    {
        var host = Finding(Dead());
        var presenter = Build(host, ReviveArm.NoReviveRouteResolved);

        await presenter.StartAsync(CancellationToken.None);

        presenter.Stage.ShouldBe(
            RunEndStage.Tallied, "the premise: the run was read and the rules would accept a revive.");

        var before = host.SubmitCallCount;
        var outcome = await presenter.ReviveAsync(CancellationToken.None);

        outcome.ShouldBe(RunEndSubmission.RefusedNotAvailable);
        presenter.ReviveAvailable.ShouldBeFalse();
        host.SubmitCallCount.ShouldBe(
            before,
            "a revive with no route was sent to the host. There is no command that can grant one for " +
            "an unentitled player — CLAIM_AD_REWARD is deferred to M15-03 — and the screen already " +
            "carries that sentence.");
    }

    /// <summary>…and an entitled player's revive does reach the host, as <c>REVIVE</c>.</summary>
    /// <remarks>
    /// 🔒 The other half of the case above, and the half that keeps it from passing vacuously: a
    /// presenter that never submitted anything would satisfy the refusal claim perfectly. The command's
    /// type is asserted because <c>REVIVE</c> and <c>END_RUN</c> are the two this screen can send and
    /// they do opposite things — one puts the run back in the fight, the other closes it for good.
    /// </remarks>
    [Fact]
    public async Task An_entitled_player_reviving_submits_REVIVE_against_this_run()
    {
        var host = Finding(Dead());
        var presenter = Build(host, ReviveArm.PlusInstant);

        await presenter.StartAsync(CancellationToken.None);

        presenter.ReviveAvailable.ShouldBeTrue(
            "02 §6 gives Plus an instant no-ad revive, and this run's rules-side offer stands.");

        var outcome = await presenter.ReviveAsync(CancellationToken.None);

        outcome.ShouldBe(RunEndSubmission.Submitted);
        host.SubmitCommand.ShouldBeOfType<ReviveCommand>();
        host.SubmitRun.ShouldBe(
            Run, "a revive resumes THIS run's lost fight, so it has to be addressed to the run.");
    }

    /// <summary>
    /// 🔒 <b>The way off this screen is never gated — not on the entitlement, not on the tally.</b>
    /// </summary>
    /// <remarks>
    /// 🔴 The failure guarded against is a run a player cannot leave. `02` §6's step 3 sends a declined
    /// or spent revive straight to the results, so <c>END_RUN</c> is the exit in every state this screen
    /// can be read into — including the state where the projection itself could not be drawn. Both rows
    /// below are ones where the revive is unavailable, and both must still submit.
    /// </remarks>
    [Fact]
    public async Task End_run_is_available_in_every_state_the_read_settled_in()
    {
        var spentHost = Finding(Dead(reviveUsed: true));
        var spent = Build(spentHost, ReviveArm.NoReviveRouteResolved);

        await spent.StartAsync(CancellationToken.None);

        spent.ReviveAvailable.ShouldBeFalse("the premise: this run has no revive left and no route.");
        (await spent.FinishAsync(CancellationToken.None)).ShouldBe(RunEndSubmission.Submitted);
        spentHost.SubmitCommand.ShouldBeOfType<EndRunCommand>();

        var victoryHost = Finding(Won());
        var victory = Build(victoryHost, ReviveArm.PlusInstant);

        await victory.StartAsync(CancellationToken.None);

        victory.ReviveAvailable.ShouldBeFalse("a run whose boss is dead has nothing to revive from.");
        (await victory.FinishAsync(CancellationToken.None)).ShouldBe(RunEndSubmission.Submitted);
        victoryHost.SubmitCommand.ShouldBeOfType<EndRunCommand>();
    }

    /// <summary>…but there is nothing to end when the host says the run does not exist.</summary>
    [Fact]
    public async Task Nothing_is_submitted_for_a_run_the_host_could_not_find()
    {
        var host = RecordingGameHost.FindingNoSuchPlayer();
        var presenter = Build(host, ReviveArm.PlusInstant);

        await presenter.StartAsync(CancellationToken.None);

        presenter.Stage.ShouldBe(RunEndStage.RunMissing);

        (await presenter.FinishAsync(CancellationToken.None))
            .ShouldBe(RunEndSubmission.RefusedNotAvailable);
        (await presenter.ReviveAsync(CancellationToken.None))
            .ShouldBe(RunEndSubmission.RefusedNotAvailable);

        host.SubmitCallCount.ShouldBe(0, "there is no run to end and none to revive.");
    }

    /// <summary>Building the screen reads nothing and submits nothing.</summary>
    /// <remarks>
    /// A read in a constructor can only throw; the read belongs to <c>StartAsync</c>, where a failure
    /// has somewhere to be reported.
    /// </remarks>
    [Fact]
    public void Building_the_screen_reaches_the_host_in_no_way_at_all()
    {
        var host = Finding(Dead());

        _ = Build(host, ReviveArm.PlusInstant);

        host.ReadCallCount.ShouldBe(0);
        host.SubmitCallCount.ShouldBe(0);
    }

    // ---- the two absences, which are two different sentences ------------------------------------

    /// <summary>
    /// 🔒 <b>An unreachable revive and a spent one are told apart, and neither is left as a dead
    /// button.</b>
    /// </summary>
    /// <remarks>
    /// 🔴 This is the finding the projection's <c>RunEndReviveStanding</c> exists for. Both rows here
    /// report <c>ReviveAvailable == false</c>, so nothing but the sentence separates them — and getting
    /// it wrong tells a player who never revived that they had already used their one, or offers a
    /// subscription to a player for something they have already spent. Asserted as *"these two differ"*
    /// rather than against a literal, because the words are the locale's and the claim is that the
    /// screen picks a different one.
    /// </remarks>
    [Fact]
    public async Task The_two_reasons_a_revive_is_missing_are_two_different_sentences()
    {
        var unentitled = Build(Finding(Dead()), ReviveArm.NoReviveRouteResolved);
        var spent = Build(Finding(Dead(reviveUsed: true)), ReviveArm.PlusInstant);

        await unentitled.StartAsync(CancellationToken.None);
        await spent.StartAsync(CancellationToken.None);

        unentitled.ReviveAvailable.ShouldBeFalse();
        spent.ReviveAvailable.ShouldBeFalse();

        unentitled.ReviveBlockText.ShouldNotBeNullOrWhiteSpace(
            "an unentitled player is left with a dead control and no reason for it. 02 §6's ad route " +
            "needs CLAIM_AD_REWARD, deferred to M15-03, so the absence has to be named.");
        spent.ReviveBlockText.ShouldNotBeNullOrWhiteSpace(
            "a run that used its one revive says nothing about why the control is gone.");

        spent.ReviveBlockText.ShouldNotBe(
            unentitled.ReviveBlockText,
            "the two absences share a sentence, so one of them is a lie: this run spent its revive and " +
            "the other never had a route to one. Told that they had 'already used' it, a player who " +
            "never revived reads the screen as broken; offered Plus for a revive they have spent, they " +
            "are being sold something that cannot help them.");
    }

    /// <summary>…and a run that never had a revive to lose says nothing about revives at all.</summary>
    /// <remarks>
    /// 🔒 A cleared chapter is not a failed revive. A sentence about reviving under a victory heading
    /// would read as a control that had broken, which is why the third standing says nothing rather
    /// than saying the least-wrong thing.
    /// </remarks>
    [Fact]
    public async Task A_victory_says_nothing_about_reviving()
    {
        var presenter = Build(Finding(Won()), ReviveArm.PlusInstant);

        await presenter.StartAsync(CancellationToken.None);

        presenter.Outcome.ShouldBe(RunEndKind.Victory);
        presenter.ReviveAvailable.ShouldBeFalse();
        presenter.ReviveBlockText.ShouldBeEmpty(
            "a run whose boss is dead never lost a fight, so a sentence about its revive would be an " +
            "answer to a question the player did not ask.");
        presenter.DeathCostsRewardsText.ShouldBeEmpty(
            "a victory pays the full multiplier, so naming what dying costs would be alarming and false.");
    }

    // ---- the tally -----------------------------------------------------------------------------

    /// <summary>
    /// 🔒 <b>The banked and the paid figures are BOTH drawn, and for a death they differ.</b>
    /// </summary>
    /// <remarks>
    /// 🔴 `24` §9 wants the completion multiplier legible, and the gap between these two numbers is the
    /// only place it appears. A screen showing one figure cannot say what dying cost; a screen showing
    /// the banked one alone would promise a payout the player does not get. The figures are compared as
    /// the strings the screen draws, because a presenter that computed the right number and rendered the
    /// wrong one would pass an assertion made against the projection.
    /// </remarks>
    [Fact]
    public async Task A_death_draws_both_figures_and_names_what_dying_cost()
    {
        var presenter = Build(
            Finding(Dead(bankedLegendXp: 4_000, bankedSoulShards: 800)), ReviveArm.PlusInstant);

        await presenter.StartAsync(CancellationToken.None);

        presenter.Outcome.ShouldBe(RunEndKind.Death);

        foreach (var figure in new[]
        {
            presenter.BankedLegendXpValue,
            presenter.BankedSoulShardsValue,
            presenter.PayoutLegendXpValue,
            presenter.PayoutSoulShardsValue,
        })
        {
            figure.ShouldNotBeNullOrWhiteSpace("every figure in the tally has to be drawn.");
        }

        presenter.PayoutLegendXpValue.ShouldNotBe(
            presenter.BankedLegendXpValue,
            "a death takes a completion multiplier below one, so a paid figure equal to the earned one " +
            "means the multiplier never reached the screen — and 24 §9's whole point is that the " +
            "difference is visible.");

        presenter.DeathCostsRewardsText.ShouldNotBeNullOrWhiteSpace(
            "a paid figure below the earned one with no explanation reads as the game having taken " +
            "something, so the gap is named rather than merely shown.");
    }

    /// <summary>…and nothing is drawn before the read has answered.</summary>
    /// <remarks>
    /// 🔒 Empty rather than a zero. A zero payout is a real answer a player can be given, so a zero
    /// drawn before the read is a lie written in the same digits as the truth.
    /// </remarks>
    [Fact]
    public void No_figure_is_drawn_before_the_read()
    {
        var presenter = Build(Finding(Dead()), ReviveArm.PlusInstant);

        presenter.Stage.ShouldBe(RunEndStage.NotYetRead);
        presenter.Outcome.ShouldBeNull();
        presenter.HeadingText.ShouldBeEmpty("the screen cannot say how a run ended before it has read it.");
        presenter.BankedLegendXpValue.ShouldBeEmpty();
        presenter.PayoutSoulShardsValue.ShouldBeEmpty();
        presenter.Counters.ShouldBeEmpty();
        presenter.StatusText.ShouldNotBeNullOrWhiteSpace("a blank screen mid-read reads as a fault.");
    }

    /// <summary>A read that faulted leaves the screen saying the run could not be tallied.</summary>
    [Fact]
    public async Task A_read_that_answers_nothing_is_a_state_rather_than_an_escape()
    {
        var presenter = Build(
            RecordingGameHost.FaultingItsRead(new InvalidOperationException("the store did not answer")),
            ReviveArm.PlusInstant);

        await presenter.StartAsync(CancellationToken.None);

        presenter.Stage.ShouldBe(RunEndStage.ReadUnavailable);
        presenter.Outcome.ShouldBeNull();
        presenter.Counters.ShouldBeEmpty();
        presenter.StatusText.ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>…and it can still be left, because a run that cannot be tallied is still a run.</summary>
    /// <remarks>
    /// 🔴 The pairing matters: a screen that reported the failure and withheld the exit would strand a
    /// player inside a run the game could not add up.
    /// </remarks>
    [Fact]
    public async Task A_run_that_could_not_be_tallied_can_still_be_ended()
    {
        var host = RecordingGameHost
            .Reading(new Application.UseCases.OwnStateResult(
                Application.UseCases.OwnStateLookup.Found,
                new Application.UseCases.OwnStateView(PlayerState.Player(Player), Dead())));

        var presenter = new RunEndPresenter(
            host,
            RunDecisionContent.Catalogue(RunDecisionContent.Strings()),

            // 🔒 A content set with the screen's strings and NO tuning, so RunEndView.Project cannot
            // read the payout table and fails the way a broken install fails.
            RunDecisionContent.Strings(),
            ReviveArm.PlusInstant,
            Player,
            Run);

        await presenter.StartAsync(CancellationToken.None);

        presenter.Stage.ShouldBe(
            RunEndStage.ReadUnavailable,
            "the run was FOUND and the projection failed, which is a different thing from a missing run.");

        (await presenter.FinishAsync(CancellationToken.None)).ShouldBe(
            RunEndSubmission.Submitted,
            "a run whose tally could not be drawn is still a run the player is inside, and a screen " +
            "that withheld the way out would strand them there.");
    }

    // ---- the footer 24 §9 requires ---------------------------------------------------------------

    /// <summary>
    /// 🔒 <b>Both <c>DROP_RUN</c> mercy counters reach the footer, and each caption NAMES ITS UNIT.</b>
    /// </summary>
    /// <remarks>
    /// 🔴 `24` §9's S14 row puts the <c>DROP_RUN</c> counters in the tally footer and §1.1's Visibility
    /// rule is *always*, so a counter standing at zero is exactly the row a "show it when it matters"
    /// reading would drop — and it is the row a player who has had no luck all session is looking for.
    /// The unit is asserted over the SHIPPED locale for the reason the draft's equivalent case gives:
    /// every fixture value is derived from its own key, so a fixture-based version could not fail
    /// whatever anyone wrote in <c>en.json</c>.
    /// </remarks>
    [Fact]
    public async Task Both_mercy_counters_reach_the_footer_and_each_names_its_unit()
    {
        var presenter = Build(Finding(Dead()), ReviveArm.PlusInstant);

        await presenter.StartAsync(CancellationToken.None);

        presenter.Counters.Count.ShouldBe(
            2, "24 §4.3 authors two DROP_RUN dry-streak breakers, the Elite mercy and the Boss mercy.");

        presenter.Counters.ShouldAllBe(row => row.Value.Contains('/'));
        presenter.Counters
            .Select(row => row.Label)
            .Distinct(StringComparer.Ordinal)
            .Count()
            .ShouldBe(2, "the two count different things, so they cannot share a caption.");

        foreach (var key in new[]
        {
            RunDecisionContent.RunEndEliteMercyLabelKey,
            RunDecisionContent.RunEndBossMercyLabelKey,
        })
        {
            RunDecisionContent.ShippedEnglish.TryGetValue(key, out var caption).ShouldBeTrue(
                $"'{key}' is not in the shipped English locale, so a mercy counter has no caption and " +
                "a player is shown its key beside a number.");

            caption.Contains("kill", StringComparison.OrdinalIgnoreCase).ShouldBeTrue(
                $"'{key}' is authored as \"{caption}\", which does not name what it counts. 24 §9 " +
                "requires each counter to name its unit rather than sit beside a bare number, and " +
                "these two count Elite kills and boss kills.");
        }
    }

    // ---- the captions --------------------------------------------------------------------------

    /// <summary>Every caption resolves rather than falling through to its own key.</summary>
    /// <remarks>
    /// The heading is read for all three outcomes, because each is a separate key and a missing one
    /// would only surface for the outcome that hit it.
    /// </remarks>
    [Fact]
    public async Task Every_caption_resolves()
    {
        var captions = new List<string>();

        foreach (var run in new[] { Dead(), Won(), Abandoned() })
        {
            var presenter = Build(Finding(run), ReviveArm.NoReviveRouteResolved);

            await presenter.StartAsync(CancellationToken.None);

            captions.Add(presenter.HeadingText);
            captions.AddRange(
            [
                presenter.BankedLabel,
                presenter.PayoutLabel,
                presenter.LegendXpLabel,
                presenter.SoulShardsLabel,
                presenter.FloorItemsLabel,
                presenter.ReviveText,
                presenter.FinishText,
                presenter.StatusText.Length == 0 ? presenter.FinishText : presenter.StatusText,
            ]);
            captions.AddRange(presenter.Counters.Select(row => row.Label));
        }

        foreach (var caption in captions)
        {
            caption.ShouldNotBeNullOrWhiteSpace();
            caption.StartsWith("loc.", StringComparison.Ordinal).ShouldBeFalse(
                "a caption fell through to its own key, so the string set does not carry it: " + caption);
        }
    }

    /// <summary>
    /// 🔒 No member of this screen reports a doubled reward or a retry.
    /// </summary>
    /// <remarks>
    /// 🔴 Stated over the type's surface rather than over one value, because the failure guarded against
    /// is a member being ADDED. `12` §7's <c>AD_DOUBLE_RUN_REWARDS</c> and <c>AD_FREE_RETRY</c> both need
    /// <c>CLAIM_AD_REWARD</c>, which is <c>Deferred → M15-03</c>, so a control offering either would be
    /// offering a command that cannot run — and a doubled TOTAL would be a number no command can pay.
    /// </remarks>
    [Fact]
    public void No_member_of_this_screen_offers_a_doubled_reward_or_a_retry()
    {
        var offenders =
            from property in typeof(RunEndPresenter).GetProperties()
            where property.Name.Contains("Double", StringComparison.Ordinal) ||
                  property.Name.Contains("Retry", StringComparison.Ordinal) ||
                  property.Name.Contains("Ad", StringComparison.Ordinal)
            select property.Name;

        offenders.ShouldBeEmpty(
            "a member of this screen reports an ad affordance. CLAIM_AD_REWARD is deferred to M15-03, " +
            "so whatever this member offers, no command can deliver it — and a doubled total shown " +
            "here would be a payout the game cannot make.");
    }

    // ---- the doors ------------------------------------------------------------------------------

    /// <summary>Every collaborator is required.</summary>
    [Fact]
    public void The_screen_refuses_a_null_collaborator()
    {
        var host = Finding(Dead());
        var strings = RunDecisionContent.Catalogue(RunDecisionContent.Strings());
        var content = BootContent.Shipped;

        Should.Throw<ArgumentNullException>(
            () => new RunEndPresenter(null!, strings, content, ReviveArm.PlusInstant, Player, Run));
        Should.Throw<ArgumentNullException>(
            () => new RunEndPresenter(host, null!, content, ReviveArm.PlusInstant, Player, Run));
        Should.Throw<ArgumentNullException>(
            () => new RunEndPresenter(host, strings, null!, ReviveArm.PlusInstant, Player, Run));
    }

    // ---- fixtures -------------------------------------------------------------------------------

    /// <summary>A host that finds this player and the given run.</summary>
    private static RecordingGameHost Finding(Core.Model.Snapshots.RunSnapshot run) =>
        RecordingGameHost.Finding(PlayerState.WithPityCounters(Player, EmptyCounters), run);

    /// <summary>
    /// A profile that has stood no drops yet, which is where a fresh account is — and the state in
    /// which every countdown is its whole rung.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, int> EmptyCounters =
        new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>
    /// A run standing dead on an unresolved stage-1 fight — where S13 opens.
    /// </summary>
    /// <remarks>
    /// 🔒 The pending tile is load-bearing rather than scenery: <c>Handlers.Revive</c> refuses a run with
    /// no pending fight, because the fight it restarts is the one the hero just lost. A fixture without
    /// one would describe a death no revive could ever apply to.
    /// </remarks>
    private static Core.Model.Snapshots.RunSnapshot Dead(
        long bankedLegendXp = 0,
        long bankedSoulShards = 0,
        bool reviveUsed = false) =>
        PlayerState.Run(
            Run,
            Player,
            RunPhase.InProgress,
            currentHp: 0,
            pendingTileKind: EnemyBattleTileKind,
            pendingTileLinearIndex: 7,
            pendingTileStage: FirstStage,
            bankedLegendXp: bankedLegendXp,
            bankedSoulShards: bankedSoulShards,
            adUses: reviveUsed
                ? new Dictionary<string, long> { [RevivePlacementId] = 1 }
                : null);

    /// <summary>A run whose Boss is dead — the only outcome that pays the victory multiplier.</summary>
    private static Core.Model.Snapshots.RunSnapshot Won() =>
        PlayerState.Run(
            Run, Player, RunPhase.InProgress, bankedLegendXp: 4_000, bossDefeated: true);

    /// <summary>A living hero on a run that could still go on, choosing to stop.</summary>
    private static Core.Model.Snapshots.RunSnapshot Abandoned() =>
        PlayerState.Run(Run, Player, RunPhase.InProgress, currentHp: 50);

    private static RunEndPresenter Build(RecordingGameHost host, ReviveArm arm)
    {
        var strings = RunDecisionContent.Strings();

        return new RunEndPresenter(
            host, RunDecisionContent.Catalogue(strings), BootContent.Shipped, arm, Player, Run);
    }
}
