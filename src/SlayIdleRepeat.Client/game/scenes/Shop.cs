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
/// 🔴 <b>There is no buy slot and no refresh control, and their absence is the point.</b> The rules
/// layer refuses every purchase and every refresh because a run carries no offer state at all, so
/// an affordance for either would assert an offer that does not exist — a disabled Buy button says
/// "not right now", and the truth is "not in this build". What is drawn instead is the named reason,
/// permanently, and the one action that is genuinely legal here: leaving.
/// </para>
/// <para>
/// ⚠️ Every type size and colour in <c>Shop.tscn</c> is a per-node override, because the shared
/// theme resource does not exist yet — M8-03's, and to be re-checked rather than re-applied.
/// </para>
/// </remarks>
public partial class Shop : Control
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
    private const string NothingStockedLabelPath = "%NothingStockedLabel";
    private const string StatusLabelPath = "%StatusLabel";
    private const string RejectionLabelPath = "%RejectionLabel";
    private const string LeaveButtonPath = "%LeaveButton";

    /// <summary>A control with something to do.</summary>
    private static readonly Color LiveColour = new(0.93f, 0.93f, 0.96f);

    /// <summary>And one with nothing to do — the same quiet grey every caption is drawn in.</summary>
    private static readonly Color UnavailableColour = new(0.66f, 0.67f, 0.73f);

    private ShopPresenter? _presenter;
    private Board? _board;
    private CancellationToken _lifetime;

    private Label? _titleLabel;
    private Label? _nothingStockedLabel;
    private Label? _statusLabel;
    private Label? _rejectionLabel;
    private Button? _leaveButton;

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
        _nothingStockedLabel = GetNode<Label>(NothingStockedLabelPath);
        _statusLabel = GetNode<Label>(StatusLabelPath);
        _rejectionLabel = GetNode<Label>(RejectionLabelPath);
        _leaveButton = GetNode<Button>(LeaveButtonPath);

        _leaveButton.Pressed += OnLeavePressed;

        // Painted because a Button draws its text by draw mode, and the disabled mode this control
        // spends every round trip in has an engine default of half-transparent grey that no override
        // of font_color reaches.
        ButtonTextColours.ApplyTo(_leaveButton, LiveColour, UnavailableColour);

        SafeAreaInsets.ApplyTo(GetNode<MarginContainer>(SafeAreaPath), GetViewportRect().Size);

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
            _titleLabel is null || _nothingStockedLabel is null || _statusLabel is null ||
            _rejectionLabel is null || _leaveButton is null)
        {
            return;
        }

        _titleLabel.Text = presenter.Title;

        // 🔒 Drawn only while the player is actually standing in the shop. Told to someone whose run
        // could not be read, "this shop has nothing in stock" is a claim about a shop nobody has
        // established they are in — and the status line below already says what did happen.
        _nothingStockedLabel.Text = presenter.NothingStockedText;
        _nothingStockedLabel.Visible = presenter.Stage == ShopStage.Ready;

        _leaveButton.Text = presenter.LeaveText;
        _leaveButton.Disabled = _busy || presenter.Stage != ShopStage.Ready;

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
    /// Prints, on one greppable line, what this screen resolved against the run the build shipped —
    /// including the two numbers that are zero on purpose.
    /// </summary>
    private static void Report(ShopPresenter presenter) =>
        GD.Print(
            $"{ShopMarker} stage={presenter.Stage} buy_slots={ShopPresenter.BuySlotCount} " +
            $"refresh_offered={presenter.RefreshOffered} host_faulted={presenter.HostFaulted} " +
            $"rejection={presenter.RulesRejection?.ToString() ?? "none"}");
}
