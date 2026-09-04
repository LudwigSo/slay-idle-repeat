using Godot;
using SlayIdleRepeat.Client.Game.Presenters;

namespace SlayIdleRepeat.Client.Game.Scenes;

/// <summary>
/// The event card the tile drew: a driving adapter over <see cref="EventPresenter"/>.
/// </summary>
/// <remarks>
/// <para>
/// It renders what the presenter says and forwards one press per option plus the continue. No rules,
/// no ports, no adapters, and no decision about what may be pressed or what a choice paid — all of
/// that is the presenter's answer to the run's own row, and this half only draws it.
/// </para>
/// <para>
/// 🔒 <b>Named <c>EventScreen</c> rather than <c>Event</c>.</b> A type called <c>Event</c> beside
/// <c>Board.RunDecision.Event</c> and the <c>event</c> keyword would read as three different things
/// spelled one way.
/// </para>
/// <para>
/// 🔒 <b>The card's prose is authored English and is drawn verbatim.</b> Only this screen's own
/// chrome comes out of the string catalogue — <see cref="EventPresenter"/> states why at length. So a
/// German player reads the chrome in German and the card in English, and that is a content pass
/// nobody has done rather than a bug in this file.
/// </para>
/// <para>
/// 🔴 <b>Most cards will report that nothing happened, and the panel says so in words.</b> Most
/// authored outcomes cannot be applied by this build, so a cleared tile with an empty result panel
/// is the ordinary case — and left silent it reads as a screen that failed rather than as a card
/// that did nothing.
/// </para>
/// <para>
/// 🔒 <b>An unaffordable option is drawn out of use with its own sentence, not left pressable.</b>
/// <c>EVENT_CHOOSE</c> refuses it with a wire value four other things share, so a round trip would
/// replace the sentence naming the price with one naming nothing.
/// </para>
/// <para>
/// ⚠️ <b>The screen's chrome names <c>SlayTheme.tres</c>; the card's own prose does not.</b> The
/// title and the continue action are the theme's <c>HudTitle</c> and <c>PrimaryButton</c>, and the
/// result panel is its <c>HudTile</c>, because the theme carries those exactly. The card's title,
/// body, status and rejection sizes stay per-node overrides: the theme is a HUD vocabulary, it holds
/// no entry for a body of authored prose, and a variation invented here that no other screen uses
/// would be worse than the override it replaced.
/// </para>
/// <para>
/// 🔴 <b>A read that could not answer has nowhere to send the player</b>, exactly as on the campfire:
/// that state is <em>offline, read-only</em> rather than terminal, the pill and the toast that belong
/// to it are drawn globally by <c>ConnectionOverlay</c>, and no back caption is invented here.
/// </para>
/// </remarks>
public partial class EventScreen : Node3D
{
    /// <summary>Where this scene lives, for the screen that instantiates it.</summary>
    public const string ScenePath = "res://game/scenes/EventScreen.tscn";

    /// <summary>The one line a headless run's screen state is read off.</summary>
    private const string EventMarker = "SIR_EVENT_READY";

    /// <summary>Where the per-option card lives, instantiated once per option the card offers.</summary>
    private const string OptionCardScenePath = "res://game/scenes/EventOptionCard.tscn";

    /// <summary>And where one line of the result panel lives, instantiated once per movement.</summary>
    /// <remarks>
    /// 🔒 A scene rather than a <c>Label</c> built here. A row is a caption and a number that have to
    /// line up down a column and be drawn in two different weights, which one label holding both and
    /// two spaces between them cannot do — and every size in it is then written where the rest of
    /// this screen's sizes are written.
    /// </remarks>
    private const string ResultRowScenePath = "res://game/scenes/EventResultRow.tscn";

    private const string OptionLabelPath = "Body/Column/OptionLabel";
    private const string OptionCostLabelPath = "Body/Column/CostLabel";
    private const string OptionBlockLabelPath = "Body/Column/BlockLabel";
    private const string OptionPressButtonPath = "PressButton";

    private const string RowCaptionLabelPath = "CaptionLabel";
    private const string RowValueLabelPath = "ValueLabel";

    private const string SafeAreaPath = "%SafeArea";
    private const string TitleLabelPath = "%TitleLabel";
    private const string CardTitlePath = "%CardTitle";
    private const string CardBodyPath = "%CardBody";
    private const string OptionColumnPath = "%OptionColumn";
    private const string ResultPanelPath = "%ResultPanel";
    private const string ResultHeadingPath = "%ResultHeading";
    private const string RunRowsPath = "%RunRows";
    private const string WalletHeadingPath = "%WalletHeading";
    private const string WalletRowsPath = "%WalletRows";
    private const string StatusLabelPath = "%StatusLabel";
    private const string RejectionLabelPath = "%RejectionLabel";
    private const string ContinueButtonPath = "%ContinueButton";

    /// <summary>The panel entry an option card's whole face — fill, outline, corners, shadow — takes.</summary>
    private const string PanelStyleOverride = "panel";

    /// <summary>
    /// How long a number has to be held before its exact value replaces its shortened one.
    /// </summary>
    /// <remarks>
    /// The same length <c>PerkDraft</c> and <c>Home</c> read a hold at, and for the same reason: long
    /// enough that a tap is never read as a hold, short enough that a player who wants the exact
    /// figure is not made to wait for it.
    /// </remarks>
    private const double LongPressSeconds = 0.4;

    /// <summary>What separates a price's caption from the price itself.</summary>
    private const string RowCaptionAndValue = "  ";

    /// <summary>
    /// The outline an option that cannot be taken is drawn with, against eight for one that can.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>The second channel, so "cannot be taken" is never read off colour alone.</b> An
    /// unaffordable card is thinner in the frame and flat on the page as well as darker in the face,
    /// and it carries the sentence naming the price in words — frame weight, depth and wording, none
    /// of which a colour-blind player has to separate by hue. Colour alone would be exactly the
    /// signalling this game's accessibility rule refuses.
    /// </remarks>
    private const int UnavailableOutlineWidth = 4;

    /// <summary>And what its lift off the page is taken down to — nothing.</summary>
    private const int UnavailableShadowSize = 0;

    /// <summary>A control with something to do.</summary>
    private static readonly Color LiveColour = new(0.93f, 0.93f, 0.96f);

    /// <summary>And one with nothing to do — the same quiet grey every caption is drawn in.</summary>
    private static readonly Color UnavailableColour = new(0.66f, 0.67f, 0.73f);

    /// <summary>What an option that cannot be taken has its fill drawn down to.</summary>
    /// <remarks>
    /// 🔒 The FACE recedes, never the text — <c>Campfire.cs</c> states the measurement: an
    /// unaffordable option carries the only sentence naming the price the player cannot meet, and a
    /// translucent veil over the card would spend it below a readable contrast.
    /// </remarks>
    private static readonly Color UnavailableFaceColour = new(0.13f, 0.13f, 0.17f);

    /// <summary>The outline that goes with it, which is what still reads the card as a card.</summary>
    private static readonly Color UnavailableOutlineColour = new(0.26f, 0.27f, 0.33f);

    private EventPresenter? _presenter;
    private Board? _board;
    private CancellationToken _lifetime;

    private Label? _titleLabel;
    private Label? _cardTitle;
    private Label? _cardBody;
    private VBoxContainer? _optionColumn;
    private PanelContainer? _resultPanel;
    private Label? _resultHeading;
    private VBoxContainer? _runRows;
    private Label? _walletHeading;
    private VBoxContainer? _walletRows;
    private Label? _statusLabel;
    private Label? _rejectionLabel;
    private Button? _continueButton;

    /// <summary>
    /// Each drawn option: its press, the card the press sits on, and whether it can be taken at all.
    /// The card is held as well as the press because both are driven by the same answer — the press
    /// stops accepting input and the card's face recedes to say so.
    /// </summary>
    private readonly List<(Button Press, PanelContainer Card, bool Available)> _optionButtons = [];

    /// <summary>The option list the column was last built from, by reference.</summary>
    /// <remarks>
    /// 🔒 What stops the column being torn down and rebuilt on every redraw, which would free and
    /// re-instantiate every card twice per press. The presenter hands back a fresh list exactly when
    /// the card it is showing has actually changed.
    /// </remarks>
    private IReadOnlyList<EventOptionRow>? _drawnOptionsFrom;

    /// <summary>The result list the panel was last built from, by reference. Same reason.</summary>
    private IReadOnlyList<EventResultLine>? _drawnResultFrom;

    /// <summary>
    /// Each drawn result row's number, with the movement it is written from. Held because the same
    /// row is written twice — shortened, and exact while a finger is on it — off one build.
    /// </summary>
    private readonly List<(Label Value, long Delta)> _resultValues = [];

    /// <summary>Whether a submission is in flight, so a second press cannot start another.</summary>
    private bool _busy;

    /// <summary>Whether one of the result numbers is being held right now.</summary>
    /// <remarks>
    /// 🔒 Read by the hold timer when it elapses, and cleared on teardown and on a rebuild as well as
    /// on release. A timer created by the tree outlives the node that asked for it, so this flag is
    /// what stops a hold started just before the screen closed from reaching a presenter nobody is
    /// looking at.
    /// </remarks>
    private bool _holdingANumber;

    /// <summary>Takes the presenter, the board to hand back to, and the app's shutdown token.</summary>
    /// <param name="presenter">Drives this screen.</param>
    /// <param name="board">The board the tile was entered from, returned to once it is resolved.</param>
    /// <param name="lifetime">Cancelled when the application shuts down.</param>
    /// <exception cref="ArgumentNullException">The presenter or the board is null.</exception>
    public void Drive(EventPresenter presenter, Board board, CancellationToken lifetime)
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
        // is asked, and this screen redraws on every press.
        _titleLabel = GetNode<Label>(TitleLabelPath);
        _cardTitle = GetNode<Label>(CardTitlePath);
        _cardBody = GetNode<Label>(CardBodyPath);
        _optionColumn = GetNode<VBoxContainer>(OptionColumnPath);
        _resultPanel = GetNode<PanelContainer>(ResultPanelPath);
        _resultHeading = GetNode<Label>(ResultHeadingPath);
        _runRows = GetNode<VBoxContainer>(RunRowsPath);
        _walletHeading = GetNode<Label>(WalletHeadingPath);
        _walletRows = GetNode<VBoxContainer>(WalletRowsPath);
        _statusLabel = GetNode<Label>(StatusLabelPath);
        _rejectionLabel = GetNode<Label>(RejectionLabelPath);
        _continueButton = GetNode<Button>(ContinueButtonPath);

        // Painted because a Button draws its text by draw mode, and the disabled mode this control
        // spends most of its life in has an engine default of half-transparent grey that no override
        // of font_color reaches.
        ButtonTextColours.ApplyTo(_continueButton, LiveColour, UnavailableColour);

        _continueButton.Pressed += OnContinuePressed;

        // Claims the viewport for this screen's own camera and puts its overlay up. Every screen
        // does this on the way in, because every handover in this build leaves the outgoing screen
        // in the tree — hidden, or freed only on the frame after.
        ScreenStage.Show(this);

        SafeAreaInsets.ApplyTo(GetNode<MarginContainer>(SafeAreaPath), GetViewport().GetVisibleRect().Size);

        Render();

        _ = StartAsync();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// 🔒 The continue control is detached here because it is the one control connected in
    /// <see cref="_Ready"/> and it outlives no rebuild. The option cards are rebuilt from their scene
    /// and die with the container they are cleared out of, so their presses go with them.
    /// </remarks>
    public override void _ExitTree()
    {
        // Asked whether it is still there rather than assumed: this screen can leave the tree
        // before _Ready ever resolved its nodes, and a handover that failed to load is exactly
        // that case.
        if (_continueButton is { } button && IsInstanceValid(button))
        {
            button.Pressed -= OnContinuePressed;
        }

        // Cleared here as well as on release: a hold timer already running belongs to the tree and
        // fires whether this screen is still there or not.
        _holdingANumber = false;
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
                    "The event screen entered the tree with no presenter. Only a screen that " +
                    "already has a run may instantiate it, and it must call Drive before adding it.");

                return;
            }

            // No ConfigureAwait(false): the continuation writes to nodes, and only the thread the
            // engine runs the scene tree on may do that. 🔒 This await is also the card's DRAW —
            // the screen's own first command, not an acknowledgement the board already sent.
            await presenter.StartAsync(_lifetime);

            Render();
            Report(presenter);
        }
        catch (Exception failure)
        {
            GD.PushError($"The event screen stopped unexpectedly: {failure}");
        }
    }

    /// <summary>Writes the presenter's state into the scene, if the scene is still there to write into.</summary>
    private void Render()
    {
        var presenter = _presenter;

        // Validity before tree membership: asking a freed node whether it is inside the tree is
        // itself the crash, and a shutdown during a slow command is the ordinary case on a handset.
        // Every node this writes to is checked, not just the one.
        if (presenter is null || !IsInstanceValid(this) || !IsInsideTree() ||
            _titleLabel is null || _cardTitle is null || _cardBody is null ||
            _optionColumn is null || _resultPanel is null || _resultHeading is null ||
            _runRows is null || _walletHeading is null || _walletRows is null ||
            _statusLabel is null || _rejectionLabel is null || _continueButton is null)
        {
            return;
        }

        _titleLabel.Text = presenter.Title;

        // Drawn verbatim: the card's own English, never a resolved key.
        _cardTitle.Text = presenter.CardTitle;
        _cardTitle.Visible = _cardTitle.Text.Length > 0;

        _cardBody.Text = presenter.CardBody;
        _cardBody.Visible = _cardBody.Text.Length > 0;

        RenderOptions(presenter);

        // Hidden as well as emptied, so a card the choice has spent cannot leave a row on screen for
        // a frame looking pressable.
        _optionColumn.Visible = presenter.Stage == EventStage.Choosing;

        RenderResult(presenter);

        _continueButton.Text = presenter.ContinueText;
        _continueButton.Disabled = _busy || !presenter.CanLeave;

        // Hidden rather than blanked once they have nothing to say: an empty label still claims a
        // full line of height, so a blank one is a sentence a player can see room for and cannot
        // read. They are two lines because they answer two different questions — what state the
        // screen is in, and what the game said about the last command.
        _statusLabel.Text = presenter.StatusText;
        _statusLabel.Visible = _statusLabel.Text.Length > 0;

        _rejectionLabel.Text = presenter.RejectionText;
        _rejectionLabel.Visible = _rejectionLabel.Text.Length > 0;
    }

    /// <summary>Draws the card's options, rebuilding them only when the card itself has changed.</summary>
    private void RenderOptions(EventPresenter presenter)
    {
        if (_optionColumn is not { } column)
        {
            return;
        }

        if (!ReferenceEquals(_drawnOptionsFrom, presenter.Options))
        {
            BuildOptions(column, presenter);

            _drawnOptionsFrom = presenter.Options;
        }

        foreach (var (press, card, available) in _optionButtons)
        {
            // Validity asked of both, not one: they are two engine objects and a teardown can have
            // freed either while this loop is running.
            if (!IsInstanceValid(press) || !IsInstanceValid(card))
            {
                continue;
            }

            var pressable = !_busy && available;

            press.Disabled = !pressable;

            // 🔒 Drawn every render rather than once at build time, which is the whole point: the
            // in-flight case comes and goes while the card stays, so a face applied once could only
            // ever describe the permanent case.
            DrawFace(card, pressable);
        }
    }

    private void BuildOptions(VBoxContainer column, EventPresenter presenter)
    {
        _optionButtons.Clear();
        Clear(column);

        if (presenter.Options.Count == 0)
        {
            return;
        }

        var cardScene = GD.Load<PackedScene>(OptionCardScenePath);

        if (cardScene is null)
        {
            // Load answers null rather than throwing when the resource is missing or its import
            // cannot be read, so an unnamed null reference is all a caller gets unless it says so.
            GD.PushError($"The event option card could not be loaded from '{OptionCardScenePath}'.");

            return;
        }

        foreach (var offered in presenter.Options)
        {
            column.AddChild(BuildOption(cardScene, offered, presenter.CostLabel));
        }
    }

    private PanelContainer BuildOption(
        PackedScene cardScene, EventOptionRow offered, string costLabel)
    {
        var card = cardScene.Instantiate<PanelContainer>();

        card.GetNode<Label>(OptionLabelPath).Text = offered.Label;

        var cost = card.GetNode<Label>(OptionCostLabelPath);

        // The price is captioned, because a bare number on a card offering a choice does not say
        // whether it is what the option costs or what it pays.
        cost.Text = offered.CostText.Length > 0
            ? costLabel + RowCaptionAndValue + offered.CostText
            : "";
        cost.Visible = cost.Text.Length > 0;

        var block = card.GetNode<Label>(OptionBlockLabelPath);

        block.Text = offered.BlockText;
        block.Visible = !offered.Available && block.Text.Length > 0;

        var press = card.GetNode<Button>(OptionPressButtonPath);

        // Applied even though the card's own text is empty and its caption is drawn by a label
        // beside it: a control that opts out of the one helper written to stop an engine default
        // reaching a draw mode is a control that silently keeps one the day it grows a caption.
        ButtonTextColours.ApplyTo(press, LiveColour, UnavailableColour);
        press.Disabled = _busy || !offered.Available;

        // Captured by value into the handler, so the option a press submits is the option the card
        // was drawn for even after the list is rebuilt beneath it.
        var choiceIndex = offered.ChoiceIndex;

        press.Pressed += () => OnOptionPressed(choiceIndex);

        _optionButtons.Add((press, card, offered.Available));

        // Drawn here as well as in RenderOptions so a card is never in the tree for a frame looking
        // live when it is not: the build runs before the render loop that follows it.
        DrawFace(card, !press.Disabled);

        return card;
    }

    /// <summary>Draws a card that cannot be pressed down into the ground and flat against it.</summary>
    /// <remarks>
    /// <para>
    /// 🔒 Duplicated off the card's OWN authored face rather than built here, so an unpressable card
    /// differs from its sibling in four properties and every other number describing a card stays
    /// written down in the scene. Reversible, because every reason a card cannot be pressed drives
    /// it — an unaffordable price for the life of the screen, and a command in flight for a moment.
    /// </para>
    /// <para>
    /// 🔴 <b>Two of the four are not colour.</b> The frame thins and the lift goes, so the card that
    /// cannot be taken differs from the one that can in weight and in depth as well as in hue — and
    /// the sentence naming the price is on the card besides. An option marked by hue alone is the one
    /// thing this game's colour rule names outright.
    /// </para>
    /// </remarks>
    private static void DrawFace(PanelContainer card, bool pressable)
    {
        if (pressable)
        {
            // Removed rather than overwritten with the authored numbers restated here: the scene is
            // where a live card's face is written down, and a second copy of it in this file would
            // be the one that went stale.
            card.RemoveThemeStyleboxOverride(PanelStyleOverride);

            return;
        }

        if (card.GetThemeStylebox(PanelStyleOverride) is not StyleBoxFlat face ||
            face.Duplicate() is not StyleBoxFlat dimmed)
        {
            GD.PushError("An event option card has no flat face to dim, so it is drawn as a live one.");

            return;
        }

        dimmed.BgColor = UnavailableFaceColour;
        dimmed.BorderColor = UnavailableOutlineColour;
        dimmed.SetBorderWidthAll(UnavailableOutlineWidth);
        dimmed.ShadowSize = UnavailableShadowSize;

        card.AddThemeStyleboxOverride(PanelStyleOverride, dimmed);
    }

    /// <summary>Draws what the choice moved, rebuilding the rows only when they have changed.</summary>
    /// <remarks>
    /// 🔒 The wallet rows are grouped under their own heading, and the heading is drawn only when
    /// there is a wallet row under it: this screen captions Gold, hit points and dice itself, while a
    /// wallet row is captioned by the currency's own key and would otherwise hang under nothing.
    /// </remarks>
    private void RenderResult(EventPresenter presenter)
    {
        if (_resultPanel is not { } panel || _resultHeading is not { } heading ||
            _runRows is not { } runRows || _walletHeading is not { } walletHeading ||
            _walletRows is not { } walletRows)
        {
            return;
        }

        panel.Visible = presenter.ResultLines.Count > 0;
        heading.Text = presenter.ResultLabel;
        walletHeading.Text = presenter.WalletLabel;

        if (!ReferenceEquals(_drawnResultFrom, presenter.ResultLines))
        {
            _drawnResultFrom = presenter.ResultLines;

            BuildResult(presenter, runRows, walletHeading, walletRows);
        }

        // Written every render rather than once at build time: which form a number is in is the
        // presenter's answer to whether a finger is on it, and that comes and goes while the rows stay.
        foreach (var (value, delta) in _resultValues)
        {
            if (IsInstanceValid(value))
            {
                value.Text = presenter.DeltaText(delta);
            }
        }
    }

    /// <summary>Fills the two row lists from the movements, and hides the wallet half if it is empty.</summary>
    private void BuildResult(
        EventPresenter presenter, VBoxContainer runRows, Label walletHeading, VBoxContainer walletRows)
    {
        // A hold whose row is about to be freed can never send its release, so the reveal it asked
        // for would stay on a number nobody is touching.
        _holdingANumber = false;
        presenter.ConcealFullValues();

        _resultValues.Clear();
        Clear(runRows);
        Clear(walletRows);

        walletHeading.Visible = false;

        if (presenter.ResultLines.Count == 0)
        {
            return;
        }

        var rowScene = GD.Load<PackedScene>(ResultRowScenePath);

        if (rowScene is null)
        {
            // Load answers null rather than throwing when the resource is missing or its import
            // cannot be read, so an unnamed null reference is all a caller gets unless it says so.
            GD.PushError($"The event result row could not be loaded from '{ResultRowScenePath}'.");

            return;
        }

        foreach (var line in presenter.ResultLines)
        {
            if (line.FromWallet)
            {
                walletHeading.Visible = true;
                walletRows.AddChild(Row(rowScene, line));
            }
            else
            {
                runRows.AddChild(Row(rowScene, line));
            }
        }
    }

    /// <summary>One row of the result panel: a caption, and the signed number beside it.</summary>
    private HBoxContainer Row(PackedScene rowScene, EventResultLine line)
    {
        var row = rowScene.Instantiate<HBoxContainer>();

        row.GetNode<Label>(RowCaptionLabelPath).Text = line.Label;

        var value = row.GetNode<Label>(RowValueLabelPath);

        // The exact figure is what a hold on this number asks for, so the number is where the
        // gesture is read. The handler dies with the row it is on, exactly as an option's press does.
        value.GuiInput += OnValueInput;

        _resultValues.Add((value, line.Delta));

        return row;
    }

    /// <summary>
    /// A number held down shows its exact value; letting go puts the shortened one back.
    /// </summary>
    /// <remarks>
    /// 🔒 Only the gesture is here. Which form each number takes is the presenter's answer, so what
    /// this file decides is the single fact an engine event carries — whether the finger is down —
    /// and nothing about how a number is written.
    /// </remarks>
    /// <param name="event">The input one of the result numbers received.</param>
    private void OnValueInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } mouse:
                HoldNumber(mouse.Pressed);
                break;

            case InputEventScreenTouch touch:
                HoldNumber(touch.Pressed);
                break;
        }
    }

    private void HoldNumber(bool pressed)
    {
        _holdingANumber = pressed;

        if (!pressed)
        {
            _presenter?.ConcealFullValues();
            Render();

            return;
        }

        // The tree's timer rather than a node of this screen's own: it is one shot, it is created on
        // the press and it is gone after it, so a timer node would be a permanent child kept for a
        // gesture most players never make.
        var hold = GetTree()?.CreateTimer(LongPressSeconds);

        if (hold is null)
        {
            GD.PushError("An event result number was held while the screen was outside the tree.");

            return;
        }

        hold.Timeout += OnHoldElapsed;
    }

    /// <remarks>
    /// The flag is read FIRST, and it is cleared on teardown and on a rebuild as well as on release:
    /// this timer belongs to the tree and fires whether or not the screen that asked for it is still
    /// there.
    /// </remarks>
    private void OnHoldElapsed()
    {
        if (!_holdingANumber || !IsInstanceValid(this) || _presenter is not { } presenter)
        {
            return;
        }

        presenter.RevealFullValues();
        Render();
    }

    /// <summary>Empties a container now, rather than at the end of the frame.</summary>
    /// <remarks>
    /// 🔒 <c>QueueFree</c> alone defers removal, so a child freed and a child added in the same frame
    /// are both in the tree and both laid out until it ends. Detaching first is what keeps a rebuilt
    /// list from briefly drawing two sets of rows over each other.
    /// </remarks>
    private static void Clear(Node container)
    {
        foreach (var child in container.GetChildren())
        {
            container.RemoveChild(child);
            child.QueueFree();
        }
    }

    private void OnOptionPressed(int choiceIndex) =>
        _ = SubmitAsync(presenter => presenter.ChooseAsync(choiceIndex, _lifetime));

    /// <remarks>
    /// 🔒 Continue only ever hands back, and only once the presenter says the tile has cleared: the
    /// board's decision latch logs "halted" if this same decision re-opens with the tile still
    /// pending, so a screen that returned early would be sent straight back here.
    /// </remarks>
    private void OnContinuePressed() => LeaveIfTheTileHasCleared();

    /// <remarks>
    /// Every control is taken out of use for the whole round trip and put back once, on one path. A
    /// second press landing mid-flight would submit a second choice off a card that offers one — two
    /// costs paid and two outcomes drawn.
    /// </remarks>
    private async Task SubmitAsync(Func<EventPresenter, Task<EventSubmission>> submit)
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
            await submit(presenter);

            Report(presenter);
        }
        catch (Exception failure)
        {
            GD.PushError($"An event command failed: {failure}");
        }
        finally
        {
            _busy = false;
            Render();
        }
    }

    /// <summary>Hands back to the board once the run no longer stands on the tile.</summary>
    /// <remarks>
    /// 🔒 Read off the run the command answered with, never off which option was pressed:
    /// <c>EVENT_CHOOSE</c> clears the tile as its last step whatever the outcome was, and that is the
    /// rules layer's answer to give. A run that could not be read at all stays here and says so.
    /// </remarks>
    private void LeaveIfTheTileHasCleared()
    {
        if (_presenter is not { CanLeave: true } || _board is not { } board ||
            !IsInstanceValid(this) || !IsInsideTree())
        {
            return;
        }

        EventHandover.Return(this, board);
    }

    /// <summary>
    /// Prints, on one greppable line, which stage this screen settled on and what it drew against the
    /// run and content the build actually shipped.
    /// </summary>
    private static void Report(EventPresenter presenter) =>
        GD.Print(
            $"{EventMarker} stage={presenter.Stage} card={(presenter.CardId.Length > 0 ? presenter.CardId : "none")} " +
            $"options={presenter.Options.Count} result_rows={presenter.ResultLines.Count} " +
            $"can_leave={presenter.CanLeave} host_faulted={presenter.HostFaulted} " +
            $"rejection={presenter.RulesRejection?.ToString() ?? "none"}");
}
