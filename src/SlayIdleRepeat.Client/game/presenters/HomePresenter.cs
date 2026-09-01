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
/// Drives the Home screen: the header the profile carries, and the one decision the screen exists
/// to make — start a run, or resume the one already open.
/// </summary>
/// <remarks>
/// <para>
/// Plain C# taking its collaborators as constructor arguments, so the whole screen runs under a test
/// runner with no engine anywhere near it. The scene above renders what is exposed here; the
/// composition root below decides what the collaborators actually are.
/// </para>
/// <para>
/// 🔒 <b>The absences are deliberate and are the design.</b> The header shows the two Energy amounts
/// the player snapshot literally carries and nothing derived from them: no maximum, no denominator,
/// no regeneration countdown, no Legend-XP percentage and no run Energy cost. Every tuning reader
/// that could compute one is internal to <c>SlayIdleRepeat.Core</c> and the host seam exposes no
/// derived-value read, so a value produced here would be a second copy of a formula the rules
/// already own — and two copies of a balance formula disagree the first time either is tuned.
/// </para>
/// <para>
/// ⚠️ Also absent, each because a later row owns it: the hero diorama, daily quests, ad widgets,
/// chest pity, event and guild cards, the inbox envelope, the account-link banner, the bottom
/// navigation, the Legend XP bar and the Dungeons entry.
/// </para>
/// </remarks>
public sealed class HomePresenter
{
    private const string LegendLevelLabelKey = "loc.home.legend_level.label";
    private const string EnergyLabelKey = "loc.home.energy.label";
    private const string EnergyReserveLabelKey = "loc.home.energy_reserve.label";
    private const string StartRunActionKey = "loc.home.start_run.action";
    private const string ContinueRunActionKey = "loc.home.continue_run.action";
    private const string GearActionKey = "loc.home.gear.action";
    private const string LoadingStatusKey = "loc.home.loading.status";
    private const string UnavailableStatusKey = "loc.home.unavailable.status";
    private const string RunLapsedStatusKey = "loc.home.run_lapsed.status";

    /// <summary>The status line of a screen that has an action to offer: there is nothing left to say.</summary>
    private const string NothingLeftToSay = "";

    private readonly IGameHost _gameHost;
    private readonly LocaleStringCatalogue _strings;
    private readonly ContentSnapshot _content;
    private readonly IClockPort _clock;
    private readonly PlayerId _player;

    /// <summary>Builds the screen over the host, the strings and the profile boot opened.</summary>
    /// <param name="gameHost">The seam the player's own state is read through.</param>
    /// <param name="strings">Key to display string, over the loaded content set.</param>
    /// <param name="content">
    /// The loaded content set, read for the authored run window — the same snapshot the strings came
    /// from, so the screen cannot be measuring one version's window against another's copy.
    /// </param>
    /// <param name="clock">
    /// The one sanctioned reading of now. Taken as a port rather than an ambient call for the reason
    /// <c>IClockPort</c> states at length, and read at the moment the decision is made rather than
    /// stored, so a screen left open does not decide against a stale instant.
    /// </param>
    /// <param name="player">The profile this screen is about.</param>
    /// <exception cref="ArgumentNullException">A collaborator is null.</exception>
    public HomePresenter(
        IGameHost gameHost,
        LocaleStringCatalogue strings,
        ContentSnapshot content,
        IClockPort clock,
        PlayerId player)
    {
        ArgumentNullException.ThrowIfNull(gameHost);
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(clock);

        _gameHost = gameHost;
        _strings = strings;
        _content = content;
        _clock = clock;
        _player = player;
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

    /// <summary>The caption beside <see cref="LegendLevel"/>, resolved.</summary>
    public string LegendLevelLabel => _strings.Resolve(LegendLevelLabelKey);

    /// <summary>The caption beside <see cref="Energy"/>, resolved.</summary>
    public string EnergyLabel => _strings.Resolve(EnergyLabelKey);

    /// <summary>The caption beside <see cref="EnergyReserve"/>, resolved.</summary>
    public string EnergyReserveLabel => _strings.Resolve(EnergyReserveLabelKey);

    /// <summary>The primary action's caption for the current <see cref="Decision"/>, resolved.</summary>
    /// <summary>
    /// The caption of the control that opens the gear stock (S16), resolved.
    /// </summary>
    /// <remarks>
    /// 🔒 Always the same word, whatever the profile read said. Unlike <see cref="ActionText"/> — which
    /// changes between starting and continuing a run — the bag is the bag: a caption that varied would be
    /// inventing a distinction the screen behind it does not have.
    /// </remarks>
    public string GearText => _strings.Resolve(GearActionKey);

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
                return;
            }

            Decision = HomeContinueDecision.ContinueRun;
            ContinuableRun = open.Id;
            return;
        }

        Decision = HomeContinueDecision.StartNewRun;
    }

    private void Carry(PlayerSnapshot player)
    {
        DisplayName = player.DisplayName;
        LegendLevel = player.LegendLevel;
        Energy = player.Energy.Energy;
        EnergyReserve = player.Energy.Reserve;
    }
}
