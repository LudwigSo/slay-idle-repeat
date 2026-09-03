using System.Globalization;
using Godot;
using SlayIdleRepeat.Client.Composition;
using SlayIdleRepeat.Client.Game.Presenters;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Client.Game.Scenes;

/// <summary>
/// S03 — the home screen: a driving adapter over <see cref="HomePresenter"/>.
/// </summary>
/// <remarks>
/// <para>
/// It renders what the presenter says and forwards one press and one hold. No rules, no ports, no
/// adapters: the boot screen composes both presenters and hands them over, and this half owns only
/// the things that genuinely need the engine — the safe-area query, the drawn ground, the buttons,
/// the long-press gesture and the handover to the picker.
/// </para>
/// <para>
/// 🔒 <b>A HUD of tiles, and every tile is a number the rows literally hold.</b> Under the name and
/// the Legend Level sit six tiles — Energy, Reserve, Crowns, Soul Shards, Power and the furthest
/// chapter cleared — each an icon, a value and a caption, and below them a run panel that appears
/// only while there is a run to go back to: its chapter and stage, the hero's hit points and its Gold.
/// A long press on any tile shows the exact figures; letting go puts the shortened ones back. There is
/// still no Energy maximum or denominator, no regeneration countdown, no Legend-XP percentage and no
/// run cost, because the presenter exposes none of them and could not without copying a formula the
/// rules already own. Daily quests, ad widgets, chest pity, event and guild cards, the inbox, the
/// account-link banner and the bottom navigation belong to later milestones and are absent rather
/// than stubbed.
/// </para>
/// <para>
/// ⚠️ <b>The hero diorama is a modelled rogue, and it is still not a RULED asset.</b>
/// <c>Hero.tscn</c> frames <c>game/art/chr_hero_rogue.glb</c> — a hooded, light-armoured rogue
/// modelled in Blender (source at <c>assets/source/hero_rogue.blend</c>, built by
/// <c>assets/source/hero_kit.py</c>), 22k triangles, one material over a baked
/// base-colour/ORM/normal set, so it draws in a single call. It holds D5's chibi proportion and
/// the warm saturated palette, and it is the first real 3D content in the build rather than a
/// shape standing in for one.
/// </para>
/// <para>
/// 🔒 <b>What it is holding is no longer part of it.</b> The rogue used to carry two daggers
/// welded into the character mesh, so a hero holding two daggers was the only hero there could be.
/// The model now carries two socket nodes instead, and each weapon — <c>wpn_sword.glb</c>,
/// <c>wpn_dagger.glb</c>, roughly 1.5k triangles and one draw call each — is a scene mounted into
/// one of them. This screen states the starting loadout in <c>Hero.tscn</c> and nothing more;
/// <see cref="Hero"/> owns the mounting, and a screen that wants a different weapon asks it rather
/// than exporting a second character. The kit is deliberately small: two weapons are enough to
/// prove a socket holds more than the thing it was modelled around.
/// </para>
/// <para>
/// 🔴 <b>What it is NOT is a discharge of the three §C6 rulings D61 reopened.</b> It carries no
/// outline shell, because `15` §A3 fixes the reference height against a render height that no
/// longer exists; it carries no per-actor light rig, because whether one exists at all is the
/// second reopened question; and it is absent from <c>asset_manifest_art.json</c>, whose rows are
/// 2D-era and whose every edit moves the <c>ContentSnapshot</c> hash. It was modelled to a brief,
/// not to a ruling, and the rulings still owe themselves answers. The two weapons are absent from
/// the manifest for the same reason and add two more rows to whatever eventually registers the
/// first — a kit that grows makes that debt grow with it rather than discharging any of it.
/// </para>
/// <para>
/// ⚠️ <b>Nothing here is metallic, and that is a lighting decision rather than an art one.</b> The
/// brass and the blades are bright albedo at low roughness, not metal: this build lights its 3D
/// with one directional key, one fill and flat ambient, and has no reflection probe or sky — a true
/// metal has nothing to reflect in it and renders black. The gloss is the light, not the material.
/// </para>
/// <para>
/// 🔒 <b>It carries no light and no environment of its own.</b> A viewport has one
/// <see cref="WorldEnvironment"/> and this build's lives on <see cref="AppRoot"/> for the life of the
/// application, with the key light beside it — the same reason <see cref="ScreenStage"/> gives for
/// keeping it off the screens. What this screen owns of the 3D world is its content and its framing.
/// </para>
/// <para>
/// 🔒 <b>The shared theme exists now, and this screen consumes it.</b> <c>SlayTheme.tres</c> is
/// assigned once, on the root of the overlay, and every control in <c>Home.tscn</c> names a type
/// variation from it — the tiles, the values, the captions, the title and both buttons — rather than
/// carrying a colour, a size or a stylebox of its own. What the scene still states per node is
/// layout: margins, gutters and minimum sizes. The fonts are still the engine's default face, so the
/// sizes the theme carries were chosen against it and have to be re-checked when the real faces land.
/// </para>
/// <para>
/// Both primary actions now reach a screen: START opens the picker, and CONTINUE opens the board
/// the run is played on — unless the read named no run to resume, which is reported rather than
/// guessed at. See <see cref="TheRunToResumeWasNotNamed"/>.
/// </para>
/// </remarks>
public partial class Home : Node3D
{
    /// <summary>Where this scene lives, for the screen that instantiates it.</summary>
    public const string ScenePath = "res://game/scenes/Home.tscn";

    /// <summary>
    /// ⚠️ Named so a resume that cannot happen is still reported. S05 exists now, so an open run
    /// does have a screen to go back to — but the decision to resume is taken from the run the
    /// profile read named, and a read that answered <c>ContinueRun</c> without naming one is a
    /// state nothing should paper over. Nothing is navigated to in that case: a board opened on a
    /// run nobody named would read the wrong run, or none.
    /// </summary>
    private const string TheRunToResumeWasNotNamed =
        "The profile read answered that a run is open but carried no run id, so there is nothing " +
        "to resume onto. The decision above is reported, not taken.";

    /// <summary>
    /// The one line a headless run's screen state is read off. Distinctive on purpose: a decision
    /// reached against real content has to be greppable out of an engine log full of everything
    /// else, the way the boot's cold-start measurement already is.
    /// </summary>
    private const string HomeMarker = "SIR_HOME_READY";

    /// <summary>
    /// How long a tile is held before its numbers show in full. The same length the perk draft's
    /// readouts use, because it is the same gesture asking the same question of a different number.
    /// </summary>
    private const double LongPressSeconds = 0.4;

    private const string SafeAreaPath = "%SafeArea";
    private const string HeaderPath = "%Header";
    private const string TilesPath = "%Tiles";
    private const string RunPanelPath = "%RunPanel";
    private const string DisplayNameLabelPath = "%DisplayNameLabel";
    private const string LegendLevelLabelPath = "%LegendLevelLabel";
    private const string LegendLevelValuePath = "%LegendLevelValue";
    private const string EnergyLabelPath = "%EnergyLabel";
    private const string EnergyValuePath = "%EnergyValue";
    private const string EnergyReserveLabelPath = "%EnergyReserveLabel";
    private const string EnergyReserveValuePath = "%EnergyReserveValue";
    private const string CrownsLabelPath = "%CrownsLabel";
    private const string CrownsValuePath = "%CrownsValue";
    private const string SoulShardsLabelPath = "%SoulShardsLabel";
    private const string SoulShardsValuePath = "%SoulShardsValue";
    private const string PowerLabelPath = "%PowerLabel";
    private const string PowerValuePath = "%PowerValue";
    private const string ProgressLabelPath = "%ProgressLabel";
    private const string HighestClearValuePath = "%HighestClearValue";
    private const string RunChapterValuePath = "%RunChapterValue";
    private const string StageLabelPath = "%StageLabel";
    private const string RunStageValuePath = "%RunStageValue";
    private const string RunHitPointsValuePath = "%RunHitPointsValue";
    private const string GoldLabelPath = "%GoldLabel";
    private const string RunGoldValuePath = "%RunGoldValue";
    private const string StatusLabelPath = "%StatusLabel";
    private const string ActionButtonPath = "%ActionButton";
    private const string GearButtonPath = "%GearButton";

    /// <summary>The surfaces a long press is read off: the six tiles and the run panel.</summary>
    private static readonly string[] HeldSurfacePaths =
    [
        "%EnergyTile", "%ReserveTile", "%CrownsTile", "%SoulShardsTile", "%PowerTile", "%ProgressTile",
        RunPanelPath,
    ];

    private HomePresenter? _presenter;
    private ChapterSelectPresenter? _picker;
    private Func<RunId, ComposedBoardScreen>? _board;

    private Func<ComposedInventoryScreen>? _gear;
    private CancellationToken _lifetime;

    private Control? _header;
    private Control? _tiles;
    private Control? _runPanel;
    private Label? _displayNameLabel;
    private Label? _legendLevelLabel;
    private Label? _legendLevelValue;
    private Label? _energyLabel;
    private Label? _energyValue;
    private Label? _energyReserveLabel;
    private Label? _energyReserveValue;
    private Label? _crownsLabel;
    private Label? _crownsValue;
    private Label? _soulShardsLabel;
    private Label? _soulShardsValue;
    private Label? _powerLabel;
    private Label? _powerValue;
    private Label? _progressLabel;
    private Label? _highestClearValue;
    private Label? _runChapterValue;
    private Label? _stageLabel;
    private Label? _runStageValue;
    private Label? _runHitPointsValue;
    private Label? _goldLabel;
    private Label? _runGoldValue;
    private Label? _statusLabel;
    private Button? _actionButton;
    private Button? _gearButton;

    private readonly List<Control> _heldSurfaces = [];

    /// <summary>Whether a finger is down on a tile — the one fact the hold timer checks when it fires.</summary>
    private bool _holdingATile;

    /// <summary>
    /// Takes both presenters the composition root built, and the token the app shuts down through.
    /// </summary>
    /// <remarks>
    /// The picker's presenter arrives here rather than being built on the press, because it reads
    /// the same profile this screen does and a read started by a tap is a tap that waits. This
    /// screen starts it and forwards it; it never composes it.
    /// </remarks>
    /// <param name="presenter">Drives this screen.</param>
    /// <param name="picker">Drives the screen the primary action opens.</param>
    /// <param name="board">
    /// Builds the board for a run. A factory rather than a presenter, because which run this screen
    /// resumes is not known until the profile read answers — and because the picker it hands on
    /// needs the same factory for the run its own confirm starts.
    /// </param>
    /// <param name="gear">Builds the Inventory screen the gear button opens.</param>
    /// <param name="lifetime">Cancelled when the application shuts down.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public void Drive(
        HomePresenter presenter,
        ChapterSelectPresenter picker,
        Func<RunId, ComposedBoardScreen> board,
        Func<ComposedInventoryScreen> gear,
        CancellationToken lifetime)
    {
        ArgumentNullException.ThrowIfNull(presenter);
        ArgumentNullException.ThrowIfNull(picker);
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(gear);

        _presenter = presenter;
        _picker = picker;
        _board = board;
        _gear = gear;
        _lifetime = lifetime;
    }

    /// <summary>Shows this screen again and reads the profile afresh, for a run that has ended.</summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>The read is the point, not the showing</b> — the same thing <c>Board.Resume</c> says about
    /// its own return, and here it is what keeps the primary action honest. A run that has ended banked
    /// its payout and closed itself, so the profile behind this screen is a different row from the one it
    /// drew: the Legend Level and both Energy amounts have moved, and the decision has moved with them
    /// from CONTINUE to START. Un-hiding without reading again would offer to resume the run that has
    /// just ended, onto a board that has been freed.
    /// </para>
    /// <para>
    /// 🔒 The picker's read is repeated with it, because <see cref="StartAsync"/> starts both: the run
    /// that ended may have cleared the chapter that opens the next rung of the ladder, and it spent the
    /// Energy that gates entering one at all.
    /// </para>
    /// <para>
    /// 🔒 <b>This screen is never freed, which is what makes it the thing to come back to.</b> It is
    /// hidden for the life of the application while a run is in front of it — see
    /// <see cref="BoardHandover"/>, where reusing Home and freeing everything in front of it is one
    /// decision — so its subscriptions are still connected and there is no second read to pay for.
    /// </para>
    /// </remarks>
    public void Resume()
    {
        // Validity before tree membership: asking a freed node whether it is inside the tree is itself
        // the crash, and a run that ends as the application shuts down is the ordinary case here.
        if (!IsInstanceValid(this) || !IsInsideTree())
        {
            return;
        }

        ScreenStage.Show(this);

        _ = StartAsync();
    }

    /// <inheritdoc/>
    public override void _Ready()
    {
        // Resolved once. A scene-unique lookup is a string search of the owner's table each time it
        // is asked, and this screen redraws whenever the read behind it moves.
        _header = GetNode<Control>(HeaderPath);
        _tiles = GetNode<Control>(TilesPath);
        _runPanel = GetNode<Control>(RunPanelPath);
        _displayNameLabel = GetNode<Label>(DisplayNameLabelPath);
        _legendLevelLabel = GetNode<Label>(LegendLevelLabelPath);
        _legendLevelValue = GetNode<Label>(LegendLevelValuePath);
        _energyLabel = GetNode<Label>(EnergyLabelPath);
        _energyValue = GetNode<Label>(EnergyValuePath);
        _energyReserveLabel = GetNode<Label>(EnergyReserveLabelPath);
        _energyReserveValue = GetNode<Label>(EnergyReserveValuePath);
        _crownsLabel = GetNode<Label>(CrownsLabelPath);
        _crownsValue = GetNode<Label>(CrownsValuePath);
        _soulShardsLabel = GetNode<Label>(SoulShardsLabelPath);
        _soulShardsValue = GetNode<Label>(SoulShardsValuePath);
        _powerLabel = GetNode<Label>(PowerLabelPath);
        _powerValue = GetNode<Label>(PowerValuePath);
        _progressLabel = GetNode<Label>(ProgressLabelPath);
        _highestClearValue = GetNode<Label>(HighestClearValuePath);
        _runChapterValue = GetNode<Label>(RunChapterValuePath);
        _stageLabel = GetNode<Label>(StageLabelPath);
        _runStageValue = GetNode<Label>(RunStageValuePath);
        _runHitPointsValue = GetNode<Label>(RunHitPointsValuePath);
        _goldLabel = GetNode<Label>(GoldLabelPath);
        _runGoldValue = GetNode<Label>(RunGoldValuePath);
        _statusLabel = GetNode<Label>(StatusLabelPath);
        _actionButton = GetNode<Button>(ActionButtonPath);
        _gearButton = GetNode<Button>(GearButtonPath);

        _actionButton.Pressed += OnActionPressed;
        _gearButton.Pressed += OnGearPressed;

        // The tiles and the run panel answer a hold rather than a press. Each stops the pointer in
        // the scene file for it; a panel that let the pointer through would leave the exact figures
        // unreachable with nothing red anywhere.
        foreach (var path in HeldSurfacePaths)
        {
            var surface = GetNode<Control>(path);

            surface.GuiInput += OnTileInput;
            _heldSurfaces.Add(surface);
        }

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
    /// The matching half of the subscriptions in <c>_Ready</c>. The controls are children and die
    /// with this node either way, but a handler left connected across a scene that is merely
    /// detached and re-added would fire twice, and once is the whole contract of a primary action.
    /// </remarks>
    public override void _ExitTree()
    {
        if (_actionButton is not null && IsInstanceValid(_actionButton))
        {
            _actionButton.Pressed -= OnActionPressed;
        }

        if (_gearButton is not null && IsInstanceValid(_gearButton))
        {
            _gearButton.Pressed -= OnGearPressed;
        }

        foreach (var surface in _heldSurfaces)
        {
            if (IsInstanceValid(surface))
            {
                surface.GuiInput -= OnTileInput;
            }
        }

        _heldSurfaces.Clear();

        // Cleared here as well as on release: a hold timer already running belongs to the tree and
        // fires whether this screen is still there or not.
        _holdingATile = false;
    }

    /// <remarks>
    /// Nothing awaits this task, so its exceptions have nowhere to surface: the whole body is
    /// guarded, or a continuation that threw would leave a screen that silently stopped moving.
    /// The two reads run one after the other rather than together — they go to the same host over
    /// the same local cache, and starting a second before the first answers buys nothing here.
    /// </remarks>
    private async Task StartAsync()
    {
        try
        {
            var presenter = _presenter;
            var picker = _picker;

            if (presenter is null || picker is null)
            {
                GD.PushError(
                    "The home screen entered the tree with no presenter. Only the boot screen may " +
                    "instantiate it, and it must call Drive before adding it to the tree.");

                return;
            }

            // No ConfigureAwait(false): the continuation writes to nodes, and only the thread the
            // engine runs the scene tree on may do that.
            await presenter.StartAsync(_lifetime);
            await picker.StartAsync(_lifetime);

            Render();
            Report(presenter, picker);
        }
        catch (Exception failure)
        {
            GD.PushError($"The home screen stopped unexpectedly: {failure}");
        }
    }

    /// <summary>Writes the presenter's state into the scene, if the scene is still there to write into.</summary>
    private void Render()
    {
        var presenter = _presenter;

        // Validity before tree membership: asking a freed node whether it is inside the tree is
        // itself the crash, and a shutdown during a slow read is the ordinary case on a handset.
        if (presenter is null || !IsInstanceValid(this) || !IsInsideTree() || _actionButton is null)
        {
            return;
        }

        // The one predicate the whole screen turns on: whether the read has produced a profile
        // there is anything to say about.
        //
        // 🔴 ASKED of the presenter rather than enumerated here, and that is the fix rather than a
        // tidy-up. This line used to list the decisions itself — and when a sixth arrived it matched
        // none of them, so the screen hid a profile it had and disabled the one action that would
        // have settled a lapsed run. A scene has no behavioural test to catch that; the presenter
        // does.
        var carried = presenter.ProfileCarried;

        // 🔒 The profile's numbers are drawn only when there ARE numbers. Before the read answers,
        // and in the two states where it never will, the name is empty and every amount is still the
        // zero an unset value carries — and "Energy 0" told to a player who has plenty is not a
        // placeholder, it is a plausible value in a hole, which is the one thing this codebase
        // refuses to put on a screen anywhere else. The header and the tiles leave instead, the
        // status line below says which of the three states this is, and nothing is invented. Two of
        // those states never end, so this is not a flicker on the way to the truth: it is what the
        // screen looks like for as long as it is up. The run panel has one more condition: a run to
        // go back to.
        Show(_header, carried);
        Show(_tiles, carried);
        Show(_runPanel, carried && presenter.RunProgress is not null);

        RenderHeader(presenter);
        RenderTiles(presenter);
        RenderRunPanel(presenter);

        // Hidden rather than blanked once there is nothing left to say, which is the same thing the
        // picker does with the same line and for the same reason: an empty label still claims a
        // full line of height, so a blank one is a sentence a player can see room for and cannot
        // read. Hiding it also hands the space back to the frame above the primary action.
        var status = presenter.StatusText;

        Write(_statusLabel, status);
        Show(_statusLabel, status.Length > 0);

        _actionButton.Text = presenter.ActionText;
        Write(_gearButton, presenter.GearText);

        // A read that has not answered, or that answered with no profile, leaves an action with
        // nothing to do. Disabled rather than hidden: a primary action that vanishes reads as a
        // screen that lost its purpose, while a disabled one under the status line reads as a
        // screen waiting, which is what it is.
        _actionButton.Disabled = !carried;
    }

    private void RenderHeader(HomePresenter presenter)
    {
        Write(_displayNameLabel, presenter.DisplayName);
        Write(_legendLevelLabel, presenter.LegendLevelLabel);
        Write(_legendLevelValue, presenter.LegendLevel.ToString(CultureInfo.InvariantCulture));
    }

    /// <remarks>
    /// Each value is the presenter's text, never a number formatted here: which form a number takes —
    /// shortened or in full, blank or spelt — is the presenter's answer, and the power tile in
    /// particular stays empty rather than reading "0" when there is no reading behind it.
    /// </remarks>
    private void RenderTiles(HomePresenter presenter)
    {
        Write(_energyValue, presenter.EnergyText);
        Write(_energyLabel, presenter.EnergyLabel);
        Write(_energyReserveValue, presenter.EnergyReserveText);
        Write(_energyReserveLabel, presenter.EnergyReserveLabel);
        Write(_crownsValue, presenter.CrownsText);
        Write(_crownsLabel, presenter.CrownsLabel);
        Write(_soulShardsValue, presenter.SoulShardsText);
        Write(_soulShardsLabel, presenter.SoulShardsLabel);
        Write(_powerValue, presenter.PowerText);
        Write(_powerLabel, presenter.PowerLabel);
        Write(_highestClearValue, presenter.HighestClearText);
        Write(_progressLabel, presenter.ProgressLabel);
    }

    private void RenderRunPanel(HomePresenter presenter)
    {
        Write(_runChapterValue, presenter.RunProgress?.ChapterName ?? "");
        Write(_stageLabel, presenter.StageLabel);
        Write(_runStageValue, presenter.RunStageText);
        Write(_runHitPointsValue, presenter.RunHitPointsText);
        Write(_runGoldValue, presenter.RunGoldText);
        Write(_goldLabel, presenter.GoldLabel);
    }

    /// <summary>
    /// Writes one control's text, or nothing when the control is not there to write into.
    /// </summary>
    /// <remarks>
    /// A scene-unique name that no longer resolves leaves a null behind, and a null-forgiving operator
    /// over it would turn a renamed node into a crash here instead of a blank label. Every write goes
    /// through this so the check is stated once.
    /// </remarks>
    private static void Write(Label? label, string text)
    {
        if (label is not null && IsInstanceValid(label))
        {
            label.Text = text;
        }
    }

    private static void Write(Button? button, string text)
    {
        if (button is not null && IsInstanceValid(button))
        {
            button.Text = text;
        }
    }

    private static void Show(Control? control, bool visible)
    {
        if (control is not null && IsInstanceValid(control))
        {
            control.Visible = visible;
        }
    }

    /// <summary>
    /// A tile held down shows its exact figures; letting go puts the shortened ones back.
    /// </summary>
    /// <remarks>
    /// 🔒 Only the gesture is here. Which form each number takes is the presenter's answer, so what
    /// this file decides is the single fact an engine event carries — whether the finger is down —
    /// and nothing about how a number is written.
    /// </remarks>
    /// <param name="event">The input one of the tiles received.</param>
    private void OnTileInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } mouse:
                HoldTile(mouse.Pressed);
                break;

            case InputEventScreenTouch touch:
                HoldTile(touch.Pressed);
                break;
        }
    }

    private void HoldTile(bool pressed)
    {
        _holdingATile = pressed;

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
            GD.PushError("A home tile was held while the screen was outside the tree.");

            return;
        }

        hold.Timeout += OnHoldElapsed;
    }

    /// <remarks>
    /// The flag is read FIRST, and it is cleared on teardown as well as on release: this timer
    /// belongs to the tree and fires whether or not the screen that asked for it is still there.
    /// </remarks>
    private void OnHoldElapsed()
    {
        if (!_holdingATile || !IsInstanceValid(this) || _presenter is not { } presenter)
        {
            return;
        }

        presenter.RevealFullValues();
        Render();
    }

    /// <summary>Opens S16, the gear stock and the equip path.</summary>
    /// <remarks>
    /// 🔒 <b>Between runs is the only place this belongs, and Home is where a player already is.</b>
    /// M7's exit criterion reads *"gear banked and equipped between runs"*, and the equipping half had
    /// nowhere to happen: a run freezes its loadout at <c>START_RUN</c> (`07` §4), so a bag opened
    /// mid-run could change nothing about the fight in progress. ⚠️ `13` §1.1 also reaches S16 from the
    /// Hero screen, which is M9's — <c>InventoryHandover</c> takes a <c>Node3D</c> rather than a named
    /// screen so that arrival is a caller rather than a change here.
    /// </remarks>
    private void OnGearPressed()
    {
        if (_gear is not { } compose)
        {
            return;
        }

        _ = InventoryHandover.Show(this, compose(), _lifetime);
    }

    /// <remarks>
    /// The two live decisions go different ways, and only one of them has anywhere to go. Every
    /// other decision leaves the button disabled, so this cannot be reached from them.
    /// </remarks>
    private void OnActionPressed()
    {
        var presenter = _presenter;

        if (presenter is null)
        {
            return;
        }

        // Both decisions that have no run to resume go the same way, and a lapsed one is not a
        // special case of starting: START_RUN is the command that settles it, so the picker is
        // exactly where it belongs.
        if (presenter.Decision is HomeContinueDecision.StartNewRun or HomeContinueDecision.RunLapsed)
        {
            ShowChapterSelect();

            return;
        }

        if (presenter.Decision != HomeContinueDecision.ContinueRun)
        {
            return;
        }

        if (presenter.ContinuableRun is not { } run)
        {
            GD.PushError($"Continue was taken · {TheRunToResumeWasNotNamed}");

            return;
        }

        ShowBoard(run);
    }

    /// <summary>Puts the board for one run beside this screen and stands down.</summary>
    /// <remarks>
    /// 🔒 This instantiates unconditionally, and that is now the point rather than a caveat: a run
    /// that ends frees its board, so the next CONTINUE builds a board for a different run with its own
    /// presenters and its own read. This screen is passed twice — once as the screen standing down and
    /// once as the starting menu the run's ending leads back to — and here they are the same node, which
    /// <see cref="BoardHandover"/> says is true of this caller and not of the picker.
    /// </remarks>
    private void ShowBoard(RunId run)
    {
        if (!IsInstanceValid(this) || !IsInsideTree())
        {
            return;
        }

        if (_board is not { } board)
        {
            GD.PushError(
                $"Continue was taken for run {run} and this screen has no way to build its board. " +
                "Only the boot screen may instantiate it, and it must pass the board factory.");

            return;
        }

        BoardHandover.Show(this, this, board(run), _lifetime);
    }

    /// <summary>Puts the chapter picker beside this screen and stands down.</summary>
    /// <remarks>
    /// <para>
    /// The picker is added to this screen's own parent rather than to this screen, and this screen
    /// is hidden — the same handover shape the application root uses for the boot screen. A child
    /// would be drawn inside a ground this screen still owns, and freeing the outgoing screen from
    /// inside its own handler is a node destroying the object the call is running on.
    /// </para>
    /// <para>
    /// 🔒 <b>This method instantiates unconditionally, and the picker is what makes that safe: it
    /// FREES ITSELF once it has handed a board over.</b> A back path exists now — a run that ends comes
    /// back here — so a picker that stayed in the tree would be joined by a second one on the next
    /// START, with its own presenter and its own read, and one more on every run after that. Freeing it
    /// costs nothing, because a spent picker holds nothing: its presenter is this screen's and outlives
    /// it, and the pick it was about has become a run. <see cref="BoardHandover"/> states that
    /// free-or-reuse decision in one place for both screens a run is entered from.
    /// </para>
    /// </remarks>
    private void ShowChapterSelect()
    {
        if (!IsInstanceValid(this) || !IsInsideTree() || _picker is not { } picker || _board is null)
        {
            return;
        }

        var parent = GetParent();

        if (parent is null)
        {
            GD.PushError("The home screen has no parent to hand the chapter picker to.");

            return;
        }

        var scene = GD.Load<PackedScene>(ChapterSelect.ScenePath);

        if (scene is null)
        {
            // Load answers null rather than throwing when the resource is missing or its import
            // cannot be read, so an unnamed null reference is all a caller gets unless it says so.
            GD.PushError($"The chapter picker could not be loaded from '{ChapterSelect.ScenePath}'.");

            return;
        }

        var picked = scene.Instantiate<ChapterSelect>();

        picked.Drive(picker, _board, this, _lifetime);

        ScreenStage.Hide(this);

        parent.AddChild(picked);
    }

    /// <summary>
    /// Prints, on one greppable line, what this screen resolved against the content the build
    /// actually shipped — and what the picker it prepared resolved with it.
    /// </summary>
    /// <remarks>
    /// One line rather than two, because a launch produces exactly one of each and the interesting
    /// claim spans both: that composition, the content load, the profile read, the power reading and
    /// the gating evaluation all ran, in the engine, against real authored data. The picker's half is
    /// described by the picker's own file so the format lives beside the screen it is about.
    /// </remarks>
    private static void Report(HomePresenter home, ChapterSelectPresenter picker)
    {
        var power = home.Power?.ToString(CultureInfo.InvariantCulture) ?? "";

        GD.Print(
            $"{HomeMarker} decision={home.Decision} legend_level={home.LegendLevel} " +
            $"energy={home.Energy} reserve={home.EnergyReserve} " +
            $"crowns={home.Crowns} soul_shards={home.SoulShards} " +
            $"power={power} power_standing={home.PowerStanding} " +
            $"highest_clear={home.HighestClearText} run_stage={home.RunStageText} " +
            $"chapters={picker.Chapters.Count} picker_stage={picker.Stage} " +
            $"verdicts=[{ChapterSelect.Describe(picker)}]");
    }
}
