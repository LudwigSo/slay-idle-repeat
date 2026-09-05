using System.Globalization;
using SlayIdleRepeat.Application.Ports.Client;
using SlayIdleRepeat.Application.UseCases;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;

namespace SlayIdleRepeat.Client.Game.Presenters;

/// <summary>How far the Minigame screen has got with the game its tile offers.</summary>
public enum MinigameStage
{
    /// <summary>The read has not happened yet. The state a freshly built presenter reports.</summary>
    NotYetRead = 1,

    /// <summary>The run is on a Minigame tile and the game is the player's to play.</summary>
    Playing = 2,

    /// <summary>A submission was accepted and the tile is cleared. What was won is on the screen.</summary>
    Resolved = 3,

    /// <summary>The run was read and is standing on some other tile. The screen is open on the wrong one.</summary>
    NotAtAMinigame = 4,

    /// <summary>
    /// This run has already resolved a minigame at the node it stands on. One submission per tile,
    /// so there is nothing left to play and nothing left to press but the way back.
    /// </summary>
    AlreadyResolved = 5,

    /// <summary>There is no such run for this player.</summary>
    RunMissing = 6,

    /// <summary>The read itself did not answer. A state, never an escape.</summary>
    ReadUnavailable = 7,

    /// <summary>
    /// The content set cannot describe this minigame — its reward table or its authored numbers are
    /// not readable. A sentence, not a crash.
    /// </summary>
    RulesUnavailable = 8,
}

/// <summary>What one submission from this screen did.</summary>
public enum MinigameSubmission
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

/// <summary>Which set of controls one arm is actually played with.</summary>
/// <remarks>
/// 🔒 <b>Answered here rather than in the scene.</b> Three arms are three different sets of controls
/// on one screen, and a scene deciding which to draw would have to hold the rules layer's own
/// minigame ids to tell a chest pick from a dice duel — a decision rule, and a transcription of
/// another assembly's vocabulary, both inside a <c>Node</c>. Asked as a question the presenter
/// answers, the scene switches on a state it was handed.
/// </remarks>
public enum MinigameControls
{
    /// <summary>
    /// None. The arm the rules layer knows and this client has no screen for; unreachable while a
    /// tile picks from the built arms alone, and named rather than defaulted to another game's.
    /// </summary>
    Unbuilt = 1,

    /// <summary>A bar, a sweeping marker, and a strike — or a step, under reduced motion.</summary>
    Timing = 2,

    /// <summary>Three chests, any of which sends the identical command.</summary>
    Chests = 3,

    /// <summary>One roll against the house.</summary>
    Dice = 4,
}

/// <summary>What the one control this screen carries does when it is pressed.</summary>
/// <remarks>
/// 🔴 <b>Every state this screen can settle in answers this with something other than nothing,
/// except the one where the player still has a game to play.</b> The screen is opened by a routing
/// table mid-run, so a settled state it cannot act in and cannot leave was a run that could only be
/// left by killing the application. Copied deliberately from <see cref="EventExit"/>, whose own
/// remarks record why the faulted read is the one state that re-reads rather than handing back.
/// </remarks>
public enum MinigameExit
{
    /// <summary>Nothing, and the control is drawn out of use — the game is still being played.</summary>
    Nowhere = 1,

    /// <summary>
    /// 🔒 Reads the run again, in place. A read that never answered knows nothing about the run, and
    /// handing back would give the board a decision it has already latched.
    /// </summary>
    ReadAgain = 2,

    /// <summary>Hands back to the board, which re-reads the run and routes it wherever it now belongs.</summary>
    ToTheBoard = 3,
}

/// <summary>
/// Drives the Minigame screen: the tile offers one of three games, the player plays it, and
/// <c>MINIGAME_SUBMIT</c> pays the outcome tier and clears the tile.
/// </summary>
/// <remarks>
/// <para>
/// Plain C# taking its collaborators as constructor arguments, so the whole screen runs under a test
/// runner with no engine anywhere near it.
/// </para>
/// <para>
/// 🔒 <b>The two authority arms are different screens behind one presenter.</b> The timing bar is
/// client-asserted: the tier is the hit count and the submission carries it, so it may only be sent
/// once the game is finished — a submission mid-game would claim a tier the player has not earned and
/// the rules layer would take it. The chest pick and the dice duel are server-rolled: they submit a
/// tier of zero, which the handler ignores, and the tier they actually got comes back on
/// <c>MinigameResolved</c>. No persisted field carries it, so a screen that reported its own claim
/// would tell the player they won something the server never paid.
/// </para>
/// <para>
/// 🔴 <b>The resolution event is found by TYPE, never by position.</b> <c>MINIGAME_SUBMIT</c> emits
/// it after the reward rows and after the pity counter's own event, and nothing pins that order — so
/// "the last event" is a reading that a fifth event added anywhere would silently break, on the one
/// number a player takes away from this screen.
/// </para>
/// <para>
/// 🔒 <b>Which arm a tile offers is <see cref="MinigameChoice"/>'s, and it is unenforced.</b> The id
/// arrives as a constructor argument rather than being decided here, so the one place the pick is
/// made is the one place its remarks describe what it is worth.
/// </para>
/// <para>
/// 🔒 <b>The guarantee line is the chest pick's alone.</b> The other three minigames are skill-scaled
/// and carry no pity counter at all, so a countdown drawn on them would be a promise nothing keeps.
/// </para>
/// <para>
/// 🔒 <b>The timing bar's numbers are read for EVERY arm, not only for the bar.</b> They are the one
/// part of this screen a content set can be missing while still describing the rewards, and a screen
/// that read them lazily would open a chest pick happily and fail at the moment a later tile offered
/// the bar. One state, answered at the read: a content set that cannot describe the whole screen
/// cannot describe this tile.
/// </para>
/// <para>
/// 🔒 <b>The pre-read reward ladder is KEPT across a refusal.</b> A refused command comes back
/// carrying an empty state slice — the player's own row and no run — so a screen that settled itself
/// again off a refused outcome would blank the ladder the player is still looking at.
/// </para>
/// <para>
/// The screen's strings are the <c>loc.minigame.*</c> group, authored in
/// <c>content/minigame/minigame.json</c> and named the way the sibling screens name theirs: a
/// camelCase member becomes a snake_case key with its group as the suffix
/// (<c>label.strikesLeft</c> → <c>loc.minigame.strikes_left.label</c>). The outcome captions are the
/// one derived group — an authored outcome token lower-cased into
/// <c>loc.minigame.&lt;token&gt;.outcome</c> — so a thirteenth reward row cannot ship without one.
/// </para>
/// <para>
/// ⚠️ <b>A reward row's fixed dice have no caption anywhere in the content set, so the ladder does
/// not list them.</b> The four currencies a reward can pay are captioned by
/// <c>tuning/currencies.json</c>'s own <c>loc.currency.&lt;snake&gt;.name</c> keys, which this screen
/// borrows rather than duplicating; a die is not a currency and carries no such key, and the
/// <c>loc.minigame.*</c> group authors none either. One shipped row — the dice duel's top outcome —
/// grants a die, and its preview is silent about it. Naming it needs one authored key
/// (<c>loc.minigame.fixed_dice.label</c>) in the document, the schema and both locales.
/// </para>
/// </remarks>
public sealed class MinigamePresenter
{
    private const string TitleNameKey = "loc.minigame.title.name";
    private const string ChestPickNameKey = "loc.minigame.chest_pick.name";
    private const string TimingBarNameKey = "loc.minigame.timing_bar.name";
    private const string DiceDuelNameKey = "loc.minigame.dice_duel.name";
    private const string MemoryRuneNameKey = "loc.minigame.memory_rune.name";

    private const string ChestPickRuleKey = "loc.minigame.chest_pick.rule";
    private const string TimingBarRuleKey = "loc.minigame.timing_bar.rule";
    private const string DiceDuelRuleKey = "loc.minigame.dice_duel.rule";
    private const string MemoryRuneRuleKey = "loc.minigame.memory_rune.rule";

    private const string RewardsLabelKey = "loc.minigame.rewards.label";
    private const string HitsLabelKey = "loc.minigame.hits.label";
    private const string StrikesLeftLabelKey = "loc.minigame.strikes_left.label";
    private const string GuaranteeLabelKey = "loc.minigame.guarantee.label";
    private const string ResultLabelKey = "loc.minigame.result.label";

    private const string StrikeActionKey = "loc.minigame.strike.action";
    private const string StepActionKey = "loc.minigame.step.action";
    private const string PickChestActionKey = "loc.minigame.pick_chest.action";
    private const string RollActionKey = "loc.minigame.roll.action";
    private const string ContinueActionKey = "loc.minigame.continue.action";

    private const string LoadingStatusKey = "loc.minigame.loading.status";
    private const string PlayingStatusKey = "loc.minigame.playing.status";
    private const string RunMissingStatusKey = "loc.minigame.run_missing.status";
    private const string NotAtAMinigameStatusKey = "loc.minigame.not_at_a_minigame.status";
    private const string ReadUnavailableStatusKey = "loc.minigame.read_unavailable.status";
    private const string RulesUnavailableStatusKey = "loc.minigame.rules_unavailable.status";
    private const string AlreadyResolvedStatusKey = "loc.minigame.already_resolved.status";
    private const string RefusedStatusKey = "loc.minigame.refused.status";
    private const string HostUnavailableStatusKey = "loc.minigame.host_unavailable.status";

    /// <summary>What an outcome caption key is derived out of, around the lower-cased token.</summary>
    private const string OutcomeKeyPrefix = "loc.minigame.";

    private const string OutcomeKeySuffix = ".outcome";

    private const string GoldCurrencyNameKey = "loc.currency.gold.name";
    private const string CrownsCurrencyNameKey = "loc.currency.crowns.name";
    private const string BeastFeedCurrencyNameKey = "loc.currency.beast_feed.name";
    private const string EnhanceStonesCurrencyNameKey = "loc.currency.enhance_stones.name";

    /// <summary>The three arms this client draws, as the reward tables key them.</summary>
    /// <remarks>
    /// 🔒 Content keys rather than transcribed enum members: <c>MinigameCatalogue</c> is internal to
    /// Core, and what this screen needs is the name under <c>#/minigameRewards</c> that also picks a
    /// string group. <see cref="MinigameArms"/> is what holds this vocabulary against the catalogue,
    /// and <c>MinigamePresenterTests</c> is where the two are compared.
    /// </remarks>
    private const string ChestPickId = "MG_CHEST_PICK";

    /// <inheritdoc cref="ChestPickId"/>
    private const string TimingBarId = "MG_TIMING_BAR";

    /// <inheritdoc cref="ChestPickId"/>
    private const string DiceDuelId = "MG_DICE_DUEL";

    /// <summary>The status line of a screen that has nothing left to say.</summary>
    private const string NothingLeftToSay = "";

    /// <summary>What separates an amount from the currency it is paid in.</summary>
    private const string AmountAndCurrency = " ";

    /// <summary>What separates one currency of a reward row from the next.</summary>
    private const string BetweenAmounts = "   ";

    /// <summary>What separates the guarantee's caption from the two numbers it names.</summary>
    private const string CaptionAndCount = "  ";

    /// <summary>What stands between the picks still to stand and the pick that is forced.</summary>
    private const string OfTheStreak = " / ";

    /// <summary>The tile kind a minigame is, as the run reports it.</summary>
    /// <remarks>
    /// 🔒 Read off the rules layer's own enum and NOT transcribed: the numbering is public, so a kind
    /// inserted above this one renumbers the constant with it rather than leaving a literal pointing
    /// at whatever moved into its slot.
    /// </remarks>
    public const int MinigameTileKind = (int)TileKind.Minigame;

    /// <summary>What <see cref="ResolvedTier"/> reads before any outcome has been paid.</summary>
    /// <remarks>
    /// Negative rather than zero, because zero is a real tier — the lowest one, and the one the
    /// server-rolled arms submit as a placeholder. A sentinel a game can also mean would report the
    /// worst outcome on a screen that has resolved nothing.
    /// </remarks>
    public const int NoTierYet = -1;

    /// <summary>The tier a server-rolled submission carries, which the handler never reads.</summary>
    public const int ClaimIgnoredByTheServer = 0;

    private readonly IGameHost _gameHost;
    private readonly LocaleStringCatalogue _strings;
    private readonly ContentSnapshot _content;
    private readonly PlayerId _player;
    private readonly RunId _run;

    /// <summary>The profile the read found, which the pity counter is read off.</summary>
    private PlayerSnapshot? _wallet;

    /// <summary>The timing bar's authored numbers, read once the content set has answered for them.</summary>
    private TimingBarRules? _rules;

    /// <summary>The game itself, built once so a redraw cannot restart a game mid-play.</summary>
    private TimingBarGame? _game;

    /// <summary>The chest pick's counter as the read left it, or null on the other arms.</summary>
    private MinigameGuaranteeView? _guarantee;

    private bool _submissionInFlight;

    /// <summary>Builds the screen over the host, the strings, the content set, the run and one arm.</summary>
    /// <param name="gameHost">The seam the run is read through and its command is submitted through.</param>
    /// <param name="strings">Key to display string, over the loaded content set.</param>
    /// <param name="content">The loaded content set the reward rows and the authored numbers come from.</param>
    /// <param name="player">The profile this run belongs to.</param>
    /// <param name="run">The run being played.</param>
    /// <param name="minigameId">Which arm this tile offers — <see cref="MinigameChoice"/>'s answer.</param>
    /// <param name="reducedMotion">
    /// Whether the timing bar is played by stepping rather than by timing. Defaulted false and
    /// nothing detects it yet, matching the two other screens that take the same flag; the settings
    /// screen that would remember it is unbuilt.
    /// </param>
    /// <exception cref="ArgumentNullException">A collaborator is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="minigameId"/> is blank.</exception>
    public MinigamePresenter(
        IGameHost gameHost,
        LocaleStringCatalogue strings,
        ContentSnapshot content,
        PlayerId player,
        RunId run,
        string minigameId,
        bool reducedMotion = false)
    {
        ArgumentNullException.ThrowIfNull(gameHost);
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(minigameId);

        _gameHost = gameHost;
        _strings = strings;
        _content = content;
        _player = player;
        _run = run;

        MinigameId = minigameId;
        ReducedMotion = reducedMotion;
    }

    /// <summary>Which arm this tile offers.</summary>
    public string MinigameId { get; }

    /// <summary>Whether the timing bar is being played by stepping rather than by timing.</summary>
    public bool ReducedMotion { get; }

    /// <summary>How far the screen has got with the game.</summary>
    public MinigameStage Stage { get; private set; } = MinigameStage.NotYetRead;

    /// <summary>Why the rules layer refused the last command that reached it, or null when none did.</summary>
    public RejectionReason? RulesRejection { get; private set; }

    /// <summary>Whether the last submission failed to complete at all.</summary>
    public bool HostFaulted { get; private set; }

    /// <summary>Whether the tile has cleared, so Continue may return to the board.</summary>
    /// <remarks>
    /// 🔒 False until the run reports no pending tile. The board's decision latch logs "halted" if
    /// the same decision re-opens with the tile still pending, so handing back early is a loop.
    /// </remarks>
    public bool CanLeave { get; private set; }

    /// <summary>What the one control this screen carries does when it is pressed.</summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>No state this screen settles in is a locked door.</b> <see cref="CanLeave"/> is the
    /// ordinary way off. Every other settled state is one this screen cannot act in at all: the run
    /// was not found, the run stands somewhere else, this tile's one submission has been spent, the
    /// content set cannot describe the game, or the read never answered. In each of those there is no
    /// game drawn, so this control is the only thing on screen — and gating it on
    /// <see cref="CanLeave"/> alone leaves the player looking at a sentence with nothing to press.
    /// </para>
    /// <para>
    /// 🔒 <b>The read that never answered goes back to the READ, not to the board.</b> The others
    /// know what the run is doing and the board can route on it. A read that faulted knows nothing:
    /// hand back on it and the board re-reads, finds the minigame tile still pending, and hands to
    /// the decision it has already latched — which it refuses as halted, leaving a live board whose
    /// own control cannot resolve that tile either.
    /// </para>
    /// </remarks>
    public MinigameExit Exit => CanLeave
        ? MinigameExit.ToTheBoard
        : Stage switch
        {
            MinigameStage.ReadUnavailable => MinigameExit.ReadAgain,
            MinigameStage.RunMissing or MinigameStage.NotAtAMinigame or
                MinigameStage.AlreadyResolved or MinigameStage.RulesUnavailable =>
                MinigameExit.ToTheBoard,
            _ => MinigameExit.Nowhere,
        };

    /// <summary>Whether this arm's outcome is the server's to draw.</summary>
    public bool IsServerRolled { get; private set; }

    /// <summary>Which set of controls this arm is played with.</summary>
    public MinigameControls Controls => MinigameId switch
    {
        ChestPickId => MinigameControls.Chests,
        TimingBarId => MinigameControls.Timing,
        DiceDuelId => MinigameControls.Dice,
        _ => MinigameControls.Unbuilt,
    };

    /// <summary>Every outcome tier this arm pays, chapter-scaled, in ascending tier order.</summary>
    public IReadOnlyList<MinigameTierRow> Rows { get; private set; } = [];

    /// <summary>Where the timing bar's cursor stands, 0 to 1. Meaningless on the other two arms.</summary>
    public double Cursor => _game?.Cursor ?? 0;

    /// <summary>Half the timing bar's scoring window, so the scene draws the window it is judged by.</summary>
    public double HitWindowHalfWidth => _game?.HalfWidth ?? 0;

    /// <summary>How many timing-bar strikes have scored.</summary>
    public int Hits => _game?.Hits ?? 0;

    /// <summary>How many timing-bar strikes are left.</summary>
    public int StrikesLeft => _game?.StrikesLeft ?? 0;

    /// <summary>Whether the timing bar has been played out, so a submission carries a settled tier.</summary>
    public bool Finished => _game?.Finished ?? false;

    /// <summary>
    /// The tier that was actually paid, or <see cref="NoTierYet"/> before anything resolved.
    /// </summary>
    /// <remarks>
    /// 🔒 On the server-rolled arms this comes off the resolution event and nowhere else. The claim
    /// the command carried is not an answer, and no persisted field records the draw.
    /// </remarks>
    public int ResolvedTier { get; private set; } = NoTierYet;

    /// <summary>The authored outcome token that was paid, empty before anything resolved.</summary>
    public string ResolvedOutcome { get; private set; } = NothingLeftToSay;

    /// <summary>The screen's heading, resolved.</summary>
    public string Title => _strings.Resolve(TitleNameKey);

    /// <summary>This arm's own name, resolved.</summary>
    public string ArmName => _strings.Resolve(NameKeyOf(MinigameId));

    /// <summary>How this arm is played, in one sentence, resolved.</summary>
    public string RuleText => _strings.Resolve(RuleKeyOf(MinigameId));

    /// <summary>The heading the reward rows are listed under, resolved.</summary>
    public string RewardsLabel => _strings.Resolve(RewardsLabelKey);

    /// <summary>The caption on the hit count, resolved.</summary>
    public string HitsLabel => _strings.Resolve(HitsLabelKey);

    /// <summary>The caption on the strikes remaining, resolved.</summary>
    public string StrikesLeftLabel => _strings.Resolve(StrikesLeftLabelKey);

    /// <summary>The caption on the guarantee line, resolved.</summary>
    public string GuaranteeLabel => _strings.Resolve(GuaranteeLabelKey);

    /// <summary>The heading the resolved outcome is shown under, resolved.</summary>
    public string ResultLabel => _strings.Resolve(ResultLabelKey);

    /// <summary>The timing bar's strike action, resolved.</summary>
    public string StrikeText => _strings.Resolve(StrikeActionKey);

    /// <summary>The reduced-motion step action, resolved.</summary>
    public string StepText => _strings.Resolve(StepActionKey);

    /// <summary>The chest pick's action, resolved.</summary>
    public string PickChestText => _strings.Resolve(PickChestActionKey);

    /// <summary>The dice duel's action, resolved.</summary>
    public string RollText => _strings.Resolve(RollActionKey);

    /// <summary>The one action that leaves this screen, resolved.</summary>
    public string ContinueText => _strings.Resolve(ContinueActionKey);

    /// <summary>
    /// The chest pick's guarantee in words, naming both counter numbers. Empty on the other arms.
    /// </summary>
    /// <remarks>
    /// 🔒 Both numbers, because either alone is unreadable: "you have missed three" says nothing
    /// about when the guarantee lands, and "one more chest" says nothing about the streak it is
    /// counting — and a player watching a pity counter is watching precisely the pair. Both are
    /// asked of the projection, which asks the guarantee rule itself, so this line cannot come to a
    /// different count from the one the server keeps.
    /// </remarks>
    public string GuaranteeText => _guarantee is { } guarantee
        ? GuaranteeLabel + CaptionAndCount +
          guarantee.PicksUntilForced.ToString(CultureInfo.InvariantCulture) + OfTheStreak +
          guarantee.ForcedOnPick.ToString(CultureInfo.InvariantCulture)
        : NothingLeftToSay;

    /// <summary>The line saying what the screen is doing while it has something to say, resolved.</summary>
    public string StatusText => Stage switch
    {
        MinigameStage.NotYetRead => _strings.Resolve(LoadingStatusKey),
        MinigameStage.Playing => _strings.Resolve(PlayingStatusKey),
        MinigameStage.Resolved => NothingLeftToSay,
        MinigameStage.NotAtAMinigame => _strings.Resolve(NotAtAMinigameStatusKey),
        MinigameStage.AlreadyResolved => _strings.Resolve(AlreadyResolvedStatusKey),
        MinigameStage.RunMissing => _strings.Resolve(RunMissingStatusKey),
        MinigameStage.RulesUnavailable => _strings.Resolve(RulesUnavailableStatusKey),
        _ => _strings.Resolve(ReadUnavailableStatusKey),
    };

    /// <summary>The line about the last command the host answered, resolved — empty until one has.</summary>
    /// <remarks>
    /// 🔒 The fault is read first, because a faulted submission carries no rejection at all and the
    /// two must not share a sentence.
    /// </remarks>
    public string RejectionText => HostFaulted
        ? _strings.Resolve(HostUnavailableStatusKey)
        : RulesRejection is null ? NothingLeftToSay : _strings.Resolve(RefusedStatusKey);

    /// <summary>One authored outcome token as a player reads it, resolved.</summary>
    /// <param name="outcome">The token, as the reward table authors it.</param>
    /// <exception cref="ArgumentNullException"><paramref name="outcome"/> is null.</exception>
    public string OutcomeText(string outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        return _strings.Resolve(OutcomeKeyPrefix + outcome.ToLowerInvariant() + OutcomeKeySuffix);
    }

    /// <summary>What one tier of the ladder pays, as a player reads it.</summary>
    /// <remarks>
    /// 🔒 Only the columns that moved, and each captioned by <c>tuning/currencies.json</c>'s own key
    /// rather than by a second copy this screen would author — a reward row describing a wallet is
    /// the last place two names for one currency should be able to drift apart. A row's fixed dice
    /// are not listed, because nothing in the content set names a die; the type's own remarks record
    /// which key would be needed.
    /// </remarks>
    /// <param name="row">The tier row, as the projection scaled it.</param>
    public string RewardText(MinigameTierRow row)
    {
        var paid = new List<string>(4);

        Pay(paid, row.Gold, GoldCurrencyNameKey);
        Pay(paid, row.Crowns, CrownsCurrencyNameKey);
        Pay(paid, row.BeastFeed, BeastFeedCurrencyNameKey);
        Pay(paid, row.EnhanceStones, EnhanceStonesCurrencyNameKey);

        return string.Join(BetweenAmounts, paid);
    }

    /// <summary>Moves the timing bar's clock forward. Does nothing on the other two arms.</summary>
    /// <param name="delta">Seconds since the last frame. Never negative.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="delta"/> is negative.</exception>
    public void Advance(double delta) => _game?.Advance(delta);

    /// <summary>Moves the timing bar's cursor one authored step — the reduced-motion way to aim.</summary>
    public void Step() => _game?.Step();

    /// <summary>Takes one timing-bar strike, and answers whether it scored.</summary>
    /// <remarks>
    /// Refused outright while the screen is not on a game the player may play, so a press that
    /// arrived a frame after a submission landed cannot spend a strike on a settled game.
    /// </remarks>
    public bool Strike() =>
        Stage == MinigameStage.Playing && !_submissionInFlight && (_game?.Strike() ?? false);

    /// <summary>Reads the run and settles the screen on the game it is standing on.</summary>
    /// <param name="ct">Cancellation.</param>
    public async Task StartAsync(CancellationToken ct)
    {
        OwnStateResult state;

        try
        {
            // Awaited inside the guard rather than merely called inside it: a real host's read is an
            // async method, so its failure arrives as a faulted task and a try around the call alone
            // would never see it.
            state = await _gameHost.ReadOwnStateAsync(_player, _run, ct).ConfigureAwait(false);
        }
        catch (Exception)
        {
            Stage = MinigameStage.ReadUnavailable;

            return;
        }

        if (state.Lookup != OwnStateLookup.Found || state.View is not { Run: { } run } found)
        {
            Stage = MinigameStage.RunMissing;

            return;
        }

        _wallet = found.Player;

        Settle(run, aSubmissionWasSpent: false);
    }

    /// <summary>Submits <c>MINIGAME_SUBMIT</c> for this arm.</summary>
    /// <remarks>
    /// <para>
    /// 🔒 Refused by this screen while the timing bar is unfinished: the tier is the hit count, and a
    /// game submitted early claims a tier the player has not played for. Every tier is a legal claim,
    /// so the rules layer would accept it, pay it and clear the tile.
    /// </para>
    /// <para>
    /// 🔒 The latch is taken BEFORE the await. Taken afterwards, a second press arriving while the
    /// first is in flight finds it unset and submits again — and the rules layer refuses the second
    /// as a duplicate at the same position, which reaches the player as <c>ILLEGAL_STATE</c> on a
    /// screen that has just paid them.
    /// </para>
    /// </remarks>
    /// <param name="ct">Cancellation.</param>
    public async Task<MinigameSubmission> SubmitAsync(CancellationToken ct)
    {
        if (Stage != MinigameStage.Playing || _submissionInFlight ||
            (!IsServerRolled && !Finished))
        {
            return MinigameSubmission.RefusedNotAvailable;
        }

        _submissionInFlight = true;
        HostFaulted = false;

        var command = new MinigameSubmitCommand(
            MinigameId, IsServerRolled ? ClaimIgnoredByTheServer : Hits);

        try
        {
            var outcome = await _gameHost.SubmitAsync(_player, _run, command, ct)
                .ConfigureAwait(false);

            RulesRejection = outcome.Rejection;

            if (!outcome.Accepted)
            {
                // 🔴 Nothing is settled off a refusal. The outcome's state slice carries the
                // player's row and no run at all, so re-reading the screen off it would blank the
                // ladder the player is still looking at on a tile that is still pending.
                return MinigameSubmission.RefusedByRules;
            }

            ReadWhatWasPaid(outcome.Events);

            // The state comes back with the outcome rather than being read again: a second read
            // would be a window in which the screen still draws a game the command has spent.
            if (outcome.State.Run?.ToSnapshot() is { } moved)
            {
                Settle(moved, aSubmissionWasSpent: true);
            }

            return MinigameSubmission.Submitted;
        }
        catch (Exception)
        {
            // A faulted call carried no outcome, so there is no rejection to report and reporting
            // one would be inventing an answer the game never gave.
            HostFaulted = true;
            RulesRejection = null;

            return MinigameSubmission.HostUnavailable;
        }
        finally
        {
            // Released on completion: a submission that never answered spent nothing and left the
            // tile pending, so the retry has to be able to reach the host.
            _submissionInFlight = false;
        }
    }

    /// <summary>Settles the screen on the run as it now stands.</summary>
    /// <param name="run">The row the read or the command answered with.</param>
    /// <param name="aSubmissionWasSpent">
    /// Whether this run arrived as the answer to <c>MINIGAME_SUBMIT</c>. 🔒 It is what tells a
    /// cleared tile apart from a screen opened on the wrong one: the two rows are identical, and the
    /// handler clears the tile as its last step.
    /// </param>
    private void Settle(RunSnapshot run, bool aSubmissionWasSpent)
    {
        CanLeave = run.PendingTileKind == BoardTileKinds.NoPendingTile;

        if (run.PendingTileKind != MinigameTileKind)
        {
            Stage = aSubmissionWasSpent && CanLeave
                ? MinigameStage.Resolved
                : MinigameStage.NotAtAMinigame;

            return;
        }

        Project(run);
    }

    /// <remarks>
    /// 🔒 Only a content failure is caught, and it is a sentence rather than a crash: a content set
    /// stripped of the timing bar's numbers, a reward table that cannot pay the strike count it is
    /// authored beside, and a minigame id this content version has lost are all the content set
    /// failing to describe a game the run really is standing on. A blanket catch would turn a
    /// programming error inside the projection into the same quiet line, and the screen would report
    /// missing data while the ladder it drew disagreed with what the tile is about to pay.
    /// </remarks>
    private void Project(RunSnapshot run)
    {
        if (_wallet is not { } profile)
        {
            // Unreachable through StartAsync, which settles only on a read that answered with both
            // rows. Named rather than defaulted to an empty profile: the chest pick's counter lives
            // on that row, and a projection without one reports a player mid-streak as one starting
            // clean.
            Stage = MinigameStage.RunMissing;

            return;
        }

        try
        {
            _rules ??= TimingBarRules.Read(_content);

            if (MinigameView.Project(run, profile, _content, MinigameId) is not { } view)
            {
                Stage = MinigameStage.NotAtAMinigame;

                return;
            }

            Rows = view.Rows;
            IsServerRolled = view.IsServerRolled;
            _guarantee = view.Guarantee;

            if (view.AlreadyResolvedHere)
            {
                Stage = MinigameStage.AlreadyResolved;

                return;
            }

            // Built once and kept: a redraw that rebuilt the game would put every strike already
            // taken back in the player's hand.
            _game ??= new TimingBarGame(_rules, ReducedMotion);

            Stage = MinigameStage.Playing;
        }
        catch (ContentException)
        {
            Stage = MinigameStage.RulesUnavailable;
        }
        catch (ArgumentException)
        {
            // The projection's own throw for an id it does not know — a content rollback across a
            // live run. Every other argument is non-null by construction, so this is that and
            // nothing else.
            Stage = MinigameStage.RulesUnavailable;
        }
    }

    /// <summary>What tier the accepted submission actually paid, and what it is called.</summary>
    /// <remarks>
    /// 🔴 <b>Found by TYPE.</b> <c>MINIGAME_SUBMIT</c> emits the resolution after the currency rows
    /// and after the pity counter's own event, and nothing pins that order — so a screen reading "the
    /// last event" would report the wrong number the day a sixth event is added anywhere in that
    /// handler, on the one figure a player takes away from here.
    /// <para>
    /// 🔒 A server-rolled arm with no resolution event claims NOTHING. Falling back to the tier the
    /// command carried would report the bottom outcome as the win, and the missing-event case is a
    /// real one: an older server, or a handler that stopped emitting.
    /// </para>
    /// </remarks>
    private void ReadWhatWasPaid(IReadOnlyList<DomainEvent> events)
    {
        if (events.OfType<MinigameResolved>().FirstOrDefault() is { } resolved)
        {
            ResolvedTier = resolved.Tier;
            ResolvedOutcome = resolved.Outcome;

            return;
        }

        if (IsServerRolled)
        {
            return;
        }

        // The client-asserted arm claimed its own hit count and the claim was accepted, so the tier
        // paid is the tier played. The caption comes off the ladder already drawn rather than being
        // restated.
        ResolvedTier = Hits;
        ResolvedOutcome = ResolvedTier >= 0 && ResolvedTier < Rows.Count
            ? Rows[ResolvedTier].Outcome
            : NothingLeftToSay;
    }

    /// <summary>Adds one currency of a reward row, when the row pays any of it.</summary>
    private void Pay(List<string> paid, long amount, string currencyNameKey)
    {
        if (amount != 0)
        {
            paid.Add(
                PlayerNumber.Abbreviated(amount) + AmountAndCurrency +
                _strings.Resolve(currencyNameKey));
        }
    }

    /// <summary>The key one arm's own name is resolved through.</summary>
    /// <remarks>
    /// 🔒 All four arms are mapped, including the one with no screen: the rules layer accepts a
    /// submission for Rune Recall and its outcome tokens are in the reward table, so an id reaching
    /// here with no name authored for it would be the one arm still reachable and the one arm with
    /// no words. The fourth is the default arm because the catalogue knows exactly four — an id
    /// outside it is refused by the projection at the read, before any caption is drawn.
    /// </remarks>
    private static string NameKeyOf(string minigameId) => minigameId switch
    {
        ChestPickId => ChestPickNameKey,
        TimingBarId => TimingBarNameKey,
        DiceDuelId => DiceDuelNameKey,
        _ => MemoryRuneNameKey,
    };

    /// <inheritdoc cref="NameKeyOf"/>
    private static string RuleKeyOf(string minigameId) => minigameId switch
    {
        ChestPickId => ChestPickRuleKey,
        TimingBarId => TimingBarRuleKey,
        DiceDuelId => DiceDuelRuleKey,
        _ => MemoryRuneRuleKey,
    };
}
