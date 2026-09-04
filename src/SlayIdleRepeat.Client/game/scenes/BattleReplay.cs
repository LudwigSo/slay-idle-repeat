using System.Globalization;
using Godot;
using SlayIdleRepeat.Client.Game.Presenters;

namespace SlayIdleRepeat.Client.Game.Scenes;

/// <summary>
/// S06 — the battle replay a run's fights are watched on: a driving adapter over
/// <see cref="BattleReplayPresenter"/>, drawing the fight on a 3D stage.
/// </summary>
/// <remarks>
/// <para>
/// It draws what the presenter says and forwards two presses. Which events this frame crossed, which
/// boss phase is in force and whether the result has been confirmed are the presenter's answers; where
/// each actor stands, how its body moves and how far back the camera sits are
/// <see cref="BattleStageLayout"/>'s, <see cref="BattleChoreography"/>'s and
/// <see cref="BattleFraming"/>'s. What is left here is applying a pose, unprojecting an anchor and
/// picking a colour — decided where nothing can test it, and kept small for that reason.
/// </para>
/// <para>
/// The plates over the actors' heads are canvas controls placed by unprojection every frame, so they
/// keep the interface's own text rendering. The bursts and the floating numbers rise from the same
/// anchors.
/// </para>
/// <para>
/// 🔒 Reduced motion is honoured in this file, not merely passed through: every body motion completes
/// on its first frame, every burst is suppressed, and every tween is shortened to
/// <see cref="ReducedMotionSeconds"/>.
/// </para>
/// <para>
/// ⚠️ Every distance, timing and canvas number below is an export assigned in
/// <c>BattleReplay.tscn</c>, under the banner that says why.
/// </para>
/// </remarks>
public partial class BattleReplay : Node3D
{
    /// <summary>Where this scene lives, for the screens that instantiate it.</summary>
    public const string ScenePath = "res://game/scenes/BattleReplay.tscn";

    /// <summary>The one line a headless run's screen state is read off.</summary>
    private const string BattleMarker = "SIR_BATTLE_READY";

    /// <summary>
    /// 🔴 Deliberately unbuilt, and named so it can be found. A fight that ends — won, lost, or
    /// skipped to its end — hands control back to the board it was entered from, because that is the
    /// one destination this build has; the design's result screen is not invented here.
    /// </summary>
    private const string TheResultScreensAreNotBuiltHere =
        "The design shows a result screen after every battle, and a skipped battle is supposed to " +
        "show it too. The death-and-revive screen and the run results screen are not built in this " +
        "milestone, so a finished fight returns to the board instead of stopping at a screen that " +
        "does not exist. Nothing about the result is invented on the way: the outcome is the one the " +
        "log settled and the board re-reads the run it changed.";

    /// <summary>
    /// 🔴 Every actor's bar has a denominator because the log carries one, and it took a rules-layer
    /// change to get it there. Kept because the same reasoning will look correct the next time a bar
    /// wants a number the log does not carry, and the answer is the same: get it into the log.
    /// </summary>
    private const string EveryActorsBarNowHasADenominatorFromTheLog =
        "The log carries each actor's Max HP in an ActorSpawned event, so a bar's denominator is read " +
        "rather than derived and every actor has one from the tick it enters on.";

    /// <summary>
    /// 🔴 Half done, and named so it can be found. Every number this screen draws is shortened past
    /// ten thousand through the shared <see cref="PlayerNumber"/>; the exact value is not given back.
    /// </summary>
    private const string LargeNumbersAreAbbreviatedButNeverRevealedHere =
        "The design abbreviates a number past ten thousand and returns the exact value on a long " +
        "press. The plates and the floating numbers shorten through PlayerNumber, the one rule the " +
        "results screens already obey. The reading-back half is not built: the plates and the " +
        "floaters ignore every tap so the skip can never be missed, and a press handler on a " +
        "number in flight belongs with the UI kit's shared readout, not with this screen alone.";

    /// <summary>
    /// How many floating numbers can be in the air at once: a fixed pool cycled round, never a label
    /// per event, because a fight at triple speed lands several events on one frame.
    /// </summary>
    private const int FloaterCount = 12;

    private const string WorldPath = "%World";
    private const string CameraRigPath = "%CameraRig";
    private const string CameraPath = "%Camera";
    private const string PlatesPath = "%Plates";
    private const string HitSparksPath = "%HitSparks";
    private const string CritPopPath = "%CritPop";
    private const string DeathPuffPath = "%DeathPuff";
    private const string WardMotesPath = "%WardMotes";
    private const string FloatingTextPath = "%FloatingText";
    private const string SafeAreaPath = "%SafeArea";
    private const string TitleLabelPath = "%TitleLabel";
    private const string OpponentLabelPath = "%OpponentLabel";
    private const string StatusLabelPath = "%StatusLabel";
    private const string SpeedLabelPath = "%SpeedLabel";
    private const string SpeedButtonPath = "%SpeedButton";
    private const string SkipButtonPath = "%SkipButton";
    private const string PhaseBandPath = "%PhaseBand";
    private const string PhaseBandLabelPath = "%PhaseBandLabel";

    private const string FontSizeOverride = "font_size";
    private const string FontColourOverride = "font_color";
    private const string PositionProperty = "position";
    private const string ModulateProperty = "modulate";

    /// <summary>What the readout writes where a value the screen has not settled would go.</summary>
    private const string NoValue = "none";

    /// <summary>
    /// What a floating number that GIVES an actor health is written with — the channel that is not
    /// colour, so healing and harm stay apart without a palette.
    /// </summary>
    private const string GainSign = "+";

    /// <summary>And one that takes health away.</summary>
    private const string LossSign = "-";

    /// <summary>How large an ordinary combat number is drawn.</summary>
    private const int BodyTextSize = 52;

    /// <summary>And a critical one, which the design draws larger as well as yellower.</summary>
    private const int CritTextSize = 72;

    /// <summary>And a tick of something lingering, drawn smaller so it is told from a blow by more than its colour.</summary>
    private const int DotTextSize = 44;

    private static readonly Color HitTextColour = new(0.95f, 0.95f, 0.97f);
    private static readonly Color CritTextColour = new(0.99f, 0.84f, 0.36f);
    private static readonly Color HealTextColour = new(0.51f, 0.87f, 0.55f);
    private static readonly Color DotTextColour = new(0.76f, 0.58f, 0.94f);

    /// <summary>A control with something to do.</summary>
    private static readonly Color LiveColour = new(0.93f, 0.93f, 0.96f);

    /// <summary>And one with nothing to do — the same quiet grey every caption is drawn in.</summary>
    private static readonly Color UnavailableColour = new(0.66f, 0.67f, 0.73f);

    /// <summary>The board's own amber, for a fight this screen cannot stage.</summary>
    private static readonly Color BlockedColour = new(0.98f, 0.83f, 0.45f);

    /// <summary>And the board's refusal colour, for a result the rules layer turned down.</summary>
    private static readonly Color RefusedColour = new(0.94f, 0.62f, 0.58f);

    private static readonly Color StandingColour = new(1, 1, 1, 1);
    private static readonly Color FallenColour = new(0.45f, 0.45f, 0.5f, 1);

    /// <summary>The hero's side, painted onto its plates' bars.</summary>
    private static readonly Color HeroTint = new(0.36f, 0.52f, 0.82f);

    /// <summary>And the enemies'.</summary>
    private static readonly Color EnemyTint = new(0.72f, 0.34f, 0.36f);

    /// <summary>How far the hero and the first enemy each stand from the stage's centre.</summary>
    [Export] public float HalfGap { get; set; } = 2.4f;

    /// <summary>The step along the stage between one enemy slot and the next.</summary>
    [Export] public float EnemySpacing { get; set; } = 1.6f;

    /// <summary>How far back each step away from the centre slot pushes an enemy.</summary>
    [Export] public float EnemyArcDepth { get; set; } = 0.45f;

    /// <summary>A swing's whole duration.</summary>
    [Export] public float SwingSeconds { get; set; } = 0.28f;

    /// <summary>How far a swing advances toward its counterpart at its peak.</summary>
    [Export] public float SwingReach { get; set; } = 0.8f;

    /// <summary>A recoil's whole duration.</summary>
    [Export] public float RecoilSeconds { get; set; } = 0.22f;

    /// <summary>How far a recoil moves away from its striker at its peak.</summary>
    [Export] public float RecoilDistance { get; set; } = 0.3f;

    /// <summary>A dodge's whole duration.</summary>
    [Export] public float DodgeSeconds { get; set; } = 0.3f;

    /// <summary>How far a dodge steps aside at its peak.</summary>
    [Export] public float DodgeSideStep { get; set; } = 0.6f;

    /// <summary>A brace's whole duration.</summary>
    [Export] public float BraceSeconds { get; set; } = 0.25f;

    /// <summary>How much a brace squashes at its peak, as a fraction of height.</summary>
    [Export] public float BraceSquash { get; set; } = 0.12f;

    /// <summary>How long a fall takes to land.</summary>
    [Export] public float FallSeconds { get; set; } = 0.6f;

    /// <summary>How far a fallen actor has tipped over once it has landed.</summary>
    [Export] public float FallTipDegrees { get; set; } = 80f;

    /// <summary>
    /// How far a fallen actor has sunk once it has landed, as a fraction of its own height, so a
    /// small enemy does not vanish under the floor where a boss would barely settle.
    /// </summary>
    [Export] public float FallSink { get; set; } = 0.15f;

    /// <summary>How long an entering actor takes to grow to full size.</summary>
    [Export] public float EnterSeconds { get; set; } = 0.35f;

    /// <summary>How far above an actor's head its plate hangs.</summary>
    [Export] public float PlateClearance { get; set; } = 0.35f;

    /// <summary>How long a pose is held: a wind-up the log states no seconds for, and the finished fight.</summary>
    [Export] public float PoseHoldSeconds { get; set; } = 0.8f;

    /// <summary>What every tween on this screen is shortened to under reduced motion.</summary>
    [Export] public float ReducedMotionSeconds { get; set; } = 0.1f;

    /// <summary>How long one floating combat number takes to rise and fade.</summary>
    [Export] public float FloatSeconds { get; set; } = 0.7f;

    /// <summary>How far a floating combat number rises, in canvas units.</summary>
    [Export] public float FloatRise { get; set; } = 220f;

    /// <summary>How far apart consecutive floating numbers are fanned, so two never sit on top.</summary>
    [Export] public float FloatFanStep { get; set; } = 44f;

    /// <summary>How long the boss phase band takes to arrive.</summary>
    [Export] public float BandFadeSeconds { get; set; } = 0.25f;

    /// <summary>How much a wind-up swells the actor at its peak, as a fraction of its size.</summary>
    [Export] public float PulseSwell { get; set; } = 0.06f;

    /// <summary>How far the hero's model is turned inside its rig to look across the stage.</summary>
    [Export] public float HeroFacingYawDegrees { get; set; } = 180f;

    /// <summary>How tall the hero stands, so its plate can sit above it.</summary>
    [Export] public float HeroHeight { get; set; } = 2.22f;

    private BattleReplayPresenter? _presenter;

    /// <summary>The board this fight was entered from, which it hands control back to.</summary>
    private Board? _board;

    private bool _reducedMotion;

    private CancellationToken _lifetime;

    private BattleChoreography? _choreography;

    private BattleWorld? _world;
    private BattleCameraRig? _cameraRig;
    private Camera3D? _camera;
    private Control? _plates;
    private CpuParticles2D? _hitSparks;
    private CpuParticles2D? _critPop;
    private CpuParticles2D? _deathPuff;
    private CpuParticles2D? _wardMotes;
    private Control? _floatingText;
    private Label? _titleLabel;
    private Label? _opponentLabel;
    private Label? _statusLabel;
    private Label? _speedLabel;
    private Button? _speedButton;
    private Button? _skipButton;
    private ColorRect? _phaseBand;
    private Label? _phaseBandLabel;

    private readonly List<ActorView> _views = [];

    /// <summary>Slot to view, so applying a cue never searches the roster.</summary>
    private readonly Dictionary<byte, ActorView> _viewBySlot = new();

    /// <summary>One texture per status, loaded on the first chip that needs it.</summary>
    private readonly Dictionary<ushort, Texture2D?> _statusIcons = new();

    private Label[] _floaters = [];
    private Tween?[] _floatTweens = [];
    private int _nextFloater;

    /// <summary>Whether a call into the presenter is in flight, so a frame cannot start another.</summary>
    private bool _busy;

    /// <summary>Whether the screen is already standing down, so it cannot stand down twice.</summary>
    private bool _closing;

    /// <summary>Whether the band was up on the previous draw, so its arrival can be animated once.</summary>
    private bool _bandUp;

    /// <summary>Whether an actor entered since the camera last framed the stage.</summary>
    private bool _reframe;

    /// <summary>How long the finished fight has been held on screen.</summary>
    private double _posed;

    private string _lastStatusText = "";
    private string _lastSpeedText = "";
    private string _lastBandText = "";

    /// <summary>Takes the presenter the composition root built, the board to return to, and the token.</summary>
    /// <param name="presenter">Drives the replay.</param>
    /// <param name="reducedMotion">Whether every burst is suppressed and every motion completes at once.</param>
    /// <param name="board">The screen the fight was entered from, which this one hands control back to.</param>
    /// <param name="lifetime">Cancelled when the application shuts down.</param>
    /// <exception cref="ArgumentNullException">The presenter or the board is null.</exception>
    public void Drive(
        BattleReplayPresenter presenter, bool reducedMotion, Board board, CancellationToken lifetime)
    {
        ArgumentNullException.ThrowIfNull(presenter);
        ArgumentNullException.ThrowIfNull(board);

        _presenter = presenter;
        _reducedMotion = reducedMotion;
        _board = board;
        _lifetime = lifetime;
    }

    /// <inheritdoc/>
    public override void _Ready()
    {
        // Resolved once: a scene-unique lookup is a string search each time it is asked, and this
        // screen redraws on every frame of a ninety-second fight.
        _world = GetNode<BattleWorld>(WorldPath);
        _cameraRig = GetNode<BattleCameraRig>(CameraRigPath);
        _camera = GetNode<Camera3D>(CameraPath);
        _plates = GetNode<Control>(PlatesPath);
        _hitSparks = GetNode<CpuParticles2D>(HitSparksPath);
        _critPop = GetNode<CpuParticles2D>(CritPopPath);
        _deathPuff = GetNode<CpuParticles2D>(DeathPuffPath);
        _wardMotes = GetNode<CpuParticles2D>(WardMotesPath);
        _floatingText = GetNode<Control>(FloatingTextPath);
        _titleLabel = GetNode<Label>(TitleLabelPath);
        _opponentLabel = GetNode<Label>(OpponentLabelPath);
        _statusLabel = GetNode<Label>(StatusLabelPath);
        _speedLabel = GetNode<Label>(SpeedLabelPath);
        _speedButton = GetNode<Button>(SpeedButtonPath);
        _skipButton = GetNode<Button>(SkipButtonPath);
        _phaseBand = GetNode<ColorRect>(PhaseBandPath);
        _phaseBandLabel = GetNode<Label>(PhaseBandLabelPath);

        _speedButton.Pressed += OnSpeedPressed;
        _skipButton.Pressed += OnSkipPressed;

        // A Button draws its text by draw mode, and the engine's default for the disabled mode is a
        // half-transparent grey no font_color reaches.
        ButtonTextColours.ApplyTo(_speedButton, LiveColour, UnavailableColour);
        ButtonTextColours.ApplyTo(_skipButton, LiveColour, UnavailableColour);

        _choreography = new BattleChoreography(Timings(), _reducedMotion);

        ScreenStage.Show(this);

        SafeAreaInsets.ApplyTo(GetNode<MarginContainer>(SafeAreaPath), GetViewport().GetVisibleRect().Size);

        BuildFloaters();
        RenderCaptions();
        Render();

        _ = StartAsync();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The matching half of the subscriptions in <c>_Ready</c>: this screen is the one in the build
    /// that is genuinely freed, and a handler left connected across a node that is merely detached and
    /// re-added would fire twice — and once is the whole contract of a skip.
    /// </remarks>
    public override void _ExitTree()
    {
        if (_speedButton is not null)
        {
            _speedButton.Pressed -= OnSpeedPressed;
        }

        if (_skipButton is not null)
        {
            _skipButton.Pressed -= OnSkipPressed;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// 🔒 Nothing about the replay is decided here, and the ordinary frame allocates nothing this file
    /// can avoid. The frame's elapsed time goes to the presenter, which moves the playhead and hands
    /// back the cues that were crossed; the same time, at the same speed, goes to the choreography,
    /// which keeps the bodies moving even while a submission is in flight.
    /// </remarks>
    /// <param name="delta">Seconds since the previous frame.</param>
    public override void _Process(double delta)
    {
        if (_closing || _presenter is not { } presenter || presenter.Readiness != BattleReadiness.Ready)
        {
            return;
        }

        if (!_busy)
        {
            if (presenter.Complete)
            {
                HoldPose(delta);
            }
            else
            {
                Advance(delta, presenter);
            }
        }

        if (!_closing)
        {
            Animate(delta, presenter);
        }
    }

    private BattleMotionTimings Timings() =>
        new(
            SwingSeconds, SwingReach, RecoilSeconds, RecoilDistance, DodgeSeconds, DodgeSideStep,
            BraceSeconds, BraceSquash, FallSeconds, FallTipDegrees, FallSink, EnterSeconds,
            PoseHoldSeconds);

    /// <remarks>
    /// Nothing awaits this task, so its exceptions have nowhere to surface: the whole body is
    /// guarded, or a continuation that threw would leave a replay that silently never started.
    /// </remarks>
    private async Task StartAsync()
    {
        try
        {
            if (_presenter is not { } presenter)
            {
                GD.PushError(
                    $"{BattleMarker} halted · The battle replay entered the tree with no presenter. " +
                    "Only a screen that already has a run may instantiate it, and it must call Drive " +
                    "before adding it to the tree.");

                return;
            }

            // No ConfigureAwait(false): the continuation writes to nodes, and only the thread the
            // engine runs the scene tree on may do that.
            await presenter.StartAsync(_lifetime);

            BuildStage(presenter);
            RenderCaptions();
            Render();
            Report(presenter);
        }
        catch (Exception failure)
        {
            GD.PushError($"The battle replay stopped unexpectedly: {failure}");
        }
    }

    private void Advance(double delta, BattleReplayPresenter presenter)
    {
        Task<BattleSubmission> advancing;

        try
        {
            advancing = presenter.AdvanceAsync(delta, _lifetime);
        }
        catch (Exception failure)
        {
            GD.PushError($"The battle replay could not be advanced: {failure}");

            return;
        }

        if (advancing.IsCompletedSuccessfully)
        {
            Settle(presenter);

            // Reported on the one frame that had something to report: a host that answers
            // synchronously would otherwise close the battle without the line this screen is read off.
            if (advancing.Result != BattleSubmission.NothingToSubmit)
            {
                Report(presenter);
            }

            return;
        }

        _busy = true;

        _ = AwaitAdvance(advancing, presenter);
    }

    private async Task AwaitAdvance(Task<BattleSubmission> advancing, BattleReplayPresenter presenter)
    {
        try
        {
            await advancing;

            Settle(presenter);
            Report(presenter);
        }
        catch (Exception failure)
        {
            GD.PushError($"The battle replay's result could not be submitted: {failure}");
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>Draws the step the presenter has just handed over.</summary>
    private void Settle(BattleReplayPresenter presenter)
    {
        var cues = presenter.StepCues;

        if (cues.Count > 0)
        {
            var mapping = MapToCanvas();

            for (var index = 0; index < cues.Count; index++)
            {
                DrawCue(cues[index], mapping);
            }
        }

        Render();
    }

    /// <summary>Moves every body on, poses it, and hangs every plate over it.</summary>
    private void Animate(double delta, BattleReplayPresenter presenter)
    {
        if (_choreography is not { } choreography || _world is not { } world || _cameraRig is not { } rig)
        {
            return;
        }

        choreography.Advance(delta * (int)presenter.Speed);
        world.Pose(choreography);

        if (_reframe)
        {
            _reframe = false;
            rig.Frames(world.StageBounds);

            // A re-framing is a camera glide, and under reduced motion it arrives at once like every other motion here.
            if (_reducedMotion)
            {
                rig.Snap();
            }
        }

        PlacePlates();
    }

    /// <summary>Holds a finished fight on screen, then hands control back.</summary>
    private void HoldPose(double delta)
    {
        _posed += delta;

        if (_posed >= (_reducedMotion ? ReducedMotionSeconds : PoseHoldSeconds))
        {
            Close();
        }
    }

    /// <remarks>
    /// 🔴 The one destination this screen has, and it is not the one the design draws — see
    /// <see cref="TheResultScreensAreNotBuiltHere"/>.
    /// </remarks>
    private void Close()
    {
        if (_closing)
        {
            return;
        }

        _closing = true;

        if (_board is not { } board)
        {
            GD.PushError(
                $"{BattleMarker} halted · The battle replay finished with no board to return to. " +
                "Only the board may instantiate it, and it must pass itself to Drive.");

            return;
        }

        BattleHandover.Return(this, board);
    }

    /// <summary>Writes the captions that never change once their strings are resolved.</summary>
    private void RenderCaptions()
    {
        if (_presenter is not { } presenter || _titleLabel is null || _opponentLabel is null ||
            _speedLabel is null || _skipButton is null)
        {
            return;
        }

        _titleLabel.Text = presenter.Title;
        _opponentLabel.Text = presenter.OpponentLabel;
        _speedLabel.Text = presenter.SpeedLabel;
        _skipButton.Text = presenter.SkipText;
    }

    /// <summary>Builds the pool of floating combat numbers, once, before any of them is needed.</summary>
    private void BuildFloaters()
    {
        if (_floatingText is not { } layer)
        {
            return;
        }

        _floaters = new Label[FloaterCount];
        _floatTweens = new Tween?[FloaterCount];

        for (var index = 0; index < FloaterCount; index++)
        {
            var floater = new Label
            {
                Visible = false,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                HorizontalAlignment = HorizontalAlignment.Center,
            };

            floater.AddThemeFontSizeOverride(FontSizeOverride, BodyTextSize);

            layer.AddChild(floater);

            _floaters[index] = floater;
        }
    }

    /// <summary>
    /// Stands the roster on the stage and hangs a plate over each actor, once, off a roster the fight
    /// does not change. Every actor the fight ever mentions is in the log before the first frame.
    /// </summary>
    private void BuildStage(BattleReplayPresenter presenter)
    {
        if (_world is not { } world || _cameraRig is not { } rig || _plates is not { } plates ||
            _choreography is not { } choreography || presenter.Actors.Count == 0)
        {
            return;
        }

        world.Build(
            presenter.Actors,
            presenter.BiomeArtSet,
            new BattleStageMetrics(HalfGap, EnemySpacing, EnemyArcDepth),
            new ActorDressing(HeroFacingYawDegrees, HeroHeight, PlateClearance, PulseSwell));

        var plateScene = GD.Load<PackedScene>(ActorPlate.ScenePath);

        if (plateScene is null)
        {
            GD.PushError($"No actor plate could be loaded from '{ActorPlate.ScenePath}'; the fight plays unlabelled.");
        }

        foreach (var actor in presenter.Actors)
        {
            // A summon is off the stage until the log's own Enter brings it on.
            if (actor.IsSummon)
            {
                choreography.SnapAbsent(actor.ActorId);
            }

            if (plateScene?.Instantiate<ActorPlate>() is not { } plate)
            {
                continue;
            }

            plate.Visible = false;
            plates.AddChild(plate);
            plate.Bind(
                presenter.CaptionOf(actor.ActorId),
                actor.MaxHp,
                actor.StartingHp,
                actor.Side == ReplaySide.Enemy ? EnemyTint : HeroTint);

            var view = new ActorView(actor.ActorId, plate, actor.StartingHp);

            _views.Add(view);
            _viewBySlot[actor.ActorId] = view;
        }

        world.Pose(choreography);
        rig.Frames(world.StageBounds);
        rig.Snap();
        PlacePlates();
    }

    /// <summary>
    /// Draws one instruction of the step the playhead has just crossed: a motion, a bar, a chip, a
    /// number and a burst — none of it decided here.
    /// </summary>
    private void DrawCue(ReplayCue cue, in CanvasMapping mapping)
    {
        _choreography?.Play(cue);

        if (cue.Motion == ReplayMotion.Enter)
        {
            _reframe = true;
        }

        if (cue.Health is { } health)
        {
            Spend(cue.ActorId, health);
        }

        if (cue.StatusId is { } status)
        {
            Stack(cue.ActorId, status, cue.StatusStacks);
        }

        if (cue.Died)
        {
            Fell(cue.ActorId);
        }

        if (!AnchorOnCanvas(cue.ActorId, mapping, out var at))
        {
            return;
        }

        if (cue.Floater != ReplayFloater.None)
        {
            Float(at, cue.FloaterAmount, SignOf(cue.Floater), ColourOf(cue.Floater), SizeOf(cue.Floater));
        }

        Burst(ParticlesFor(cue.Burst), at);
    }

    /// <remarks>The design's palette: ordinary damage plain, a critical one yellow, healing green, a tick purple.</remarks>
    private static Color ColourOf(ReplayFloater floater) => floater switch
    {
        ReplayFloater.Crit => CritTextColour,
        ReplayFloater.Heal => HealTextColour,
        ReplayFloater.DamageOverTime => DotTextColour,
        _ => HitTextColour,
    };

    private static int SizeOf(ReplayFloater floater) => floater switch
    {
        ReplayFloater.Crit => CritTextSize,
        ReplayFloater.DamageOverTime => DotTextSize,
        _ => BodyTextSize,
    };

    private static string SignOf(ReplayFloater floater) =>
        floater == ReplayFloater.Heal ? GainSign : LossSign;

    /// <remarks>A ward giving way scatters the same motes that granted it; nothing else in this build is a ward's.</remarks>
    private CpuParticles2D? ParticlesFor(ReplayBurst burst) => burst switch
    {
        ReplayBurst.HitSpark => _hitSparks,
        ReplayBurst.CritPop => _critPop,
        ReplayBurst.DeathPuff => _deathPuff,
        ReplayBurst.Ward or ReplayBurst.WardBroken => _wardMotes,
        _ => null,
    };

    /// <summary>Puts an actor's health bar on the value the playhead has walked it to.</summary>
    private void Spend(byte slot, double health)
    {
        if (!_viewBySlot.TryGetValue(slot, out var view) || view.Current is null)
        {
            return;
        }

        view.Current = health;
        view.Dirty = true;
    }

    /// <summary>Greys the plate of an actor the log has just recorded going down.</summary>
    private void Fell(byte slot)
    {
        if (_viewBySlot.TryGetValue(slot, out var view))
        {
            view.Plate.Modulate = FallenColour;
        }
    }

    private void Stack(byte slot, ushort status, int stacks)
    {
        if (!_viewBySlot.TryGetValue(slot, out var view))
        {
            return;
        }

        if (stacks <= 0)
        {
            view.Stacks.Remove(status);
        }
        else
        {
            view.Stacks[status] = stacks;
        }

        view.StatusesDirty = true;
    }

    /// <summary>Puts one number in the air over the actor it happened to.</summary>
    /// <remarks>
    /// Fanned by a fixed step round the pool rather than scattered randomly: two numbers landing on
    /// one frame have to be readable as two, and a random offset in a scene is a second source of
    /// randomness in a game whose every other one is seeded and reproducible.
    /// </remarks>
    private void Float(Vector2 from, double amount, string sign, Color colour, int size)
    {
        if (_floaters.Length == 0)
        {
            return;
        }

        var index = _nextFloater;

        _nextFloater = (_nextFloater + 1) % _floaters.Length;

        var floater = _floaters[index];

        _floatTweens[index]?.Kill();

        floater.Text = sign + PlayerNumber.Abbreviated((long)Math.Round(amount));
        floater.AddThemeFontSizeOverride(FontSizeOverride, size);
        floater.AddThemeColorOverride(FontColourOverride, colour);
        floater.Modulate = StandingColour;
        floater.Visible = true;
        floater.Position = new Vector2(
            from.X - (floater.Size.X / 2f), from.Y + ((index - (FloaterCount / 2)) * FloatFanStep));

        var seconds = _reducedMotion ? ReducedMotionSeconds : FloatSeconds;

        var tween = CreateTween();

        tween.SetParallel(true);
        tween.TweenProperty(
            floater, PositionProperty, new Vector2(floater.Position.X, floater.Position.Y - FloatRise), seconds);
        tween.TweenProperty(floater, ModulateProperty, new Color(StandingColour, 0), seconds);

        _floatTweens[index] = tween;
    }

    /// <summary>Fires one procedural burst, unless motion is reduced — the systems stay, and are never started.</summary>
    private void Burst(CpuParticles2D? particles, Vector2 at)
    {
        if (_reducedMotion || particles is null)
        {
            return;
        }

        particles.Position = at;
        particles.Restart();
    }

    /// <summary>Hangs every plate over its actor, or hides it while the actor is off stage or behind the camera.</summary>
    private void PlacePlates()
    {
        if (_views.Count == 0)
        {
            return;
        }

        var mapping = MapToCanvas();

        for (var index = 0; index < _views.Count; index++)
        {
            var view = _views[index];
            var plate = view.Plate;

            if (!AnchorOnCanvas(view.ActorId, mapping, out var anchor))
            {
                plate.Visible = false;

                continue;
            }

            plate.Visible = true;
            plate.Position = new Vector2(anchor.X - (plate.Size.X / 2f), anchor.Y - plate.Size.Y);
        }
    }

    /// <summary>Where an actor's anchor lands on the canvas, or false when it has none or it is behind the camera.</summary>
    private bool AnchorOnCanvas(byte actorId, in CanvasMapping mapping, out Vector2 canvas)
    {
        canvas = Vector2.Zero;

        if (_world is not { } world || _camera is not { } camera || world.AnchorOf(actorId) is not { } anchor ||
            camera.IsPositionBehind(anchor))
        {
            return false;
        }

        canvas = mapping.Map(camera.UnprojectPosition(anchor));

        return true;
    }

    /// <summary>The step from an unprojected point to the overlay's canvas, read once per frame rather than once per actor.</summary>
    /// <remarks>
    /// Unprojection answers in the viewport's visible rect, and the overlay is laid out in the
    /// canvas; the two are one and the same under the canvas_items stretch and differ under none, so
    /// the step between them is taken through the viewport's own transforms rather than assumed.
    /// </remarks>
    private CanvasMapping MapToCanvas()
    {
        var viewport = GetViewport();
        var visible = viewport.GetVisibleRect().Size;
        var window = (Vector2)GetWindow().Size;
        var toWindow = new Vector2(
            visible.X > 0f ? window.X / visible.X : 1f,
            visible.Y > 0f ? window.Y / visible.Y : 1f);

        return new CanvasMapping(toWindow, viewport.GetFinalTransform().AffineInverse());
    }

    /// <summary>Writes what has changed since the previous draw, and nothing that has not.</summary>
    private void Render()
    {
        var presenter = _presenter;

        // Validity before tree membership: asking a freed node whether it is inside the tree is
        // itself the crash, and this is the one screen in the build that genuinely frees itself.
        if (presenter is null || !IsInstanceValid(this) || !IsInsideTree() ||
            _statusLabel is null || _speedButton is null || _skipButton is null)
        {
            return;
        }

        var status = presenter.StatusText;

        if (!string.Equals(status, _lastStatusText, StringComparison.Ordinal))
        {
            _lastStatusText = status;
            _statusLabel.Text = status;
            _statusLabel.AddThemeColorOverride(FontColourOverride, StatusColourOf(presenter));
            _statusLabel.Visible = status.Length > 0;
        }

        var speed = SpeedTextOf(presenter);

        if (!string.Equals(speed, _lastSpeedText, StringComparison.Ordinal))
        {
            _lastSpeedText = speed;
            _speedButton.Text = speed;
        }

        // Never disabled, in any state: the skip is an accessibility clause and the only way off a
        // screen whose fight never materialised.
        _skipButton.Disabled = !presenter.SkipAvailable;

        RenderBand(presenter);
        RenderPlates();
    }

    /// <summary>Which of the three things the status line can be saying it is saying now.</summary>
    private static Color StatusColourOf(BattleReplayPresenter presenter)
    {
        if (presenter.RulesRejection is not null)
        {
            return RefusedColour;
        }

        return presenter.Readiness is not null and not BattleReadiness.Ready
            ? BlockedColour
            : UnavailableColour;
    }

    private static string SpeedTextOf(BattleReplayPresenter presenter) => presenter.Speed switch
    {
        BattleSpeed.Double => presenter.SpeedDoubleText,
        BattleSpeed.Triple => presenter.SpeedTripleText,
        _ => presenter.SpeedSingleText,
    };

    private void RenderBand(BattleReplayPresenter presenter)
    {
        if (_phaseBand is not { } band || _phaseBandLabel is not { } label)
        {
            return;
        }

        var up = presenter.PhaseBandVisible;

        if (up)
        {
            var text = presenter.PhaseBandText;

            if (!string.Equals(text, _lastBandText, StringComparison.Ordinal))
            {
                _lastBandText = text;
                label.Text = text;
            }
        }

        if (up == _bandUp)
        {
            return;
        }

        _bandUp = up;
        band.Visible = up;

        if (!up)
        {
            return;
        }

        band.Modulate = new Color(StandingColour, 0);

        CreateTween().TweenProperty(
            band, ModulateProperty, StandingColour, _reducedMotion ? ReducedMotionSeconds : BandFadeSeconds);
    }

    private void RenderPlates()
    {
        for (var index = 0; index < _views.Count; index++)
        {
            var view = _views[index];

            if (view.Dirty)
            {
                view.Dirty = false;

                if (view.Current is { } current)
                {
                    view.Plate.Spend(current);
                }
            }

            if (view.StatusesDirty)
            {
                view.StatusesDirty = false;

                RenderStatuses(view);
            }
        }
    }

    /// <remarks>Rebuilt only when the ledger under it changed, which is on a status event and never on an ordinary frame.</remarks>
    private void RenderStatuses(ActorView view)
    {
        view.Plate.ClearStatuses();

        foreach (var (status, stacks) in view.Stacks)
        {
            view.Plate.AddStatus(IconOf(status), status, stacks);
        }
    }

    /// <summary>The icon for a status, loaded once per status this fight; null for one the build has no icon for.</summary>
    private Texture2D? IconOf(ushort status)
    {
        if (_statusIcons.TryGetValue(status, out var icon))
        {
            return icon;
        }

        icon = IconCatalogue.Status(status) is { } path ? GD.Load<Texture2D>(path) : null;

        _statusIcons[status] = icon;

        return icon;
    }

    private void OnSpeedPressed()
    {
        _presenter?.CycleSpeed();

        Render();
    }

    /// <remarks>
    /// The skip answers in every state, including the states with nothing to skip: a fight that
    /// materialised is jumped to its end and confirmed; one that never did submits nothing and the
    /// screen stands down.
    /// </remarks>
    private void OnSkipPressed()
    {
        if (_closing || _busy || _presenter is not { } presenter)
        {
            return;
        }

        _busy = true;

        _ = SkipAsync(presenter);
    }

    private async Task SkipAsync(BattleReplayPresenter presenter)
    {
        // Read before the jump: a second press, on a fight already at its end, is a player asking to
        // leave rather than to skip again, and the pose is skippable by clause.
        var finished = presenter.Complete;

        try
        {
            var outcome = await presenter.SkipAsync(_lifetime);

            AnchorToEnd(presenter);
            Render();
            Report(presenter);

            if (finished || outcome == BattleSubmission.RefusedNotAvailable)
            {
                Close();
            }
        }
        catch (Exception failure)
        {
            GD.PushError($"The battle replay could not be skipped: {failure}");
        }
        finally
        {
            _busy = false;
        }
    }

    /// <remarks>
    /// A skipped fight emits none of the instructions it skipped, so the bars are re-read rather than
    /// walked, every actor the end leaves at nothing is laid down at once, and every other actor is
    /// stood on the stage — a summon whose Enter was skipped past included, or it would stay off the
    /// stage while a summon that died lies on it. Where the bars are read to is the presenter's
    /// answer, so a fight watched to its end and one skipped to it cannot land on two different
    /// numbers.
    /// </remarks>
    private void AnchorToEnd(BattleReplayPresenter presenter)
    {
        foreach (var actor in presenter.Actors)
        {
            if (presenter.HealthOf(actor.ActorId) is not { } ending)
            {
                continue;
            }

            if (_viewBySlot.TryGetValue(actor.ActorId, out var view))
            {
                view.Current = ending;
                view.Dirty = true;
            }

            if (ending <= 0d)
            {
                _choreography?.SnapFallen(actor.ActorId);
                Fell(actor.ActorId);
            }
            else
            {
                _choreography?.SnapPresent(actor.ActorId);
            }
        }
    }

    /// <summary>Prints, on one greppable line, what this screen resolved against the run the build shipped.</summary>
    private void Report(BattleReplayPresenter presenter) =>
        GD.Print(
            $"{BattleMarker} readiness={Describe(presenter.Readiness)} " +
            $"seed={presenter.BattleSeed.ToString(CultureInfo.InvariantCulture)} " +
            $"seed_derived={presenter.SeedDerived} " +
            $"tick={presenter.CurrentTick.ToString(CultureInfo.InvariantCulture)}/" +
            $"{presenter.TotalTicks.ToString(CultureInfo.InvariantCulture)} " +
            $"speed={presenter.Speed} skip={presenter.SkipAvailable} " +
            $"actors={presenter.Actors.Count.ToString(CultureInfo.InvariantCulture)} " +
            $"phase={DescribePhase(presenter.CurrentBossPhase)} won={Describe(presenter.HeroWon)} " +
            $"rejection={Describe(presenter.RulesRejection)}");

    /// <remarks>Invariant, like every other number on the readout: a line a grep has to match cannot be one the locale gets a say in.</remarks>
    private static string DescribePhase(int? phase) =>
        phase?.ToString(CultureInfo.InvariantCulture) ?? NoValue;

    private static string Describe<T>(T? value) where T : struct => value?.ToString() ?? NoValue;

    /// <summary>One actor's plate, and the running state behind it.</summary>
    /// <remarks>
    /// A mutable holder rather than a record, because it is written to on every blow and the point of
    /// it is to be written to in place rather than allocated per event.
    /// </remarks>
    private sealed class ActorView(byte actorId, ActorPlate plate, double? startingHp)
    {
        internal byte ActorId { get; } = actorId;

        internal ActorPlate Plate { get; } = plate;

        /// <summary>Where the playhead has walked this actor's health to; null when the log never fixes it.</summary>
        internal double? Current { get; set; } = startingHp;

        /// <summary>What is on this actor now, by status number — ordered by it, so the chips never reshuffle.</summary>
        internal SortedDictionary<ushort, int> Stacks { get; } = new();

        internal bool Dirty { get; set; } = true;

        internal bool StatusesDirty { get; set; }
    }

    /// <summary>The viewport-to-canvas step for one frame: a scale into the window, then the canvas transform's inverse.</summary>
    private readonly record struct CanvasMapping(Vector2 ToWindow, Transform2D FromWindow)
    {
        internal Vector2 Map(Vector2 viewportPoint) => FromWindow * (viewportPoint * ToWindow);
    }
}
