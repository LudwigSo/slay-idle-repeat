using System.Globalization;
using Godot;
using SlayIdleRepeat.Client.Composition;
using SlayIdleRepeat.Client.Game.Presenters;

namespace SlayIdleRepeat.Client.Game.Scenes;

/// <summary>
/// S05 — the board a run is played on: a driving adapter over <see cref="BoardPresenter"/>, with
/// S12's die panel drawn over it from <see cref="DiePanelPresenter"/>.
/// </summary>
/// <remarks>
/// <para>
/// It renders what the presenters say and forwards five presses. No rules, no ports, no adapters,
/// and above all no decision about what may be pressed: which of the roll, the reroll, the tile
/// acknowledgement and the two branches is live at any moment is the presenter's answer, and this
/// half only draws it. That split is load-bearing rather than stylistic here — there is no scene
/// test harness in this repository, so anything decided in this file is decided where nothing can
/// check it.
/// </para>
/// <para>
/// 🔴 <b>There is no track of tiles, because no client can read one.</b> The board's graph is
/// generated inside the rules layer and never leaves it: which tile sits at which node, what a fork
/// branch holds, what is coming up. What is drawn instead is honest — a strip of pips as long as
/// the stage the chapter authors, with the one the run stands on lit, and the name of the tile it
/// stands on. The upcoming-tile preview, the two branch previews with their labels and icon sets,
/// and the perks and consumable-pouch controls of the HUD are all absent; the first three because
/// they are unreadable, the last two because later milestones own them.
/// </para>
/// <para>
/// 🔒 <b>There is no art here at all, placeholder or otherwise, and no VFX.</b> The tile pips, the
/// hero token, the HP bar and the reroll ring are drawn as containers and coloured rectangles the
/// engine already provides. The die tumble, the dust puff and the floating result number the design
/// asks for are not built: they are procedural in-engine work by ruling, never a sprite sheet, and
/// this task adds no asset row for them.
/// </para>
/// <para>
/// ⚠️ Every type size, colour and gap in <c>Board.tscn</c> and <c>DieFaceRow.tscn</c> is a per-node
/// override, because the shared theme resource and the display faces it will carry do not exist yet
/// — they are M8-03's, and these overrides are debt owed to it rather than a naming scheme of this
/// screen's own. The sizes were chosen against the engine's default font, so they have to be
/// re-checked — not merely re-applied — when the real faces land. The layout itself is structural:
/// containers and stretch ratios, so it holds its proportions across the whole supported aspect
/// range without an override taking part.
/// </para>
/// <para>
/// 🔴 <b>One destination this screen stops at rather than builds.</b> A run that has ended belongs
/// to a screen this milestone does not own — see <see cref="TheRunEndScreensAreNotBuiltHere"/>. The
/// board says in words what the player is waiting on, disables the roll, and navigates nowhere.
/// </para>
/// <para>
/// 🔒 <b>Four destinations it does navigate to, and it navigates to each once.</b> The open battle,
/// the perk draft a won fight leaves, and the shop, campfire and shrine tiles a run lands on all
/// hand control back to this same screen rather than to a new one, so the board is read again on
/// return — every one of them is left by a command that has moved the run underneath it. A
/// destination still open after its own screen has handed back is a dead end rather than a reason to
/// go round again; see <see cref="TheBattleDidNotCloseWhenItsReplayEnded"/> and
/// <see cref="TheDecisionDidNotCloseWhenItsScreenHandedBack"/>.
/// </para>
/// </remarks>
public partial class Board : Control
{
    /// <summary>Where this scene lives, for the screens that instantiate it.</summary>
    public const string ScenePath = "res://game/scenes/Board.tscn";

    /// <summary>
    /// 🔴 Named so the dead end can be found. A battle that is still open once its replay has been
    /// watched is a battle nothing on either screen can close, and re-entering it would put the
    /// player in a loop between two screens with no way out of either.
    /// </summary>
    private const string TheBattleDidNotCloseWhenItsReplayEnded =
        "A battle was still open when its replay handed control back, so it is not entered a second " +
        "time. The confirmation that closes a battle is the replay's last step, and it is not made " +
        "when the fight could not be simulated at all — a corrupt battle counter, a simulator that " +
        "threw, a log with no events — or when the rules layer refused the result. Either way the run " +
        "stays parked in the battle phase, which refuses every command except that confirmation, and " +
        "a board that re-entered the replay on every read would trap the player between two screens. " +
        "This message reached a player once, for a whole milestone, because the shipped prediction " +
        "refused every fight by construction: if it is on screen now, read the replay's own status " +
        "line for which of the reasons it was.";

    /// <summary>
    /// 🔴 Named so the dead end can be found, and the exact counterpart of the battle's. A decision
    /// screen hands back once the run has left the state that opened it, so a run still in that state
    /// on the read that follows is a decision nothing on either screen can close.
    /// </summary>
    private const string TheDecisionDidNotCloseWhenItsScreenHandedBack =
        "A decision was still open when its screen handed control back, so it is not entered a " +
        "second time. Every one of these screens leaves only once the run itself says the draft has " +
        "closed or the tile has cleared, so a run that comes back still holding either could not be " +
        "read at all or was refused the command that would have finished it — and a board that " +
        "re-entered the screen on every read would trap the player between two screens.";

    /// <summary>
    /// 🔴 Deliberately unbuilt, and named so it can be found. The run's death and results screens are
    /// M7-08's. A finished run is drawn as finished and left there; this screen offers no way out of
    /// it, because every way out is somebody else's.
    /// </summary>
    private const string TheRunEndScreensAreNotBuiltHere =
        "The death and results screens are not built in this milestone: a run that has ended is " +
        "drawn as ended and has nowhere to go. The board neither banks it nor abandons it.";

    /// <summary>
    /// ⚠️ This task's choice, and the only timing on this screen that is not authored. The design
    /// says the die panel opens on a long press of the roll button and does not say how long a long
    /// press is. The panel is therefore also reachable from a control of its own, so nothing about
    /// the disclosure depends on a number nobody wrote down.
    /// </summary>
    private const double LongPressSeconds = 0.5;

    /// <summary>
    /// The one line a headless run's screen state is read off. Distinctive on purpose: a board
    /// resolved against real content has to be greppable out of an engine log full of everything
    /// else, the way the home screen's readout already is.
    /// </summary>
    private const string BoardMarker = "SIR_BOARD_READY";

    /// <summary>Where the per-face row of the die panel lives, instantiated once per face kind.</summary>
    private const string DieFaceRowScenePath = "res://game/scenes/DieFaceRow.tscn";

    private const string RowNameLabelPath = "NameLabel";
    private const string RowEffectLabelPath = "EffectLabel";

    private const string SafeAreaPath = "%SafeArea";
    private const string GroundPath = "%Ground";
    private const string HudPath = "%Hud";
    private const string StageRowPath = "%StageRow";
    private const string HpLabelPath = "%HpLabel";
    private const string HpValuePath = "%HpValue";
    private const string HpBarPath = "%HpBar";
    private const string GoldLabelPath = "%GoldLabel";
    private const string GoldValuePath = "%GoldValue";
    private const string StageLabelPath = "%StageLabel";
    private const string StageValuePath = "%StageValue";
    private const string TrackFramePath = "%TrackFrame";
    private const string TrackPath = "%Track";
    private const string StandingOnRowPath = "%StandingOnRow";
    private const string StandingOnLabelPath = "%StandingOnLabel";
    private const string PendingTileLabelPath = "%PendingTileLabel";
    private const string StatusLabelPath = "%StatusLabel";
    private const string BlockLabelPath = "%BlockLabel";
    private const string RejectionLabelPath = "%RejectionLabel";
    private const string ForkPanelPath = "%ForkPanel";
    private const string ForkTitleLabelPath = "%ForkTitleLabel";
    private const string ForkButtonsPath = "%ForkButtons";
    private const string PromptPanelPath = "%PromptPanel";
    private const string RolledLabelPath = "%RolledLabel";
    private const string RolledValuePath = "%RolledValue";
    private const string RingBarPath = "%RingBar";
    private const string RerollButtonPath = "%RerollButton";
    private const string RerollChangesNextRollLabelPath = "%RerollChangesNextRollLabel";
    private const string DiePanelButtonPath = "%DiePanelButton";
    private const string RollButtonPath = "%RollButton";
    private const string ResolveButtonPath = "%ResolveButton";
    private const string DiePanelOverlayPath = "%DiePanelOverlay";
    private const string OverlaySafeAreaPath = "%OverlaySafeArea";
    private const string DiePanelTitleLabelPath = "%DiePanelTitleLabel";
    private const string FacesUnavailableLabelPath = "%FacesUnavailableLabel";
    private const string FaceListPath = "%FaceList";
    private const string LastFaceLabelPath = "%LastFaceLabel";
    private const string LastFaceValuePath = "%LastFaceValue";
    private const string CloseButtonPath = "%CloseButton";

    /// <summary>Separates the two halves of one value pair: the amount, then its denominator.</summary>
    private const char OverSeparator = '/';

    /// <summary>Joins the faces one command reported, in the order it produced them.</summary>
    private const string FaceJoin = " · ";

    /// <summary>The theme entry a control's own text size is written into.</summary>
    private const string FontSizeOverride = "font_size";

    /// <summary>What a fork branch's caption is drawn at, matching the body size beside it.</summary>
    private const int BranchFontSize = 48;

    /// <summary>The pip a node the run has not reached is drawn as.</summary>
    private static readonly Color UnvisitedNodeColour = new(0.24f, 0.25f, 0.30f);

    /// <summary>And the one it is standing on — the token, in the palette's own live colour.</summary>
    private static readonly Color TokenColour = new(0.93f, 0.93f, 0.96f);

    /// <summary>A control with something to do.</summary>
    private static readonly Color LiveColour = new(0.93f, 0.93f, 0.96f);

    /// <summary>And one with nothing to do — the same quiet grey every caption is drawn in.</summary>
    private static readonly Color UnavailableColour = new(0.66f, 0.67f, 0.73f);

    /// <summary>
    /// How tall one node pip is drawn — and only how tall.
    /// </summary>
    /// <remarks>
    /// 🔒 The width is left to the container and each pip is told to expand, so the strip divides
    /// the row it is given however many nodes the stage has. A fixed width would be a number chosen
    /// against the two shipped chapters, and stage lengths are authored content: a chapter with
    /// twice as many nodes would push the strip straight through the safe area on both sides.
    /// </remarks>
    private static readonly Vector2 NodePipSize = new(0, 24);

    private BoardPresenter? _presenter;
    private DiePanelPresenter? _diePanel;

    /// <summary>Builds the replay of the fight the run is standing in, once there is one.</summary>
    private Func<ComposedBattleScreen>? _battle;

    /// <summary>Builds the draft screen for the draft a won fight has left open.</summary>
    private Func<ComposedPerkDraftScreen>? _perkDraft;

    /// <summary>Builds the shop screen for the shop tile the run has landed on.</summary>
    private Func<ComposedShopScreen>? _shop;

    /// <summary>Builds the campfire / shrine screen for the tile the run has landed on.</summary>
    private Func<ComposedCampfireScreen>? _campfire;
    private Func<ComposedRunEndScreen>? _runEnd;

    /// <summary>Whether the battle now open has already had its replay watched.</summary>
    private bool _battleShown;

    /// <summary>Which decision the run's present state has already been handed over for, if any.</summary>
    private RunDecision? _decisionShown;

    /// <summary>The starting menu this run's ending leads back to.</summary>
    /// <remarks>
    /// 🔒 Held rather than looked up, because there is nothing to look it up by: it is a hidden sibling
    /// among the screens the application has opened, and a search of the parent for one would be a
    /// screen deciding its own navigation from the shape of the tree. It arrives with the handover for
    /// the reason <see cref="BoardHandover"/> states — only the caller knows where a run was entered
    /// from, and only Home is where a finished one leads.
    /// </remarks>
    private Home? _home;

    private CancellationToken _lifetime;

    private ColorRect? _ground;
    private Control? _hud;
    private Control? _stageRow;
    private Label? _hpLabel;
    private Label? _hpValue;
    private ProgressBar? _hpBar;
    private Label? _goldLabel;
    private Label? _goldValue;
    private Label? _stageLabel;
    private Label? _stageValue;
    private Control? _trackFrame;
    private HBoxContainer? _track;
    private Control? _standingOnRow;
    private Label? _standingOnLabel;
    private Label? _pendingTileLabel;
    private Label? _statusLabel;
    private Label? _blockLabel;
    private Label? _rejectionLabel;
    private Control? _forkPanel;
    private Label? _forkTitleLabel;
    private HBoxContainer? _forkButtons;
    private Control? _promptPanel;
    private Label? _rolledLabel;
    private Label? _rolledValue;
    private ProgressBar? _ringBar;
    private Button? _rerollButton;
    private Label? _rerollChangesNextRollLabel;
    private Button? _diePanelButton;
    private Button? _rollButton;
    private Button? _resolveButton;
    private Control? _diePanelOverlay;
    private Label? _diePanelTitleLabel;
    private Label? _facesUnavailableLabel;
    private VBoxContainer? _faceList;
    private Label? _lastFaceLabel;
    private Label? _lastFaceValue;
    private Button? _closeButton;

    /// <summary>How long the roll button has been held, or null when it is not being held.</summary>
    private double? _heldFor;

    /// <summary>Whether the current hold has already opened the panel, so its release does not roll.</summary>
    private bool _holdConsumed;

    /// <summary>Whether a submission is in flight, so a second press cannot start another.</summary>
    private bool _busy;

    /// <summary>Takes everything the composition root built for this screen, and the shutdown token.</summary>
    /// <remarks>
    /// The whole composed screen rather than its parts, because the parts had reached six: two
    /// presenters and a factory for each of the four destinations a run can reach from here. The
    /// holder is the client's own composition type, and this reads factories off it exactly as it
    /// already did for the battle — it calls them, it assembles nothing.
    /// </remarks>
    /// <param name="screen">Everything the composition root built for this board's run.</param>
    /// <param name="home">
    /// The starting menu a finished run leads back to, kept for <see cref="LeaveToHome"/>.
    /// </param>
    /// <param name="lifetime">Cancelled when the application shuts down.</param>
    /// <exception cref="ArgumentNullException"><paramref name="screen"/> or <paramref name="home"/> is null.</exception>
    public void Drive(ComposedBoardScreen screen, Home home, CancellationToken lifetime)
    {
        ArgumentNullException.ThrowIfNull(screen);
        ArgumentNullException.ThrowIfNull(home);

        _presenter = screen.Board;
        _diePanel = screen.DiePanel;
        _battle = screen.Battle;
        _perkDraft = screen.PerkDraft;
        _shop = screen.Shop;
        _campfire = screen.Campfire;
        _runEnd = screen.RunEnd;
        _home = home;
        _lifetime = lifetime;
    }

    /// <summary>Shows this screen again and reads the run afresh, for a fight handing control back.</summary>
    /// <remarks>
    /// 🔒 The read is the point, not the showing. A replay that reached its end submitted the
    /// confirmation that closes the battle, so the run behind this screen is a different row from the
    /// one it drew: the phase has moved, the health has moved, and a won fight has opened a draft.
    /// Un-hiding without reading again would put a pre-battle board in front of a post-battle run.
    /// </remarks>
    public void Resume()
    {
        if (!IsInstanceValid(this) || !IsInsideTree())
        {
            return;
        }

        Visible = true;

        _ = StartAsync();
    }

    /// <summary>
    /// Stands this board down for good and hands the player back to the starting menu, for a run that
    /// has ended.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>FREED, not hidden, which is the opposite of what <see cref="Resume"/> does and for the
    /// opposite reason.</b> A run is entered once and ends once — by a death or by a chapter cleared,
    /// and <c>02</c> §6 makes those one moment — so there is nothing on this screen a later run could
    /// use. <c>BoardComposition</c> builds the next run a board of its own with its own presenters and
    /// its own read, so keeping this one would leave a dead board and a finished projection in memory
    /// per run for the life of the application, and re-showing it would be the second run played on the
    /// first run's screen.
    /// </para>
    /// <para>
    /// 🔒 <b>Home is RE-READ rather than merely un-hidden</b>, for the reason <see cref="Resume"/> gives
    /// about this screen: the run behind it has closed and its payout is banked, so the profile Home
    /// drew is a different row from the one it holds. Un-hiding alone would leave a CONTINUE offering to
    /// resume the run that has just ended, onto the board this call is freeing.
    /// </para>
    /// <para>
    /// 🔒 <b>Detached before it is queued.</b> <c>QueueFree</c> alone defers removal to the end of the
    /// frame, and this board is hidden but still processing — it counts a held roll button every frame —
    /// so a board merely queued would keep running over the Home it just revealed. The free stays
    /// queued rather than taken because this is reached from the run-end screen's handler.
    /// </para>
    /// </remarks>
    /// <returns>
    /// True when the starting menu is back and this board has stood down. False says it is still here
    /// and still the player's only screen, which is <see cref="RunEndHandover.Leave"/>'s cue to hand
    /// back to it rather than free the screen in front of it over nothing.
    /// </returns>
    internal bool LeaveToHome()
    {
        if (_home is not { } home)
        {
            GD.PushError(
                "A run ended and this board has no starting menu to return to. Only a screen that can " +
                "be returned to may instantiate the board, and it must pass Home to Drive.");

            return false;
        }

        if (!IsInstanceValid(home))
        {
            // The starting menu freed underneath a run is a shutdown, which is the ordinary way it
            // happens on a handset. Named rather than navigated to, and the board stays.
            GD.PushError("A run ended and the starting menu it was entered from is gone.");

            return false;
        }

        home.Resume();

        GetParent()?.RemoveChild(this);
        QueueFree();

        return true;
    }

    /// <inheritdoc/>
    public override void _Ready()
    {
        // Resolved once. A scene-unique lookup is a string search of the owner's table each time it
        // is asked, and this screen redraws on every command and on every frame the ring is open.
        _ground = GetNode<ColorRect>(GroundPath);
        _hud = GetNode<Control>(HudPath);
        _stageRow = GetNode<Control>(StageRowPath);
        _hpLabel = GetNode<Label>(HpLabelPath);
        _hpValue = GetNode<Label>(HpValuePath);
        _hpBar = GetNode<ProgressBar>(HpBarPath);
        _goldLabel = GetNode<Label>(GoldLabelPath);
        _goldValue = GetNode<Label>(GoldValuePath);
        _stageLabel = GetNode<Label>(StageLabelPath);
        _stageValue = GetNode<Label>(StageValuePath);
        _trackFrame = GetNode<Control>(TrackFramePath);
        _track = GetNode<HBoxContainer>(TrackPath);
        _standingOnRow = GetNode<Control>(StandingOnRowPath);
        _standingOnLabel = GetNode<Label>(StandingOnLabelPath);
        _pendingTileLabel = GetNode<Label>(PendingTileLabelPath);
        _statusLabel = GetNode<Label>(StatusLabelPath);
        _blockLabel = GetNode<Label>(BlockLabelPath);
        _rejectionLabel = GetNode<Label>(RejectionLabelPath);
        _forkPanel = GetNode<Control>(ForkPanelPath);
        _forkTitleLabel = GetNode<Label>(ForkTitleLabelPath);
        _forkButtons = GetNode<HBoxContainer>(ForkButtonsPath);
        _promptPanel = GetNode<Control>(PromptPanelPath);
        _rolledLabel = GetNode<Label>(RolledLabelPath);
        _rolledValue = GetNode<Label>(RolledValuePath);
        _ringBar = GetNode<ProgressBar>(RingBarPath);
        _rerollButton = GetNode<Button>(RerollButtonPath);
        _rerollChangesNextRollLabel = GetNode<Label>(RerollChangesNextRollLabelPath);
        _diePanelButton = GetNode<Button>(DiePanelButtonPath);
        _rollButton = GetNode<Button>(RollButtonPath);
        _resolveButton = GetNode<Button>(ResolveButtonPath);
        _diePanelOverlay = GetNode<Control>(DiePanelOverlayPath);
        _diePanelTitleLabel = GetNode<Label>(DiePanelTitleLabelPath);
        _facesUnavailableLabel = GetNode<Label>(FacesUnavailableLabelPath);
        _faceList = GetNode<VBoxContainer>(FaceListPath);
        _lastFaceLabel = GetNode<Label>(LastFaceLabelPath);
        _lastFaceValue = GetNode<Label>(LastFaceValuePath);
        _closeButton = GetNode<Button>(CloseButtonPath);

        _rollButton.Pressed += OnRollPressed;
        _rollButton.ButtonDown += OnRollHoldStarted;
        _rollButton.ButtonUp += OnRollHoldEnded;
        _rerollButton.Pressed += OnRerollPressed;
        _resolveButton.Pressed += OnResolvePressed;
        _diePanelButton.Pressed += OnDiePanelPressed;
        _closeButton.Pressed += OnClosePressed;
        _ground.GuiInput += OnGroundInput;

        // Painted once, because nothing about which colour belongs to which state changes while the
        // screen is up. It is painted at all because a Button draws its text by draw mode, and the
        // disabled mode every one of these controls spends most of its life in has an engine default
        // of half-transparent grey that no override of font_color reaches.
        foreach (var button in new[] { _rollButton, _resolveButton, _rerollButton, _diePanelButton })
        {
            ButtonTextColours.ApplyTo(button, LiveColour, UnavailableColour);
        }

        SafeAreaInsets.ApplyTo(GetNode<MarginContainer>(SafeAreaPath), GetViewportRect().Size);
        SafeAreaInsets.ApplyTo(GetNode<MarginContainer>(OverlaySafeAreaPath), GetViewportRect().Size);

        BuildFaceList();
        Render();

        _ = StartAsync();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The matching half of the subscriptions in <c>_Ready</c>. The buttons are children and die
    /// with this node either way, but a handler left connected across a scene that is merely
    /// detached and re-added would fire twice — and once is the whole contract of a roll.
    /// </remarks>
    public override void _ExitTree()
    {
        if (_rollButton is not null)
        {
            _rollButton.Pressed -= OnRollPressed;
            _rollButton.ButtonDown -= OnRollHoldStarted;
            _rollButton.ButtonUp -= OnRollHoldEnded;
        }

        if (_rerollButton is not null)
        {
            _rerollButton.Pressed -= OnRerollPressed;
        }

        if (_resolveButton is not null)
        {
            _resolveButton.Pressed -= OnResolvePressed;
        }

        if (_diePanelButton is not null)
        {
            _diePanelButton.Pressed -= OnDiePanelPressed;
        }

        if (_closeButton is not null)
        {
            _closeButton.Pressed -= OnClosePressed;
        }

        if (_ground is not null)
        {
            _ground.GuiInput -= OnGroundInput;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// Two things need a frame rather than an event: the ring, which is a countdown nothing else
    /// ticks, and the hold that opens the die panel.
    /// </para>
    /// <para>
    /// 🔒 The ring's DECISION is not made here. This asks the presenter to look at its clock and
    /// answer whether the window has closed; whether four seconds have passed, and what a lapse
    /// means, are the presenter's, where a case can drive them. What is left here is the redraw.
    /// </para>
    /// </remarks>
    /// <param name="delta">Seconds since the previous frame.</param>
    public override void _Process(double delta)
    {
        // 🔒 Nothing ticks while the die panel is up. The prompt's window is a deadline the player
        // is answering, and letting it drain behind a modal they opened to read their die spends
        // their answer on the act of looking something up.
        if (_diePanelOverlay is { Visible: true })
        {
            return;
        }

        AdvanceHold(delta);

        if (_presenter is not { } presenter || presenter.Prompt is null)
        {
            return;
        }

        if (presenter.TickRerollPrompt())
        {
            Render();

            return;
        }

        RenderPrompt(presenter);
    }

    /// <remarks>
    /// Nothing awaits this task, so its exceptions have nowhere to surface: the whole body is
    /// guarded, or a continuation that threw would leave a board that silently stopped moving.
    /// </remarks>
    private async Task StartAsync()
    {
        try
        {
            if (_presenter is not { } presenter)
            {
                GD.PushError(
                    "The board entered the tree with no presenter. Only a screen that already has " +
                    "a run may instantiate it, and it must call Drive before adding it to the tree.");

                return;
            }

            // No ConfigureAwait(false): the continuation writes to nodes, and only the thread the
            // engine runs the scene tree on may do that.
            await presenter.StartAsync(_lifetime);

            Render();
            Report(presenter);
        }
        catch (Exception failure)
        {
            GD.PushError($"The board stopped unexpectedly: {failure}");
        }
    }

    private void AdvanceHold(double delta)
    {
        if (_heldFor is not { } held || _holdConsumed)
        {
            return;
        }

        _heldFor = held + delta;

        if (_heldFor < LongPressSeconds)
        {
            return;
        }

        // Consumed before the panel opens, so the release that follows is not also a roll. A player
        // who holds the button to read their die has not asked to spend a turn.
        _holdConsumed = true;

        ShowDiePanel();
    }

    /// <summary>Writes both presenters' state into the scene, if the scene is still there to write into.</summary>
    private void Render()
    {
        var presenter = _presenter;

        // Validity before tree membership: asking a freed node whether it is inside the tree is
        // itself the crash, and a shutdown during a slow command is the ordinary case on a handset.
        // Every node this writes to is checked, not just the one: a scene-unique name that no longer
        // resolves leaves a null behind, and a null-forgiving operator over it would turn a renamed
        // node into a crash here instead of a blank label.
        if (presenter is null || !IsInstanceValid(this) || !IsInsideTree() ||
            _hud is null || _hpLabel is null || _hpValue is null || _hpBar is null ||
            _goldLabel is null || _goldValue is null || _stageLabel is null || _stageValue is null ||
            _trackFrame is null || _track is null || _standingOnRow is null ||
            _standingOnLabel is null || _pendingTileLabel is null ||
            _statusLabel is null || _blockLabel is null || _rejectionLabel is null ||
            _forkPanel is null || _forkTitleLabel is null || _forkButtons is null ||
            _promptPanel is null || _rollButton is null || _resolveButton is null ||
            _diePanelButton is null)
        {
            return;
        }

        // The one predicate most of the screen turns on: whether the read produced a run there is
        // anything to draw. An ended run still carries its numbers and still draws them — what it
        // does not carry is anything to press.
        var carried = presenter.Stage is BoardStage.Ready or BoardStage.RunEnded;

        // 🔒 Drawn only when there ARE numbers. Before the read answers, and in the two states where
        // it never will, the HP, Gold and stage are all still the zero an unset field carries — and
        // "HP 0/0" told to a player mid-run is not a placeholder, it is a plausible value in a hole.
        // The block leaves instead and the status line says which state this is.
        _hud.Visible = carried;

        // 🔒 The track frame is NEVER hidden, and that is a layout fact rather than a content one.
        // Its two children hide themselves when they have nothing to show, but the frame itself has
        // to stay: hiding it once left the scrolling middle band as the column's only expander, and
        // in the three states where there is no run the whole screen packed against the top with
        // the primary action stranded in the upper third.
        _trackFrame.Visible = true;

        _hpLabel.Text = presenter.HpLabel;
        _hpValue.Text = $"{presenter.CurrentHp.ToString(CultureInfo.InvariantCulture)}" +
                        $"{OverSeparator}{presenter.MaxHp.ToString(CultureInfo.InvariantCulture)}";
        _hpBar.MaxValue = Math.Max(presenter.MaxHp, 1);
        _hpBar.Value = presenter.CurrentHp;

        _goldLabel.Text = presenter.GoldLabel;
        _goldValue.Text = presenter.Gold.ToString(CultureInfo.InvariantCulture);

        _stageLabel.Text = presenter.StageLabel;
        _stageValue.Text = StageReadout(presenter);

        // Hidden rather than blanked once it has nothing to say, which is what every other screen in
        // this build does with the same line and for the same reason: an empty label still claims a
        // full line of height, so a blank one is a sentence a player can see room for and cannot
        // read. The whole ROW goes, caption included — "Stage" with no number after it reads as a
        // value that failed to load rather than as a value there is not yet one of.
        if (_stageRow is not null)
        {
            _stageRow.Visible = _stageValue.Text.Length > 0;
        }

        RenderTrack(presenter);

        _standingOnLabel.Text = presenter.StandingOnLabel;
        _pendingTileLabel.Text = presenter.PendingTileName;
        _standingOnRow.Visible = _pendingTileLabel.Text.Length > 0;

        _statusLabel.Text = presenter.StatusText;
        _statusLabel.Visible = _statusLabel.Text.Length > 0;

        _blockLabel.Text = presenter.BlockText;
        _blockLabel.Visible = _blockLabel.Text.Length > 0;

        _rejectionLabel.Text = presenter.RejectionText;
        _rejectionLabel.Visible = _rejectionLabel.Text.Length > 0;

        RenderFork(presenter);
        RenderPrompt(presenter);

        // 🔒 The three things that move a run on share the bottom of the screen and are never live
        // together: a pending tile is exactly what refuses a roll, and so is an open fork. Swapping
        // between them rather than greying two out is what keeps the control the player is supposed
        // to press the largest one on screen and inside the thumb zone — a 340-pixel dead roll
        // button over two small live branch buttons is the design's rule exactly inverted.
        var tilePending = presenter.RollBlock == BoardRollBlock.TilePending;
        var forkOpen = presenter.RollBlock == BoardRollBlock.ForkOpen;

        _rollButton.Text = presenter.RollText;
        _rollButton.Visible = !tilePending && !forkOpen;
        _rollButton.Disabled = _busy || presenter.RollBlock != BoardRollBlock.None;

        // A hold in progress on a button that has just been taken out of use is abandoned here. The
        // engine raises no release for a control disabled mid-press, so without this the hold keeps
        // accumulating and the die panel opens on its own some seconds later.
        if (_rollButton.Disabled || !_rollButton.Visible)
        {
            _heldFor = null;
            _holdConsumed = false;
        }

        _resolveButton.Text = presenter.ResolveText;
        _resolveButton.Visible = tilePending;
        _resolveButton.Disabled = _busy;

        _diePanelButton.Text = presenter.DiePanelText;
    }

    /// <summary>
    /// Draws the run's progress through the stage it is in, one pip per authored node.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>This is a progress strip, not the board.</b> Each pip is a node the chapter authors and
    /// nothing more — no tile kind, no fork, no preview, because none of that is readable from a
    /// client. The pip the run stands on is lit; every other pip is identical, which is honest: this
    /// screen genuinely does not know what is on them. It draws nothing at all when either the
    /// stage's length or the run's exact distance along it is unknown, rather than guessing a length
    /// or a position.
    /// </remarks>
    private void RenderTrack(BoardPresenter presenter)
    {
        if (_track is not { } track)
        {
            return;
        }

        Clear(track);

        if (presenter.StageLength is not { } length)
        {
            track.Visible = false;

            return;
        }

        track.Visible = true;

        // Which pip carries the token is the presenter's answer, not this file's: it is arithmetic
        // over the chapter's authored stage lengths, and arithmetic in a scene is arithmetic nothing
        // can test. Null lights no pip, which is the honest drawing of a position not known exactly.
        var token = presenter.StageTrackIndex;

        for (var node = 0; node < length; node++)
        {
            track.AddChild(new ColorRect
            {
                CustomMinimumSize = NodePipSize,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                Color = node == token ? TokenColour : UnvisitedNodeColour,
            });
        }
    }

    /// <summary>Empties a container now, rather than at the end of the frame.</summary>
    /// <remarks>
    /// 🔒 <c>QueueFree</c> alone defers removal, so a child freed and a child added in the same
    /// frame are both in the tree and both laid out until it ends. This screen redraws twice on one
    /// path when a command completes synchronously, so that is the ordinary case here rather than a
    /// rare one: detaching first is what keeps a rebuilt row from briefly drawing twice over.
    /// </remarks>
    private static void Clear(Node container)
    {
        foreach (var child in container.GetChildren())
        {
            container.RemoveChild(child);
            child.QueueFree();
        }
    }

    private void RenderFork(BoardPresenter presenter)
    {
        if (_forkPanel is not { } panel || _forkButtons is not { } buttons ||
            _forkTitleLabel is not { } title)
        {
            return;
        }

        Clear(buttons);

        if (presenter.Fork is not { } fork)
        {
            panel.Visible = false;

            return;
        }

        panel.Visible = true;
        title.Text = presenter.ForkTitle;

        foreach (var branch in fork.Branches)
        {
            var button = new Button
            {
                Text = presenter.BranchText(branch),
                Disabled = _busy,
                CustomMinimumSize = new Vector2(0, 200),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };

            // A control built in code inherits the engine's default face, which is caption-sized on
            // a canvas this wide — unreadable inside a button this tall, on the run's one real
            // navigation choice. The sibling screens set the same override for the same reason.
            button.AddThemeFontSizeOverride(FontSizeOverride, BranchFontSize);

            ButtonTextColours.ApplyTo(button, LiveColour, UnavailableColour);

            // Captured by value into the handler, so the index a press submits is the index the
            // button was drawn for even after the list is rebuilt beneath it.
            var index = branch.BranchIndex;

            button.Pressed += () => OnBranchPressed(index);

            buttons.AddChild(button);
        }
    }

    private void RenderPrompt(BoardPresenter presenter)
    {
        if (_promptPanel is not { } panel || _rolledLabel is not { } label ||
            _rolledValue is not { } value || _ringBar is not { } ring ||
            _rerollButton is not { } reroll)
        {
            return;
        }

        if (presenter.Prompt is not { } prompt)
        {
            panel.Visible = false;

            return;
        }

        panel.Visible = true;
        label.Text = presenter.RolledLabel;
        value.Text = FaceReadout(presenter);

        // A prompt that never lapses carries no countdown, so it draws no ring at all — a bar that
        // sat full forever would read as a timer that had stopped rather than as no timer.
        ring.Visible = prompt.Remaining is not null;
        ring.Value = prompt.RingFraction;

        reroll.Text = presenter.RerollText;
        reroll.Disabled = _busy;

        // 🔒 04 §3.1: the caption is what stops a bare "Reroll" beside a settled face reading as a
        // redo. Drawn on every prompt rather than only the first — a player taught the rule once and
        // then shown a bare control on every later roll has been taught the other thing by repetition.
        if (_rerollChangesNextRollLabel is { } explains)
        {
            explains.Text = presenter.RerollChangesNextRollText;
        }
    }

    private void BuildFaceList()
    {
        if (_diePanel is not { } panel || _faceList is not { } list ||
            _diePanelTitleLabel is not { } title || _facesUnavailableLabel is not { } unavailable)
        {
            return;
        }

        title.Text = panel.Title;
        unavailable.Text = panel.FacesUnavailableStatus;

        var rowScene = GD.Load<PackedScene>(DieFaceRowScenePath);

        if (rowScene is null)
        {
            // Load answers null rather than throwing when the resource is missing or its import
            // cannot be read, so an unnamed null reference is all a caller gets unless it says so.
            GD.PushError($"The die-face row could not be loaded from '{DieFaceRowScenePath}'.");

            return;
        }

        foreach (var face in panel.Faces)
        {
            var row = rowScene.Instantiate<VBoxContainer>();

            row.GetNode<Label>(RowNameLabelPath).Text = face.Name;
            row.GetNode<Label>(RowEffectLabelPath).Text = face.Effect;

            list.AddChild(row);
        }
    }

    private void RenderDiePanel()
    {
        if (_diePanel is not { } panel || _presenter is not { } presenter ||
            _lastFaceLabel is not { } label || _lastFaceValue is not { } value ||
            _closeButton is not { } close)
        {
            return;
        }

        label.Text = panel.LastFaceLabel;

        // The one thing about the player's own die this panel can honestly show: what the run has
        // actually been observed to roll. Before that there is nothing, and it says so.
        value.Text = presenter.LastRolledFaces.Count > 0
            ? FaceReadout(presenter)
            : panel.NoRollYetStatus;

        close.Text = presenter.DiePanelText;
    }

    private string StageReadout(BoardPresenter presenter) =>
        presenter is { StageNumber: { } stage, StageCount: { } count }
            ? $"{stage.ToString(CultureInfo.InvariantCulture)}{OverSeparator}" +
              $"{count.ToString(CultureInfo.InvariantCulture)}"
            : "";

    /// <remarks>
    /// Every face the last command reported, joined — a chain reports several, and showing only one
    /// would hide the roll that produced the movement the player just watched. A pip face reads as
    /// its number; every other face reads as its kind, because its magnitude is the rules layer's.
    /// </remarks>
    private static string FaceReadout(BoardPresenter presenter) =>
        string.Join(
            FaceJoin,
            presenter.LastRolledFaces.Select(face =>
                face.Kind == nameof(SlayIdleRepeat.Core.Content.Dice.DieFaceKind.Pip)
                    ? face.Value.ToString(CultureInfo.InvariantCulture)
                    : face.Kind));

    private void OnRollHoldStarted()
    {
        _heldFor = 0;
        _holdConsumed = false;
    }

    private void OnRollHoldEnded() => _heldFor = null;

    private void OnRollPressed()
    {
        // A press that has already opened the die panel is not also a roll. The engine raises
        // Pressed on release, so without this a player reading their die spends a turn doing it.
        if (_holdConsumed)
        {
            _holdConsumed = false;

            return;
        }

        _ = SubmitAsync(presenter => presenter.RollAsync(_lifetime));
    }

    private void OnRerollPressed() => _ = SubmitAsync(presenter => presenter.UseRerollAsync(_lifetime));

    private void OnResolvePressed() =>
        _ = SubmitAsync(presenter => presenter.ResolvePendingTileAsync(_lifetime));

    private void OnBranchPressed(int branchIndex) =>
        _ = SubmitAsync(presenter => presenter.ChooseForkAsync(branchIndex, _lifetime));

    private void OnDiePanelPressed() => ShowDiePanel();

    private void OnClosePressed()
    {
        if (_diePanelOverlay is not { } overlay)
        {
            return;
        }

        overlay.Visible = false;

        // The ring gets back exactly what the panel covered, rather than resuming already lapsed.
        _presenter?.ResumeRerollPrompt();

        Render();
    }

    private void ShowDiePanel()
    {
        if (_diePanelOverlay is not { } overlay)
        {
            return;
        }

        _presenter?.SuspendRerollPrompt();

        RenderDiePanel();

        overlay.Visible = true;
    }

    /// <remarks>
    /// 🔒 The design makes a tap anywhere else an acceptance of the roll, and the ground is where
    /// "anywhere else" lands. Without this the only way out of an open prompt is to spend a reroll
    /// charge — and with the no-timer accessibility setting on, where nothing lapses, the prompt
    /// would never close at all.
    /// </remarks>
    /// <param name="event">The input the ground received.</param>
    private void OnGroundInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { Pressed: true } and
            not InputEventScreenTouch { Pressed: true })
        {
            return;
        }

        if (_presenter?.AcceptRoll() == true)
        {
            Render();
        }
    }

    /// <remarks>
    /// Every control is taken out of use for the whole round trip and put back once, on one path.
    /// A second press landing mid-flight would submit a command against a run the first has already
    /// moved — and on a board that is a second roll, which is the one thing a turn may not be.
    /// </remarks>
    private async Task SubmitAsync(Func<BoardPresenter, Task<BoardSubmission>> submit)
    {
        if (_busy || _presenter is not { } presenter)
        {
            return;
        }

        _busy = true;
        Render();

        try
        {
            await submit(presenter);

            Report(presenter);
        }
        catch (Exception failure)
        {
            GD.PushError($"A board command failed: {failure}");
        }
        finally
        {
            _busy = false;
            Render();
        }
    }

    /// <summary>
    /// Prints, on one greppable line, what this screen resolved against the run and content the
    /// build actually shipped — including the destinations it stopped at.
    /// </summary>
    private void Report(BoardPresenter presenter)
    {
        GD.Print(
            $"{BoardMarker} stage={presenter.Stage} block={presenter.RollBlock} " +
            $"hp={presenter.CurrentHp}/{presenter.MaxHp} gold={presenter.Gold} " +
            $"position={presenter.Position} track={Describe(presenter.TrackIndex)} " +
            $"stage_no={Describe(presenter.StageNumber)}/{Describe(presenter.StageCount)} " +
            $"tile={presenter.PendingTile?.Kind.ToString(CultureInfo.InvariantCulture) ?? "none"} " +
            $"fork={presenter.Fork?.Branches.Count.ToString(CultureInfo.InvariantCulture) ?? "none"} " +
            $"faces=[{FaceReadout(presenter)}] rejection={Describe(presenter.RulesRejection)}");

        ReportUnbuiltDestination(presenter);
        OpenBattle(presenter);
        OpenDecision(presenter);
    }

    /// <remarks>
    /// Reported rather than navigated to. A finished run belongs to a screen a later row owns, and a
    /// run that reaches it stops here with the reason named in the log.
    /// </remarks>
    private static void ReportUnbuiltDestination(BoardPresenter presenter)
    {
        if (presenter.RollBlock == BoardRollBlock.RunEnded)
        {
            GD.PushError($"{BoardMarker} halted · {TheRunEndScreensAreNotBuiltHere}");
        }
    }

    /// <summary>Hands over to the replay of the fight the run is standing in, at most once per fight.</summary>
    /// <remarks>
    /// 🔒 The latch is what makes the return path terminate. This runs after every read and after
    /// every accepted command, and the replay's return path is itself a read — so without it a
    /// battle the replay could not close would send the player straight back into the replay, and
    /// round again, forever. A run that has left the battle phase clears the latch, because the next
    /// battle is a different battle.
    /// </remarks>
    private void OpenBattle(BoardPresenter presenter)
    {
        if (presenter.RollBlock != BoardRollBlock.BattleOpen)
        {
            _battleShown = false;

            return;
        }

        if (_battleShown)
        {
            GD.PushError($"{BoardMarker} halted · {TheBattleDidNotCloseWhenItsReplayEnded}");

            return;
        }

        if (!IsInstanceValid(this) || !IsInsideTree())
        {
            return;
        }

        if (_battle is not { } battle)
        {
            GD.PushError(
                "A battle is open and this screen has no way to build its replay. Only a screen " +
                "that already has a run may instantiate the board, and it must pass the battle " +
                "factory to Drive.");

            return;
        }

        // Latched on the handover having HAPPENED, not on having been attempted. A handover that
        // could not load its scene left the board on screen with the battle still open, and a latch
        // set anyway would answer the next read with the dead-end sentence for a replay nobody ever
        // watched.
        _battleShown = BattleHandover.Show(this, battle(), _lifetime);
    }

    /// <summary>Hands over to the screen the run's own state belongs on, at most once per state.</summary>
    /// <remarks>
    /// 🔒 The latch is what makes the return path terminate, exactly as the battle's does. Each of
    /// these screens hands back by reading the run and finding the state that opened it gone — so a
    /// run that comes back still holding it is one the screen could not finish, and without the latch
    /// the board would send the player straight back in, and round again, forever. A run that has
    /// left the state clears the latch, because the next shop is a different shop.
    /// </remarks>
    private void OpenDecision(BoardPresenter presenter)
    {
        if (DecisionFor(presenter) is not { } decision)
        {
            _decisionShown = null;

            return;
        }

        if (_decisionShown == decision)
        {
            GD.PushError($"{BoardMarker} halted · {TheDecisionDidNotCloseWhenItsScreenHandedBack}");

            return;
        }

        if (!IsInstanceValid(this) || !IsInsideTree())
        {
            return;
        }

        // Latched on the handover having HAPPENED, not on having been attempted — a handover that
        // could not load its scene left the board on screen with the decision still open, and a latch
        // set anyway would answer the next read with the dead-end sentence for a screen nobody saw.
        _decisionShown = HandOver(decision) ? decision : null;
    }

    /// <summary>Which screen, if any, the run's present state belongs on.</summary>
    /// <remarks>
    /// 🔴 The tile numbers are NOT restated here. Which integer is a shop and which is a shrine is a
    /// transcription of another assembly's internal enum, and each of those screens already owns its
    /// own copy and pins it with a case of its own — so this asks them rather than keeping a third
    /// copy that nothing would notice going stale.
    /// </remarks>
    private static RunDecision? DecisionFor(BoardPresenter presenter)
    {
        // 🔒 Asked FIRST, and ahead of the block, because a run that is over outranks anything still
        // pending on it. A hero at zero hit points leaves the fight's tile pending — a loss does not
        // clear it, which is what lets 02 §6's revive restart the same fight — so a board that read the
        // block first would send a dead run to the tile's own screen and offer it a shop.
        if (presenter.RunAwaitingResults)
        {
            return RunDecision.RunEnd;
        }

        return presenter.RollBlock switch
        {
            BoardRollBlock.DraftOpen => RunDecision.PerkDraft,
            BoardRollBlock.TilePending => presenter.PendingTile?.Kind switch
            {
                ShopPresenter.ShopTileKind => RunDecision.Shop,
                CampfirePresenter.CampfireTileKind or CampfirePresenter.ShrineTileKind =>
                    RunDecision.Campfire,
                _ => null,
            },
            _ => null,
        };
    }

    /// <summary>Builds the screen for one decision and puts it in front of this one.</summary>
    private bool HandOver(RunDecision decision)
    {
        switch (decision)
        {
            case RunDecision.PerkDraft when _perkDraft is { } draft:
                return PerkDraftHandover.Show(this, draft(), _lifetime);

            case RunDecision.Shop when _shop is { } shop:
                return ShopHandover.Show(this, shop(), _lifetime);

            case RunDecision.Campfire when _campfire is { } campfire:
                return CampfireHandover.Show(this, campfire(), _lifetime);

            case RunDecision.RunEnd when _runEnd is { } runEnd:
                return RunEndHandover.Show(this, runEnd(), _lifetime);

            default:
                GD.PushError(
                    $"The run reached {decision} and this screen has no way to build it. Only a " +
                    "screen that already has a run may instantiate the board, and it must pass the " +
                    "composed screen to Drive.");

                return false;
        }
    }

    private static string Describe<T>(T? value) where T : struct =>
        value?.ToString() ?? "none";

    /// <summary>The screens a run's own state sends it to from here.</summary>
    /// <remarks>
    /// Named rather than tested inline, so the latch that stops a returned screen being re-entered
    /// has one value to compare — and so a shrine and a campfire, which are two tile kinds on ONE
    /// screen, count as one destination rather than two the run could be bounced between.
    /// </remarks>
    private enum RunDecision
    {
        /// <summary>S07, the perk draft a won fight leaves open.</summary>
        PerkDraft = 1,

        /// <summary>S08, the shop tile.</summary>
        Shop = 2,

        /// <summary>S11, the campfire and the shrine — one screen with two arms.</summary>
        Campfire = 3,

        /// <summary>
        /// S13 and S14, the death offer and the reward tally — one screen, because <c>02</c> §6 makes
        /// them one moment.
        /// </summary>
        RunEnd = 4,
    }
}
