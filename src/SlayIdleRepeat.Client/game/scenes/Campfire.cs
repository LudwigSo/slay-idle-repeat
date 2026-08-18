using Godot;
using SlayIdleRepeat.Client.Game.Presenters;

namespace SlayIdleRepeat.Client.Game.Scenes;

/// <summary>
/// S11 — the campfire and the shrine, one screen with two arms: a driving adapter over
/// <see cref="CampfirePresenter"/>.
/// </summary>
/// <remarks>
/// <para>
/// It renders what the presenter says and forwards up to four presses. No rules, no ports, no
/// adapters, and no decision about which arm opens or what may be pressed — both are the presenter's
/// answer to the run's own pending tile, and this half only draws it.
/// </para>
/// <para>
/// 🔒 <b>The two arms never overlap and the screen never draws both.</b> A campfire draws option
/// cards and no buff rows; a shrine draws buff rows and no option cards. The presenter empties
/// whichever list does not belong to the arm it settled on, and each container is hidden as well as
/// emptied so a stale row can never survive an arm change.
/// </para>
/// <para>
/// 🔴 <b>Two of the three campfire options are unavailable, for two different missing systems, and
/// each carries its own sentence on its own card.</b> They are drawn out of use rather than left
/// pressable: the rules layer would refuse both with a value four other things share, so a round
/// trip would replace the sentence naming the missing system with one naming nothing.
/// </para>
/// <para>
/// 🔒 <b>The option cards are PINNED, not scrolled.</b> They sit in the bottom action column beside
/// the shrine's continue, because resting is this arm's primary action and every other screen in the
/// build keeps its primary action in the thumb's reach whatever the scroll is doing. The scrolling
/// band above them carries the shrine arm, and on a campfire it simply holds nothing.
/// </para>
/// <para>
/// 🔴 <b>The shrine's choice is not the player's, and this screen says so.</b> No shrine-choose
/// command exists, so the rows are shown with the one that will be taken marked, and the absence is
/// named rather than drawn as two buttons one of which is a lie. The cleanse arm is named too — a
/// shrine that silently never cleanses looks exactly like one that rolled badly.
/// </para>
/// <para>
/// ⚠️ Every type size, colour, corner and outline in <c>Campfire.tscn</c>,
/// <c>CampfireOptionCard.tscn</c> and <c>ShrineBuffRow.tscn</c> is a per-node override, because the
/// shared theme resource does not exist yet — M8-03's, to be re-checked rather than re-applied.
/// </para>
/// <para>
/// 🔴 <b>The way this screen draws a host that did not answer is <c>M7-02</c>'s to replace, and it
/// is named here so the interim is not mistaken for the design.</b> <c>13</c> §11 is a 🔒 and authors
/// five connection states: connected shows nothing; reconnecting slides a non-blocking pill in after
/// 2 s and leaves the screen interactive; offline dims server-backed buttons to 40% with a
/// cloud-slash glyph and answers a tap with an inline toast, never a modal; a resync flashes green;
/// a resumed run shows a card. None of that exists anywhere in this client — <c>M7-02</c> owns all
/// five, together with <c>ReconnectManager</c> — so this screen does the most honest thing available
/// to it without inventing the mechanism: it disables what it cannot submit and prints one status
/// line. 🔒 The one rule §11 states as a hard prohibition <b>is</b> kept: no full-screen blocking
/// connection error, during a run or otherwise.
/// </para>
/// <para>
/// 🔴 <b>And the dead end above is the same gap seen from the other side.</b> A read that failed
/// leaves this screen with nothing to draw and nowhere to send the player, because under §11 that
/// state is not terminal at all — it is <em>offline, read-only</em>, which waits and reconnects. So
/// the dead end is not a missing back button; it is the absence of <c>M7-02</c>'s reconnect. That is
/// also why no back caption is invented for it: the control §11 calls for is a pill and a toast, not
/// a way out.
/// </para>
/// </remarks>
public partial class Campfire : Control
{
    /// <summary>Where this scene lives, for the screen that instantiates it.</summary>
    public const string ScenePath = "res://game/scenes/Campfire.tscn";

    /// <summary>
    /// 🔴 Named so the dead end can be found — the same one every decision screen in this milestone
    /// has, for the same reason.
    /// </summary>
    private const string TheUnreadableRunHasNowhereToGo =
        "The campfire screen could not read the run it was opened for, so it knows neither which arm " +
        "it is nor what to draw, and there is no command it may submit. It stays and names the " +
        "failure rather than handing back: the board it came from reads the same row through the " +
        "same host, so returning would move the player one screen away from the message without " +
        "changing the answer. There is no authored caption for a back control, so none is drawn.";

    /// <summary>
    /// 🔴 Named so it can be found. Neither arm's heading is drawn while the read has not settled on
    /// an arm, because the two tiles share this screen and nothing in an unsettled state says which
    /// of them the player is standing on.
    /// </summary>
    private const string NeitherHeadingFitsAnUnsettledArm =
        "A campfire and a shrine are two tiles on one screen, and until the read answers, which of " +
        "them opened is unknown. Drawing either heading would name a tile the player may not be on " +
        "— a shrine that failed to read would be titled Campfire — so the heading leaves and the " +
        "status line carries what actually happened.";

    /// <summary>The one line a headless run's screen state is read off.</summary>
    private const string CampfireMarker = "SIR_CAMPFIRE_READY";

    /// <summary>Where the per-option card lives, instantiated once per option a campfire offers.</summary>
    private const string OptionCardScenePath = "res://game/scenes/CampfireOptionCard.tscn";

    /// <summary>Where the per-buff row lives, instantiated once per row a shrine drew.</summary>
    private const string ShrineRowScenePath = "res://game/scenes/ShrineBuffRow.tscn";

    private const string OptionLabelPath = "Body/Column/OptionLabel";
    private const string OptionBlockLabelPath = "Body/Column/BlockLabel";
    private const string OptionPressButtonPath = "PressButton";

    private const string RowTakenMarkPath = "TakenMark";
    private const string RowNameLabelPath = "NameLabel";

    private const string SafeAreaPath = "%SafeArea";
    private const string TitleLabelPath = "%TitleLabel";
    private const string OptionColumnPath = "%OptionColumn";
    private const string ShrinePanelPath = "%ShrinePanel";
    private const string ShrineBuffsLabelPath = "%ShrineBuffsLabel";
    private const string ShrineRowListPath = "%ShrineRowList";
    private const string ShrineChoiceBlockLabelPath = "%ShrineChoiceBlockLabel";
    private const string ShrineCleanseBlockLabelPath = "%ShrineCleanseBlockLabel";
    private const string StatusLabelPath = "%StatusLabel";
    private const string RejectionLabelPath = "%RejectionLabel";
    private const string ContinueButtonPath = "%ContinueButton";

    /// <summary>The theme entry a label's own text colour is written into.</summary>
    private const string FontColourOverride = "font_color";

    /// <summary>The panel entry an option card's whole face — fill, outline, corners, shadow — takes.</summary>
    private const string PanelStyleOverride = "panel";

    /// <summary>A control with something to do.</summary>
    private static readonly Color LiveColour = new(0.93f, 0.93f, 0.96f);

    /// <summary>And one with nothing to do — the same quiet grey every caption is drawn in.</summary>
    private static readonly Color UnavailableColour = new(0.66f, 0.67f, 0.73f);

    /// <summary>The mark beside the row the shrine will actually apply.</summary>
    private static readonly Color TakenMarkColour = new(0.98f, 0.83f, 0.45f);

    /// <summary>And the absence of one beside every other row — drawn as nothing, not as a colour.</summary>
    private static readonly Color UntakenMarkColour = new(0, 0, 0, 0);

    /// <summary>What an option that cannot be taken has its fill and its outline drawn down to.</summary>
    /// <remarks>
    /// 🔒 The FACE recedes, never the text. Two of the three options are unavailable for the whole
    /// life of this screen, and each carries the only sentence in the build naming the system it is
    /// waiting on — so a translucent veil across the card, which is the obvious way to draw a control
    /// out of use, would spend the screen's dominant state below a readable contrast. Dimming the
    /// card's own fill and outline instead leaves every glyph on it at better than eleven to one.
    /// </remarks>
    private static readonly Color UnavailableFaceColour = new(0.13f, 0.13f, 0.17f);

    /// <summary>The outline that goes with it, which is what still reads the card as a card.</summary>
    private static readonly Color UnavailableOutlineColour = new(0.26f, 0.27f, 0.33f);

    private CampfirePresenter? _presenter;
    private Board? _board;
    private CancellationToken _lifetime;

    private Label? _titleLabel;
    private VBoxContainer? _optionColumn;
    private Control? _shrinePanel;
    private Label? _shrineBuffsLabel;
    private VBoxContainer? _shrineRowList;
    private Label? _shrineChoiceBlockLabel;
    private Label? _shrineCleanseBlockLabel;
    private Label? _statusLabel;
    private Label? _rejectionLabel;
    private Button? _continueButton;

    /// <summary>The press control of each option now drawn, paired with whether it can be taken.</summary>
    /// <summary>
    /// Each drawn option: its press, the card the press sits on, and whether the option is offered at
    /// all. The card is held as well as the press because BOTH are driven by whether it can be
    /// pressed — the press stops accepting input and the card's face recedes to say so.
    /// </summary>
    private readonly List<(Button Press, PanelContainer Card, bool Available)> _optionButtons = [];

    /// <summary>The option list the column was last built from, by reference.</summary>
    /// <remarks>
    /// 🔒 What stops the column being torn down and rebuilt on every redraw. A rebuild frees three
    /// nodes, and this screen redraws twice for every press. The presenter hands back a fresh list
    /// exactly when the arm it settled on has actually changed.
    /// </remarks>
    private IReadOnlyList<CampfireOptionRow>? _drawnOptionsFrom;

    /// <summary>The row list the shrine panel was last built from, by reference. Same reason.</summary>
    private IReadOnlyList<CampfireShrineRow>? _drawnRowsFrom;

    /// <summary>Whether a submission is in flight, so a second press cannot start another.</summary>
    private bool _busy;

    /// <summary>Takes the presenter, the board to hand back to, and the app's shutdown token.</summary>
    /// <param name="presenter">Drives this screen, both arms of it.</param>
    /// <param name="board">The board the tile was entered from, returned to once it is resolved.</param>
    /// <param name="lifetime">Cancelled when the application shuts down.</param>
    /// <exception cref="ArgumentNullException">The presenter or the board is null.</exception>
    public void Drive(CampfirePresenter presenter, Board board, CancellationToken lifetime)
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
        _optionColumn = GetNode<VBoxContainer>(OptionColumnPath);
        _shrinePanel = GetNode<Control>(ShrinePanelPath);
        _shrineBuffsLabel = GetNode<Label>(ShrineBuffsLabelPath);
        _shrineRowList = GetNode<VBoxContainer>(ShrineRowListPath);
        _shrineChoiceBlockLabel = GetNode<Label>(ShrineChoiceBlockLabelPath);
        _shrineCleanseBlockLabel = GetNode<Label>(ShrineCleanseBlockLabelPath);
        _statusLabel = GetNode<Label>(StatusLabelPath);
        _rejectionLabel = GetNode<Label>(RejectionLabelPath);
        _continueButton = GetNode<Button>(ContinueButtonPath);

        _continueButton.Pressed += OnContinuePressed;

        // Painted because a Button draws its text by draw mode, and the disabled mode this control
        // spends the whole campfire arm in has an engine default of half-transparent grey that no
        // override of font_color reaches.
        ButtonTextColours.ApplyTo(_continueButton, LiveColour, UnavailableColour);

        SafeAreaInsets.ApplyTo(GetNode<MarginContainer>(SafeAreaPath), GetViewportRect().Size);

        Render();

        _ = StartAsync();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The matching half of the subscription in <c>_Ready</c>. The control is a child and dies with
    /// this node either way, but a handler left connected across a scene that is merely detached and
    /// re-added would fire twice — and once is the whole contract of resolving a tile.
    /// </remarks>
    public override void _ExitTree()
    {
        if (_continueButton is not null)
        {
            _continueButton.Pressed -= OnContinuePressed;
        }
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
                    "The campfire entered the tree with no presenter. Only a screen that already " +
                    "has a run may instantiate it, and it must call Drive before adding it.");

                return;
            }

            // No ConfigureAwait(false): the continuation writes to nodes, and only the thread the
            // engine runs the scene tree on may do that.
            await presenter.StartAsync(_lifetime);

            Render();
            Report(presenter);
            LeaveIfTheTileHasCleared();
        }
        catch (Exception failure)
        {
            GD.PushError($"The campfire stopped unexpectedly: {failure}");
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
            _titleLabel is null || _optionColumn is null || _shrinePanel is null ||
            _shrineBuffsLabel is null || _shrineRowList is null ||
            _shrineChoiceBlockLabel is null || _shrineCleanseBlockLabel is null ||
            _statusLabel is null || _rejectionLabel is null || _continueButton is null)
        {
            return;
        }

        var campfire = presenter.Stage == CampfireStage.Campfire;
        var shrine = presenter.Stage == CampfireStage.Shrine;

        // See NeitherHeadingFitsAnUnsettledArm.
        _titleLabel.Text = shrine ? presenter.ShrineTitle : presenter.Title;
        _titleLabel.Visible = campfire || shrine;

        RenderOptions(presenter);
        _optionColumn.Visible = campfire;

        RenderShrine(presenter);
        _shrinePanel.Visible = shrine;

        _shrineBuffsLabel.Text = presenter.ShrineBuffsLabel;
        _shrineChoiceBlockLabel.Text = presenter.ShrineChoiceBlockText;
        _shrineCleanseBlockLabel.Text = presenter.ShrineCleanseBlockText;

        // 🔒 The shrine's one action, and only the shrine's. The campfire arm is left by resting,
        // which is a campfire choose — a Continue drawn there would submit a command the rules layer
        // accepts without clearing the tile, and the player would press it and stay put.
        _continueButton.Text = presenter.ContinueText;
        _continueButton.Visible = shrine;
        _continueButton.Disabled = _busy || !shrine;

        // Hidden rather than blanked once it has nothing to say: an empty label still claims a full
        // line of height, so a blank one is a sentence a player can see room for and cannot read.
        // They are two lines because they answer two different questions — what state the screen is
        // in, and what the game said about the last command.
        _statusLabel.Text = presenter.StatusText;
        _statusLabel.Visible = _statusLabel.Text.Length > 0;

        _rejectionLabel.Text = presenter.RejectionText;
        _rejectionLabel.Visible = _rejectionLabel.Text.Length > 0;
    }

    /// <summary>Draws the campfire's options, rebuilding them only when the arm itself has changed.</summary>
    private void RenderOptions(CampfirePresenter presenter)
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

    private void BuildOptions(VBoxContainer column, CampfirePresenter presenter)
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
            GD.PushError($"The campfire option card could not be loaded from '{OptionCardScenePath}'.");

            return;
        }

        foreach (var offered in presenter.Options)
        {
            column.AddChild(BuildOption(cardScene, offered));
        }
    }

    private PanelContainer BuildOption(PackedScene cardScene, CampfireOptionRow offered)
    {
        var card = cardScene.Instantiate<PanelContainer>();

        card.GetNode<Label>(OptionLabelPath).Text = offered.Label;

        var block = card.GetNode<Label>(OptionBlockLabelPath);

        // Its own sentence, never shared: the two unavailable options are missing two completely
        // different systems, and one message covering both tells a player neither.
        block.Text = offered.BlockText;
        block.Visible = !offered.Available;

        var press = card.GetNode<Button>(OptionPressButtonPath);

        // Applied even though the card's own text is empty and its caption is drawn by a label
        // beside it: a control that opts out of the one helper written to stop an engine default
        // reaching a draw mode is a control that silently keeps one the day it grows a caption.
        ButtonTextColours.ApplyTo(press, LiveColour, UnavailableColour);
        press.Disabled = _busy || !offered.Available;

        // Captured by value into the handler, so the option a press submits is the option the card
        // was drawn for even after the list is rebuilt beneath it.
        var option = offered.Option;

        press.Pressed += () => OnOptionPressed(option);

        _optionButtons.Add((press, card, offered.Available));

        // Drawn here as well as in RenderOptions so a card is never in the tree for a frame looking
        // live when it is not: the build runs before the render loop that follows it.
        DrawFace(card, !press.Disabled);

        return card;
    }

    /// <summary>Draws a card that cannot be pressed down into the ground, face and outline only.</summary>
    /// <remarks>
    /// <para>
    /// 🔒 Duplicated off the card's OWN authored face rather than built here, so an unpressable card
    /// differs from its sibling in exactly two properties and every other number describing a card
    /// stays written down in one place. See <see cref="UnavailableFaceColour"/>.
    /// </para>
    /// <para>
    /// 🔒 <b>Reversible, and driven by every reason a card cannot be pressed rather than only the
    /// permanent one.</b> Two of the three campfire options are unavailable for this screen's whole
    /// life, but ALL of them are unpressable while a command is in flight — and a card that quietly
    /// looks live while it is ignoring presses is the same defect as one that never said it was
    /// unavailable. The override is added and removed rather than baked in at build time, which is
    /// what lets the in-flight case use the mechanism the permanent case already had.
    /// </para>
    /// </remarks>
    private static void DrawFace(PanelContainer card, bool pressable)
    {
        if (pressable)
        {
            // Removed rather than overwritten with the authored numbers restated here: the scene is
            // where a live card's face is written down, and a second copy of it in this file would be
            // the one that went stale.
            card.RemoveThemeStyleboxOverride(PanelStyleOverride);

            return;
        }

        if (card.GetThemeStylebox(PanelStyleOverride) is not StyleBoxFlat face ||
            face.Duplicate() is not StyleBoxFlat dimmed)
        {
            GD.PushError("A campfire option card has no flat face to dim, so it is drawn as a live one.");

            return;
        }

        dimmed.BgColor = UnavailableFaceColour;
        dimmed.BorderColor = UnavailableOutlineColour;

        card.AddThemeStyleboxOverride(PanelStyleOverride, dimmed);
    }

    /// <summary>Draws the rows the shrine drew, rebuilding them only when the draw has changed.</summary>
    /// <remarks>
    /// 🔒 Every row is walked and each is asked whether it is the taken one. The cleanse branch draws
    /// a single row, so a screen that reached for the second one by index would crash on the day
    /// curses land — and would be reading the taken row off a position rather than off the fact.
    /// </remarks>
    private void RenderShrine(CampfirePresenter presenter)
    {
        if (_shrineRowList is not { } list || ReferenceEquals(_drawnRowsFrom, presenter.ShrineRows))
        {
            return;
        }

        _drawnRowsFrom = presenter.ShrineRows;

        Clear(list);

        if (presenter.ShrineRows.Count == 0)
        {
            return;
        }

        var rowScene = GD.Load<PackedScene>(ShrineRowScenePath);

        if (rowScene is null)
        {
            // Load answers null rather than throwing when the resource is missing or its import
            // cannot be read, so an unnamed null reference is all a caller gets unless it says so.
            GD.PushError($"The shrine buff row could not be loaded from '{ShrineRowScenePath}'.");

            return;
        }

        foreach (var drew in presenter.ShrineRows)
        {
            var row = rowScene.Instantiate<HBoxContainer>();
            var name = row.GetNode<Label>(RowNameLabelPath);

            row.GetNode<ColorRect>(RowTakenMarkPath).Color =
                drew.IsTaken ? TakenMarkColour : UntakenMarkColour;

            // Marked twice over, because a mark that is only a colour is a mark some players cannot
            // read: the taken row keeps the live text colour and the rest drop to the quiet one, so
            // the difference survives with the mark itself unseen.
            name.AddThemeColorOverride(
                FontColourOverride, drew.IsTaken ? LiveColour : UnavailableColour);

            name.Text = drew.Name;

            list.AddChild(row);
        }
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

    private void OnOptionPressed(CampfireOption option) =>
        _ = SubmitAsync(presenter => presenter.ChooseAsync(option, _lifetime));

    private void OnContinuePressed() => _ = SubmitAsync(presenter => presenter.ContinueAsync(_lifetime));

    /// <remarks>
    /// Every control is taken out of use for the whole round trip and put back once, on one path. A
    /// second press landing mid-flight would submit a command against a run the first has already
    /// moved — and on a campfire that is a second rest, which heals twice.
    /// </remarks>
    private async Task SubmitAsync(Func<CampfirePresenter, Task<CampfireSubmission>> submit)
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
            GD.PushError($"A campfire command failed: {failure}");
        }
        finally
        {
            _busy = false;
            Render();
            LeaveIfTheTileHasCleared();
        }
    }

    /// <summary>Hands back to the board once the run no longer stands on either tile.</summary>
    /// <remarks>
    /// 🔒 Read off the run the command answered with, never off which command was pressed: a rest
    /// clears the tile and the two refused options do not, but that is the rules layer's answer to
    /// give. A run that could not be read at all stays here and says so — see
    /// <see cref="TheUnreadableRunHasNowhereToGo"/>.
    /// </remarks>
    private void LeaveIfTheTileHasCleared()
    {
        if (_presenter is not { Stage: CampfireStage.NotAtEither } || _board is not { } board ||
            !IsInstanceValid(this) || !IsInsideTree())
        {
            return;
        }

        CampfireHandover.Return(this, board);
    }

    /// <summary>
    /// Prints, on one greppable line, which arm this screen settled on and what it drew against the
    /// run and content the build actually shipped.
    /// </summary>
    private static void Report(CampfirePresenter presenter) =>
        GD.Print(
            $"{CampfireMarker} stage={presenter.Stage} options={presenter.Options.Count} " +
            $"shrine_rows={presenter.ShrineRows.Count} available={presenter.ShrineRowsAvailable} " +
            $"taken={presenter.ShrineRows.FirstOrDefault(row => row.IsTaken)?.BuffId ?? "none"} " +
            $"host_faulted={presenter.HostFaulted} " +
            $"rejection={presenter.RulesRejection?.ToString() ?? "none"}");
}
