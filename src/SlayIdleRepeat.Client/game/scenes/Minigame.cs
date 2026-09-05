using Godot;
using SlayIdleRepeat.Client.Game.Presenters;
using SlayIdleRepeat.Core.Rules.Board;

namespace SlayIdleRepeat.Client.Game.Scenes;

/// <summary>
/// The minigame the tile offers: a driving adapter over <see cref="MinigamePresenter"/>.
/// </summary>
/// <remarks>
/// <para>
/// It renders what the presenter says and forwards one press per control. No rules, no ports, no
/// adapters, and no decision about which game this is, what may be pressed or what was won — all of
/// that is the presenter's answer to the run's own row, and this half only draws it. Which set of
/// controls the arm needs is asked as <see cref="MinigamePresenter.Controls"/> rather than worked out
/// from a minigame id, so the rules layer's vocabulary never reaches a <c>Node</c>.
/// </para>
/// <para>
/// 🔒 <b>The cursor is moved in <c>_Process</c> and NOTHING else is.</b> Every node is resolved once
/// in <c>_Ready</c>, the per-frame path allocates nothing and looks nothing up by name, and the
/// process callback is switched off outright whenever the bar is not sweeping — which is every state
/// but one, and both of the other two games.
/// </para>
/// <para>
/// 🔴 <b>The last strike submits.</b> The timing bar's tier IS its hit count, so a game played out is
/// a game already decided — there is nothing left for the player to choose and no authored caption
/// for a control that would ask them to confirm it. Submitting on the final strike is what keeps the
/// screen from ending in a settled game with nothing pressable but the way back, which would leave
/// the tile pending behind them.
/// </para>
/// <para>
/// 🔒 <b>Three chests, one command.</b> The outcome is the server's draw and which chest was opened
/// decides nothing — but a single button would be a screen honest about the rules and dishonest about
/// the game, so the pick is offered as it reads and answered as it is.
/// </para>
/// <para>
/// 🔴 <b>A read that could not answer is <em>offline, read-only</em> rather than terminal</b> — the
/// pill and the toast that belong to it are drawn globally by <c>ConnectionOverlay</c>, and no back
/// caption is invented here. What this screen adds is that the one control it has stays live and
/// asks again, because a state with a sentence and nothing to press is a run that can only be left
/// by killing the application. <see cref="MinigamePresenter.Exit"/> holds which states leave and
/// which re-read, and why.
/// </para>
/// </remarks>
public partial class Minigame : Node3D
{
    /// <summary>Where this scene lives, for the screen that instantiates it.</summary>
    public const string ScenePath = "res://game/scenes/Minigame.tscn";

    /// <summary>The one line a headless run's screen state is read off.</summary>
    private const string MinigameMarker = "SIR_MINIGAME_READY";

    /// <summary>And where one row of the reward ladder lives, instantiated once per tier.</summary>
    private const string TierRowScenePath = "res://game/scenes/MinigameTierRow.tscn";

    private const string RowCaptionLabelPath = "CaptionLabel";
    private const string RowRewardLabelPath = "RewardLabel";

    private const string SafeAreaPath = "%SafeArea";
    private const string TitleLabelPath = "%TitleLabel";
    private const string ArmLabelPath = "%ArmLabel";
    private const string RuleLabelPath = "%RuleLabel";
    private const string RewardsHeadingPath = "%RewardsHeading";
    private const string RewardRowsPath = "%RewardRows";
    private const string GuaranteeLabelPath = "%GuaranteeLabel";
    private const string TimingBarPanelPath = "%TimingBarPanel";
    private const string HitWindowPath = "%HitWindow";
    private const string CursorPath = "%Cursor";
    private const string HitsLabelPath = "%HitsLabel";
    private const string ChestPanelPath = "%ChestPanel";
    private const string ChestOnePath = "%ChestOne";
    private const string ChestTwoPath = "%ChestTwo";
    private const string ChestThreePath = "%ChestThree";
    private const string DiceDuelPanelPath = "%DiceDuelPanel";
    private const string DiceLabelPath = "%DiceLabel";
    private const string ResultPanelPath = "%ResultPanel";
    private const string ResultHeadingPath = "%ResultHeading";
    private const string ResultLabelPath = "%ResultLabel";
    private const string StatusLabelPath = "%StatusLabel";
    private const string RejectionLabelPath = "%RejectionLabel";
    private const string StrikeButtonPath = "%StrikeButton";
    private const string StepButtonPath = "%StepButton";
    private const string RollButtonPath = "%RollButton";
    private const string ContinueButtonPath = "%ContinueButton";

    /// <summary>What separates a caption from the count beside it.</summary>
    private const string CaptionAndCount = "  ";

    /// <summary>What separates the hit count from the strikes still to take.</summary>
    private const string BetweenCounts = "      ";

    /// <summary>The centre of the bar, which is where the scoring band is drawn from.</summary>
    private const float BarCentre = 0.5f;

    /// <summary>A control with something to do.</summary>
    private static readonly Color LiveColour = new(0.93f, 0.93f, 0.96f);

    /// <summary>And one with nothing to do — the same quiet grey every caption is drawn in.</summary>
    private static readonly Color UnavailableColour = new(0.66f, 0.67f, 0.73f);

    private MinigamePresenter? _presenter;
    private Board? _board;
    private CancellationToken _lifetime;

    private Label? _titleLabel;
    private Label? _armLabel;
    private Label? _ruleLabel;
    private Label? _rewardsHeading;
    private VBoxContainer? _rewardRows;
    private Label? _guaranteeLabel;
    private PanelContainer? _timingBarPanel;
    private Control? _hitWindow;
    private Control? _cursor;
    private Label? _hitsLabel;
    private VBoxContainer? _chestPanel;
    private Button? _chestOne;
    private Button? _chestTwo;
    private Button? _chestThree;
    private PanelContainer? _diceDuelPanel;
    private Label? _diceLabel;
    private PanelContainer? _resultPanel;
    private Label? _resultHeading;
    private Label? _resultLabel;
    private Label? _statusLabel;
    private Label? _rejectionLabel;
    private Button? _strikeButton;
    private Button? _stepButton;
    private Button? _rollButton;
    private Button? _continueButton;

    /// <summary>The ladder the rows were last built from, by reference.</summary>
    /// <remarks>
    /// 🔒 What stops the ladder being torn down and rebuilt on every redraw, which would free and
    /// re-instantiate every row twice per press. The presenter hands back a fresh list exactly when
    /// the game it is showing has actually changed.
    /// </remarks>
    private IReadOnlyList<MinigameTierRow>? _drawnLadderFrom;

    /// <summary>Whether a submission is in flight, so a second press cannot start another.</summary>
    private bool _busy;

    /// <summary>Takes the presenter, the board to hand back to, and the app's shutdown token.</summary>
    /// <param name="presenter">Drives this screen.</param>
    /// <param name="board">The board the tile was entered from, returned to once it is resolved.</param>
    /// <param name="lifetime">Cancelled when the application shuts down.</param>
    /// <exception cref="ArgumentNullException">The presenter or the board is null.</exception>
    public void Drive(MinigamePresenter presenter, Board board, CancellationToken lifetime)
    {
        ArgumentNullException.ThrowIfNull(presenter);
        ArgumentNullException.ThrowIfNull(board);

        _presenter = presenter;
        _board = board;
        _lifetime = lifetime;
    }

    /// <inheritdoc/>
    public override void _Ready()
    {
        // Resolved once. A scene-unique lookup is a string search of the owner's table each time it
        // is asked, and the cursor is drawn every frame.
        _titleLabel = GetNode<Label>(TitleLabelPath);
        _armLabel = GetNode<Label>(ArmLabelPath);
        _ruleLabel = GetNode<Label>(RuleLabelPath);
        _rewardsHeading = GetNode<Label>(RewardsHeadingPath);
        _rewardRows = GetNode<VBoxContainer>(RewardRowsPath);
        _guaranteeLabel = GetNode<Label>(GuaranteeLabelPath);
        _timingBarPanel = GetNode<PanelContainer>(TimingBarPanelPath);
        _hitWindow = GetNode<Control>(HitWindowPath);
        _cursor = GetNode<Control>(CursorPath);
        _hitsLabel = GetNode<Label>(HitsLabelPath);
        _chestPanel = GetNode<VBoxContainer>(ChestPanelPath);
        _chestOne = GetNode<Button>(ChestOnePath);
        _chestTwo = GetNode<Button>(ChestTwoPath);
        _chestThree = GetNode<Button>(ChestThreePath);
        _diceDuelPanel = GetNode<PanelContainer>(DiceDuelPanelPath);
        _diceLabel = GetNode<Label>(DiceLabelPath);
        _resultPanel = GetNode<PanelContainer>(ResultPanelPath);
        _resultHeading = GetNode<Label>(ResultHeadingPath);
        _resultLabel = GetNode<Label>(ResultLabelPath);
        _statusLabel = GetNode<Label>(StatusLabelPath);
        _rejectionLabel = GetNode<Label>(RejectionLabelPath);
        _strikeButton = GetNode<Button>(StrikeButtonPath);
        _stepButton = GetNode<Button>(StepButtonPath);
        _rollButton = GetNode<Button>(RollButtonPath);
        _continueButton = GetNode<Button>(ContinueButtonPath);

        // Painted because a Button draws its text by draw mode, and the disabled mode these controls
        // spend part of their life in has an engine default of half-transparent grey that no
        // override of font_color reaches.
        ButtonTextColours.ApplyTo(_strikeButton, LiveColour, UnavailableColour);
        ButtonTextColours.ApplyTo(_stepButton, LiveColour, UnavailableColour);
        ButtonTextColours.ApplyTo(_rollButton, LiveColour, UnavailableColour);
        ButtonTextColours.ApplyTo(_continueButton, LiveColour, UnavailableColour);
        ButtonTextColours.ApplyTo(_chestOne, LiveColour, UnavailableColour);
        ButtonTextColours.ApplyTo(_chestTwo, LiveColour, UnavailableColour);
        ButtonTextColours.ApplyTo(_chestThree, LiveColour, UnavailableColour);

        _strikeButton.Pressed += OnStrikePressed;
        _stepButton.Pressed += OnStepPressed;
        _rollButton.Pressed += OnSubmitPressed;
        _continueButton.Pressed += OnContinuePressed;
        _chestOne.Pressed += OnSubmitPressed;
        _chestTwo.Pressed += OnSubmitPressed;
        _chestThree.Pressed += OnSubmitPressed;

        // Claims the viewport for this screen's own camera and puts its overlay up. Every screen
        // does this on the way in, because every handover in this build leaves the outgoing screen
        // in the tree — hidden, or freed only on the frame after.
        ScreenStage.Show(this);

        SafeAreaInsets.ApplyTo(
            GetNode<MarginContainer>(SafeAreaPath), GetViewport().GetVisibleRect().Size);

        Render();

        _ = StartAsync();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// 🔒 Every control connected in <see cref="_Ready"/> is detached here. The reward rows are
    /// instantiated from their own scene and die with the container they are cleared out of, and
    /// nothing on one is connected to anything.
    /// </remarks>
    public override void _ExitTree()
    {
        // Asked whether each is still there rather than assumed: this screen can leave the tree
        // before _Ready ever resolved its nodes, and a handover that failed to load is exactly that
        // case.
        Detach(_strikeButton, OnStrikePressed);
        Detach(_stepButton, OnStepPressed);
        Detach(_rollButton, OnSubmitPressed);
        Detach(_continueButton, OnContinuePressed);
        Detach(_chestOne, OnSubmitPressed);
        Detach(_chestTwo, OnSubmitPressed);
        Detach(_chestThree, OnSubmitPressed);

        // Nothing is left running behind a screen on its way out.
        SetProcess(false);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// 🔴 The one per-frame path in this screen, and it is kept to two writes. Nothing is allocated,
    /// nothing is looked up by name, and no text is composed — the hit counter is written from
    /// <see cref="Render"/> instead, because it only changes on a press. The callback is switched off
    /// entirely whenever the bar is not sweeping, which is every state but one.
    /// </remarks>
    public override void _Process(double delta)
    {
        if (_presenter is not { } presenter)
        {
            return;
        }

        presenter.Advance(delta);

        MoveCursor(presenter);
    }

    /// <remarks>
    /// Nothing awaits this task, so its exceptions have nowhere to surface: the whole body is
    /// guarded, or a continuation that threw would leave a tile that silently stopped moving.
    /// </remarks>
    private async Task StartAsync()
    {
        try
        {
            if (_presenter is not { } presenter)
            {
                GD.PushError(
                    "The minigame screen entered the tree with no presenter. Only a screen that " +
                    "already has a run may instantiate it, and it must call Drive before adding it.");

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
            GD.PushError($"The minigame screen stopped unexpectedly: {failure}");
        }
    }

    /// <summary>Writes the presenter's state into the scene, if the scene is still there to write into.</summary>
    private void Render()
    {
        var presenter = _presenter;

        // Validity before tree membership: asking a freed node whether it is inside the tree is
        // itself the crash, and a shutdown during a slow command is the ordinary case on a handset.
        if (presenter is null || !IsInstanceValid(this) || !IsInsideTree() ||
            _titleLabel is null || _armLabel is null || _ruleLabel is null ||
            _rewardsHeading is null || _rewardRows is null || _guaranteeLabel is null ||
            _timingBarPanel is null || _hitWindow is null || _cursor is null ||
            _hitsLabel is null || _chestPanel is null || _diceDuelPanel is null ||
            _diceLabel is null || _resultPanel is null || _resultHeading is null ||
            _resultLabel is null || _statusLabel is null || _rejectionLabel is null ||
            _strikeButton is null || _stepButton is null || _rollButton is null ||
            _continueButton is null)
        {
            return;
        }

        _titleLabel.Text = presenter.Title;
        _armLabel.Text = presenter.ArmName;
        _ruleLabel.Text = presenter.RuleText;
        _rewardsHeading.Text = presenter.RewardsLabel;

        RenderLadder(presenter, _rewardRows);

        _guaranteeLabel.Text = presenter.GuaranteeText;
        _guaranteeLabel.Visible = _guaranteeLabel.Text.Length > 0;

        var playing = presenter.Stage == MinigameStage.Playing;
        var controls = presenter.Controls;

        _timingBarPanel.Visible = playing && controls == MinigameControls.Timing;
        _chestPanel.Visible = playing && controls == MinigameControls.Chests;
        _diceDuelPanel.Visible = playing && controls == MinigameControls.Dice;

        _diceLabel.Text = presenter.RuleText;

        RenderTimingBar(presenter);

        // 🔴 One answer for every control that can send the command, and it is the PRESENTER's rather
        // than this screen's. A timing bar played out to its last strike is still being played until
        // a submission is accepted, and a press on it is that submission again — so a control taken
        // away here because the game was finished takes away the retry as well, and a submission that
        // faulted then leaves a screen with a sentence on it and nothing at all to press.
        var offered = !_busy && presenter.PlayOffered;

        // All three carry the same caption and the same answer, because the outcome is the server's
        // draw: which chest was opened decides nothing at all.
        DrawChest(_chestOne, presenter.PickChestText, offered);
        DrawChest(_chestTwo, presenter.PickChestText, offered);
        DrawChest(_chestThree, presenter.PickChestText, offered);

        _strikeButton.Text = presenter.StrikeText;
        _strikeButton.Visible = playing && controls == MinigameControls.Timing;
        _strikeButton.Disabled = !offered;

        // Drawn only for a player who asked for nothing to move: with the sweep off, the step is the
        // whole of how they aim, and with it on the cursor is already where the clock says. Unlike
        // the strike beside it this one really is spent once the bar is played out — a step sends
        // nothing, so on a finished game there is nothing for it to do.
        _stepButton.Text = presenter.StepText;
        _stepButton.Visible = _strikeButton.Visible && presenter.ReducedMotion;
        _stepButton.Disabled = !offered || presenter.Finished;

        _rollButton.Text = presenter.RollText;
        _rollButton.Visible = playing && controls == MinigameControls.Dice;
        _rollButton.Disabled = !offered;

        _continueButton.Text = presenter.ContinueText;

        // 🔴 Live in every state this screen can settle in except the one where a game is still
        // being played. Gated on the tile having cleared alone it was drawn out of use on four
        // states that draw no game either — a faulted read, a missing run, a run standing elsewhere,
        // a tile already played and a content set that cannot describe the game — which left the
        // player mid-run on a screen with a sentence and nothing to press.
        _continueButton.Disabled = _busy || presenter.Exit == MinigameExit.Nowhere;

        _resultHeading.Text = presenter.ResultLabel;
        _resultLabel.Text = presenter.ResolvedOutcome.Length > 0
            ? presenter.OutcomeText(presenter.ResolvedOutcome)
            : "";
        _resultPanel.Visible = _resultLabel.Text.Length > 0;

        // Hidden rather than blanked once they have nothing to say: an empty label still claims a
        // full line of height, so a blank one is a sentence a player can see room for and cannot
        // read. They are two lines because they answer two different questions — what state the
        // screen is in, and what the game said about the last command.
        _statusLabel.Text = presenter.StatusText;
        _statusLabel.Visible = _statusLabel.Text.Length > 0;

        _rejectionLabel.Text = presenter.RejectionText;
        _rejectionLabel.Visible = _rejectionLabel.Text.Length > 0;

        // 🔒 The per-frame callback is switched OFF unless there is a cursor sweeping. Every other
        // state — the two server-rolled arms, a reduced-motion bar, a settled game — has nothing
        // moving, and a screen that kept processing would be paying a frame's work to write the
        // same anchor back.
        SetProcess(
            playing && controls == MinigameControls.Timing && !presenter.ReducedMotion &&
            !presenter.Finished);
    }

    /// <summary>Writes the bar's band, its marker and its counter from the presenter's numbers.</summary>
    /// <remarks>
    /// 🔒 The band is drawn from the same half-width the strike is judged against, so a retuned
    /// window moves the target and the scoring together — a picture that disagreed with the rule
    /// would have the player aiming at a band that scores nothing.
    /// </remarks>
    private void RenderTimingBar(MinigamePresenter presenter)
    {
        if (_hitWindow is not { } window || _hitsLabel is not { } hits)
        {
            return;
        }

        var halfWidth = (float)presenter.HitWindowHalfWidth;

        window.AnchorLeft = BarCentre - halfWidth;
        window.AnchorRight = BarCentre + halfWidth;

        MoveCursor(presenter);

        hits.Text =
            presenter.HitsLabel + CaptionAndCount + presenter.Hits + BetweenCounts +
            presenter.StrikesLeftLabel + CaptionAndCount + presenter.StrikesLeft;
    }

    /// <summary>Puts the marker where the game says it stands.</summary>
    private void MoveCursor(MinigamePresenter presenter)
    {
        if (_cursor is not { } cursor)
        {
            return;
        }

        var position = (float)presenter.Cursor;

        cursor.AnchorLeft = position;
        cursor.AnchorRight = position;
    }

    /// <summary>Draws the reward ladder, rebuilding it only when the game itself has changed.</summary>
    private void RenderLadder(MinigamePresenter presenter, VBoxContainer rows)
    {
        if (ReferenceEquals(_drawnLadderFrom, presenter.Rows))
        {
            return;
        }

        _drawnLadderFrom = presenter.Rows;

        Clear(rows);

        if (presenter.Rows.Count == 0)
        {
            return;
        }

        var rowScene = GD.Load<PackedScene>(TierRowScenePath);

        if (rowScene is null)
        {
            // Load answers null rather than throwing when the resource is missing or its import
            // cannot be read, so an unnamed null reference is all a caller gets unless it says so.
            GD.PushError($"The minigame tier row could not be loaded from '{TierRowScenePath}'.");

            return;
        }

        foreach (var tier in presenter.Rows)
        {
            var row = rowScene.Instantiate<HBoxContainer>();

            row.GetNode<Label>(RowCaptionLabelPath).Text = presenter.OutcomeText(tier.Outcome);
            row.GetNode<Label>(RowRewardLabelPath).Text = presenter.RewardText(tier);

            rows.AddChild(row);
        }
    }

    /// <remarks>
    /// 🔴 The last strike SUBMITS. The tier is the hit count, so a played-out bar has nothing left
    /// to decide and no control that could honestly ask about it — and a screen that stopped here
    /// would leave a finished game with the tile still pending behind it.
    /// </remarks>
    private void OnStrikePressed()
    {
        if (_busy || _presenter is not { } presenter)
        {
            return;
        }

        presenter.Strike();

        if (presenter.Finished)
        {
            OnSubmitPressed();

            return;
        }

        Render();
    }

    private void OnStepPressed()
    {
        if (_busy)
        {
            return;
        }

        _presenter?.Step();

        Render();
    }

    private void OnSubmitPressed() => _ = SubmitAsync();

    /// <remarks>
    /// Every control is taken out of use for the whole round trip and put back once, on one path. A
    /// second press landing mid-flight would submit a second time for one tile — refused by the
    /// rules layer as a duplicate at the same position, which reaches the player as an illegal state
    /// on a screen that has just paid them.
    /// </remarks>
    private async Task SubmitAsync()
    {
        if (_busy || _presenter is not { } presenter)
        {
            return;
        }

        // Latched before the await, not after it: taken afterwards, a second press arriving while
        // the first is in flight finds it unset and submits again.
        _busy = true;
        Render();

        try
        {
            await presenter.SubmitAsync(_lifetime);

            Report(presenter);
        }
        catch (Exception failure)
        {
            GD.PushError($"A minigame submission failed: {failure}");
        }
        finally
        {
            _busy = false;
            Render();
        }
    }

    /// <remarks>
    /// <para>
    /// 🔒 The press does what the presenter says this state's way out is, and the presenter states
    /// at length why each state has the one it has. Ordinarily that is handing back once the tile
    /// has cleared: the board's decision latch logs "halted" if this same decision re-opens with the
    /// tile still pending, so a screen that returned mid-game would be sent straight back here.
    /// </para>
    /// <para>
    /// 🔴 The other arm is the read that never answered, which is asked again rather than handed
    /// back — this screen would be handing the board a tile it has already latched, and the board's
    /// own control cannot resolve a minigame tile either.
    /// </para>
    /// </remarks>
    private void OnContinuePressed()
    {
        switch (_presenter?.Exit)
        {
            case MinigameExit.ToTheBoard:
                LeaveForTheBoard();
                break;

            case MinigameExit.ReadAgain:
                _ = ReadAgainAsync();
                break;

            default:
                break;
        }
    }

    /// <summary>Reads the run again, for a screen whose first read never came back.</summary>
    /// <remarks>
    /// Nothing awaits this task, so the whole body is guarded — and it takes the same latch a
    /// submission does, so a second press while the read is out cannot start a second one.
    /// </remarks>
    private async Task ReadAgainAsync()
    {
        if (_busy || _presenter is not { } presenter)
        {
            return;
        }

        _busy = true;

        // Drawn before the await as well as after it: taking the control out of use is the only
        // thing on screen that says the press landed at all, because the sentence above it cannot
        // change until the read answers.
        Render();

        try
        {
            await presenter.StartAsync(_lifetime);

            Report(presenter);
        }
        catch (Exception failure)
        {
            GD.PushError($"A minigame read failed: {failure}");
        }
        finally
        {
            _busy = false;
            Render();
        }
    }

    /// <summary>Hands back to the board, for a screen that has nothing left to do here.</summary>
    private void LeaveForTheBoard()
    {
        if (_presenter is not { Exit: MinigameExit.ToTheBoard } || _board is not { } board ||
            !IsInstanceValid(this) || !IsInsideTree())
        {
            return;
        }

        MinigameHandover.Return(this, board);
    }

    /// <summary>Empties a container now, rather than at the end of the frame.</summary>
    /// <remarks>
    /// 🔒 <c>QueueFree</c> alone defers removal, so a child freed and a child added in the same frame
    /// are both in the tree and both laid out until it ends. Detaching first is what keeps a rebuilt
    /// ladder from briefly drawing two sets of rows over each other.
    /// </remarks>
    private static void Clear(Node container)
    {
        foreach (var child in container.GetChildren())
        {
            container.RemoveChild(child);
            child.QueueFree();
        }
    }

    /// <summary>Writes one chest's caption and whether it may be opened right now.</summary>
    /// <param name="chest">The control, or null when this screen left the tree before it resolved.</param>
    /// <param name="caption">What it reads. The same on all three, because they do the same thing.</param>
    /// <param name="offered">Whether the presenter says a submission may still be sent.</param>
    private static void DrawChest(Button? chest, string caption, bool offered)
    {
        if (chest is not { } control)
        {
            return;
        }

        control.Text = caption;
        control.Disabled = !offered;
    }

    /// <summary>Takes one press handler back off a control, if the control is still there.</summary>
    private static void Detach(Button? button, Action pressed)
    {
        if (button is { } control && IsInstanceValid(control))
        {
            control.Pressed -= pressed;
        }
    }

    /// <summary>
    /// Prints, on one greppable line, which stage this screen settled on and what it drew against the
    /// run and content the build actually shipped.
    /// </summary>
    private static void Report(MinigamePresenter presenter) =>
        GD.Print(
            $"{MinigameMarker} stage={presenter.Stage} arm={presenter.MinigameId} " +
            $"server_rolled={presenter.IsServerRolled} rows={presenter.Rows.Count} " +
            $"hits={presenter.Hits} strikes_left={presenter.StrikesLeft} " +
            $"tier={presenter.ResolvedTier} " +
            $"outcome={(presenter.ResolvedOutcome.Length > 0 ? presenter.ResolvedOutcome : "none")} " +
            $"can_leave={presenter.CanLeave} host_faulted={presenter.HostFaulted} " +
            $"rejection={presenter.RulesRejection?.ToString() ?? "none"}");
}
