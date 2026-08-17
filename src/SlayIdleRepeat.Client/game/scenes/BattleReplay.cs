using System.Globalization;
using Godot;
using SlayIdleRepeat.Client.Game.Presenters;

namespace SlayIdleRepeat.Client.Game.Scenes;

/// <summary>
/// S06 — the battle replay a run's fights are watched on: a driving adapter over
/// <see cref="BattleReplayPresenter"/>.
/// </summary>
/// <remarks>
/// <para>
/// It draws what the presenter says and forwards two presses. No rules, no ports, no adapters, and
/// no decision about the fight: how fast the log is consumed, where the playhead stands, which events
/// this frame crossed, which boss phase is in force and whether the result has been confirmed are all
/// the presenter's answers. What is left here is drawing them — and the drawing is the whole reason
/// the split is worth keeping, because there is no scene test harness in this repository and anything
/// decided in this file is decided where nothing can check it.
/// </para>
/// <para>
/// 🔒 <b>Every effect on this screen is procedural, and that is a ruling rather than a shortcut.</b>
/// The hit sparks, the crit pop and the death puff are particle systems the engine already provides;
/// the floating combat text is a pool of labels on tweens; the boss phase band is a coloured
/// rectangle that fades in; the actors are rectangles with their role written under them. There is no
/// sprite sheet, no atlas, no texture and no asset row anywhere in this screen, and none is coming
/// from here.
/// </para>
/// <para>
/// 🔴 <b>There is no art at all, placeholder or otherwise, and no biome backdrop.</b> The design's
/// three parallax layers need a backdrop that does not exist, so nothing parallaxes: the ground is
/// one flat colour. Every type size, colour and gap in <c>BattleReplay.tscn</c> is a per-node
/// override owed to M8-03's UI kit, chosen against the engine's default font, and has to be
/// re-checked rather than re-applied when the real faces land. The layout itself is structural —
/// anchors, containers and stretch ratios — so it holds its proportions across the whole supported
/// aspect range, and every label autowraps inside a container so a larger text size reflows rather
/// than clips.
/// </para>
/// <para>
/// 🔒 <b>Reduced motion is honoured in this file, not merely passed through.</b> The presenter
/// shortens the phase band's dwell; this half suppresses every particle burst and shortens every
/// animation to a tenth of a second. Nothing here shakes the screen and nothing parallaxes, so those
/// two clauses are satisfied by there being nothing to disable.
/// </para>
/// <para>
/// 🔴 Three things this screen cannot name, each named instead — see
/// <see cref="TheResultScreensAreNotBuiltHere"/>,
/// <see cref="ASurvivingEnemysHealthBarHasNoDenominator"/> and
/// <see cref="AStatusEffectHasNoNameOrIconHere"/>.
/// </para>
/// <para>
/// 🔒 <b>Nothing here reads the combat log.</b> The presenter hands over one
/// <see cref="ReplayCue"/> per event that asks for anything — which actor, which side, which number
/// in which kind, which burst, what the health now stands at, whether the actor went down, which
/// status changed and by how many — and this file turns each of those into a colour, a particle
/// system and a tween. The reading of the log used to live here, where nothing can test it.
/// </para>
/// </remarks>
public partial class BattleReplay : Control
{
    /// <summary>Where this scene lives, for the screens that instantiate it.</summary>
    public const string ScenePath = "res://game/scenes/BattleReplay.tscn";

    /// <summary>
    /// The one line a headless run's screen state is read off. Distinctive on purpose: a replay
    /// resolved against a real run has to be greppable out of an engine log full of everything else,
    /// the way the board's and the home screen's readouts already are.
    /// </summary>
    private const string BattleMarker = "SIR_BATTLE_READY";

    /// <summary>
    /// 🔴 Deliberately unbuilt, and named so it can be found. The death and revive screen and the run
    /// results screen are later rows'. A fight that ends — won, lost, or skipped to its end — hands
    /// control back to the board it was entered from, because that is the one destination this build
    /// has; the design's result screen is not invented here.
    /// </summary>
    private const string TheResultScreensAreNotBuiltHere =
        "The design shows a result screen after every battle, and a skipped battle is supposed to " +
        "show it too. The death-and-revive screen and the run results screen are not built in this " +
        "milestone, so a finished fight returns to the board instead of stopping at a screen that " +
        "does not exist. Nothing about the result is invented on the way: the outcome is the one the " +
        "log settled and the board re-reads the run it changed.";

    /// <summary>
    /// 🔴 Deliberately undrawn, and named so it can be found. An enemy that survives the fight leaves
    /// the log with no anchor for its starting health, so it is given no bar rather than a guessed
    /// one.
    /// </summary>
    private const string ASurvivingEnemysHealthBarHasNoDenominator =
        "A health bar needs a starting value and the log carries none. The hero's follows from the " +
        "reported remaining health with every change the log records undone in reverse, and any " +
        "actor the log records a death for ended at zero, which anchors the same arithmetic. An " +
        "enemy still " +
        "standing at the end anchors neither equation. A denominator invented here would draw a bar " +
        "wrong by exactly however far the guess was off, and a player watching a bar is reading the " +
        "fraction, not the number — so that actor is drawn with its name and no bar at all.";

    /// <summary>
    /// 🔴 Deliberately unnamed, and named so it can be found. A status arrives as an integer, and the
    /// table that would turn it into a word or a picture never leaves the rules assembly.
    /// </summary>
    private const string AStatusEffectHasNoNameOrIconHere =
        "The design puts status icons with their stack counts under each health bar. The stack count " +
        "is real — the log carries it — and the identity is not: a status arrives as a bare integer, " +
        "the enum behind it and every potency it implies are internal to the rules, and there is no " +
        "icon set in this build for anything. So a status is drawn as a coloured chip with its " +
        "number and its stack count on it, which claims exactly what is known.";

    /// <summary>
    /// How long a finished fight is held on screen before control goes back to the board.
    /// </summary>
    /// <remarks>
    /// ⚠️ This task's choice of number, from the design's victory pose. It is a pause rather than a
    /// transition, so the three-hundred-millisecond ceiling on transitions does not bind it — and it
    /// is skippable anyway, because the skip control stays live through it and closes the screen at
    /// once.
    /// </remarks>
    private const double PoseSeconds = 0.8;

    /// <summary>
    /// What every animation on this screen is shortened to under reduced motion.
    /// </summary>
    /// <remarks>
    /// 🔒 An accessibility clause stated as one number, because the clause is one number: reduced
    /// motion shortens every animation to this, the pose included.
    /// </remarks>
    private const double ReducedMotionSeconds = 0.1;

    /// <summary>How long one floating combat number takes to rise and fade.</summary>
    private const double FloatSeconds = 0.7;

    /// <summary>And how long the boss phase band takes to arrive — inside the transition ceiling.</summary>
    private const double BandFadeSeconds = 0.25;

    /// <summary>How far a floating combat number rises, in canvas units.</summary>
    private const float FloatRise = 220;

    /// <summary>
    /// How many floating numbers can be in the air at once.
    /// </summary>
    /// <remarks>
    /// 🔒 A fixed pool built once, cycled round, rather than a label created per event. Every frame
    /// of this screen runs inside the engine's per-frame callback, and a fight at triple speed lands
    /// several events on one frame: allocating and freeing a node per number is the one shape that
    /// turns a ninety-second fight into a garbage-collection pause on a handset.
    /// </remarks>
    private const int FloaterCount = 12;

    /// <summary>How far apart consecutive floating numbers are fanned, so two never sit on top.</summary>
    private const float FloatFanStep = 44;

    private const string SafeAreaPath = "%SafeArea";
    private const string TitleLabelPath = "%TitleLabel";
    private const string OpponentLabelPath = "%OpponentLabel";
    private const string HeroTokenPath = "%HeroToken";
    private const string HeroNamePath = "%HeroName";
    private const string EnemyTokenPath = "%EnemyToken";
    private const string EnemyNamePath = "%EnemyName";
    private const string EffectsPath = "%Effects";
    private const string HitSparksPath = "%HitSparks";
    private const string CritPopPath = "%CritPop";
    private const string DeathPuffPath = "%DeathPuff";
    private const string FloatingTextPath = "%FloatingText";
    private const string BarsPath = "%Bars";
    private const string StatusLabelPath = "%StatusLabel";
    private const string SpeedLabelPath = "%SpeedLabel";
    private const string SpeedButtonPath = "%SpeedButton";
    private const string SkipButtonPath = "%SkipButton";
    private const string PhaseBandPath = "%PhaseBand";
    private const string PhaseBandLabelPath = "%PhaseBandLabel";

    /// <summary>The theme entry a control's own text size is written into.</summary>
    private const string FontSizeOverride = "font_size";

    /// <summary>The theme entry a label's own text colour is written into.</summary>
    private const string FontColourOverride = "font_color";

    /// <summary>The property one floating number's rise is tweened along.</summary>
    private const string PositionProperty = "position";

    /// <summary>And the one both the fade and the band's arrival are tweened along.</summary>
    private const string ModulateProperty = "modulate";

    /// <summary>Separates a current health value from the value it started at.</summary>
    private const string OverSeparator = " / ";

    /// <summary>Introduces a status effect's own number, which is all this build knows it by.</summary>
    private const string StatusPrefix = "#";

    /// <summary>And how many of it are stacked.</summary>
    private const string StackPrefix = " ×";

    /// <summary>What the readout writes where a value the screen has not settled would go.</summary>
    private const string NoValue = "none";

    /// <summary>What ordinary damage is drawn in.</summary>
    private static readonly Color HitTextColour = new(0.95f, 0.95f, 0.97f);

    /// <summary>A critical hit, which is also drawn larger — the one number the design shouts.</summary>
    private static readonly Color CritTextColour = new(0.99f, 0.84f, 0.36f);

    /// <summary>Healing.</summary>
    private static readonly Color HealTextColour = new(0.51f, 0.87f, 0.55f);

    /// <summary>And a damage-over-time tick, which is neither a blow nor a heal.</summary>
    private static readonly Color DotTextColour = new(0.76f, 0.58f, 0.94f);

    /// <summary>A control with something to do.</summary>
    private static readonly Color LiveColour = new(0.93f, 0.93f, 0.96f);

    /// <summary>And one with nothing to do — the same quiet grey every caption is drawn in.</summary>
    private static readonly Color UnavailableColour = new(0.66f, 0.67f, 0.73f);

    /// <summary>An actor still in the fight.</summary>
    private static readonly Color StandingColour = new(1, 1, 1, 1);

    /// <summary>And one the log has recorded a death for.</summary>
    private static readonly Color FallenColour = new(0.45f, 0.45f, 0.5f, 1);

    /// <summary>The palette one status chip is tinted from, by its own number.</summary>
    private static readonly Color[] StatusChipColours =
    [
        new(0.85f, 0.42f, 0.38f),
        new(0.40f, 0.62f, 0.86f),
        new(0.53f, 0.79f, 0.47f),
        new(0.83f, 0.68f, 0.34f),
        new(0.68f, 0.51f, 0.86f),
        new(0.38f, 0.76f, 0.74f),
    ];

    /// <summary>How large an ordinary combat number is drawn.</summary>
    private const int BodyTextSize = 52;

    /// <summary>And a critical one, which the design draws larger as well as yellower.</summary>
    private const int CritTextSize = 76;

    /// <summary>How large one status chip's number is drawn.</summary>
    private const int ChipTextSize = 32;

    /// <summary>How large an actor's caption and its health readout are drawn.</summary>
    private const int BarTextSize = 40;

    /// <summary>One status chip's square, in canvas units.</summary>
    private static readonly Vector2 ChipSwatchSize = new(28, 28);

    /// <summary>How tall one actor's health bar is drawn.</summary>
    private static readonly Vector2 BarSize = new(0, 28);

    private BattleReplayPresenter? _presenter;

    /// <summary>The board this fight was entered from, which it hands control back to.</summary>
    private Board? _board;

    private bool _reducedMotion;

    private CancellationToken _lifetime;

    private Label? _titleLabel;
    private Label? _opponentLabel;
    private ColorRect? _heroToken;
    private Label? _heroName;
    private ColorRect? _enemyToken;
    private Label? _enemyName;
    private Control? _effects;
    private CpuParticles2D? _hitSparks;
    private CpuParticles2D? _critPop;
    private CpuParticles2D? _deathPuff;
    private Control? _floatingText;
    private VBoxContainer? _bars;
    private Label? _statusLabel;
    private Label? _speedLabel;
    private Button? _speedButton;
    private Button? _skipButton;
    private ColorRect? _phaseBand;
    private Label? _phaseBandLabel;

    private readonly List<ActorBar> _rows = [];

    /// <summary>Slot to bar, so applying an event never searches the roster.</summary>
    private readonly Dictionary<byte, ActorBar> _rowBySlot = new();

    private Label[] _floaters = [];
    private Tween?[] _floatTweens = [];
    private int _nextFloater;

    /// <summary>Whether a call into the presenter is in flight, so a frame cannot start another.</summary>
    private bool _busy;

    /// <summary>Whether the screen is already standing down, so it cannot stand down twice.</summary>
    private bool _closing;

    /// <summary>Whether the band was up on the previous draw, so its arrival can be animated once.</summary>
    private bool _bandUp;

    /// <summary>How long the finished fight has been held on screen.</summary>
    private double _posed;

    private string _lastStatusText = "";
    private string _lastSpeedText = "";
    private string _lastBandText = "";

    /// <summary>Takes the presenter the composition root built, the board to return to, and the token.</summary>
    /// <param name="presenter">Drives the replay.</param>
    /// <param name="reducedMotion">Whether every burst is suppressed and every animation shortened.</param>
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
        // Resolved once. A scene-unique lookup is a string search of the owner's table each time it
        // is asked, and this screen redraws on every frame of a ninety-second fight.
        _titleLabel = GetNode<Label>(TitleLabelPath);
        _opponentLabel = GetNode<Label>(OpponentLabelPath);
        _heroToken = GetNode<ColorRect>(HeroTokenPath);
        _heroName = GetNode<Label>(HeroNamePath);
        _enemyToken = GetNode<ColorRect>(EnemyTokenPath);
        _enemyName = GetNode<Label>(EnemyNamePath);
        _effects = GetNode<Control>(EffectsPath);
        _hitSparks = GetNode<CpuParticles2D>(HitSparksPath);
        _critPop = GetNode<CpuParticles2D>(CritPopPath);
        _deathPuff = GetNode<CpuParticles2D>(DeathPuffPath);
        _floatingText = GetNode<Control>(FloatingTextPath);
        _bars = GetNode<VBoxContainer>(BarsPath);
        _statusLabel = GetNode<Label>(StatusLabelPath);
        _speedLabel = GetNode<Label>(SpeedLabelPath);
        _speedButton = GetNode<Button>(SpeedButtonPath);
        _skipButton = GetNode<Button>(SkipButtonPath);
        _phaseBand = GetNode<ColorRect>(PhaseBandPath);
        _phaseBandLabel = GetNode<Label>(PhaseBandLabelPath);

        _speedButton.Pressed += OnSpeedPressed;
        _skipButton.Pressed += OnSkipPressed;

        // Painted once, because nothing about which colour belongs to which state changes while the
        // screen is up. It is painted at all because a Button draws its text by draw mode, and the
        // engine's default for the disabled mode is a half-transparent grey no font_color reaches.
        ButtonTextColours.ApplyTo(_speedButton, LiveColour, UnavailableColour);
        ButtonTextColours.ApplyTo(_skipButton, LiveColour, UnavailableColour);

        SafeAreaInsets.ApplyTo(GetNode<MarginContainer>(SafeAreaPath), GetViewportRect().Size);

        BuildFloaters();
        RenderCaptions();
        Render();

        _ = StartAsync();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The matching half of the subscriptions in <c>_Ready</c>. The buttons are children and die with
    /// this node either way, but this screen is the one in the build that is genuinely freed, and a
    /// handler left connected across a node that is merely detached and re-added would fire twice —
    /// and once is the whole contract of a skip.
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
    /// <para>
    /// 🔒 Nothing about the replay is decided here. The frame's own elapsed time goes to the
    /// presenter, which moves the playhead and hands back the events that were crossed; this half
    /// draws them.
    /// </para>
    /// <para>
    /// 🔒 The ordinary frame allocates nothing this file can avoid: no closure, no state machine, no
    /// query, no node lookup and no string. The one allocation left is the task the presenter's own
    /// asynchronous method returns, which is why the completed case is taken without awaiting it —
    /// an await would add a state machine and a continuation to every frame of every fight to
    /// resume something that had already finished.
    /// </para>
    /// </remarks>
    /// <param name="delta">Seconds since the previous frame.</param>
    public override void _Process(double delta)
    {
        if (_closing || _busy || _presenter is not { } presenter)
        {
            return;
        }

        // Nothing to advance before the read answers, and nothing ever to advance when it answered
        // with a reason there is no fight. A screen with no log still draws, still says why, and
        // still offers the skip — it just has no playhead to move.
        if (presenter.Readiness != BattleReadiness.Ready)
        {
            return;
        }

        if (presenter.Complete)
        {
            HoldPose(delta);

            return;
        }

        Advance(delta, presenter);
    }

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

            BuildBars(presenter);
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

            // Reported on the one frame that had something to report, and never on an ordinary one:
            // a host that answers synchronously would otherwise close the battle without the line
            // this screen is read off, and the readout is the only gate a headless run has.
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

        for (var index = 0; index < cues.Count; index++)
        {
            DrawCue(cues[index]);
        }

        Render();
    }

    /// <summary>Holds a finished fight on screen, then hands control back.</summary>
    private void HoldPose(double delta)
    {
        _posed += delta;

        if (_posed >= (_reducedMotion ? ReducedMotionSeconds : PoseSeconds))
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
        if (_presenter is not { } presenter || _titleLabel is null || _heroName is null ||
            _speedLabel is null || _skipButton is null || _opponentLabel is null ||
            _enemyName is null)
        {
            return;
        }

        _titleLabel.Text = presenter.Title;
        _heroName.Text = presenter.HeroLabel;
        _speedLabel.Text = presenter.SpeedLabel;
        _skipButton.Text = presenter.SkipText;

        // 🔴 By role and index rather than by name — no enemy name is reachable from a client, and
        // the presenter is where that is decided and said.
        var opponents = presenter.OpponentLabel;

        _opponentLabel.Text = opponents;
        _enemyName.Text = opponents;
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
                MouseFilter = MouseFilterEnum.Ignore,
                HorizontalAlignment = HorizontalAlignment.Center,
            };

            floater.AddThemeFontSizeOverride(FontSizeOverride, BodyTextSize);

            layer.AddChild(floater);

            _floaters[index] = floater;
        }
    }

    /// <summary>
    /// Builds one row per actor the log mentions, once, off a roster the fight does not change.
    /// </summary>
    /// <remarks>
    /// 🔴 An actor whose starting health the log does not fix gets no bar — see
    /// <see cref="ASurvivingEnemysHealthBarHasNoDenominator"/>. Built here rather than per frame
    /// because the roster is settled by the read: every actor the fight ever mentions is in the log
    /// before the first frame is drawn.
    /// </remarks>
    private void BuildBars(BattleReplayPresenter presenter)
    {
        if (_bars is not { } column)
        {
            return;
        }

        foreach (var actor in presenter.Actors)
        {
            var row = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };

            var captionRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };

            // 🔒 The wrapping half of the pair expands and the fixed half does not. An autowrapping
            // label in a row with no expand flag is measured as one character wide, which collapses
            // it to a sliver and pushes everything beside it off the far edge of the screen.
            var caption = Caption(presenter.CaptionOf(actor.ActorId), UnavailableColour, wrapping: true);

            caption.SizeFlagsHorizontal = SizeFlags.ExpandFill;

            var value = Caption("", LiveColour, wrapping: false);

            value.HorizontalAlignment = HorizontalAlignment.Right;

            captionRow.AddChild(caption);
            captionRow.AddChild(value);

            var bar = new ProgressBar
            {
                CustomMinimumSize = BarSize,
                ShowPercentage = false,
                MaxValue = Math.Max(actor.StartingHp ?? 0, 1),
                Value = actor.StartingHp ?? 0,
                Visible = actor.StartingHp is not null,
            };

            var statuses = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };

            row.AddChild(captionRow);
            row.AddChild(bar);
            row.AddChild(statuses);

            column.AddChild(row);

            var tracked = new ActorBar(actor.ActorId, value, bar, statuses, actor.StartingHp);

            _rows.Add(tracked);
            _rowBySlot[actor.ActorId] = tracked;
        }
    }

    private static Label Caption(string text, Color colour, bool wrapping)
    {
        var label = new Label
        {
            Text = text,
            AutowrapMode = wrapping
                ? TextServer.AutowrapMode.WordSmart
                : TextServer.AutowrapMode.Off,
        };

        // A control built in code inherits the engine's default face, which is caption-sized on a
        // canvas this wide. The sibling screens set the same override for the same reason.
        label.AddThemeFontSizeOverride(FontSizeOverride, BarTextSize);
        label.AddThemeColorOverride(FontColourOverride, colour);

        return label;
    }

    /// <summary>
    /// Draws one instruction of the step the playhead has just crossed.
    /// </summary>
    /// <remarks>
    /// 🔒 Nothing is decided here. Which actor, which number, which kind of number, which burst, what
    /// the health now stands at and whether the actor went down are all settled before this file sees
    /// them; what is left is a colour, a size, a particle system and a tween.
    /// </remarks>
    private void DrawCue(ReplayCue cue)
    {
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
            Fell(cue.Side);
        }

        if (cue.Floater != ReplayFloater.None)
        {
            Float(cue.Side, cue.FloaterAmount, ColourOf(cue.Floater), SizeOf(cue.Floater));
        }

        Burst(ParticlesFor(cue.Burst), PointOf(cue.Side));
    }

    /// <remarks>
    /// The design's palette, and the one place it is written: ordinary damage plain, a critical one
    /// yellow, healing green, a damage-over-time tick purple.
    /// </remarks>
    private static Color ColourOf(ReplayFloater floater) => floater switch
    {
        ReplayFloater.Crit => CritTextColour,
        ReplayFloater.Heal => HealTextColour,
        ReplayFloater.DamageOverTime => DotTextColour,
        _ => HitTextColour,
    };

    /// <remarks>A critical hit is the one number the design draws larger as well as louder.</remarks>
    private static int SizeOf(ReplayFloater floater) =>
        floater == ReplayFloater.Crit ? CritTextSize : BodyTextSize;

    private CpuParticles2D? ParticlesFor(ReplayBurst burst) => burst switch
    {
        ReplayBurst.HitSpark => _hitSparks,
        ReplayBurst.CritPop => _critPop,
        ReplayBurst.DeathPuff => _deathPuff,
        _ => null,
    };

    /// <summary>Puts an actor's health bar on the value the playhead has walked it to.</summary>
    private void Spend(byte slot, double health)
    {
        if (!_rowBySlot.TryGetValue(slot, out var row) || row.Current is null)
        {
            return;
        }

        row.Current = health;
        row.Dirty = true;
    }

    /// <summary>Greys the token of a side one of whose actors has just gone down.</summary>
    private void Fell(ReplaySide side)
    {
        var token = side switch
        {
            ReplaySide.Hero => _heroToken,
            ReplaySide.Enemy => _enemyToken,
            _ => null,
        };

        if (token is not null)
        {
            token.Modulate = FallenColour;
        }
    }

    private void Stack(byte slot, ushort status, int stacks)
    {
        if (!_rowBySlot.TryGetValue(slot, out var row))
        {
            return;
        }

        if (stacks <= 0)
        {
            row.Stacks.Remove(status);
        }
        else
        {
            row.Stacks[status] = stacks;
        }

        row.StatusesDirty = true;
    }

    /// <summary>Puts one number in the air over the actor it happened to.</summary>
    /// <remarks>
    /// Fanned by a fixed step round the pool rather than scattered randomly: two numbers landing on
    /// one frame have to be readable as two, and a random offset in a scene is a second source of
    /// randomness in a game whose every other one is seeded and reproducible.
    /// </remarks>
    private void Float(ReplaySide side, double amount, Color colour, int size)
    {
        if (_floaters.Length == 0)
        {
            return;
        }

        var index = _nextFloater;

        _nextFloater = (_nextFloater + 1) % _floaters.Length;

        var floater = _floaters[index];

        _floatTweens[index]?.Kill();

        floater.Text = Math.Round(amount).ToString("0", CultureInfo.InvariantCulture);
        floater.AddThemeFontSizeOverride(FontSizeOverride, size);
        floater.AddThemeColorOverride(FontColourOverride, colour);
        floater.Modulate = StandingColour;
        floater.Visible = true;

        var from = PointOf(side);

        floater.Position = new Vector2(from.X, from.Y + (index * FloatFanStep));

        var seconds = _reducedMotion ? ReducedMotionSeconds : FloatSeconds;

        var tween = CreateTween();

        tween.SetParallel(true);
        tween.TweenProperty(
            floater, PositionProperty, new Vector2(floater.Position.X, floater.Position.Y - FloatRise),
            seconds);
        tween.TweenProperty(floater, ModulateProperty, new Color(StandingColour, 0), seconds);

        _floatTweens[index] = tween;
    }

    /// <summary>Fires one procedural burst, unless motion is reduced.</summary>
    /// <remarks>
    /// 🔒 The accessibility clause names particle bursts specifically, so this is where it is obeyed:
    /// the systems stay in the scene and are simply never started, which keeps the reduced-motion
    /// screen the same screen rather than a different one.
    /// </remarks>
    private void Burst(CpuParticles2D? particles, Vector2 at)
    {
        if (_reducedMotion || particles is null)
        {
            return;
        }

        particles.Position = at;
        particles.Restart();
    }

    /// <summary>Where on the stage an actor's effects happen — the hero's side, or the enemies'.</summary>
    private Vector2 PointOf(ReplaySide side)
    {
        if (_effects is not { } stage)
        {
            return Vector2.Zero;
        }

        var size = stage.Size;

        return new Vector2(size.X * (side == ReplaySide.Enemy ? 0.75f : 0.25f), size.Y * 0.5f);
    }

    /// <summary>Writes what has changed since the previous draw, and nothing that has not.</summary>
    /// <remarks>
    /// 🔒 Every write is behind a comparison because this runs on every frame of the fight. Formatting
    /// a health readout that has not moved allocates a string sixty times a second to produce the
    /// string that is already on screen.
    /// </remarks>
    private void Render()
    {
        var presenter = _presenter;

        // Validity before tree membership: asking a freed node whether it is inside the tree is
        // itself the crash, and this is the one screen in the build that genuinely frees itself.
        if (presenter is null || !IsInstanceValid(this) || !IsInsideTree() ||
            _statusLabel is null || _speedButton is null || _skipButton is null ||
            _phaseBand is null || _phaseBandLabel is null)
        {
            return;
        }

        var status = presenter.StatusText;

        if (!string.Equals(status, _lastStatusText, StringComparison.Ordinal))
        {
            _lastStatusText = status;
            _statusLabel.Text = status;

            // Hidden rather than blanked once it has nothing to say, which is what every other screen
            // in this build does with the same line: an empty label still claims a full line of
            // height, so a blank one is a sentence a player can see room for and cannot read.
            _statusLabel.Visible = status.Length > 0;
        }

        var speed = SpeedTextOf(presenter);

        if (!string.Equals(speed, _lastSpeedText, StringComparison.Ordinal))
        {
            _lastSpeedText = speed;
            _speedButton.Text = speed;
        }

        // 🔒 Never disabled, in any state. The skip is an accessibility clause rather than a
        // convenience, and it is the only way off a screen whose fight never materialised.
        _skipButton.Disabled = !presenter.SkipAvailable;

        RenderBand(presenter);
        RenderBars();
    }

    private string SpeedTextOf(BattleReplayPresenter presenter) => presenter.Speed switch
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

        // The flash, and the one transition on this screen: well inside the ceiling on how long a
        // transition may take, and gone by itself rather than waiting to be dismissed.
        band.Modulate = new Color(StandingColour, 0);

        CreateTween().TweenProperty(
            band, ModulateProperty, StandingColour,
            _reducedMotion ? ReducedMotionSeconds : BandFadeSeconds);
    }

    private void RenderBars()
    {
        for (var index = 0; index < _rows.Count; index++)
        {
            var row = _rows[index];

            if (row.Dirty)
            {
                row.Dirty = false;

                if (row.Current is { } current && row.Start is { } start)
                {
                    row.Bar.Value = current;
                    row.Value.Text =
                        Math.Round(current).ToString("0", CultureInfo.InvariantCulture) +
                        OverSeparator +
                        Math.Round(start).ToString("0", CultureInfo.InvariantCulture);
                }
            }

            if (row.StatusesDirty)
            {
                row.StatusesDirty = false;

                RenderStatuses(row);
            }
        }
    }

    /// <remarks>
    /// Rebuilt only when the ledger under it changed, which is on a status event and never on an
    /// ordinary frame. 🔴 A chip carries the status's number and its stack count and nothing else —
    /// see <see cref="AStatusEffectHasNoNameOrIconHere"/>.
    /// </remarks>
    private static void RenderStatuses(ActorBar row)
    {
        foreach (var child in row.Statuses.GetChildren())
        {
            row.Statuses.RemoveChild(child);
            child.QueueFree();
        }

        foreach (var (status, stacks) in row.Stacks)
        {
            var chip = new HBoxContainer();

            chip.AddChild(new ColorRect
            {
                CustomMinimumSize = ChipSwatchSize,
                Color = StatusChipColours[status % StatusChipColours.Length],
            });

            var label = new Label
            {
                Text = StatusPrefix + status.ToString(CultureInfo.InvariantCulture) +
                       StackPrefix + stacks.ToString(CultureInfo.InvariantCulture),
            };

            label.AddThemeFontSizeOverride(FontSizeOverride, ChipTextSize);
            label.AddThemeColorOverride(FontColourOverride, UnavailableColour);

            chip.AddChild(label);

            row.Statuses.AddChild(chip);
        }
    }

    private void OnSpeedPressed()
    {
        _presenter?.CycleSpeed();

        Render();
    }

    /// <remarks>
    /// 🔒 The skip answers in every state, including the states with nothing to skip. A fight that
    /// materialised is jumped to its end and confirmed; a fight that never did submits nothing at all
    /// and the screen simply stands down, which is what makes this control a way out rather than a
    /// convenience.
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
    /// walked. Where they are read to is the presenter's answer, and it is the presenter's answer
    /// precisely so that a fight watched to its end and one skipped to it cannot land on two
    /// different numbers. An actor whose health the log never fixed is left alone, which is the same
    /// answer it had all along.
    /// </remarks>
    private void AnchorToEnd(BattleReplayPresenter presenter)
    {
        for (var index = 0; index < _rows.Count; index++)
        {
            var row = _rows[index];

            if (presenter.HealthOf(row.Slot) is { } ending)
            {
                row.Current = ending;
                row.Dirty = true;
            }
        }
    }

    /// <summary>
    /// Prints, on one greppable line, what this screen resolved against the run the build actually
    /// shipped — including which half of the local prediction it got.
    /// </summary>
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

    /// <remarks>
    /// Invariant, like every other number on the readout. The generic form below reaches
    /// <c>ToString()</c>, which is the CURRENT culture for anything numeric — and a readout a grep
    /// has to match cannot be one the device's locale gets a say in.
    /// </remarks>
    private static string DescribePhase(int? phase) =>
        phase?.ToString(CultureInfo.InvariantCulture) ?? NoValue;

    private static string Describe<T>(T? value) where T : struct => value?.ToString() ?? NoValue;

    /// <summary>One actor's row of the health column, and the running state behind it.</summary>
    /// <remarks>
    /// A mutable holder rather than a record, because it is written to on every blow and the point of
    /// it is to be written to in place: a fresh instance per event would allocate on the frames this
    /// screen has least room to.
    /// </remarks>
    private sealed class ActorBar(
        byte slot, Label value, ProgressBar bar, HBoxContainer statuses, double? start)
    {
        /// <summary>The slot the log identifies this actor by, which the presenter is asked about it by.</summary>
        internal byte Slot { get; } = slot;

        /// <summary>The readout beside the bar.</summary>
        internal Label Value { get; } = value;

        /// <summary>The bar itself, hidden outright when the log fixes no starting health.</summary>
        internal ProgressBar Bar { get; } = bar;

        /// <summary>The row of status chips under the bar.</summary>
        internal HBoxContainer Statuses { get; } = statuses;

        /// <summary>What the log fixes this actor started on, or null when it fixes nothing.</summary>
        internal double? Start { get; } = start;

        /// <summary>Where the playhead has walked this actor's health to.</summary>
        internal double? Current { get; set; } = start;

        /// <summary>What is on this actor now, by status number.</summary>
        internal Dictionary<ushort, int> Stacks { get; } = new();

        /// <summary>Whether the health readout has moved since it was last drawn.</summary>
        internal bool Dirty { get; set; } = true;

        /// <summary>And whether the status ledger has.</summary>
        internal bool StatusesDirty { get; set; }
    }
}
