using Godot;
using SlayIdleRepeat.Client.Game.Presenters;

namespace SlayIdleRepeat.Client.Game.Scenes;

/// <summary>
/// S08 — the shop tile, at the only surface this build can honestly give it: a driving adapter over
/// <see cref="ShopPresenter"/>.
/// </summary>
/// <remarks>
/// <para>
/// It renders what the presenter says and forwards one press. No rules, no ports, no adapters, and
/// no decision about what may be pressed.
/// </para>
/// <para>
/// 🔒 <b>Four buy slots, a restock and a departure.</b> Each slot is one button, captioned with what
/// it sells and what it costs, and a slot that cannot be pressed carries the reason under it in
/// words — "Sold", "Too dear", "Nothing left in this pool" — rather than being greyed out and left
/// to be guessed at.
/// </para>
/// <para>
/// ⚠️ Every type size and colour in <c>Shop.tscn</c> is a per-node override, because the shared
/// theme resource does not exist yet — M8-03's, and to be re-checked rather than re-applied.
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
public partial class Shop : Node3D
{
    /// <summary>Where this scene lives, for the screen that instantiates it.</summary>
    public const string ScenePath = "res://game/scenes/Shop.tscn";

    /// <summary>
    /// 🔴 Named so the dead end can be found — the same one every decision screen in this milestone
    /// has, for the same reason.
    /// </summary>
    private const string TheUnreadableRunHasNowhereToGo =
        "The shop screen could not read the run it was opened for, so it has nothing to draw and no " +
        "command it may submit. It stays and names the failure rather than handing back: the board " +
        "it came from reads the same row through the same host, so returning would move the player " +
        "one screen away from the message without changing the answer. There is no authored caption " +
        "for a back control, so none is drawn — a screen may not invent wording.";

    /// <summary>The one line a headless run's screen state is read off.</summary>
    private const string ShopMarker = "SIR_SHOP_READY";

    private const string SafeAreaPath = "%SafeArea";
    private const string TitleLabelPath = "%TitleLabel";
    private const string GoldLabelPath = "%GoldLabel";
    private const string SlotListPath = "%SlotList";
    private const string RefreshSpentLabelPath = "%RefreshSpentLabel";
    private const string StatusLabelPath = "%StatusLabel";
    private const string RejectionLabelPath = "%RejectionLabel";
    private const string RefreshButtonPath = "%RefreshButton";
    private const string LeaveButtonPath = "%LeaveButton";

    /// <summary>Where one slot's row scene lives.</summary>
    private const string SlotRowScenePath = "res://game/scenes/ShopSlotRow.tscn";

    private const string SlotBuyButtonPath = "BuyButton";
    private const string SlotBlockedLabelPath = "BlockedLabel";

    /// <summary>A control with something to do.</summary>
    private static readonly Color LiveColour = new(0.93f, 0.93f, 0.96f);

    /// <summary>And one with nothing to do — the same quiet grey every caption is drawn in.</summary>
    private static readonly Color UnavailableColour = new(0.66f, 0.67f, 0.73f);

    private ShopPresenter? _presenter;
    private Board? _board;
    private CancellationToken _lifetime;

    private Label? _titleLabel;
    private Label? _goldLabel;
    private VBoxContainer? _slotList;
    private Label? _refreshSpentLabel;
    private Label? _statusLabel;
    private Label? _rejectionLabel;
    private Button? _refreshButton;
    private Button? _leaveButton;

    /// <summary>
    /// The offer the slot rows currently drawn were built from, so a redraw that changed nothing
    /// does not rebuild them.
    /// </summary>
    /// <remarks>
    /// Rebuilding on every render would destroy and re-instantiate four scenes each time a button
    /// went busy — and would drop the focus a player is holding, which on a handset is how a
    /// keyboard or a controller loses its place mid-purchase.
    /// </remarks>
    private IReadOnlyList<ShopSlotCard> _drawn = Array.Empty<ShopSlotCard>();

    /// <summary>Whether a submission is in flight, so a second press cannot start another.</summary>
    private bool _busy;

    /// <summary>Takes the presenter, the board to hand back to, and the app's shutdown token.</summary>
    /// <param name="presenter">Drives this screen.</param>
    /// <param name="board">The board the tile was entered from, returned to once it is resolved.</param>
    /// <param name="lifetime">Cancelled when the application shuts down.</param>
    /// <exception cref="ArgumentNullException">The presenter or the board is null.</exception>
    public void Drive(ShopPresenter presenter, Board board, CancellationToken lifetime)
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
        _goldLabel = GetNode<Label>(GoldLabelPath);
        _slotList = GetNode<VBoxContainer>(SlotListPath);
        _refreshSpentLabel = GetNode<Label>(RefreshSpentLabelPath);
        _statusLabel = GetNode<Label>(StatusLabelPath);
        _rejectionLabel = GetNode<Label>(RejectionLabelPath);
        _refreshButton = GetNode<Button>(RefreshButtonPath);
        _leaveButton = GetNode<Button>(LeaveButtonPath);

        _refreshButton.Pressed += OnRefreshPressed;
        _leaveButton.Pressed += OnLeavePressed;

        // Painted because a Button draws its text by draw mode, and the disabled mode these controls
        // spend every round trip in has an engine default of half-transparent grey that no override
        // of font_color reaches.
        ButtonTextColours.ApplyTo(_refreshButton, LiveColour, UnavailableColour);
        ButtonTextColours.ApplyTo(_leaveButton, LiveColour, UnavailableColour);

        // Claims the viewport for this screen's own camera and puts its overlay up. Every screen
        // does this on the way in, because every handover in this build leaves the outgoing screen
        // in the tree — hidden, or freed only on the frame after — so two cameras and two overlays
        // are alive at the moment this one becomes the visible screen.
        ScreenStage.Show(this);

        SafeAreaInsets.ApplyTo(GetNode<MarginContainer>(SafeAreaPath), GetViewport().GetVisibleRect().Size);

        Render();

        _ = StartAsync();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The matching half of the subscription in <c>_Ready</c>. The control is a child and dies with
    /// this node either way, but a handler left connected across a scene that is merely detached and
    /// re-added would fire twice — and once is the whole contract of leaving a tile.
    /// </remarks>
    public override void _ExitTree()
    {
        if (_refreshButton is not null)
        {
            _refreshButton.Pressed -= OnRefreshPressed;
        }

        if (_leaveButton is not null)
        {
            _leaveButton.Pressed -= OnLeavePressed;
        }
    }

    /// <remarks>
    /// Nothing awaits this task, so its exceptions have nowhere to surface: the whole body is
    /// guarded, or a continuation that threw would leave a shop that silently stopped moving.
    /// </remarks>
    private async Task StartAsync()
    {
        try
        {
            if (_presenter is not { } presenter)
            {
                GD.PushError(
                    "The shop entered the tree with no presenter. Only a screen that already has a " +
                    "run may instantiate it, and it must call Drive before adding it.");

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
            GD.PushError($"The shop stopped unexpectedly: {failure}");
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
            _titleLabel is null || _goldLabel is null || _slotList is null ||
            _refreshSpentLabel is null || _statusLabel is null || _rejectionLabel is null ||
            _refreshButton is null || _leaveButton is null)
        {
            return;
        }

        _titleLabel.Text = presenter.Title;

        // 🔒 Drawn only while the player is actually standing in the shop: a Gold total and four
        // slots shown to someone whose run could not be read are claims about a shop nobody has
        // established they are in, and the status line below already says what did happen.
        var open = presenter.Stage == ShopStage.Ready;

        _goldLabel.Text = presenter.GoldLabel + " " + presenter.Gold.ToString(Culture);
        _goldLabel.Visible = open;

        DrawSlots(presenter, open);

        _refreshSpentLabel.Text = presenter.RefreshSpentText;
        _refreshSpentLabel.Visible = open && _refreshSpentLabel.Text.Length > 0;

        _refreshButton.Text = presenter.RefreshText;
        _refreshButton.Visible = open && presenter.RefreshOffered;
        _refreshButton.Disabled = _busy || !open || !presenter.RefreshOffered;

        _leaveButton.Text = presenter.LeaveText;
        _leaveButton.Disabled = _busy || !open;

        // Hidden rather than blanked once it has nothing to say: an empty label still claims a full
        // line of height, so a blank one is a sentence a player can see room for and cannot read.
        // They are two lines because they answer two different questions — what state the screen is
        // in, and what the game said about the last command. Both sit OUTSIDE the scrolling band, on
        // the column itself: a refusal parked inside a viewport is a refusal a scroll position can
        // hide, which is the same silence as never printing it.
        _statusLabel.Text = presenter.StatusText;
        _statusLabel.Visible = _statusLabel.Text.Length > 0;

        _rejectionLabel.Text = presenter.RejectionText;
        _rejectionLabel.Visible = _rejectionLabel.Text.Length > 0;
    }

    /// <summary>Rebuilds the slot rows when the offer changed, and re-captions them when it did not.</summary>
    /// <remarks>
    /// The identity check is on the CARDS rather than on a version number, because a purchase
    /// changes exactly one of them and a restock changes all four — and the record's own equality is
    /// the one comparison that cannot fall out of step with what is drawn.
    /// </remarks>
    private void DrawSlots(ShopPresenter presenter, bool open)
    {
        if (_slotList is null)
        {
            return;
        }

        _slotList.Visible = open;

        if (!_drawn.SequenceEqual(presenter.Slots))
        {
            Rebuild(presenter);
        }

        for (var slot = 0; slot < _drawn.Count && slot < _slotList.GetChildCount(); slot++)
        {
            if (_slotList.GetChild(slot) is not Control row)
            {
                continue;
            }

            var card = _drawn[slot];

            row.GetNode<Button>(SlotBuyButtonPath).Disabled = _busy || !open || !card.Buyable;

            var blocked = row.GetNode<Label>(SlotBlockedLabelPath);

            blocked.Text = card.Blocked;
            blocked.Visible = card.Blocked.Length > 0;
        }
    }

    /// <summary>Instantiates one row per slot, captioned and wired to its own index.</summary>
    private void Rebuild(ShopPresenter presenter)
    {
        if (_slotList is null)
        {
            return;
        }

        foreach (var stale in _slotList.GetChildren())
        {
            _slotList.RemoveChild(stale);
            stale.QueueFree();
        }

        _drawn = presenter.Slots;

        if (GD.Load<PackedScene>(SlotRowScenePath) is not { } rowScene)
        {
            GD.PushError($"The shop slot row could not be loaded from '{SlotRowScenePath}'.");

            return;
        }

        foreach (var card in _drawn)
        {
            var row = rowScene.Instantiate<VBoxContainer>();
            var buy = row.GetNode<Button>(SlotBuyButtonPath);

            buy.Text = card.Name + "  —  " + card.Price.ToString(Culture) + "  " + presenter.BuyText;
            ButtonTextColours.ApplyTo(buy, LiveColour, UnavailableColour);

            // Captured by value, because the loop variable would otherwise be shared by all four
            // handlers and every slot would buy the last one.
            var slotIndex = card.SlotIndex;

            buy.Pressed += () => OnBuyPressed(slotIndex);

            _slotList.AddChild(row);
        }
    }

    /// <summary>Numbers are drawn invariantly: a price is a quantity, not prose.</summary>
    private static System.Globalization.CultureInfo Culture =>
        System.Globalization.CultureInfo.InvariantCulture;

    private void OnBuyPressed(int slotIndex) =>
        _ = SubmitAsync(presenter => presenter.BuyAsync(slotIndex, _lifetime));

    private void OnRefreshPressed() => _ = SubmitAsync(presenter => presenter.RefreshAsync(_lifetime));

    private void OnLeavePressed() => _ = SubmitAsync(presenter => presenter.LeaveAsync(_lifetime));

    /// <remarks>
    /// The control is taken out of use for the whole round trip and put back once, on one path. A
    /// second press landing mid-flight would submit a command against a run the first has already
    /// moved.
    /// </remarks>
    private async Task SubmitAsync(Func<ShopPresenter, Task<ShopSubmission>> submit)
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
            GD.PushError($"A shop command failed: {failure}");
        }
        finally
        {
            _busy = false;
            Render();
            LeaveIfTheTileHasCleared();
        }
    }

    /// <summary>Hands back to the board once the run no longer stands on the shop tile.</summary>
    /// <remarks>
    /// 🔒 Read off the run the command answered with, never off which command was pressed: a screen
    /// that left because it had submitted a departure would leave on a departure the rules layer had
    /// refused. A run that could not be read at all stays here and says so — see
    /// <see cref="TheUnreadableRunHasNowhereToGo"/>.
    /// </remarks>
    private void LeaveIfTheTileHasCleared()
    {
        if (_presenter is not { Stage: ShopStage.NotAtAShop } || _board is not { } board ||
            !IsInstanceValid(this) || !IsInsideTree())
        {
            return;
        }

        ShopHandover.Return(this, board);
    }

    /// <summary>
    /// Prints, on one greppable line, what this screen resolved against the run the build shipped.
    /// </summary>
    private static void Report(ShopPresenter presenter) =>
        GD.Print(
            $"{ShopMarker} stage={presenter.Stage} buy_slots={presenter.Slots.Count} " +
            $"gold={presenter.Gold} " +
            $"buyable={presenter.Slots.Count(slot => slot.Buyable)} " +
            $"refresh_offered={presenter.RefreshOffered} host_faulted={presenter.HostFaulted} " +
            $"rejection={presenter.RulesRejection?.ToString() ?? "none"}");
}
