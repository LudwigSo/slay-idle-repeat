using SlayIdleRepeat.Application.Ports.Client;
using SlayIdleRepeat.Application.UseCases;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Economy;

namespace SlayIdleRepeat.Client.Game.Presenters;

/// <summary>How far the run-end screen has got with the read everything it draws depends on.</summary>
public enum RunEndStage
{
    /// <summary>The read has not happened yet. The state a freshly built presenter reports.</summary>
    NotYetRead = 1,

    /// <summary>The run was read and the tally is drawn.</summary>
    Tallied = 2,

    /// <summary>There is no such run for this player.</summary>
    RunMissing = 3,

    /// <summary>The read itself did not answer. A state, never an escape.</summary>
    ReadUnavailable = 4,
}

/// <summary>What one submission from this screen did.</summary>
public enum RunEndSubmission
{
    /// <summary>The command went to the host and was accepted.</summary>
    Submitted = 1,

    /// <summary>Nothing was submitted, because the screen's own state does not permit it.</summary>
    RefusedNotAvailable = 2,

    /// <summary>The command went to the host and the rules layer refused it.</summary>
    RefusedByRules = 3,

    /// <summary>
    /// 🔒 The call itself did not complete. Told apart from <see cref="RefusedByRules"/> because a
    /// refusal is an answer about the game and a fault is the game not answering.
    /// </summary>
    HostUnavailable = 4,
}

/// <summary>How this player reaches a revive, resolved once by the composition root.</summary>
/// <remarks>
/// <para>
/// 🔒 <b>An arm rather than the entitlement flag, because a presenter may not branch on the flag at
/// all.</b> <c>IsolationTests.No_entitlement_branch_outside_a_composition_root</c> forbids any
/// <c>if</c>, <c>switch</c> or ternary outside a <c>Composition/</c> directory whose condition names a
/// subscription flag — <c>12</c> §3.2's rule that the Plus promise is an adapter swap chosen once, never
/// a condition sprinkled through the game. <c>ClientComposition.SelectRewardedAdPort</c> is the
/// precedent this mirrors: the root takes the one branch and hands the decision on as a value.
/// </para>
/// <para>
/// ⚠️ <b>Two arms, and the second is an absence rather than a denial.</b> <c>02</c> §6 gives Plus
/// subscribers an instant no-ad revive and everyone else the ad path — and the ad path needs
/// <c>CLAIM_AD_REWARD</c>, which is <c>Deferred → M15-03</c>. So the unentitled arm is *"no route is
/// built yet"* rather than *"you may not"* — and the screen draws nothing for it, neither a control
/// nor a sentence, because a sentence about a route that is not built advertises a feature the game
/// does not have.
/// </para>
/// </remarks>
public enum ReviveArm
{
    /// <summary>Plus is active, so <c>02</c> §6's revive resolves instantly with no ad.</summary>
    PlusInstant = 1,

    /// <summary>
    /// ⚠️ No Plus, and the ad route is not built: <c>CLAIM_AD_REWARD</c> is deferred to M15-03. The
    /// slot is kept and named rather than silently defaulted.
    /// </summary>
    NoReviveRouteResolved = 2,
}

/// <summary>
/// One <c>DROP_RUN</c> mercy counter as a row in the tally footer: the caption and the numbers.
/// </summary>
/// <remarks>
/// 🔒 Two fields, for the reason <see cref="PerkDraftGuaranteeRow"/> carries two: the arithmetic that
/// turns a counter and a rung into a countdown belongs to <c>RunEndView</c>, and a row carrying the raw
/// numbers would invite a second screen to subtract them differently. <c>24</c> §1.1's Authority rule is
/// that the client displays and never computes.
/// </remarks>
/// <param name="Label">
/// The counter's caption, already resolved — and it names the counter's UNIT, because <c>24</c> §9
/// requires that of every counter and the two here count different things.
/// </param>
/// <param name="Value">
/// The numbers beside it, <c>kills-remaining/rung</c>. Numerals and a separator only — never a word,
/// because the words are the caption's.
/// </param>
public sealed record RunEndCounterRow(string Label, string Value);

/// <summary>
/// S13 and S14 — the run-end moment: why the run is over, what it pays, and the two commands that
/// close it.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>One screen for the death offer and the tally, because <c>02</c> §6 makes them one moment.</b>
/// Its step 3 is explicit — <em>"If declined or already used, go to <c>RUN_RESULTS</c> with the Death
/// completion multiplier"</em> — so the revive offer and the reward tally are the same instant either
/// side of one decision. <see cref="RunEndView"/> is one projection for the same reason, and this screen
/// is one reader of it.
/// </para>
/// <para>
/// 🔒 <b>Every figure comes off the projection and none is computed here.</b> The gap between what the
/// run banked and what it is paid IS the completion multiplier, which <c>24</c> §9 wants legible — and a
/// screen that multiplied the banked figures itself would be a second implementation of
/// <c>Banked × CompletionMultiplier</c>, parting company with <c>END_RUN</c> the first time either was
/// retuned, with the player believing the screen.
/// </para>
/// <para>
/// 🔒 <b>Two commands, and <c>END_RUN</c> is always one of them.</b> <c>REVIVE</c> is offered only when
/// the rules would accept one and this player can reach one; <c>END_RUN</c> is the way off this screen in
/// every state it can be in, including the states where the revive is not on the table. A run-end screen
/// with no way out is a run a player cannot leave.
/// </para>
/// <para>
/// 🔒 <b>A revive this screen cannot reach is refused HERE, before the host.</b> The same argument
/// <c>InventoryPresenter</c> makes about an overflow equip: the reason is already on the screen, and
/// spending a round trip to be told what the screen knew teaches a player that the sentence beside the
/// button is not to be trusted.
/// </para>
/// <para>
/// ⚠️ <b>Deliberately absent, and named so it can be found: the ad surfaces.</b>
/// <c>AD_DOUBLE_RUN_REWARDS</c> and <c>AD_FREE_RETRY</c> both need <c>CLAIM_AD_REWARD</c>, which is
/// <c>Deferred → M15-03</c>, so this screen shows no doubled total and offers no retry — see
/// <see cref="ThereIsNoDoubledRewardToShow"/>. The revive's unentitled arm is the same kind of absence
/// and is drawn the same way — no control and no sentence — so nothing on this screen names an ad.
/// </para>
/// </remarks>
public sealed class RunEndPresenter
{
    /// <summary>
    /// ⚠️ Deliberately absent, and named so it can be found. There is no doubled-reward figure and no
    /// retry on this screen, and there is nothing it could submit if there were.
    /// </summary>
    private const string ThereIsNoDoubledRewardToShow =
        "12 §7's AD_DOUBLE_RUN_REWARDS and AD_FREE_RETRY are ad rewards, and the command that grants " +
        "an ad reward — CLAIM_AD_REWARD — is Deferred to M15-03. A screen showing a doubled total it " +
        "cannot pay would be promising a number no command can produce, and one offering a retry it " +
        "cannot start would be a dead control. RunEndView withholds the doubled figure for the same " +
        "reason and passes watchedAd: false explicitly, so the day M15-03 lands there is one call site " +
        "on each side to change and both are already named. The layout slot is kept (24 §9 / kickoff " +
        "assumption A2) so M15-03 fills it rather than re-laying out this screen.";

    private const string DefeatedNameKey = "loc.run_end.defeated.name";
    private const string VictoryNameKey = "loc.run_end.victory.name";
    private const string AbandonedNameKey = "loc.run_end.abandoned.name";

    private const string BankedLabelKey = "loc.run_end.banked.label";
    private const string PayoutLabelKey = "loc.run_end.payout.label";
    private const string LegendXpLabelKey = "loc.run_end.legend_xp.label";
    private const string FloorItemsLabelKey = "loc.run_end.floor_items.label";
    private const string EliteMercyLabelKey = "loc.run_end.elite_mercy.label";
    private const string BossMercyLabelKey = "loc.run_end.boss_mercy.label";

    /// <summary>
    /// 🔒 <c>tuning/currencies.json</c>'s own display name, borrowed rather than re-authored.
    /// </summary>
    /// <remarks>
    /// The same precedent the campfire's shrine rows follow for their buff names: the key is already
    /// referenced and already translated, so a second key for the same words would be a string somebody
    /// pays a human localiser for twice. Legend XP has no such name anywhere — it is not a
    /// <c>CurrencyId</c> — which is why the key above it belongs to this screen's own document.
    /// </remarks>
    private const string SoulShardsLabelKey = "loc.currency.soul_shards.name";

    private const string ReviveActionKey = "loc.run_end.revive.action";
    private const string FinishActionKey = "loc.run_end.finish.action";

    private const string ReviveSpentBlockKey = "loc.run_end.revive_spent.block";
    private const string DeathCostsRewardsBlockKey = "loc.run_end.death_costs_rewards.block";

    private const string LoadingStatusKey = "loc.run_end.loading.status";
    private const string RunMissingStatusKey = "loc.run_end.run_missing.status";
    private const string UnavailableStatusKey = "loc.run_end.unavailable.status";
    private const string RefusedStatusKey = "loc.run_end.refused.status";
    private const string HostUnavailableStatusKey = "loc.run_end.host_unavailable.status";

    /// <summary>What a line with nothing to say answers.</summary>
    private const string NothingLeftToSay = "";

    /// <summary>Separates a countdown from the rung it is counting towards, as the draft's rows do.</summary>
    private const char OverSeparator = '/';

    private readonly IGameHost _gameHost;
    private readonly LocaleStringCatalogue _strings;
    private readonly ContentSnapshot _content;
    private readonly ReviveArm _reviveArm;
    private readonly PlayerId _player;
    private readonly RunId _run;

    private RunEndView? _view;
    private bool _submissionInFlight;

    /// <summary>Builds the screen's driver.</summary>
    /// <param name="gameHost">The host every read and submission goes through.</param>
    /// <param name="strings">The device-locale string catalogue.</param>
    /// <param name="content">The loaded content set the projection reads against.</param>
    /// <param name="reviveArm">
    /// How this player reaches a revive, already resolved — see <see cref="ReviveArm"/> for why the
    /// entitlement itself never arrives here.
    /// </param>
    /// <param name="player">The profile whose run this is.</param>
    /// <param name="run">The run being closed.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public RunEndPresenter(
        IGameHost gameHost,
        LocaleStringCatalogue strings,
        ContentSnapshot content,
        ReviveArm reviveArm,
        PlayerId player,
        RunId run)
    {
        ArgumentNullException.ThrowIfNull(gameHost);
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentNullException.ThrowIfNull(content);

        _gameHost = gameHost;
        _strings = strings;
        _content = content;
        _reviveArm = reviveArm;
        _player = player;
        _run = run;
    }

    /// <summary>How far the read has got.</summary>
    public RunEndStage Stage { get; private set; } = RunEndStage.NotYetRead;

    /// <summary>Why the rules layer refused the last command that reached it, or null when none did.</summary>
    public RejectionReason? RulesRejection { get; private set; }

    /// <summary>Whether the last submission failed to complete at all.</summary>
    public bool HostFaulted { get; private set; }

    /// <summary>Why the run is over, or null until the read has settled.</summary>
    /// <remarks>
    /// Nullable rather than defaulted to one of the three: a screen that reported a victory before it
    /// had read anything would announce a cleared chapter to a player who had just died.
    /// </remarks>
    public RunEndKind? Outcome => _view?.Kind;

    /// <summary>The heading, which names which of the three ways this run ended, resolved.</summary>
    /// <remarks>
    /// 🔒 Three separate strings rather than one with the outcome substituted in: the catalogue has no
    /// interpolation, and an outcome that this build was never taught throws rather than borrowing a
    /// neighbour's heading — telling someone who quit that they died is worse than a crash on a screen
    /// whose first job is to say what happened.
    /// </remarks>
    public string HeadingText => Outcome switch
    {
        null => NothingLeftToSay,
        RunEndKind.Victory => _strings.Resolve(VictoryNameKey),
        RunEndKind.Death => _strings.Resolve(DefeatedNameKey),
        RunEndKind.Abandoned => _strings.Resolve(AbandonedNameKey),
        _ => throw new ArgumentOutOfRangeException(
            nameof(Outcome), Outcome, "No heading is authored for this run-end outcome."),
    };

    /// <summary>The caption over what the run earned, resolved.</summary>
    public string BankedLabel => _strings.Resolve(BankedLabelKey);

    /// <summary>The caption over what the player is actually paid, resolved.</summary>
    public string PayoutLabel => _strings.Resolve(PayoutLabelKey);

    /// <summary>The name of the Legend XP line, resolved.</summary>
    public string LegendXpLabel => _strings.Resolve(LegendXpLabelKey);

    /// <summary>The name of the Soul Shards line, resolved — see <see cref="SoulShardsLabelKey"/>.</summary>
    public string SoulShardsLabel => _strings.Resolve(SoulShardsLabelKey);

    /// <summary>The caption over the session-floor count, resolved.</summary>
    public string FloorItemsLabel => _strings.Resolve(FloorItemsLabelKey);

    /// <summary>Legend XP the run banked, as a player reads it. Empty until the read has settled.</summary>
    public string BankedLegendXpValue => Figure(_view?.BankedLegendXp);

    /// <summary>Soul Shards the run banked, as a player reads it.</summary>
    public string BankedSoulShardsValue => Figure(_view?.BankedSoulShards);

    /// <summary>Legend XP the player is paid, as a player reads it.</summary>
    public string PayoutLegendXpValue => Figure(_view?.PayoutLegendXp);

    /// <summary>Soul Shards the player is paid, as a player reads it.</summary>
    public string PayoutSoulShardsValue => Figure(_view?.PayoutSoulShards);

    /// <summary>How many items at or above the session floor's band this run produced.</summary>
    public string FloorItemsValue => Figure(_view?.ItemsAtOrAboveFloorBand);

    /// <summary>
    /// The <c>DROP_RUN</c> mercy counters in the tally footer, in <c>24</c> §4.3's order.
    /// </summary>
    /// <remarks>
    /// 🔒 <c>24</c> §1.1's Visibility rule is <em>always</em>, so these are drawn whatever they stand at
    /// — a counter at zero is exactly the row a "show it when it matters" reading would drop, and it is
    /// the row a player who has had no luck all session is looking for.
    /// </remarks>
    public IReadOnlyList<RunEndCounterRow> Counters { get; private set; } = [];

    /// <summary>Whether the revive control is offered at all.</summary>
    /// <remarks>
    /// 🔒 <b>Two facts, and they answer different questions.</b> <c>RunEndView.ReviveOffered</c> says the
    /// RULES would accept a revive; the arm says whether this player can reach one. Both have to hold, and
    /// neither is inferred from the other: a screen that took the projection's word alone would offer a
    /// button that cannot resolve, and one that took the arm's alone would offer a revive to a Plus
    /// subscriber whose run never died.
    /// </remarks>
    public bool ReviveAvailable =>
        Stage == RunEndStage.Tallied &&
        _view?.ReviveOffered == true &&
        _reviveArm == ReviveArm.PlusInstant;

    /// <summary>The revive control's caption, resolved.</summary>
    public string ReviveText => _strings.Resolve(ReviveActionKey);

    /// <summary>The way off this screen, resolved. Never conditional — see the type's remarks.</summary>
    public string FinishText => _strings.Resolve(FinishActionKey);

    /// <summary>
    /// The one sentence this screen keeps about a missing revive, resolved: the run spent its one.
    /// Empty in every other state.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>Only the spent revive gets a sentence.</b> <c>RunEndReviveStanding.AlreadyUsed</c> is the
    /// once-per-run limit reached — the run had its one safety net, and a player who presses nothing
    /// and finds no button deserves to be told why. An unentitled player whose run the rules would
    /// still revive gets neither a control nor a sentence: the only route that would serve them is an
    /// ad the game cannot show, and a sentence about a revive route that is not built advertises a
    /// feature the game does not have.
    /// </para>
    /// <para>
    /// 🔒 <b>Nothing is said when a revive never applied.</b> A cleared chapter and an abandoned run are
    /// not failed revives, and a sentence about reviving under a victory would read as a control that
    /// had broken.
    /// </para>
    /// </remarks>
    public string ReviveBlockText => _view?.ReviveStanding == RunEndReviveStanding.AlreadyUsed
        ? _strings.Resolve(ReviveSpentBlockKey)
        : NothingLeftToSay;

    /// <summary>
    /// The sentence naming what dying cost, resolved — and empty for a run that did not die.
    /// </summary>
    /// <remarks>
    /// 🔒 It is what makes the two figures legible rather than alarming. <c>24</c> §9 wants the completion
    /// multiplier visible, and a player shown a paid figure below the earned one with no explanation
    /// reads it as the game having taken something — so the gap is named, not merely displayed.
    /// </remarks>
    public string DeathCostsRewardsText => Outcome == RunEndKind.Death
        ? _strings.Resolve(DeathCostsRewardsBlockKey)
        : NothingLeftToSay;

    /// <summary>The one line a player reads for the state the screen is in, resolved.</summary>
    public string StatusText => Stage switch
    {
        RunEndStage.NotYetRead => _strings.Resolve(LoadingStatusKey),
        RunEndStage.Tallied => NothingLeftToSay,
        RunEndStage.RunMissing => _strings.Resolve(RunMissingStatusKey),
        _ => _strings.Resolve(UnavailableStatusKey),
    };

    /// <summary>The line about the last command the host answered, resolved — empty until one has.</summary>
    public string RejectionText => HostFaulted
        ? _strings.Resolve(HostUnavailableStatusKey)
        : RulesRejection is null ? NothingLeftToSay : _strings.Resolve(RefusedStatusKey);

    /// <summary>Reads the run and settles what the screen draws.</summary>
    /// <param name="ct">Cancelled when the application shuts down.</param>
    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            // Awaited inside the guard rather than merely called inside it: a real host's read is an
            // async method, so its failure arrives as a faulted task and a try around the call alone
            // would never see it.
            Settle(await _gameHost.ReadOwnStateAsync(_player, _run, ct).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            Unavailable(RunEndStage.ReadUnavailable);
        }
    }

    /// <summary>Submits <c>REVIVE</c> — <c>02</c> §6's one-per-run safety net.</summary>
    /// <remarks>
    /// 🔒 Refused here rather than sent when the screen already knows it cannot resolve. A spent revive
    /// has its sentence on screen before any tap, and the unentitled arm draws no control to tap at all —
    /// so a press reaching this guard is a press against a control the screen never offered. See the
    /// type's remarks for why the round trip is not spent.
    /// </remarks>
    /// <param name="ct">Cancelled when the application shuts down.</param>
    public async Task<RunEndSubmission> ReviveAsync(CancellationToken ct)
    {
        if (!ReviveAvailable)
        {
            return RunEndSubmission.RefusedNotAvailable;
        }

        return await SubmitAsync(new ReviveCommand(), ct).ConfigureAwait(false);
    }

    /// <summary>Submits <c>END_RUN</c> — banks the run's rewards and closes it.</summary>
    /// <remarks>
    /// 🔒 <b>Available in every state the read settled in, including the ones the tally could not be
    /// drawn for.</b> A run whose projection failed is still a run the player is inside, and a screen
    /// that withheld the way out until it could draw a tally would strand them there. What it will not
    /// do is submit against a run the host said does not exist — there is nothing to end.
    /// </remarks>
    /// <param name="ct">Cancelled when the application shuts down.</param>
    public async Task<RunEndSubmission> FinishAsync(CancellationToken ct)
    {
        if (Stage is RunEndStage.NotYetRead or RunEndStage.RunMissing)
        {
            return RunEndSubmission.RefusedNotAvailable;
        }

        return await SubmitAsync(new EndRunCommand(), ct).ConfigureAwait(false);
    }

    private async Task<RunEndSubmission> SubmitAsync(GameCommand command, CancellationToken ct)
    {
        if (_submissionInFlight)
        {
            return RunEndSubmission.RefusedNotAvailable;
        }

        _submissionInFlight = true;
        HostFaulted = false;

        try
        {
            var outcome = await _gameHost
                .SubmitAsync(_player, _run, command, ct)
                .ConfigureAwait(false);

            RulesRejection = outcome.Rejection;

            // 🔒 Nothing is re-read and nothing is redrawn on acceptance, and that is this screen's
            // shape rather than an omission: both commands END this screen. A revive puts the run back
            // into the fight it lost and END_RUN closes the run, so the scene hands over either way —
            // and a tally redrawn from a run that has just re-entered a battle would be a payout shown
            // for a run that is playing again.
            return outcome.Accepted ? RunEndSubmission.Submitted : RunEndSubmission.RefusedByRules;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // A faulted call carried no outcome, so there is no rejection to report and reporting one
            // would be inventing an answer the game never gave.
            HostFaulted = true;
            RulesRejection = null;

            return RunEndSubmission.HostUnavailable;
        }
        finally
        {
            // Released on completion: a collect that never answered banked nothing and left the run
            // open, so the retry has to be able to reach the host.
            _submissionInFlight = false;
        }
    }

    private void Settle(OwnStateResult state)
    {
        if (state.Lookup != OwnStateLookup.Found ||
            state.View is not { Player: { } player, Run: { } run })
        {
            Unavailable(RunEndStage.RunMissing);

            return;
        }

        try
        {
            _view = RunEndView.Project(player, run, _content);
            Counters = Rows(_view.DropCounters);
            Stage = RunEndStage.Tallied;
        }
        catch (ContentException)
        {
            // A content set that cannot answer is a state, not an exception to leak: the read itself
            // succeeded, so the screen says the run could not be tallied rather than that it could not
            // be found. Only a content read's failure is caught — a blanket catch would turn a
            // programming error inside the projection into the same quiet sentence.
            Unavailable(RunEndStage.ReadUnavailable);
        }
    }

    /// <summary>
    /// Puts the screen into a state that draws no tally, and clears the one it was drawing.
    /// </summary>
    /// <remarks>
    /// 🔒 The figures are dropped with the stage. Rows and numbers left standing from an earlier read
    /// would be a payout drawn for a run this screen can no longer see — and on this screen a stale
    /// figure is a promise about money.
    /// </remarks>
    private void Unavailable(RunEndStage stage)
    {
        Stage = stage;
        _view = null;
        Counters = [];
    }

    /// <summary>The projection's counters as rows, each with its caption already resolved.</summary>
    private IReadOnlyList<RunEndCounterRow> Rows(IReadOnlyList<RunEndCounterView> counters)
    {
        var rows = new RunEndCounterRow[counters.Count];

        for (var index = 0; index < rows.Length; index++)
        {
            var counter = counters[index];

            rows[index] = new RunEndCounterRow(
                _strings.Resolve(CaptionKey(counter.Kind)),
                Figure(counter.DropsUntilForced) + OverSeparator + Figure(counter.ForcedOnDrop));
        }

        return Array.AsReadOnly(rows);
    }

    /// <summary>The caption one counter's row is drawn with.</summary>
    /// <remarks>
    /// 🔒 Chosen from the counter's KIND rather than from its position, and two keys rather than one with
    /// the breaker's name substituted in: the catalogue has no interpolation, and the two count different
    /// units — Elite kills and boss kills — which <c>24</c> §9 requires each to say. A kind this build was
    /// never taught throws rather than borrowing its neighbour's caption, because a wrong unit beside a
    /// real number is the failure the Disclosure rule exists to prevent.
    /// </remarks>
    private static string CaptionKey(RunEndCounterKind kind) => kind switch
    {
        RunEndCounterKind.EliteMercy => EliteMercyLabelKey,
        RunEndCounterKind.BossMercy => BossMercyLabelKey,
        _ => throw new ArgumentOutOfRangeException(
            nameof(kind), kind, "No caption is authored for this DROP_RUN counter."),
    };

    /// <summary>
    /// A figure as a player reads it, or empty when there is no figure yet.
    /// </summary>
    /// <remarks>
    /// Through <see cref="PlayerNumber"/> rather than formatted here, because the shortening rule is the
    /// design's and binds every screen — and a run's Legend XP passes ten thousand, so this is a screen
    /// the rule actually acts on. Empty rather than a zero for an unread figure: a zero payout is a real
    /// answer a player can be given, and one drawn before the read would be a lie in the same digits.
    /// </remarks>
    private static string Figure(long? value) =>
        value is { } number ? PlayerNumber.Abbreviated(number) : NothingLeftToSay;
}
