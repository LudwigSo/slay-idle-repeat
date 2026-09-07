using System.Globalization;
using SlayIdleRepeat.Application.Ports.Client;
using SlayIdleRepeat.Application.Ports.Shared;
using SlayIdleRepeat.Application.Services;
using SlayIdleRepeat.Application.UseCases;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Economy;

namespace SlayIdleRepeat.Client.Game.Presenters;

/// <summary>
/// What the Home screen's primary action does, decided from stored state and nothing else.
/// </summary>
/// <remarks>
/// A named value rather than a bool, because "there is no run", "the last run finished", "there is
/// no such profile" and "the read did not answer" are four different facts about the world that a
/// two-state answer would collapse into one. Nothing is persisted or remembered by the client to
/// decide this: every value below is a function of one <c>ReadOwnStateAsync</c> answer.
/// </remarks>
public enum HomeContinueDecision
{
    /// <summary>The read has not happened yet. The state a freshly built presenter reports.</summary>
    NotYetRead = 1,

    /// <summary>
    /// There is no run to resume — either none was ever started, or the last one has ended. A
    /// finished run is what the next <c>START_RUN</c> clears, so a second run is startable.
    /// </summary>
    StartNewRun = 2,

    /// <summary>A run is open, and <see cref="HomePresenter.ContinuableRun"/> names it.</summary>
    ContinueRun = 3,

    /// <summary>Nothing is stored for this player. Named, so it can never read as "start".</summary>
    ProfileMissing = 4,

    /// <summary>The read itself did not answer. A state, never an escape.</summary>
    ReadUnavailable = 5,

    /// <summary>
    /// A run is open in the stored row but its window has passed, so nothing may be done to it any
    /// more. The action starts a NEW run — which is also what settles the lapsed one.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>A sixth value rather than folding into <see cref="StartNewRun"/>, and the difference is
    /// the sentence.</b> Both offer the same button, because `03`'s expiry rule makes
    /// <c>START_RUN</c> the one command a lapsed run accepts. But a player who left a run going and
    /// comes back to a screen offering a fresh one has had something taken away, and a screen that
    /// says nothing about it looks like lost progress rather than an authored two-day window. So
    /// this state exists to carry a line saying so.
    /// </remarks>
    RunLapsed = 6,
}

/// <summary>Which of the five states the launch block is in.</summary>
/// <remarks>
/// 🔒 Five named states rather than a pair of booleans over "can start" and "is loaded". The five
/// differ in the button's word, the button's colour role, what the cost badge says and whether a
/// press does anything, and no two of those four vary together — a screen deriving one from another
/// would show a Refill button charging a run's price.
/// </remarks>
public enum HomeLaunchState
{
    /// <summary>Energy covers the cost and the hero is at or above the recommendation.</summary>
    Ready = 1,

    /// <summary>The two banks together do not cover the run's cost. A press must not start a run.</summary>
    InsufficientEnergy = 2,

    /// <summary>
    /// The hero is below the stage's recommended power. The run is still startable — this is a
    /// warning, never a gate.
    /// </summary>
    Underpowered = 3,

    /// <summary>The read has not answered yet. Skeletons, in the same places the real rows will be.</summary>
    Loading = 4,

    /// <summary>The read did not answer at all. An inline retry row, never a modal.</summary>
    PresenterFailure = 5,
}

/// <summary>What one press of the screen's single primary button actually does.</summary>
/// <remarks>
/// <para>
/// 🔒 <b>This is where the screen's TWO state models meet, and it exists because that meeting is a
/// rule.</b> <see cref="HomeContinueDecision"/> answers <em>what is there to go back to</em> — a
/// question about the stored profile and the run under it. <see cref="HomeLaunchState"/> answers
/// <em>what shape is the launch block in</em> — a question about the hub's view model, the price of
/// a run and the hero's power. Neither derives from the other and neither is redundant: a player
/// mid-run has a launch state, and a player with no run still has a decision.
/// </para>
/// <para>
/// 🔴 <b>But one button cannot be in two states, so something has to say which model wins, and that
/// something used to be <c>Home.cs</c> — a <c>Node</c>, which this repository has no tier that can
/// test.</b> The scene read <c>Decision == ContinueRun</c> and, off the back of it, overrode the
/// button's word, its colour, its badge and what a press did. That is a decision about the screen,
/// not a rendering of one, and Rule A10 puts it here. Stated once, in one enum, it is a fact the
/// suite can hold and a reader can find; four separate overrides in a scene were neither.
/// </para>
/// <para>
/// The precedence is: <b>an open run wins over everything.</b> A launch block that is loading, out
/// of Energy or reporting a failed read still must not offer to throw away a board the player is
/// standing on — the run is the thing with progress in it, and START_RUN is what destroys it.
/// </para>
/// </remarks>
public enum HomePrimaryAction
{
    /// <summary>
    /// Nothing yet — the hub's read is still out. The button is on the page and disabled, never
    /// hidden: a primary action that vanishes reads as a screen that lost its purpose.
    /// </summary>
    Wait = 1,

    /// <summary>
    /// Go back to the run that is already open. <see cref="HomePresenter.ContinuableRun"/> names it.
    /// </summary>
    Resume = 2,

    /// <summary>Submit a <c>START_RUN</c> for the chapter the campaign offers next.</summary>
    StartRun = 3,

    /// <summary>Open the sheet that sells Energy — the two banks cannot pay for a run.</summary>
    OfferRefill = 4,

    /// <summary>Read again. The one action that gets a player off a screen whose read faulted.</summary>
    Retry = 5,
}

/// <summary>The three entries the hero band's side rail reaches.</summary>
/// <remarks>
/// ⚠️ Named rather than counted. The reference draws a numeric badge on two of them, and nothing in
/// this build records what would feed those counts — so the rail carries the three destinations and
/// no numbers at all (steering S6).
/// </remarks>
public enum HomeRailEntry
{
    /// <summary>The inbox.</summary>
    Mail = 1,

    /// <summary>The leaderboards.</summary>
    Ranking = 2,

    /// <summary>The quest slate.</summary>
    Quests = 3,
}

/// <summary>Which theme accent a control is drawn in.</summary>
/// <remarks>
/// A ROLE rather than a colour: the value a role resolves to is a named entry in
/// <c>SlayTheme.tres</c>, and a presenter that carried the hex would be the hard-coded colour the
/// brief forbids and the scene rules catch.
/// </remarks>
public enum HomeColourRole
{
    /// <summary>The action accent — the ember the primary button is normally drawn in.</summary>
    Action = 1,

    /// <summary>The energy accent — what the button becomes when it is offering a refill instead.</summary>
    Energy = 2,

    /// <summary>The quiet surface, for a control that is doing nothing yet.</summary>
    Quiet = 3,
}

/// <summary>What the badge on the primary button is saying.</summary>
public enum HomeCostBadgeKind
{
    /// <summary>The run's price. <see cref="HomeCostBadge.Amount"/> is what it costs.</summary>
    Price = 1,

    /// <summary>The shortfall. <see cref="HomeCostBadge.Amount"/> is how much more is needed.</summary>
    Shortfall = 2,

    /// <summary>A skeleton standing in the badge's place so the button does not resize when the read lands.</summary>
    Placeholder = 3,

    /// <summary>No badge at all — there is no price to quote because there is nothing to buy.</summary>
    None = 4,
}

/// <summary>The badge on the primary button: what it is saying, and the number it says it with.</summary>
/// <param name="Kind">What the number means.</param>
/// <param name="Amount">
/// The number, or zero for the two kinds that have none. Zero is never a meaningful amount here: a
/// price of nothing and a shortfall of nothing are both states the screen cannot be in.
/// </param>
public sealed record HomeCostBadge(HomeCostBadgeKind Kind, int Amount);


/// <summary>
/// Drives the Home screen: the profile's own readouts, the run hub's launch block, and the one
/// decision the screen exists to make — start a run, or resume the one already open.
/// </summary>
/// <remarks>
/// <para>
/// Plain C# taking its collaborators as constructor arguments, so the whole screen runs under a test
/// runner with no engine anywhere near it. The scene above renders what is exposed here; the
/// composition root below decides what the collaborators actually are.
/// </para>
/// <para>
/// 🔒 <b>TWO state models live here, they answer different questions, and the file is laid out in
/// the three sections that say so.</b> <see cref="HomeContinueDecision"/> and the readouts under it
/// are settled by <see cref="StartAsync"/> from the profile's own row — <em>what is there to go back
/// to</em>. <see cref="HomeLaunchState"/> and the hub's pills, tabs and stage card are settled by
/// <see cref="LoadAsync"/> from <see cref="IHomeScreen"/>'s view model — <em>what shape is the
/// launch block in</em>. Where the two meet is <see cref="PrimaryAction"/> and the four members
/// beside it, in the last section, and that meeting is a rule rather than a rendering:
/// <see cref="HomePrimaryAction"/>'s own remarks carry the argument. Use
/// <see cref="RefreshAsync"/> rather than the two reads separately, so the models a single frame is
/// drawn from were settled from one pass over the same row.
/// </para>
/// <para>
/// 🔒 <b>What is shown is carried or read through a seam; what is absent is absent on purpose.</b>
/// The wallet balances and the run's Gold are the numbers the rows literally hold. The three derived
/// numbers on the screen each come out of the layer that owns the arithmetic rather than out of a
/// copy of it: power through <see cref="IHeroPowerSource"/>, behind which sits
/// <c>PowerCalculator</c>; and the Energy maximum, the regeneration countdown and a run's price
/// through <c>Core.Rules.Economy.HomeEnergyView</c>, the public projection over the tuning readers
/// that are otherwise <c>internal</c> to <c>SlayIdleRepeat.Core</c>. ⚠️ Still absent, and still for
/// the original reason — no route out of <c>Core</c> exists — is the Legend-XP percentage.
/// </para>
/// <para>
/// ⚠️ Also absent, each because a later row owns it: daily quests, ad widgets, chest pity, event
/// and guild cards, the inbox envelope, the account-link banner, the bottom navigation, the Legend
/// XP bar and the Dungeons entry.
/// </para>
/// </remarks>
public sealed class HomePresenter
{
    private const string LegendLevelLabelKey = "loc.home.legend_level.label";
    private const string EnergyLabelKey = "loc.home.energy.label";
    private const string EnergyReserveLabelKey = "loc.home.energy_reserve.label";
    private const string PowerLabelKey = "loc.home.power.label";
    private const string ProgressLabelKey = "loc.home.progress.label";
    private const string StageLabelKey = "loc.home.stage.label";
    private const string StartRunActionKey = "loc.home.start_run.action";
    private const string ContinueRunActionKey = "loc.home.continue_run.action";
    private const string GearActionKey = "loc.home.gear.action";
    private const string LoadingStatusKey = "loc.home.loading.status";
    private const string UnavailableStatusKey = "loc.home.unavailable.status";
    private const string RunLapsedStatusKey = "loc.home.run_lapsed.status";
    private const string NothingClearedStatusKey = "loc.home.nothing_cleared.status";

    /// <summary>
    /// Captions the HUD borrows from the currency documents that own them (the tier names come
    /// through <see cref="TierNames"/>). Named in the home document as well, so retiring one fails
    /// the content load rather than rendering a key on this screen.
    /// </summary>
    private const string CrownsNameKey = "loc.currency.crowns.name";
    private const string SoulShardsNameKey = "loc.currency.soul_shards.name";
    private const string GoldNameKey = "loc.currency.gold.name";

    /// <summary>The status line of a screen that has an action to offer: there is nothing left to say.</summary>
    private const string NothingLeftToSay = "";

    /// <summary>What a tile's text is when there is no value behind it — blank, never a zero.</summary>
    private const string NoValue = "";

    /// <summary>
    /// The collaborators the header's own read needs, or <c>null</c> when this presenter was built
    /// over the launch block's seam alone.
    /// </summary>
    /// <remarks>
    /// 🔒 <b>One nullable field rather than five fields assigned <c>null!</c>.</b> The narrow
    /// constructor genuinely has no host, no clock, no power source and no player to name, and
    /// writing five null-forgiving assignments would state the opposite — the compiler would stop
    /// asking, and <see cref="StartAsync"/> would answer
    /// <see cref="HomeContinueDecision.ReadUnavailable"/> off the back of a
    /// <c>NullReferenceException</c>, which says the read failed when there was never a read to
    /// make. Absent as a whole, the absence is checked once and the contract this class documents —
    /// a hub answers <see cref="HomeContinueDecision.NotYetRead"/> for ever — is the one it keeps.
    /// </remarks>
    private readonly HeaderReads? _header;

    private readonly LocaleStringCatalogue _strings;
    private readonly IReadOnlyList<ChapterDocument> _chapters;

    /// <summary>Builds the screen over the host, the strings and the profile boot opened.</summary>
    /// <param name="gameHost">The seam the player's own state is read through.</param>
    /// <param name="strings">Key to display string, over the loaded content set.</param>
    /// <param name="content">
    /// The loaded content set, read for the authored run window, the chapter names and the run's
    /// board — the same snapshot the strings came from, so the screen cannot be measuring one
    /// version's window against another's copy.
    /// </param>
    /// <param name="clock">
    /// The one sanctioned reading of now. Taken as a port rather than an ambient call for the reason
    /// <c>IClockPort</c> states at length, and read at the moment the decision is made rather than
    /// stored, so a screen left open does not decide against a stale instant.
    /// </param>
    /// <param name="power">Where the power tile's number comes from.</param>
    /// <param name="player">The profile this screen is about.</param>
    /// <exception cref="ArgumentNullException">A collaborator is null.</exception>
    public HomePresenter(
        IGameHost gameHost,
        LocaleStringCatalogue strings,
        ContentSnapshot content,
        IClockPort clock,
        IHeroPowerSource power,
        PlayerId player)
    {
        ArgumentNullException.ThrowIfNull(gameHost);
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(power);

        _header = new HeaderReads(gameHost, content, clock, power, player);
        _strings = strings;
        _chapters = ChapterDocuments.Read(content, strings);

        // 🔒 The launch block's seam is a projection over the five collaborators this constructor
        // already takes, so it is built here rather than demanded a second time as a parameter: a
        // sixth argument would be the same host, clock, content, power source and profile handed
        // over twice, and the two copies would be free to disagree about which player they are for.
        _screen = new HomeScreen(gameHost, clock, content, power, player);
    }

    // ==================================================== MODEL 1 — what is there to go back to
    //
    // Settled by StartAsync from the profile's own row, read straight through IGameHost. Everything
    // from here to the "run hub's launch block" divider belongs to this model, and nothing below
    // that divider reads any of it.
    //
    // ⚠️ Several of these readouts fed the header tile grid and run panel the run-hub rebuild
    // replaced with bands, so no production code renders them today — CrownsText, EnergyText,
    // EnergyReserveText, SoulShardsText, RunGoldText, PowerText, HighestClearText, RunChapterText,
    // RunStageText, RunHitPointsText, GearText and their captions. They are kept, not deleted:
    // Decision, ContinuableRun, ActionText, StatusText and ProfileCarried in the same model are the
    // resume feature, which is live and which the bands do not replace, and the readouts are the
    // same read's other answers. Whether the rebuilt Home still owes a run panel and a progress
    // readout is a design question and it is stated here rather than settled by a deletion.

    /// <summary>What the primary action does, and why.</summary>
    public HomeContinueDecision Decision { get; private set; } = HomeContinueDecision.NotYetRead;

    /// <summary>The run to resume, or null unless <see cref="Decision"/> is <see cref="HomeContinueDecision.ContinueRun"/>.</summary>
    public RunId? ContinuableRun { get; private set; }

    /// <summary>The player's display name, as the snapshot carries it.</summary>
    public string DisplayName { get; private set; } = "";

    /// <summary>The player's Legend Level, as the snapshot carries it.</summary>
    public int LegendLevel { get; private set; }

    /// <summary>The main Energy bar's amount, as the snapshot carries it — no denominator exists.</summary>
    public int Energy { get; private set; }

    /// <summary>The Energy Reserve's amount, as the snapshot carries it — no capacity exists.</summary>
    public int EnergyReserve { get; private set; }

    /// <summary>The Crowns balance, as the wallet carries it; a wallet with no row is a balance of nothing.</summary>
    public long Crowns { get; private set; }

    /// <summary>The Soul Shards balance, likewise.</summary>
    public long SoulShards { get; private set; }

    /// <summary>The Gold of the run being continued, or null when there is no run to continue.</summary>
    /// <remarks>
    /// 🔒 Null under <see cref="HomeContinueDecision.RunLapsed"/> too, although the lapsed row still
    /// carries a balance: it belongs to a run nobody can play, and showing it promises Gold the player
    /// will never spend.
    /// </remarks>
    public long? RunGold { get; private set; }

    /// <summary>The hero's power, floored to the whole number the player has reached — or null when no reading exists.</summary>
    public double? Power { get; private set; }

    /// <summary>How the power reading ended, or null until the source has been asked.</summary>
    /// <remarks>
    /// Kept beside <see cref="Power"/> because an empty power tile has four different causes, and the
    /// scene has to be able to say which one it is looking at.
    /// </remarks>
    public HeroPowerStanding? PowerStanding { get; private set; }

    /// <summary>The furthest chapter cleared, or null when nothing the content authors has been.</summary>
    public HighestChapterClear? HighestClear { get; private set; }

    /// <summary>Where the run being continued stands, or null when there is no run to continue.</summary>
    public RunStageProgress? RunProgress { get; private set; }

    /// <summary>
    /// Whether the tile numbers are showing their exact values rather than their shortened ones.
    /// </summary>
    /// <remarks>
    /// The gesture that sets it is the scene's — a long press is an engine event — but what a long
    /// press MEANS to the numbers is decided here, where a test can read it. One state for the whole
    /// screen rather than one per tile, since the gesture asks "how many, exactly?" of all of them.
    /// </remarks>
    public bool FullValuesRevealed { get; private set; }

    /// <summary>The caption beside <see cref="LegendLevel"/>, resolved.</summary>
    public string LegendLevelLabel => _strings.Resolve(LegendLevelLabelKey);

    /// <summary>The caption beside <see cref="Energy"/>, resolved.</summary>
    public string EnergyLabel => _strings.Resolve(EnergyLabelKey);

    /// <summary>The caption beside <see cref="EnergyReserve"/>, resolved.</summary>
    public string EnergyReserveLabel => _strings.Resolve(EnergyReserveLabelKey);

    /// <summary>The caption on the Crowns tile: the currency's own name, resolved.</summary>
    public string CrownsLabel => _strings.Resolve(CrownsNameKey);

    /// <summary>The caption on the Soul Shards tile: the currency's own name, resolved.</summary>
    public string SoulShardsLabel => _strings.Resolve(SoulShardsNameKey);

    /// <summary>The caption on the run panel's Gold: the currency's own name, resolved.</summary>
    public string GoldLabel => _strings.Resolve(GoldNameKey);

    /// <summary>The caption on the power tile, resolved.</summary>
    public string PowerLabel => _strings.Resolve(PowerLabelKey);

    /// <summary>The caption on the progress tile, resolved.</summary>
    public string ProgressLabel => _strings.Resolve(ProgressLabelKey);

    /// <summary>The caption on the run panel's stage, resolved.</summary>
    public string StageLabel => _strings.Resolve(StageLabelKey);

    /// <summary><see cref="Energy"/> as a player reads it.</summary>
    public string EnergyText => Number(Energy);

    /// <summary><see cref="LegendLevel"/> as a player reads it — always in full, since a level is never shortened.</summary>
    public string LegendLevelText => PlayerNumber.Full(LegendLevel);

    /// <summary><see cref="EnergyReserve"/> as a player reads it.</summary>
    public string EnergyReserveText => Number(EnergyReserve);

    /// <summary><see cref="Crowns"/> as a player reads it.</summary>
    public string CrownsText => Number(Crowns);

    /// <summary><see cref="SoulShards"/> as a player reads it.</summary>
    public string SoulShardsText => Number(SoulShards);

    /// <summary><see cref="RunGold"/> as a player reads it, or blank when there is none.</summary>
    public string RunGoldText => RunGold is { } gold ? Number(gold) : NoValue;

    /// <summary><see cref="Power"/> as a whole number, or blank when there is no reading.</summary>
    /// <remarks>Blank rather than "0": a zero is a real reading of a hero with nothing, and is not what happened.</remarks>
    public string PowerText => Power is { } power ? Number((long)power) : NoValue;

    /// <summary>The chapter name and tier of <see cref="HighestClear"/>, or the nothing-cleared line.</summary>
    public string HighestClearText => HighestClear is { } clear
        ? $"{clear.ChapterName} · {TierName(clear.Tier)}"
        : _strings.Resolve(NothingClearedStatusKey);

    /// <summary>The name of the chapter the run being continued is in, or blank when there is no run.</summary>
    public string RunChapterText => RunProgress is { } progress ? progress.ChapterName : NoValue;

    /// <summary>The stage the run stands in over the stages a chapter has, or blank when there is no run.</summary>
    public string RunStageText => RunProgress is { } progress
        ? $"{PlayerNumber.Full(progress.Stage)}/{PlayerNumber.Full(ChapterProgressReadout.StageCount)}"
        : NoValue;

    /// <summary>The hero's hit points over the pool they are out of, both in full, or blank when there is no run.</summary>
    /// <remarks>Never shortened: a health readout of "10.0k/12.3k" hides the one number a player checks before continuing.</remarks>
    public string RunHitPointsText => RunProgress is { } progress
        ? $"{PlayerNumber.Full(progress.CurrentHp)}/{PlayerNumber.Full(progress.MaxHp)}"
        : NoValue;

    /// <summary>
    /// The caption of the control that opens the gear stock (S16), resolved.
    /// </summary>
    /// <remarks>
    /// 🔒 Always the same word, whatever the profile read said. Unlike <see cref="ActionText"/> — which
    /// changes between starting and continuing a run — the bag is the bag: a caption that varied would be
    /// inventing a distinction the screen behind it does not have.
    /// </remarks>
    public string GearText => _strings.Resolve(GearActionKey);

    /// <summary>The primary action's caption for the current <see cref="Decision"/>, resolved.</summary>
    public string ActionText => _strings.Resolve(
        Decision == HomeContinueDecision.ContinueRun ? ContinueRunActionKey : StartRunActionKey);

    /// <summary>
    /// Whether the read produced a profile — so the header has numbers to draw and the primary
    /// action has something to do.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>Here rather than in the scene, because the scene had its own copy of this list and the
    /// copy went stale the moment a sixth decision existed.</b> <c>Home.cs</c> enumerated
    /// <c>StartNewRun or ContinueRun</c> to decide both the header's visibility and the button's
    /// disabled state; <see cref="HomeContinueDecision.RunLapsed"/> arrived and matched neither, so
    /// the screen hid a profile it had and disabled the one action that would have settled the
    /// lapsed run — stranding the player on Home instead of on the perk draft. Stated once, where
    /// the decisions are, a seventh member throws below and is caught by the suite, rather than
    /// being a silent omission in a scene nothing tests.
    /// </remarks>
    public bool ProfileCarried => Decision switch
    {
        HomeContinueDecision.StartNewRun or
        HomeContinueDecision.ContinueRun or
        HomeContinueDecision.RunLapsed => true,

        HomeContinueDecision.NotYetRead or
        HomeContinueDecision.ProfileMissing or
        HomeContinueDecision.ReadUnavailable => false,

        // 🔒 Throws rather than answering false, and the difference is the whole point of the
        // member being here. A silent default is what let RunLapsed read as "no profile" in the
        // scene: a decision nobody had thought about got an answer anyway, and the answer disabled
        // the only control on the screen. A seventh member arrives here as a loud failure in the
        // suite instead. Same shape, for the same reason, as BoardPresenter.LabelKeyFor.
        _ => throw new ArgumentOutOfRangeException(
            nameof(Decision), Decision,
            "this screen has a decision it was never told whether to draw a profile for. Answer it " +
            "here — a default would decide by accident, and the accident hides the header and " +
            "disables the primary action."),
    };

    /// <summary>The line shown while there is no decision to offer, resolved.</summary>
    public string StatusText => Decision switch
    {
        HomeContinueDecision.NotYetRead => _strings.Resolve(LoadingStatusKey),
        HomeContinueDecision.StartNewRun or HomeContinueDecision.ContinueRun => NothingLeftToSay,

        // The one decision that offers an action AND still has something to say: the run the player
        // left is gone, and they are owed the reason rather than a silently different button.
        HomeContinueDecision.RunLapsed => _strings.Resolve(RunLapsedStatusKey),
        _ => _strings.Resolve(UnavailableStatusKey),
    };

    /// <summary>What went wrong when <see cref="Decision"/> is <see cref="HomeContinueDecision.ReadUnavailable"/>.</summary>
    public string? FailureDetail { get; private set; }

    /// <summary>Shows the exact value of every tile number — a long press is holding it.</summary>
    public void RevealFullValues() => FullValuesRevealed = true;

    /// <summary>And puts the shortened forms back, which is what the press ending means.</summary>
    public void ConcealFullValues() => FullValuesRevealed = false;

    /// <summary>Reads the player's own state and settles <see cref="Decision"/>.</summary>
    /// <param name="ct">Cancellation.</param>
    public async Task StartAsync(CancellationToken ct)
    {
        // A presenter built over the launch block's seam alone has no host to read: it stays at
        // NotYetRead, which is what its own constructor's remarks say it does. Answering
        // ReadUnavailable here would report a read that failed when none was ever attempted.
        if (_header is not { } header)
        {
            return;
        }

        try
        {
            // Awaited inside the guard rather than merely called inside it: a real host's read is an
            // async method, so its failure arrives as a faulted task and a try around the call alone
            // would never see it.
            var state = await header.GameHost
                .ReadOwnStateAsync(header.Player, run: null, ct).ConfigureAwait(false);

            Settle(header, state);
        }
        catch (Exception failure)
        {
            Decision = HomeContinueDecision.ReadUnavailable;
            FailureDetail = $"{failure.GetType().Name}: {failure.Message}";
        }
    }

    private void Settle(HeaderReads header, OwnStateResult state)
    {
        // Every read starts from "nothing to continue": this screen is re-read when a run ends, and
        // a run panel left standing from the previous read would draw a finished run under START.
        ContinuableRun = null;
        RunGold = null;
        RunProgress = null;

        if (state.Lookup != OwnStateLookup.Found || state.View is not { } view)
        {
            Decision = HomeContinueDecision.ProfileMissing;
            return;
        }

        Carry(view.Player);

        // A finished run is still a row the read hands back, and the next START_RUN is the only
        // thing that clears it — so an ended run is a run to start over, not a run to resume.
        if (view.Run is { Phase: RunPhase.InProgress or RunPhase.BattlePending } open)
        {
            // 🔴 A run's phase says it is open; only the clock says it is still PLAYABLE. GameRules
            // refuses every run command on a lapsed run with RUN_EXPIRED and settles it on the next
            // command the player is allowed to make — which is START_RUN and the meta commands, and
            // is deliberately NOT anything the board or its decision screens submit. So a Home that
            // offered to resume this one sent the player into a screen where every control is
            // refused and none of them goes back: the perk draft has Pick, Reroll and Skip, all
            // three are run commands, and closing the game returns to this same offer. `16` D70.
            if (RunExpiry.HasLapsed(open, header.Clock.UtcNow, header.Content))
            {
                Decision = HomeContinueDecision.RunLapsed;
                ReadPower(header, view.Player, run: null);
                return;
            }

            Decision = HomeContinueDecision.ContinueRun;
            ContinuableRun = open.Id;
            RunGold = open.Gold;
            RunProgress = ChapterProgressReadout.RunProgress(open, header.Content, _chapters);
            ReadPower(header, view.Player, open);
            return;
        }

        Decision = HomeContinueDecision.StartNewRun;
        ReadPower(header, view.Player, run: null);
    }

    private void Carry(PlayerSnapshot player)
    {
        DisplayName = player.DisplayName;
        LegendLevel = player.LegendLevel;
        Energy = player.Energy.Energy;
        EnergyReserve = player.Energy.Reserve;
        Crowns = Balance(player, CurrencyId.CROWNS);
        SoulShards = Balance(player, CurrencyId.SOUL_SHARDS);
        HighestClear = ChapterProgressReadout.HighestClear(player.ClearedChapterTiers, _chapters);
    }

    /// <summary>
    /// Asks the source about the hero the player IS right now: inside the run being continued, or
    /// the between-runs hero when there is nothing to continue. A lapsed run counts as nothing to
    /// continue, so its drafted perks are left out: nobody will stand on that board again.
    /// </summary>
    private void ReadPower(HeaderReads header, PlayerSnapshot player, RunSnapshot? run)
    {
        var reading = header.Power.Read(player, run);

        PowerStanding = reading.Standing;

        // Floored, not rounded: the tile shows the power a player HAS, the way a level is the one
        // completed rather than the one nearest.
        Power = reading.Standing == HeroPowerStanding.Computed && reading.PowerIndex is { } index
            ? Math.Floor(index)
            : null;
    }

    private static long Balance(PlayerSnapshot player, CurrencyId currency) =>
        player.Wallet.TryGetValue(currency, out var balance) ? balance : 0L;

    private string Number(long value) =>
        FullValuesRevealed ? PlayerNumber.Full(value) : PlayerNumber.Abbreviated(value);

    /// <summary>A tier's authored name — Chapter Select's, since the tile spells a clear the way the picker spelt the choice.</summary>
    private string TierName(DifficultyTier tier) => TierNames.KeyOf(tier) is { } key
        ? _strings.Resolve(key)
        : throw new ArgumentOutOfRangeException(
            nameof(tier), tier, "this tier has no authored name the progress tile can spell; add its key before a row can carry it.");

    // ============================== MODEL 2 — what shape is the launch block in (the run hub)
    //
    // Settled by LoadAsync from IHomeScreen's view model. Everything from here to the third section
    // belongs to this model, and none of it reads Decision or any readout above.

    private const string LaunchRefillActionKey = "loc.home.launch.refill.action";
    private const string LaunchUnderpoweredActionKey = "loc.home.launch.start_underpowered.action";
    private const string LaunchLoadingActionKey = "loc.home.launch.loading.action";
    private const string LaunchRetryActionKey = "loc.home.launch.retry.action";
    private const string ChangeStageActionKey = "loc.home.launch.change.action";
    private const string SettingsActionKey = "loc.home.settings.action";
    private const string NextUpLabelKey = "loc.home.launch.next_up.label";
    private const string RecommendedPowerLabelKey = "loc.home.launch.recommended_power.label";
    private const string NoStageLabelKey = "loc.home.launch.no_stage.label";
    private const string HeroInspectLabelKey = "loc.home.hero.inspect.label";
    private const string TabHomeLabelKey = "loc.home.tab.home.label";
    private const string TabGearLabelKey = "loc.home.tab.gear.label";
    private const string TabTalentsLabelKey = "loc.home.tab.talents.label";
    private const string TabCollectionLabelKey = "loc.home.tab.collection.label";
    private const string TabShopLabelKey = "loc.home.tab.shop.label";
    private const string RailMailLabelKey = "loc.home.rail.mail.label";
    private const string RailRankingLabelKey = "loc.home.rail.ranking.label";
    private const string RailQuestsLabelKey = "loc.home.rail.quests.label";

    /// <summary>How the Energy pill joins its bar to the bar's own capacity. Punctuation, not copy.</summary>
    private const string BarSeparator = "/";

    /// <summary>What separates two halves of one line. Punctuation, not copy.</summary>
    private const string LineSeparator = " · ";

    /// <summary>The countdown under an hour, and the one over it. Neither is ever a bare second count.</summary>
    private const string ShortCountdownFormat = @"m\:ss";

    private const string LongCountdownFormat = @"h\:mm\:ss";

    /// <summary>The seam the whole launch block is drawn from.</summary>
    private readonly IHomeScreen _screen;

    private HomeViewModel? _view;

    /// <summary>
    /// Whether a <c>START_RUN</c> is outstanding.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>The double-tap latch, and it is not a nicety.</b> The seam raises rather than translates
    /// a refusal it has no sentence for, and the plainest one a player can produce is
    /// <c>ILLEGAL_STATE</c> from a second <c>START_RUN</c> submitted while the first is still in
    /// flight — the run is already open by the time the second lands. Taken BEFORE the await, so the
    /// second press finds it set while the first is genuinely outstanding.
    /// </remarks>
    private bool _startInFlight;

    /// <summary>The power the previous read carried, so a rise can be told from a first reading.</summary>
    private double? _powerBefore;

    /// <summary>Builds the hub half of the screen over the application seam.</summary>
    /// <param name="screen">Everything the hub draws, and the one thing it does.</param>
    /// <param name="strings">Key to display string, over the loaded content set.</param>
    /// <param name="content">
    /// The loaded content set the chapter documents are read from, or <c>null</c> when this
    /// presenter is not naming a chapter. With none, <see cref="StageName"/> is absent and
    /// <see cref="StageTitle"/> falls back to the authored unnamed-stage line rather than inventing
    /// a name from an id.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="screen"/> or <paramref name="strings"/> is null.</exception>
    /// <remarks>
    /// ⚠️ The narrow constructor: it drives the launch block, the pills and the tab bar, and it is
    /// the one the header's own read is NOT wired through — a presenter built this way answers
    /// <see cref="HomeContinueDecision.NotYetRead"/> for ever, because there is no host to read.
    /// The wide constructor above drives both halves and builds this one's seam out of the
    /// collaborators it already takes.
    /// </remarks>
    public HomePresenter(
        IHomeScreen screen, LocaleStringCatalogue strings, ContentSnapshot? content = null)
    {
        ArgumentNullException.ThrowIfNull(screen);
        ArgumentNullException.ThrowIfNull(strings);

        _screen = screen;
        _header = null;
        _strings = strings;
        _chapters = content is null ? [] : ChapterDocuments.Read(content, strings);
    }

    /// <summary>Which of the five states the launch block is in.</summary>
    public HomeLaunchState LaunchState { get; private set; } = HomeLaunchState.Loading;

    /// <summary>The primary button's word, resolved. Its own word in every state.</summary>
    public string ActionLabel => _strings.Resolve(LaunchState switch
    {
        HomeLaunchState.Ready => StartRunActionKey,
        HomeLaunchState.InsufficientEnergy => LaunchRefillActionKey,
        HomeLaunchState.Underpowered => LaunchUnderpoweredActionKey,
        HomeLaunchState.Loading => LaunchLoadingActionKey,
        _ => LaunchRetryActionKey,
    });

    /// <summary>
    /// The theme accent the primary button is drawn in.
    /// </summary>
    /// <remarks>
    /// 🔒 Decided from "cannot pay" rather than from "not ready": the energy accent means <em>this
    /// button buys Energy</em>, and a state that merely has nothing to offer yet wearing it is a
    /// button promising something it does not do.
    /// </remarks>
    public HomeColourRole ActionColour => LaunchState switch
    {
        HomeLaunchState.Ready or HomeLaunchState.Underpowered => HomeColourRole.Action,
        HomeLaunchState.InsufficientEnergy => HomeColourRole.Energy,
        _ => HomeColourRole.Quiet,
    };

    /// <summary>What the badge on the primary button is saying.</summary>
    public HomeCostBadge CostBadge => LaunchState switch
    {
        HomeLaunchState.Ready or HomeLaunchState.Underpowered =>
            new HomeCostBadge(HomeCostBadgeKind.Price, _view?.EnergyCost ?? 0),
        HomeLaunchState.InsufficientEnergy =>
            new HomeCostBadge(HomeCostBadgeKind.Shortfall, _view?.EnergyShortfall ?? 0),
        HomeLaunchState.Loading => new HomeCostBadge(HomeCostBadgeKind.Placeholder, 0),
        _ => NoBadge,
    };

    /// <summary>No badge at all: there is no price to quote because there is nothing to buy.</summary>
    private static HomeCostBadge NoBadge { get; } = new(HomeCostBadgeKind.None, 0);

    /// <summary>Whether pressing the primary button starts a run.</summary>
    public bool CanStartRun =>
        LaunchState is HomeLaunchState.Ready or HomeLaunchState.Underpowered;

    /// <summary>
    /// Whether the launch block's three rows — stage card, button, reward line — are on the page.
    /// </summary>
    /// <remarks>
    /// 🔒 True in every state, including <see cref="HomeLaunchState.Loading"/>, and that is the
    /// point: the brief's loading state is skeletons in the same places, so nothing on the screen
    /// moves when the read lands. A block hidden while loading makes the whole hero band jump.
    /// </remarks>
    public bool LaunchRowsVisible => true;

    /// <summary>What failed, when <see cref="LaunchState"/> is <see cref="HomeLaunchState.PresenterFailure"/>.</summary>
    /// <remarks>Names the thing that failed; the copy carries no apology, per the brief.</remarks>
    public string? FailureLine { get; private set; }

    /// <summary>The player's display name, for the hero band's name block.</summary>
    public string HubPlayerName => _view?.PlayerName ?? NoValue;

    /// <summary>The Legend Level on the avatar's badge.</summary>
    public string HubLevelText => _view is { } view ? PlayerNumber.Full(view.PlayerLevel) : NoValue;

    /// <summary>The line under the hero's name. No gear count: nothing in this build authors one.</summary>
    public string HeroCaption => _strings.Resolve(HeroInspectLabelKey);

    /// <summary>
    /// The Crowns balance behind the pill.
    /// </summary>
    /// <remarks>
    /// 🔒 The pills carry NUMBERS as well as the strings they are written as, and both come from
    /// here. A pill counting up from one value to another has to interpolate the number and write
    /// each step by the same rule the settled value is written by — so the rule is a function this
    /// layer hands over, and the scene never learns how a number is spelt.
    /// </remarks>
    public long HubCrowns => _view?.Crowns ?? 0L;

    /// <summary>
    /// How the Crowns pill writes an amount — abbreviated above ten thousand, and in full while a
    /// finger is holding it.
    /// </summary>
    /// <remarks>
    /// 🔒 The one pill on this screen whose figure is shortened, so the one the long press has
    /// anything to reveal: the Energy pill already reads <c>bar/max</c> in full and the Power pill is
    /// thousands-separated rather than abbreviated. Written through
    /// <see cref="FullValuesRevealed"/> rather than around it, so the reveal is one state for the
    /// whole screen and not a second rule per pill.
    /// </remarks>
    /// <param name="crowns">The amount, which mid-count-up is not yet the settled one.</param>
    public string CrownsPillTextFor(long crowns) => Number(crowns);

    /// <summary>The Crowns pill's value, settled. Never captioned.</summary>
    public string CrownsPillText => _view is null ? NoValue : CrownsPillTextFor(HubCrowns);

    /// <summary>The main Energy bar behind the pill.</summary>
    public long HubEnergy => _view?.Energy ?? 0L;

    /// <summary>How the Energy pill writes a bar: over what the bar holds.</summary>
    /// <remarks>
    /// 🔒 The BAR's denominator, not the two banks' — the Reserve is a separate bank, which is why
    /// affordability is <see cref="HomeViewModel.EnergyShortfall"/>'s answer and not this readout's.
    /// </remarks>
    /// <param name="bar">The bar, which mid-count-up is not yet the settled one.</param>
    public string EnergyPillTextFor(long bar) => _view is { } view
        ? string.Create(CultureInfo.InvariantCulture, $"{bar}{BarSeparator}{view.EnergyMax}")
        : NoValue;

    /// <summary>The Energy pill's value, settled.</summary>
    public string EnergyPillText => EnergyPillTextFor(HubEnergy);

    /// <summary>The regeneration countdown in the Energy pill's caption slot. Blank at full.</summary>
    public string EnergyPillCaption => _view is { EnergyRefillIn.Ticks: > 0 } view
        ? view.EnergyRefillIn.ToString(
            view.EnergyRefillIn.TotalHours >= 1d ? LongCountdownFormat : ShortCountdownFormat,
            CultureInfo.InvariantCulture)
        : NoValue;

    /// <summary>Whether the Energy pill shows a caption at all — hidden at full, per the brief.</summary>
    public bool EnergyCaptionVisible => EnergyPillCaption.Length > 0;

    /// <summary>
    /// The power index behind the pill, floored — the power a player HAS rather than the one
    /// nearest, the same direction every other number on this screen is shortened in.
    /// </summary>
    public long HubPower => _view?.Power is { } power ? (long)Math.Floor(power) : 0L;

    /// <summary>Whether a power reading exists at all. A pill with none is blank, never a zero.</summary>
    public bool PowerReadable => _view?.Power is not null;

    /// <summary>
    /// How the Power pill writes an index — by the same rule every other number in this client is
    /// written by: shortened above ten thousand, and exact while a finger is holding the pill.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>It used to be thousands-separated, and that was two defects in one.</b> The reference
    /// draws <c>12,480</c>, but a grouped index is (a) the only number anywhere in this client not
    /// written by <see cref="PlayerNumber"/> — a second number rule introduced by one screen — and
    /// (b) NINE characters at the 1.2M the reference itself names as the value that must not push a
    /// pill off the row. Nine characters reserve 252 units of a 684-unit row that also owes 207 to
    /// Crowns and 285 to a captioned Energy bar, which is 744 against 684: the widest pill was drawn
    /// off the screen edge. Shortened, the same index is at most six characters, and the long press
    /// this pill already carries is what gives the exact figure back.
    /// </remarks>
    /// <param name="power">The index, which mid-count-up is not yet the settled one.</param>
    public string PowerPillTextFor(long power) => PowerReadable ? Number(power) : NoValue;

    /// <summary>The Power pill's value, settled.</summary>
    public string PowerPillText => PowerPillTextFor(HubPower);

    /// <summary>
    /// Whether the power reading rose since the previous one, which is the gain accent's trigger.
    /// </summary>
    /// <remarks>
    /// 🔒 A FIRST reading is not a rise. A screen that flashed the gain accent on its opening frame
    /// would tell a player who had only just opened the game that they had gained something.
    /// </remarks>
    public bool PowerRose { get; private set; }

    /// <summary>The stage card's first line: the chapter the campaign offers next, by name.</summary>
    public string StageTitle => StageName ?? _strings.Resolve(NoStageLabelKey);

    /// <summary>
    /// The next chapter's authored name, or <c>null</c> when the campaign offers no next chapter —
    /// or when this presenter holds no chapter documents to name one from.
    /// </summary>
    /// <remarks>
    /// 🔒 Resolved HERE rather than carried across the seam. A chapter document authors its name as
    /// a loc key, and the catalogue that turns one into words lives in this assembly — so a
    /// name-shaped field filled with a key at the layer below would be drawn as if it were a name.
    /// </remarks>
    public string? StageName => _view?.NextStageId is { } stage
        ? _chapters.FirstOrDefault(chapter => chapter.ChapterId == stage)?.ChapterName
        : null;

    /// <summary>The stage card's second line: what is next, and what it is balanced against.</summary>
    public string StageSubtitle
    {
        get
        {
            var nextUp = _strings.Resolve(NextUpLabelKey);

            // Written by the screen's own number rule, exactly as the power pill above it is: the
            // recommendation and the index the player compares it against are two readings of the
            // same quantity, and two spellings of one quantity on one card is a comparison the
            // player has to do twice.
            return _view?.RecommendedPower is { } recommended
                ? nextUp + LineSeparator + _strings.Resolve(RecommendedPowerLabelKey) + " " +
                  Number((long)Math.Floor(recommended))
                : nextUp;
        }
    }

    /// <summary>Whether the stage card wears the warning that the hero is under the recommendation.</summary>
    public bool StageCardWarned => LaunchState == HomeLaunchState.Underpowered;

    /// <summary>
    /// The reward line under the button.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Empty, and deliberately so</b> (steering S6). Nothing in <c>game-data/</c> authors a
    /// reward vocabulary, so there is nothing for this line to name; the row is kept anyway so the
    /// block's height does not change on the day one is authored.
    /// </remarks>
    public string RewardLineText => string.Join(LineSeparator, _view?.RewardTags ?? []);

    /// <summary>The stage card's own control.</summary>
    public string ChangeLabel => _strings.Resolve(ChangeStageActionKey);

    /// <summary>The top bar's settings control.</summary>
    public string SettingsLabel => _strings.Resolve(SettingsActionKey);

    /// <summary>One tab's caption.</summary>
    /// <param name="tab">The tab asked about.</param>
    /// <exception cref="ArgumentOutOfRangeException">A tab this screen has no caption for.</exception>
    public string TabLabel(HomeTab tab) => _strings.Resolve(tab switch
    {
        HomeTab.Home => TabHomeLabelKey,
        HomeTab.Gear => TabGearLabelKey,
        HomeTab.Talents => TabTalentsLabelKey,
        HomeTab.Collection => TabCollectionLabelKey,
        HomeTab.Shop => TabShopLabelKey,
        _ => throw new ArgumentOutOfRangeException(
            nameof(tab), tab,
            "this tab has no caption. Author one before the bar carries it — an unnamed tab draws " +
            "its own key in front of a player."),
    });

    /// <summary>Whether a tab is carrying a dot. Nothing lights one until something authorises it.</summary>
    /// <param name="tab">The tab asked about.</param>
    public bool TabBadgeOn(HomeTab tab) => _view?.Badges.On(tab) ?? false;

    /// <summary>One side-rail entry's caption.</summary>
    /// <param name="entry">The entry asked about.</param>
    /// <exception cref="ArgumentOutOfRangeException">An entry this screen has no caption for.</exception>
    public string RailLabel(HomeRailEntry entry) => _strings.Resolve(entry switch
    {
        HomeRailEntry.Mail => RailMailLabelKey,
        HomeRailEntry.Ranking => RailRankingLabelKey,
        HomeRailEntry.Quests => RailQuestsLabelKey,
        _ => throw new ArgumentOutOfRangeException(
            nameof(entry), entry,
            "this rail entry has no caption. Author one before the rail carries it."),
    });

    /// <summary>Reads the hub's view model and settles <see cref="LaunchState"/>.</summary>
    /// <param name="ct">Cancellation.</param>
    public async Task LoadAsync(CancellationToken ct)
    {
        try
        {
            // Awaited inside the guard rather than merely called inside it: the seam's read is an
            // async method, so its failure arrives as a faulted task and a try around the call alone
            // would never see it.
            Settle(await _screen.GetViewModelAsync(ct).ConfigureAwait(false));
        }
        catch (Exception failure)
        {
            _view = null;
            PowerRose = false;
            LaunchState = HomeLaunchState.PresenterFailure;
            FailureLine = $"{failure.GetType().Name}: {failure.Message}";
        }
    }

    /// <summary>Presses the primary button.</summary>
    /// <param name="ct">Cancellation.</param>
    /// <returns>What the seam answered, or <c>null</c> when the press starts nothing at all.</returns>
    public async Task<StartRunOutcome?> PressStartAsync(CancellationToken ct)
    {
        // 🔒 Three separate refusals, and none of them is a reason to invent a stage id: a state
        // that offers something else, a submission already outstanding, and a campaign with no next
        // chapter to run.
        if (!CanStartRun || _startInFlight || _view?.NextStageId is not { } stage)
        {
            return null;
        }

        _startInFlight = true;

        try
        {
            return await _screen.StartRunAsync(stage, ct).ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            // 🔴 <b>A press that faulted has to say so.</b> The seam RAISES a refusal it has no
            // sentence for — a run opened on another device, or a stage this screen last read before
            // the ladder moved — and every one of them arrives here. Left to propagate, the scene
            // logs it and returns, and the button keeps the word it already had: a primary action
            // that was pressed, did nothing, and offers nothing to press next. Settled into the
            // failure state instead, the block says what failed and its button becomes Retry, which
            // is the one state on this screen that can get the player out of it.
            _view = null;
            PowerRose = false;
            LaunchState = HomeLaunchState.PresenterFailure;
            FailureLine = $"{failure.GetType().Name}: {failure.Message}";

            return null;
        }
        finally
        {
            _startInFlight = false;
        }
    }

    /// <summary>Settles every launch-block answer from one view model.</summary>
    private void Settle(HomeViewModel view)
    {
        // A rise is measured against the reading BEFORE this one, and a first reading has none.
        PowerRose = _powerBefore is { } before && view.Power is { } now && now > before;
        _powerBefore = view.Power ?? _powerBefore;

        _view = view;
        FailureLine = null;

        // 🔒 The shortfall the rules reported, never the difference between the two numbers on this
        // screen: a run is paid from the main bar AND the Reserve, and the pill carries only the bar.
        if (view.EnergyShortfall > 0)
        {
            LaunchState = HomeLaunchState.InsufficientEnergy;
            return;
        }

        // Underpowered is a warning and never a gate, so it is decided last and changes nothing but
        // the card. Both numbers must exist: an unread power is not a hero below a recommendation.
        LaunchState =
            view.Power is { } power && view.RecommendedPower is { } recommended && power < recommended
                ? HomeLaunchState.Underpowered
                : HomeLaunchState.Ready;
    }

    // ================================================ THE ONE SCREEN — where the two models meet
    //
    // One button, one notice line, two models. Which one wins is a rule, and HomePrimaryAction's
    // remarks carry the argument for it living here rather than in the scene that used to hold it.

    /// <summary>
    /// Settles both models from one pass.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>The screen's one entry point, and the reason is that the two reads can disagree.</b>
    /// <see cref="StartAsync"/> and <see cref="LoadAsync"/> each read the same profile row through
    /// the same host, and a frame drawn after only one of them shows one model's answer beside the
    /// other's from some earlier moment. The retry a failed read offers is exactly that case: it
    /// re-read the launch block alone, so a hub that came back green was still drawn under the
    /// notice line the header's failed read had left standing, and the player pressed Retry on a
    /// screen that had already succeeded.
    /// <para>
    /// The header first, because <see cref="LoadAsync"/> is the read that can leave the launch block
    /// in <see cref="HomeLaunchState.PresenterFailure"/>, and a failure settled last is the one the
    /// screen is actually in.
    /// </para>
    /// </remarks>
    /// <param name="ct">Cancellation.</param>
    public async Task RefreshAsync(CancellationToken ct)
    {
        await StartAsync(ct).ConfigureAwait(false);
        await LoadAsync(ct).ConfigureAwait(false);
    }

    /// <summary>What one press of the primary button does.</summary>
    /// <remarks>
    /// 🔒 An open run outranks every launch state, including the two that offer something else: a
    /// button that started a fresh run because the hub's read was still out would destroy a board
    /// the player was standing on, and the two presses look identical.
    /// </remarks>
    public HomePrimaryAction PrimaryAction => Decision == HomeContinueDecision.ContinueRun
        ? HomePrimaryAction.Resume
        : LaunchState switch
        {
            HomeLaunchState.Ready or HomeLaunchState.Underpowered => HomePrimaryAction.StartRun,
            HomeLaunchState.InsufficientEnergy => HomePrimaryAction.OfferRefill,
            HomeLaunchState.PresenterFailure => HomePrimaryAction.Retry,
            HomeLaunchState.Loading => HomePrimaryAction.Wait,

            // Throws rather than waiting, for ProfileCarried's reason: a sixth launch state answered
            // by accident would disable the only control on the screen and nothing would fail.
            _ => throw new ArgumentOutOfRangeException(
                nameof(LaunchState), LaunchState,
                "this launch state says nothing about what the primary button does. Answer it here " +
                "— a default would decide by accident, and the accident is a dead primary action."),
        };

    /// <summary>The primary button's word for <see cref="PrimaryAction"/>, resolved.</summary>
    public string PrimaryActionLabel => PrimaryAction == HomePrimaryAction.Resume
        ? ActionText
        : ActionLabel;

    /// <summary>The theme accent the primary button is drawn in.</summary>
    /// <remarks>
    /// Resuming wears the action ember whatever the launch block would have worn: going back to a
    /// board is the screen's ordinary business, and the energy accent means <em>this button buys
    /// Energy</em>, which it does not.
    /// </remarks>
    public HomeColourRole PrimaryActionColour => PrimaryAction == HomePrimaryAction.Resume
        ? HomeColourRole.Action
        : ActionColour;

    /// <summary>What the badge on the primary button is saying.</summary>
    /// <remarks>
    /// 🔒 No badge at all while resuming, rather than the price of the run that is not being
    /// started: a figure beside Continue reads as what continuing costs, and continuing is free.
    /// </remarks>
    public HomeCostBadge PrimaryActionBadge => PrimaryAction == HomePrimaryAction.Resume
        ? NoBadge
        : CostBadge;

    /// <summary>
    /// The one sentence shown in place of a stage, or empty when there is a stage to show.
    /// </summary>
    /// <remarks>
    /// Four states produce one and each has its own authored line: a read that faulted, a read that
    /// has not answered, a profile the device has lost, and a run left alone past its window. The
    /// launch block's own failure is asked about FIRST because it is the more recent read and it is
    /// the one whose button offers a way out of it.
    /// </remarks>
    public string Notice => LaunchState == HomeLaunchState.PresenterFailure
        ? FailureLine ?? NothingLeftToSay
        : Decision is HomeContinueDecision.RunLapsed
            or HomeContinueDecision.ProfileMissing
            or HomeContinueDecision.ReadUnavailable
            ? StatusText
            : NothingLeftToSay;

    /// <summary>Everything the header's own read needs, held together so its absence is one fact.</summary>
    /// <param name="GameHost">The seam the player's own state is read through.</param>
    /// <param name="Content">The loaded content set the run window and the chapter names come from.</param>
    /// <param name="Clock">The one sanctioned reading of now.</param>
    /// <param name="Power">Where the power tile's number comes from.</param>
    /// <param name="Player">The profile this screen is about.</param>
    private sealed record HeaderReads(
        IGameHost GameHost,
        ContentSnapshot Content,
        IClockPort Clock,
        IHeroPowerSource Power,
        PlayerId Player);
}
