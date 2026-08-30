using Godot;
using SlayIdleRepeat.Client.Game.Net;
using SlayIdleRepeat.Client.Game.Presenters;

namespace SlayIdleRepeat.Client.Game.Scenes;

/// <summary>
/// The connection, drawn: a driving adapter over <see cref="ConnectionPresenter"/>, and the only
/// thing in the build that draws any of the five connection presentations.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Global, never per-screen.</b> It is its own <see cref="CanvasLayer"/> parented to
/// <see cref="AppRoot"/>, a sibling of the root's <c>%Ui</c> layer rather than a child of it, for two
/// measured reasons: the root hides <c>%Ui</c> the moment the first screen takes over, so a child of
/// it would go dark on the first handover; and every screen's own overlay is layer 1 while the root's
/// is layer 10, so anything meant to survive all of them has to sit above both. A per-screen copy
/// would draw two pills during a handover and none on the screen that forgot to build one.
/// </para>
/// <para>
/// 🔒 <b>Connected draws NOTHING.</b> There is no tick, no badge and no colour for a working
/// connection anywhere in this file — every node starts hidden and only a state that has something to
/// say turns one on.
/// </para>
/// <para>
/// 🔒 <b>It cannot block, and that is structural rather than promised.</b> Every control in
/// <c>ConnectionOverlay.tscn</c> is authored with <c>mouse_filter = 2</c> (Ignore), and
/// <see cref="IgnoreInputEverywhere"/> walks the whole subtree on the way in and sets it again on
/// every descendant. So a control added later — by hand, or by a scene merge that dropped an
/// override — cannot swallow a tap: the screen underneath this layer stays interactive in every one
/// of the five states, which is what "never a full-screen blocking connection error" means once it is
/// drawn rather than merely stated.
/// </para>
/// <para>
/// 🔒 <b>Instantiated only on the arm that has a connection to lose.</b> The presenter is composed
/// when the client was composed over a server, which <c>GodotClientComposition.ComposeClient</c>
/// decides from the environment; an in-process build has no connection to lose, so no
/// <c>ConnectionOverlay</c> is instantiated at all — which is exactly the specified rendering for
/// <em>Connected</em>: nothing at all. On the server arm the state this draws moves because
/// <see cref="AppRoot"/> advances the ladder every frame, and <see cref="StateMarker"/> is how a
/// headless run proves both halves of that from outside the process.
/// </para>
/// <para>
/// ⚠️ Every colour, corner and type size here is a per-node override with an inline
/// <c>StyleBoxFlat</c>, because the shared theme resource does not exist — M8-03's, to be re-checked
/// rather than re-applied. The green is the fixed palette's B-rarity green, the one green this client
/// already draws twice (<c>Inventory</c> and <c>PerkDraft</c> both write
/// <c>Color(0.298, 0.6863, 0.3137)</c>); a second, invented green would be a second green in a game
/// whose palette is fixed.
/// </para>
/// </remarks>
public partial class ConnectionOverlay : CanvasLayer
{
    /// <summary>Where this scene lives, for the root that instantiates it.</summary>
    public const string ScenePath = "res://game/scenes/ConnectionOverlay.tscn";

    /// <summary>
    /// The one line a headless run reads the connection off. Distinctive on purpose, and the same
    /// mechanism <c>Boot</c>'s cold-start marker already uses rather than a second scheme.
    /// </summary>
    /// <remarks>
    /// 🔒 Two claims no unit tier can make, because both reach GodotSharp: that this overlay is
    /// instantiated at all in a composed build, and that the state it draws actually moves. The
    /// first is the first line printed; the second is the line printed again with a different
    /// state, which can only happen if something advanced the ladder.
    /// </remarks>
    private const string StateMarker = "SIR_CONNECTION_OVERLAY";

    private const string SafeAreaPath = "%SafeArea";
    private const string PillRowPath = "%PillRow";
    private const string PillPath = "%Pill";
    private const string PillLabelPath = "%PillLabel";
    private const string CardRowPath = "%CardRow";
    private const string ResumeCardPath = "%ResumeCard";
    private const string ResumeLabelPath = "%ResumeLabel";
    private const string ToastRowPath = "%ToastRow";
    private const string ToastPath = "%Toast";
    private const string ToastLabelPath = "%ToastLabel";
    private const string FlashPath = "%Flash";

    /// <summary>The property a slide moves, and the two an appearance and the flash fade.</summary>
    private const string SlideProperty = "position:y";
    private const string AlphaProperty = "modulate:a";
    private const string FlashAlphaProperty = "color:a";

    /// <summary>
    /// How far above its resting place the pill starts, in canvas units.
    /// </summary>
    /// <remarks>
    /// ⚠️ A chosen number, not an authored one: nothing in the specification says how far a pill
    /// slides. 240 units is roughly 80 dp — about two and a half pill heights — which reads as
    /// "arriving from off the top" without the pill spending the whole slide outside the safe area,
    /// where a resolved inset could leave it visible above the cutout.
    /// </remarks>
    private const float SlideTravel = 240f;

    /// <summary>How long the pill takes to arrive. A chosen number.</summary>
    private const double SlideSeconds = 0.28;

    /// <summary>How long the toast and the resume card take to fade up. A chosen number.</summary>
    private const double AppearSeconds = 0.18;

    /// <summary>Half of one pulse: the pill dims over this and comes back over the same. Chosen.</summary>
    /// <remarks>
    /// "Slow" is the only thing the specification says about the pulse, so it is stated as one number
    /// here rather than pretended to be read from anywhere. 1.1 s each way is a 2.2 s cycle — slower
    /// than a resting pulse, which is what stops it reading as an alarm.
    /// </remarks>
    private const double PulseSeconds = 1.1;

    /// <summary>How far down the pill's opacity travels on each pulse. A chosen number.</summary>
    private const float PulseFloor = 0.55f;

    /// <summary>
    /// The whole resync announcement: 🔒 0.4 s, the one duration here the specification fixes.
    /// </summary>
    /// <remarks>
    /// Split into a fast rise and a slower fall so the flash reads as a flash rather than as a fade
    /// in and out. The two add up to the authored total, which is also the window
    /// <see cref="ConnectionPresenter"/> keeps the accompanying line of text up for.
    /// </remarks>
    private const double FlashRiseSeconds = 0.12;

    private const double FlashFallSeconds = 0.28;

    /// <summary>How green the screen actually goes. A chosen number.</summary>
    /// <remarks>
    /// It is a tint over a live screen rather than a colour of its own, so it is drawn well below
    /// half: the point is that something good happened, and a player mid-run must still be able to
    /// read the board through it.
    /// </remarks>
    private const float FlashPeakAlpha = 0.22f;

    /// <summary>Fully opaque, for a control coming back from a fade.</summary>
    private const float Opaque = 1f;

    /// <summary>And fully transparent, for one about to arrive.</summary>
    private const float Transparent = 0f;

    private ConnectionPresenter? _presenter;
    private CancellationToken _lifetime;

    /// <summary>
    /// 🔴 Whether every animation here is suppressed and the same information shown statically.
    /// </summary>
    /// <remarks>
    /// Deliberately a plain field with no source, and named so the gap can be found. This client has
    /// no settings screen and no stored preference — <c>BattleReplay</c> and <c>PerkDraft</c> carry
    /// the identical seam for the identical reason, as a constructor argument that nothing ever
    /// passes true for. The day a preference exists, one call site flips this and the pulse, the
    /// slide, the fades and the flash all stand down together; until then it is honest to say the
    /// overlay does not honour a setting that does not exist, rather than to invent a source for it.
    /// </remarks>
    private bool _reducedMotion;

    private MarginContainer? _safeArea;
    private Control? _pillRow;
    private Control? _pill;
    private Label? _pillLabel;
    private Control? _cardRow;
    private Control? _resumeCard;
    private Label? _resumeLabel;
    private Control? _toastRow;
    private Control? _toast;
    private Label? _toastLabel;
    private ColorRect? _flash;

    private Tween? _slide;
    private Tween? _pulse;

    // 🔒 One field per control, never one shared "appear". The toast and the resume card can be up
    // at the same moment — a tap on a dimmed control during a resume is the ordinary way — and a
    // single field meant the second fade killed the first mid-flight, leaving a control that is
    // Visible and fully transparent for as long as it is up.
    private Tween? _toastAppear;
    private Tween? _cardAppear;
    private Tween? _flashTween;

    /// <summary>What each label was last written with, so a redraw writes nothing when nothing moved.</summary>
    private string _drawnPillText = "";
    private string _drawnToastText = "";
    private string _drawnResumeText = "";

    /// <summary>The state the marker last carried, so a redraw prints nothing when nothing moved.</summary>
    private ConnectionState? _reportedState;

    private bool _pillWasVisible;
    private bool _toastWasVisible;
    private bool _cardWasVisible;
    private bool _flashWasActive;

    /// <summary>Takes the presenter this overlay draws and the application's shutdown token.</summary>
    /// <param name="presenter">The one connection presenter the whole build shares.</param>
    /// <param name="lifetime">Cancelled when the application shuts down.</param>
    /// <param name="reducedMotion">
    /// 🔴 Whether motion is suppressed. Nothing passes true — see <see cref="_reducedMotion"/>.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="presenter"/> is null.</exception>
    public void Drive(ConnectionPresenter presenter, CancellationToken lifetime, bool reducedMotion = false)
    {
        ArgumentNullException.ThrowIfNull(presenter);

        _presenter = presenter;
        _lifetime = lifetime;
        _reducedMotion = reducedMotion;
    }

    /// <inheritdoc/>
    public override void _Ready()
    {
        // Resolved once. A scene-unique lookup is a string search of the owner's table, and this
        // node redraws every frame for the life of the application.
        _safeArea = Find<MarginContainer>(SafeAreaPath);
        _pillRow = Find<Control>(PillRowPath);
        _pill = Find<Control>(PillPath);
        _pillLabel = Find<Label>(PillLabelPath);
        _cardRow = Find<Control>(CardRowPath);
        _resumeCard = Find<Control>(ResumeCardPath);
        _resumeLabel = Find<Label>(ResumeLabelPath);
        _toastRow = Find<Control>(ToastRowPath);
        _toast = Find<Control>(ToastPath);
        _toastLabel = Find<Label>(ToastLabelPath);
        _flash = Find<ColorRect>(FlashPath);

        IgnoreInputEverywhere(this);

        if (_safeArea is not null)
        {
            SafeAreaInsets.ApplyTo(_safeArea, GetViewport().GetVisibleRect().Size);
        }

        Render();
        Report();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// ⚠️ Every tween is killed, including the looping one. A tween outlives the node that created
    /// it unless it is bound or killed, and the pulse loops forever by construction — so a build that
    /// forgot this line would leave one running against a freed control for the life of the process.
    /// </remarks>
    public override void _ExitTree()
    {
        Stop(ref _slide);
        Stop(ref _pulse);
        Stop(ref _toastAppear);
        Stop(ref _cardAppear);
        Stop(ref _flashTween);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Every frame, because two of the five presentations expire on a clock the presenter owns and
    /// nothing else would ask it what time it is. It allocates nothing: the nodes were resolved in
    /// <c>_Ready</c>, every text is compared before it is written, and a tween is only ever built on
    /// the frame a state actually changes.
    /// </remarks>
    /// <param name="delta">Unused — the presenter measures against the injected clock, not frames.</param>
    public override void _Process(double delta)
    {
        if (_presenter is not { } presenter || _lifetime.IsCancellationRequested)
        {
            return;
        }

        presenter.Poll();

        Render();
        Report();
    }

    /// <summary>Prints the connection the moment it changes, and on no other frame.</summary>
    /// <remarks>
    /// Guarded by the state it last printed rather than by a frame counter, so a run's log carries
    /// one line per transition — which is what makes "the ladder moved" readable from outside the
    /// process at all. Printed here rather than inside <c>Render</c> because the marker is about the
    /// connection, not about whether this overlay found its nodes.
    /// </remarks>
    private void Report()
    {
        if (_presenter is not { } presenter || _reportedState == presenter.State)
        {
            return;
        }

        _reportedState = presenter.State;

        GD.Print($"{StateMarker} state={presenter.State}");
    }

    /// <summary>Writes the presenter's five answers into the scene, and animates the edges.</summary>
    private void Render()
    {
        if (_presenter is not { } presenter || !IsInstanceValid(this) || !IsInsideTree() ||
            _pillRow is null || _pill is null || _pillLabel is null ||
            _cardRow is null || _resumeCard is null || _resumeLabel is null ||
            _toastRow is null || _toast is null || _toastLabel is null || _flash is null)
        {
            return;
        }

        RenderPill(presenter);
        RenderToast(presenter);
        RenderResumeCard(presenter);
        RenderFlash(presenter);
    }

    private void RenderPill(ConnectionPresenter presenter)
    {
        var visible = presenter.PillVisible;

        if (visible)
        {
            Write(_pillLabel!, presenter.PillText, ref _drawnPillText);
        }

        if (visible == _pillWasVisible)
        {
            return;
        }

        _pillWasVisible = visible;
        _pillRow!.Visible = visible;

        Stop(ref _slide);
        Stop(ref _pulse);

        if (!visible)
        {
            return;
        }

        if (_reducedMotion)
        {
            // The same information, held still: the pill is simply there, at rest and at full
            // opacity. A reduced-motion setting is about movement, not about being told less.
            _pillRow.Position = new Vector2(_pillRow.Position.X, 0f);
            _pill!.Modulate = new Color(Opaque, Opaque, Opaque, Opaque);

            return;
        }

        _pillRow.Position = new Vector2(_pillRow.Position.X, -SlideTravel);
        _pill!.Modulate = new Color(Opaque, Opaque, Opaque, Transparent);

        _slide = CreateTween().SetParallel();
        _slide.TweenProperty(_pillRow, SlideProperty, 0f, SlideSeconds)
              .SetTrans(Tween.TransitionType.Cubic)
              .SetEase(Tween.EaseType.Out);
        _slide.TweenProperty(_pill, AlphaProperty, Opaque, SlideSeconds);

        // A chained callback rather than the Finished signal: a signal owes a disconnect, and a
        // tween that is killed mid-slide simply never reaches this.
        _slide.Chain().TweenCallback(Callable.From(StartPulse));
    }

    /// <summary>Starts the slow pulse, once the pill has finished arriving.</summary>
    private void StartPulse()
    {
        if (_pill is null || !IsInstanceValid(_pill))
        {
            return;
        }

        Stop(ref _pulse);

        _pulse = CreateTween().SetLoops();
        _pulse.TweenProperty(_pill, AlphaProperty, PulseFloor, PulseSeconds)
              .SetTrans(Tween.TransitionType.Sine);
        _pulse.TweenProperty(_pill, AlphaProperty, Opaque, PulseSeconds)
              .SetTrans(Tween.TransitionType.Sine);
    }

    /// <remarks>
    /// 🔒 A strip, never a modal and never a dialogue with a button. The presenter decides when it is
    /// up and what it says; this half decides only that it is drawn along the bottom of the safe area
    /// and that it cannot be tapped.
    /// </remarks>
    private void RenderToast(ConnectionPresenter presenter)
    {
        var text = presenter.ToastText;
        var visible = text is not null;

        if (text is not null)
        {
            Write(_toastLabel!, text, ref _drawnToastText);
        }

        if (visible == _toastWasVisible)
        {
            return;
        }

        _toastWasVisible = visible;
        _toastRow!.Visible = visible;

        FadeUp(_toast!, visible, ref _toastAppear);
    }

    private void RenderResumeCard(ConnectionPresenter presenter)
    {
        var visible = presenter.ResumeCardVisible;

        if (visible)
        {
            Write(_resumeLabel!, presenter.ResumeCardText, ref _drawnResumeText);
        }

        if (visible == _cardWasVisible)
        {
            return;
        }

        _cardWasVisible = visible;
        _cardRow!.Visible = visible;

        FadeUp(_resumeCard!, visible, ref _cardAppear);
    }

    private void FadeUp(Control control, bool visible, ref Tween? appear)
    {
        Stop(ref appear);

        if (!visible)
        {
            return;
        }

        if (_reducedMotion)
        {
            control.Modulate = new Color(Opaque, Opaque, Opaque, Opaque);

            return;
        }

        control.Modulate = new Color(Opaque, Opaque, Opaque, Transparent);

        appear = CreateTween();
        appear.TweenProperty(control, AlphaProperty, Opaque, AppearSeconds);
    }

    private void RenderFlash(ConnectionPresenter presenter)
    {
        var active = presenter.ResyncFlashActive;

        if (active == _flashWasActive)
        {
            return;
        }

        _flashWasActive = active;

        if (!active)
        {
            return;
        }

        if (_reducedMotion)
        {
            // 🔒 Suppressed outright rather than shortened, and no information is lost with it: the
            // presenter raises the "caught up" line in the same instant and for the same window, so
            // the resync is still announced — in words, which is the static form of the same fact.
            return;
        }

        Stop(ref _flashTween);

        _flash!.Color = new Color(_flash.Color, Transparent);
        _flash.Visible = true;

        _flashTween = CreateTween();
        _flashTween.TweenProperty(_flash, FlashAlphaProperty, FlashPeakAlpha, FlashRiseSeconds);
        _flashTween.TweenProperty(_flash, FlashAlphaProperty, Transparent, FlashFallSeconds);
        _flashTween.TweenCallback(Callable.From(HideFlash));
    }

    private void HideFlash()
    {
        if (_flash is not null && IsInstanceValid(_flash))
        {
            _flash.Visible = false;
        }
    }

    /// <summary>Writes a label only when what it should say has actually changed.</summary>
    /// <remarks>
    /// This runs every frame. Assigning <c>Text</c> marshals a managed string across the engine
    /// boundary whether or not it differs, so the comparison is what keeps the per-frame cost at a
    /// reference check.
    /// </remarks>
    private static void Write(Label label, string text, ref string drawn)
    {
        if (string.Equals(drawn, text, StringComparison.Ordinal))
        {
            return;
        }

        drawn = text;
        label.Text = text;
    }

    /// <summary>
    /// Makes every control under this layer transparent to input, whatever the scene authored.
    /// </summary>
    /// <remarks>
    /// 🔒 The structural half of "this overlay can never block". The scene sets
    /// <c>mouse_filter = 2</c> on every node, and this sets it again on every descendant at runtime,
    /// so a control added later or an override lost in a merge cannot quietly start swallowing taps
    /// on the screen underneath. A connection overlay that eats a press IS the full-screen blocking
    /// error the specification forbids, wearing a smaller rectangle.
    /// </remarks>
    private static void IgnoreInputEverywhere(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is Control control)
            {
                control.MouseFilter = Control.MouseFilterEnum.Ignore;
            }

            IgnoreInputEverywhere(child);
        }
    }

    /// <summary>Kills a tween if there is one, and forgets it either way.</summary>
    private static void Stop(ref Tween? tween)
    {
        if (tween is not null && tween.IsValid())
        {
            tween.Kill();
        }

        tween = null;
    }

    /// <summary>
    /// Resolves one scene-unique node, naming it if it is missing rather than crashing on it.
    /// </summary>
    /// <remarks>
    /// An overlay that cannot find its pill must not take the application down: it is drawn over a
    /// run in progress, and the whole point of it is that a connection problem never stops one.
    /// </remarks>
    private T? Find<T>(string path)
        where T : Node
    {
        var node = GetNodeOrNull<T>(path);

        if (node is null)
        {
            GD.PushError(
                $"The connection overlay has no '{path}', so that part of it will not be drawn. " +
                $"Every node it renders is marked scene-unique in {ScenePath}.");
        }

        return node;
    }
}
