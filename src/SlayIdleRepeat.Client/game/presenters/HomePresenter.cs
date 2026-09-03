using SlayIdleRepeat.Application.Ports.Client;
using SlayIdleRepeat.Application.Ports.Shared;
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

/// <summary>
/// Drives the Home screen: the header and HUD tiles the profile carries, the run panel when there is
/// a run to go back to, and the one decision the screen exists to make — start a run, or resume the
/// one already open.
/// </summary>
/// <remarks>
/// <para>
/// Plain C# taking its collaborators as constructor arguments, so the whole screen runs under a test
/// runner with no engine anywhere near it. The scene above renders what is exposed here; the
/// composition root below decides what the collaborators actually are.
/// </para>
/// <para>
/// 🔒 <b>What is shown is carried or read through a seam; what is absent is absent on purpose.</b>
/// The Energy amounts, the wallet balances and the run's Gold are the numbers the rows literally
/// hold. Power is the one derived number on the screen, and it comes through
/// <see cref="IHeroPowerSource"/> — behind which sits <c>PowerCalculator</c>, the one public rules
/// type built for exactly this readout — so it is not a second copy of a formula either. There is
/// still no Energy maximum, no denominator, no regeneration countdown, no Legend-XP percentage and
/// no run Energy cost: every tuning reader that could compute one is internal to
/// <c>SlayIdleRepeat.Core</c> and the host seam exposes no derived-value read, so a value produced
/// here would be a copy that disagrees with the rules the first time either is tuned.
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
    /// Captions the HUD borrows from the documents that own them. Named in the home document as well,
    /// so retiring one fails the content load rather than rendering a key on this screen.
    /// </summary>
    private const string CrownsNameKey = "loc.currency.crowns.name";
    private const string SoulShardsNameKey = "loc.currency.soul_shards.name";
    private const string GoldNameKey = "loc.currency.gold.name";
    private const string TierNormalKey = "loc.chapter_select.tier_normal.name";
    private const string TierHeroicKey = "loc.chapter_select.tier_heroic.name";
    private const string TierMythicKey = "loc.chapter_select.tier_mythic.name";

    /// <summary>The status line of a screen that has an action to offer: there is nothing left to say.</summary>
    private const string NothingLeftToSay = "";

    /// <summary>What a tile's text is when there is no value behind it — blank, never a zero.</summary>
    private const string NoValue = "";

    private const string ClearJoin = " · ";

    private const string OutOf = "/";

    private readonly IGameHost _gameHost;
    private readonly LocaleStringCatalogue _strings;
    private readonly ContentSnapshot _content;
    private readonly IClockPort _clock;
    private readonly IHeroPowerSource _power;
    private readonly PlayerId _player;
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

        _gameHost = gameHost;
        _strings = strings;
        _content = content;
        _clock = clock;
        _power = power;
        _player = player;
        _chapters = ChapterDocuments.Read(content, strings);
    }

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
        ? clear.ChapterName + ClearJoin + TierName(clear.Tier)
        : _strings.Resolve(NothingClearedStatusKey);

    /// <summary>The stage the run stands in over the stages a chapter has, or blank when there is no run.</summary>
    public string RunStageText => RunProgress is { } progress
        ? PlayerNumber.Full(progress.Stage) + OutOf + PlayerNumber.Full(ChapterProgressReadout.StageCount)
        : NoValue;

    /// <summary>The hero's hit points over the pool they are out of, both in full, or blank when there is no run.</summary>
    /// <remarks>Never shortened: a health readout of "10.0k/12.3k" hides the one number a player checks before continuing.</remarks>
    public string RunHitPointsText => RunProgress is { } progress
        ? PlayerNumber.Full(progress.CurrentHp) + OutOf + PlayerNumber.Full(progress.MaxHp)
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
        try
        {
            // Awaited inside the guard rather than merely called inside it: a real host's read is an
            // async method, so its failure arrives as a faulted task and a try around the call alone
            // would never see it.
            var state = await _gameHost.ReadOwnStateAsync(_player, run: null, ct).ConfigureAwait(false);

            Settle(state);
        }
        catch (Exception failure)
        {
            Decision = HomeContinueDecision.ReadUnavailable;
            FailureDetail = $"{failure.GetType().Name}: {failure.Message}";
        }
    }

    private void Settle(OwnStateResult state)
    {
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
            if (RunExpiry.HasLapsed(open, _clock.UtcNow, _content))
            {
                Decision = HomeContinueDecision.RunLapsed;
                ReadPower(view.Player, run: null);
                return;
            }

            Decision = HomeContinueDecision.ContinueRun;
            ContinuableRun = open.Id;
            RunGold = open.Gold;
            RunProgress = ChapterProgressReadout.RunProgress(open, _content, _chapters);
            ReadPower(view.Player, open);
            return;
        }

        Decision = HomeContinueDecision.StartNewRun;
        ReadPower(view.Player, run: null);
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
    /// the between-runs hero when there is nothing to continue — a lapsed run's drafted perks included,
    /// since nobody will stand on that board again.
    /// </summary>
    private void ReadPower(PlayerSnapshot player, RunSnapshot? run)
    {
        var reading = _power.Read(player, run);

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
    private string TierName(DifficultyTier tier) => _strings.Resolve(tier switch
    {
        DifficultyTier.NORMAL => TierNormalKey,
        DifficultyTier.HEROIC => TierHeroicKey,
        DifficultyTier.MYTHIC => TierMythicKey,
        _ => throw new ArgumentOutOfRangeException(
            nameof(tier), tier, "this tier has no authored name the progress tile can spell; add its key before a row can carry it."),
    });
}
