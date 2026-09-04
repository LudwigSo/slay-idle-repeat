using Godot;
using SlayIdleRepeat.Client.Game.Presenters;

namespace SlayIdleRepeat.Client.Game.Scenes;

/// <summary>
/// S13 + S14 — the run's ending: a driving adapter over <see cref="RunEndPresenter"/>.
/// </summary>
/// <remarks>
/// <para>
/// It renders what the presenter says and forwards two presses. No rules, no ports, no adapters, and
/// no decision about whether a revive is possible: that is the presenter's answer and this half only
/// draws it. The split is load-bearing rather than stylistic — there is no scene test harness in this
/// repository, so anything decided here is decided where nothing can check it.
/// </para>
/// <para>
/// 🔒 <b>Every figure comes off the presenter and this file computes none of them.</b> The gap between
/// the earned and the paid column IS the completion multiplier, which <c>24</c> §9 wants legible — and a
/// subtraction on this side would be a third implementation of an arithmetic <c>END_RUN</c> already owns.
/// </para>
/// <para>
/// 🔒 <b>The paid column is told from the earned one by more than colour.</b> It has its own heading, its
/// own column and, on a death, a sentence under both saying what dying cost. A player who reads the two
/// hues as one still sees which figure is which — colour alone on a pair of numbers that differ by a
/// multiplier is exactly the failure that leaves somebody believing they were shortchanged.
/// </para>
/// <para>
/// 🔒 <b>The revive is a live button, a sentence, or nothing at all — never a dead control.</b> Which of
/// the three is the presenter's answer: the button for a player who can reach one, the spent sentence for
/// a run that used its one, and nothing for an unentitled player, because the only route that would serve
/// them is not built and a sentence naming it would advertise a feature the game does not have.
/// </para>
/// <para>
/// ⚠️ Every type size, colour, corner and outline in the two <c>RunEnd*.tscn</c> files is a per-node
/// override, because the shared theme resource does not exist yet — it is M8-03's, and these overrides are
/// debt owed to it rather than a naming scheme of this screen's own. The primary button's four-state set
/// is the same one M7-07's three screens and M7-11's inventory carry, which means <b>M8-03 now has five
/// copies of one button to reconcile</b> rather than five unrelated buttons.
/// </para>
/// </remarks>
public partial class RunEnd : Node3D
{
    /// <summary>Where this scene lives, for the screen that instantiates it.</summary>
    public const string ScenePath = "res://game/scenes/RunEnd.tscn";

    /// <summary>The one line a headless run's screen state is read off.</summary>
    private const string RunEndMarker = "SIR_RUN_END_READY";

    private const string CounterRowScenePath = "res://game/scenes/RunEndCounterRow.tscn";

    private const string RowCaptionLabelPath = "CaptionLabel";
    private const string RowValueLabelPath = "ValueLabel";

    private const string SafeAreaPath = "%SafeArea";
    private const string HeadingLabelPath = "%HeadingLabel";
    private const string BankedHeaderPath = "%BankedHeader";
    private const string PayoutHeaderPath = "%PayoutHeader";
    private const string LegendXpLabelPath = "%LegendXpLabel";
    private const string BankedLegendXpValuePath = "%BankedLegendXpValue";
    private const string PayoutLegendXpValuePath = "%PayoutLegendXpValue";
    private const string SoulShardsLabelPath = "%SoulShardsLabel";
    private const string BankedSoulShardsValuePath = "%BankedSoulShardsValue";
    private const string PayoutSoulShardsValuePath = "%PayoutSoulShardsValue";
    private const string MultiplierLabelPath = "%MultiplierLabel";
    private const string FloorItemsLabelPath = "%FloorItemsLabel";
    private const string FloorItemsValuePath = "%FloorItemsValue";
    private const string CounterColumnPath = "%CounterColumn";
    private const string StatusLabelPath = "%StatusLabel";
    private const string RejectionLabelPath = "%RejectionLabel";
    private const string ReviveBlockLabelPath = "%ReviveBlockLabel";
    private const string ReviveButtonPath = "%ReviveButton";
    private const string FinishButtonPath = "%FinishButton";

    /// <summary>A control with something to do.</summary>
    private static readonly Color LiveColour = new(0.93f, 0.93f, 0.96f);

    /// <summary>And one with nothing to do — the same quiet grey every caption is drawn in.</summary>
    private static readonly Color UnavailableColour = new(0.66f, 0.67f, 0.73f);

    private RunEndPresenter? _presenter;
    private Board? _board;
    private CancellationToken _lifetime;

    private Label? _headingLabel;
    private Label? _bankedHeader;
    private Label? _payoutHeader;
    private Label? _legendXpLabel;
    private Label? _bankedLegendXpValue;
    private Label? _payoutLegendXpValue;
    private Label? _soulShardsLabel;
    private Label? _bankedSoulShardsValue;
    private Label? _payoutSoulShardsValue;
    private Label? _multiplierLabel;
    private Label? _floorItemsLabel;
    private Label? _floorItemsValue;
    private VBoxContainer? _counterColumn;
    private Label? _statusLabel;
    private Label? _rejectionLabel;
    private Label? _reviveBlockLabel;
    private Button? _reviveButton;
    private Button? _finishButton;

    private IReadOnlyList<RunEndCounterRow>? _drawnCountersFrom;
    private bool _busy;

    /// <summary>Binds the screen to its driver and the board it was entered from.</summary>
    /// <param name="presenter">Drives this screen.</param>
    /// <param name="board">The board shown again when this screen stands down.</param>
    /// <param name="lifetime">Cancelled when the application shuts down.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public void Drive(RunEndPresenter presenter, Board board, CancellationToken lifetime)
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
        // Resolved once. A scene-unique lookup is a string search of the owner's table each time it is
        // asked, and this screen redraws on every press.
        _headingLabel = GetNode<Label>(HeadingLabelPath);
        _bankedHeader = GetNode<Label>(BankedHeaderPath);
        _payoutHeader = GetNode<Label>(PayoutHeaderPath);
        _legendXpLabel = GetNode<Label>(LegendXpLabelPath);
        _bankedLegendXpValue = GetNode<Label>(BankedLegendXpValuePath);
        _payoutLegendXpValue = GetNode<Label>(PayoutLegendXpValuePath);
        _soulShardsLabel = GetNode<Label>(SoulShardsLabelPath);
        _bankedSoulShardsValue = GetNode<Label>(BankedSoulShardsValuePath);
        _payoutSoulShardsValue = GetNode<Label>(PayoutSoulShardsValuePath);
        _multiplierLabel = GetNode<Label>(MultiplierLabelPath);
        _floorItemsLabel = GetNode<Label>(FloorItemsLabelPath);
        _floorItemsValue = GetNode<Label>(FloorItemsValuePath);
        _counterColumn = GetNode<VBoxContainer>(CounterColumnPath);
        _statusLabel = GetNode<Label>(StatusLabelPath);
        _rejectionLabel = GetNode<Label>(RejectionLabelPath);
        _reviveBlockLabel = GetNode<Label>(ReviveBlockLabelPath);
        _reviveButton = GetNode<Button>(ReviveButtonPath);
        _finishButton = GetNode<Button>(FinishButtonPath);

        ButtonTextColours.ApplyTo(_reviveButton, LiveColour, UnavailableColour);
        ButtonTextColours.ApplyTo(_finishButton, LiveColour, UnavailableColour);

        _reviveButton.Pressed += OnRevivePressed;
        _finishButton.Pressed += OnFinishPressed;

        // Claims the viewport for this screen's own camera and puts its overlay up. Every screen
        // does this on the way in, because every handover in this build leaves the outgoing screen
        // in the tree — hidden, or freed only on the frame after — so two cameras and two overlays
        // are alive at the moment this one becomes the visible screen.
        ScreenStage.Show(this);

        SafeAreaInsets.ApplyTo(GetNode<MarginContainer>(SafeAreaPath), GetViewport().GetVisibleRect().Size);

        _ = StartAsync();
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_reviveButton is not null && IsInstanceValid(_reviveButton))
            {
                _reviveButton.Pressed -= OnRevivePressed;
            }

            if (_finishButton is not null && IsInstanceValid(_finishButton))
            {
                _finishButton.Pressed -= OnFinishPressed;
            }
        }

        base.Dispose(disposing);
    }

    private async Task StartAsync()
    {
        if (_presenter is not { } presenter)
        {
            return;
        }

        await presenter.StartAsync(_lifetime).ConfigureAwait(true);

        Render();
    }

    private void Render()
    {
        // Every node this writes to is checked, not just the one: a scene-unique name that no longer
        // resolves leaves a null behind, and a null-forgiving operator over it would turn a renamed node
        // into a crash here instead of a blank label.
        if (_presenter is not { } presenter || !IsInstanceValid(this) || !IsInsideTree() ||
            _headingLabel is null || _bankedHeader is null || _payoutHeader is null ||
            _legendXpLabel is null || _bankedLegendXpValue is null || _payoutLegendXpValue is null ||
            _soulShardsLabel is null || _bankedSoulShardsValue is null ||
            _payoutSoulShardsValue is null || _multiplierLabel is null ||
            _floorItemsLabel is null || _floorItemsValue is null || _counterColumn is null ||
            _statusLabel is null || _rejectionLabel is null || _reviveBlockLabel is null ||
            _reviveButton is null || _finishButton is null)
        {
            return;
        }

        _headingLabel.Text = presenter.HeadingText;
        _headingLabel.Visible = _headingLabel.Text.Length > 0;

        _bankedHeader.Text = presenter.BankedLabel;
        _payoutHeader.Text = presenter.PayoutLabel;

        _legendXpLabel.Text = presenter.LegendXpLabel;
        _bankedLegendXpValue.Text = presenter.BankedLegendXpValue;
        _payoutLegendXpValue.Text = presenter.PayoutLegendXpValue;

        _soulShardsLabel.Text = presenter.SoulShardsLabel;
        _bankedSoulShardsValue.Text = presenter.BankedSoulShardsValue;
        _payoutSoulShardsValue.Text = presenter.PayoutSoulShardsValue;

        // 🔒 The sentence that makes a paid figure below an earned one legible rather than alarming, and
        // it is a THIRD channel beside the two columns and their two hues.
        _multiplierLabel.Text = presenter.DeathCostsRewardsText;
        _multiplierLabel.Visible = _multiplierLabel.Text.Length > 0;

        _floorItemsLabel.Text = presenter.FloorItemsLabel;
        _floorItemsValue.Text = presenter.FloorItemsValue;

        RenderCounters(presenter);

        // Hidden rather than blanked once it has nothing to say: an empty label still claims a full line
        // of height, so a blank one is a sentence a player can see room for and cannot read. Both sit
        // OUTSIDE the scrolling band — a refusal a scroll position can hide is the same silence as never
        // printing it.
        _statusLabel.Text = presenter.StatusText;
        _statusLabel.Visible = _statusLabel.Text.Length > 0;

        _rejectionLabel.Text = presenter.RejectionText;
        _rejectionLabel.Visible = _rejectionLabel.Text.Length > 0;

        // 🔒 The button and the sentence are mutually exclusive by construction: whichever is drawn, the
        // other is gone. A visible-but-disabled revive beside a sentence explaining it would be the dead
        // control the sentence exists to replace.
        _reviveButton.Text = presenter.ReviveText;
        _reviveButton.Visible = presenter.ReviveAvailable;
        _reviveButton.Disabled = !presenter.ReviveAvailable || _busy;

        _reviveBlockLabel.Text = presenter.ReviveBlockText;
        _reviveBlockLabel.Visible = !presenter.ReviveAvailable && _reviveBlockLabel.Text.Length > 0;

        _finishButton.Text = presenter.FinishText;
        _finishButton.Disabled = _busy;

        Report(presenter);
    }

    /// <summary>Draws the footer's counters, rebuilding only when the list itself has changed.</summary>
    private void RenderCounters(RunEndPresenter presenter)
    {
        if (_counterColumn is not { } column ||
            ReferenceEquals(_drawnCountersFrom, presenter.Counters))
        {
            return;
        }

        Clear(column);

        if (GD.Load<PackedScene>(CounterRowScenePath) is not { } rowScene)
        {
            GD.PushError(
                "The run-end counter row scene did not load, so the DROP_RUN counters are not drawn. " +
                "24 §1.1's Disclosure rule is a store-policy requirement on both platforms, so this is " +
                "a defect rather than a degradation.");

            return;
        }

        foreach (var counter in presenter.Counters)
        {
            var row = rowScene.Instantiate<HBoxContainer>();

            row.GetNode<Label>(RowCaptionLabelPath).Text = counter.Label;
            row.GetNode<Label>(RowValueLabelPath).Text = counter.Value;

            column.AddChild(row);
        }

        _drawnCountersFrom = presenter.Counters;
    }

    /// <summary>Empties a container now, rather than at the end of the frame.</summary>
    /// <remarks>
    /// 🔒 <c>QueueFree</c> alone defers removal, so a child freed and a child added in the same frame are
    /// both in the tree and both laid out until it ends. Detaching first is what keeps a rebuilt column
    /// from briefly drawing two sets of rows over each other.
    /// </remarks>
    private static void Clear(Node container)
    {
        foreach (var child in container.GetChildren())
        {
            container.RemoveChild(child);
            child.QueueFree();
        }
    }

    private void OnRevivePressed() =>
        _ = SubmitAsync(presenter => presenter.ReviveAsync(_lifetime), RunEndExit.BackIntoTheRun);

    private void OnFinishPressed() =>
        _ = SubmitAsync(presenter => presenter.FinishAsync(_lifetime), RunEndExit.OffTheRunForGood);

    /// <summary>
    /// Submits one command and stands the screen down once it is accepted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>Both accepted commands leave this screen, and they leave it for opposite places.</b> A revive
    /// puts the run back into the fight it lost, so the board is shown and read again and the run carries
    /// on. <c>END_RUN</c> closes the run for good, so the board is freed and the starting menu comes
    /// back — a finished run has no screen left to be played on. A refusal keeps the screen, because a
    /// refusal is something the player has to be able to read.
    /// </para>
    /// <para>
    /// 🔒 <b>Which of the two it was is carried by the CALLER, not inferred from a further read.</b> The
    /// two presses already know which command they submitted, and a second read of the run to recover it
    /// would be a projection derived twice — and derived at exactly the moment the run has just moved,
    /// where the two states it could be found in are the two this decision is between.
    /// </para>
    /// </remarks>
    private async Task SubmitAsync(
        Func<RunEndPresenter, Task<RunEndSubmission>> submit,
        RunEndExit exit)
    {
        if (_busy || _presenter is not { } presenter)
        {
            return;
        }

        _busy = true;

        RunEndSubmission outcome;

        try
        {
            Render();

            outcome = await submit(presenter).ConfigureAwait(true);
        }
        finally
        {
            _busy = false;
        }

        if (outcome != RunEndSubmission.Submitted)
        {
            Render();

            return;
        }

        if (_board is { } board && IsInstanceValid(this) && IsInsideTree())
        {
            if (exit == RunEndExit.OffTheRunForGood)
            {
                RunEndHandover.Leave(this, board);
            }
            else
            {
                RunEndHandover.Return(this, board);
            }

            return;
        }

        // A board freed underneath a screen mid-command is the ordinary way a shutdown on a handset
        // happens. The command was accepted, so there is nothing to retry and nothing to report — the
        // screen simply has nowhere to hand back to.
        Render();
    }

    /// <summary>Where an accepted command from this screen leaves it for.</summary>
    /// <remarks>
    /// 🔒 Named rather than passed as a bare flag, because the two are not degrees of the same thing:
    /// one hands a live run back to the screen it is played on, and the other tears that screen down.
    /// A <c>bool</c> at the call site would read as a preference either way round.
    /// </remarks>
    private enum RunEndExit
    {
        /// <summary>
        /// The revive's, and the only one that leaves a run to keep playing: <c>02</c> §6 puts the hero
        /// back into the fight that killed them, on the board that fight is on.
        /// </summary>
        BackIntoTheRun = 1,

        /// <summary>
        /// <c>END_RUN</c>'s, whichever way the run ended. The run is closed, its rewards are banked, and
        /// neither it nor its board is ever entered again — so the starting menu comes back and both
        /// screens stand down.
        /// </summary>
        OffTheRunForGood = 2,
    }

    /// <summary>
    /// The one line a headless run's screen state is read off, carrying what a case would assert on.
    /// </summary>
    /// <remarks>
    /// The figures are in it because the claim worth checking from outside the process is that a real run
    /// produced a real tally — a marker naming only the stage would be printed just as happily by a screen
    /// that drew nothing.
    /// </remarks>
    private static void Report(RunEndPresenter presenter) =>
        GD.Print(
            $"{RunEndMarker} stage={presenter.Stage} " +
            $"outcome={presenter.Outcome?.ToString() ?? "none"} " +
            $"revive={presenter.ReviveAvailable} " +
            $"banked_xp={presenter.BankedLegendXpValue} paid_xp={presenter.PayoutLegendXpValue} " +
            $"banked_shards={presenter.BankedSoulShardsValue} " +
            $"paid_shards={presenter.PayoutSoulShardsValue} " +
            $"floor_items={presenter.FloorItemsValue} counters={presenter.Counters.Count} " +
            $"host_faulted={presenter.HostFaulted} " +
            $"rejection={presenter.RulesRejection?.ToString() ?? "none"}");
}
