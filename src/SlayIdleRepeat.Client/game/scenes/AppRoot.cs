using Godot;
using SlayIdleRepeat.Client.Composition;
using SlayIdleRepeat.Client.Game.Net;
using SlayIdleRepeat.Client.Game.Presenters;

namespace SlayIdleRepeat.Client.Game.Scenes;

/// <summary>
/// The root scene: a driving adapter over <see cref="AppRootPresenter"/>.
/// </summary>
/// <remarks>
/// <para>
/// Scenes are the outermost ring, not the foundation. This one renders what the presenter
/// says and forwards what the player does; it holds no rules, no ports and no state of its
/// own, and it is the only half of the pair that is allowed to name the engine.
/// </para>
/// <para>
/// It is also the negative control for the presenter boundary rule: a scene script that
/// names the engine on purpose, so a rule finding nothing to object to here would be a rule
/// looking in the wrong place.
/// </para>
/// <para>
/// 🔒 The composition/lifecycle seam and nothing else. There is no splash here, no first
/// sign-in, no atlas load and no cold-start budget — the boot screen owns all four, and a root
/// that grew them would be the boot screen under another name. What the root does own is the
/// handover: once the graph is composed it puts <see cref="Boot"/> on screen and stops drawing.
/// </para>
/// <para>
/// 🔒 …and the two things whose lifetime is the application's rather than a screen's: the
/// connection overlay, and the per-frame tick that advances the connection behind it. Both are
/// asked for by factory and neither reads anything out of the graph, which is what keeps a scene
/// that drives the network from naming any of it.
/// </para>
/// <para>
/// ⚠️ The scene's SafeArea margin is a static worst-case guess, not a measurement: 128/64
/// canvas units land between roughly 43 and 53 dp across the density range, which clears a
/// 40 dp cutout even at the narrow end. The real insets are a runtime question for the
/// display server, and <see cref="Boot"/> is where it now gets asked — a node named for a
/// safe area is not evidence that one was resolved, and this number should not be copied as
/// if it were.
/// </para>
/// </remarks>
public partial class AppRoot : Node3D
{
    /// <summary>The scene-unique label the root reports its phase through.</summary>
    private const string StatusLabelPath = "%StatusLabel";

    /// <summary>The root's own layer, hidden the moment the first screen takes over.</summary>
    private const string UiLayerPath = "%Ui";

    /// <summary>Cancelled when the root leaves the tree, so a half-finished open stops there.</summary>
    private readonly CancellationTokenSource _lifetime = new();

    /// <summary>
    /// The composed graph, held for the life of the root.
    /// </summary>
    /// <remarks>
    /// 🔒 Ownership, not use. Hand-rolled composition has no container to keep the graph alive,
    /// and the root node is the only object whose lifetime is the application's — so if the root
    /// lets go, every later screen has to compose again, and a second graph means a second cache
    /// over the same directory. Holding it is what makes injection downward possible; the root
    /// reads nothing out of it at all, and hands it whole to the factories that do.
    /// </remarks>
    private ComposedGodotClient? _composed;

    private AppRootPresenter? _presenter;

    /// <summary>
    /// What advances the connection each frame on a build that has one, and null on one that does not.
    /// </summary>
    /// <remarks>
    /// 🔒 Driven from here for the reason the overlay is parented here, which
    /// <see cref="ShowConnectionOverlay"/> states.
    /// </remarks>
    private ConnectionPump? _pump;

    /// <inheritdoc/>
    public override void _Ready()
    {
        Render("Composing…");

        _ = ComposeAndStartAsync();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Every frame, and a no-op on the arm every shipped build takes: the driver is null there, and
    /// what it would advance was never built. On the server arm this is the only thing that turns a
    /// composed connection into a live one — the ladder retries nothing nobody asks it to.
    /// </remarks>
    /// <param name="delta">Unused — the ladder measures against the injected clock, not frames.</param>
    public override void _Process(double delta) => _pump?.Advance(_lifetime.Token);

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// Cancelled and deliberately not disposed. The token is still held by an open profile call
    /// at exactly this moment, and reading it from a disposed source throws — so disposing here
    /// would trade a leak of one wait handle for a crash on the way out.
    /// </para>
    /// <para>
    /// The order is the point. Cancelling first stops anything new being started, stopping the
    /// driver keeps it from starting one on the way past, and disposing last closes the transport
    /// underneath both. A request already inside the socket at this instant faults — after
    /// cancellation that is a shutdown rather than an error, and the driver reads it rather than
    /// leaving it to be dropped in silence.
    /// </para>
    /// </remarks>
    public override void _ExitTree()
    {
        _lifetime.Cancel();
        _pump?.Stop();
        _composed?.Dispose();
    }

    /// <summary>
    /// Runs the composition root once, asks it for the root presenter, and shows where it got to.
    /// </summary>
    /// <remarks>
    /// The graph is composed once, here, and kept. What the root does NOT do is read it: it asks a
    /// factory for a presenter and touches nothing inside the graph itself. That is the line the
    /// split actually draws — a scene may not name a port, and something has to.
    /// </remarks>
    private async Task ComposeAndStartAsync()
    {
        try
        {
            _composed = GodotClientComposition.ComposeClient(GodotClientComposition.BuildCapabilities(this));

            // Before the profile opens rather than after the boot screen is up: the connection is
            // the application's, not a screen's, and a build that only started climbing once a
            // screen existed would spend its whole cold start reporting a connection it had never
            // attempted.
            _pump = AppRootComposition.CreateConnectionPump(_composed.Client);

            // Up before the first screen is, for the same reason: what it draws belongs to the
            // application. Putting it up only once the boot screen appeared left the whole cold
            // start — the part of a launch a connection is most likely to be missing during —
            // drawing nothing about a connection that was already being climbed.
            ShowConnectionOverlay(_composed);

            _presenter = AppRootComposition.CreateAppRootPresenter(_composed);

            // No ConfigureAwait(false) here, and that is deliberate rather than an omission: the
            // continuation writes to a node, and only the thread the engine runs the scene tree on
            // may do that. Awaiting on the engine's synchronization context is what puts it back.
            await _presenter.StartAsync(_lifetime.Token);

            Render(_presenter.Phase switch
            {
                AppRootPhase.Ready => $"Ready — profile {_presenter.PlayerId}",
                AppRootPhase.Failed => $"Failed — {_presenter.FailureReason}",
                _ => "Composing…",
            });

            if (_presenter.Phase == AppRootPhase.Ready)
            {
                ShowBoot(_composed);
            }
        }
        catch (Exception failure)
        {
            // Broad on purpose: this is the outermost frame of the application, so anything not
            // caught here is caught by nobody. Composition failing is not the presenter's Failed
            // phase either — there is no presenter yet to hold it — so it is reported twice, once
            // where a developer will see it and once where a player would.
            GD.PushError($"The client could not be composed: {failure.Message}");

            Render($"Failed — {failure.Message}");
        }
    }

    /// <summary>Puts the boot screen on top of the root and stands down.</summary>
    /// <remarks>
    /// <para>
    /// The root keeps owning the composed graph — a second graph would mean a second cache over the
    /// same directory — and hands the boot screen a presenter built from it rather than the graph
    /// itself, which is the same shape the root's own presenter gets.
    /// </para>
    /// <para>
    /// ⚠️ The boot screen opens the profile again. That is one cache read on the cold-start path,
    /// spent because the boot's profile stage is a stage a player watches and a failure it must be
    /// able to name — not a fact it inherits. Cheap because the open is idempotent, and stated here
    /// so the cost is a decision rather than an accident.
    /// </para>
    /// </remarks>
    private void ShowBoot(ComposedGodotClient composed)
    {
        // This runs in a continuation, so the root may have been freed or pulled out of the tree
        // while the profile was opening. Validity before tree membership, for the same reason
        // Render checks them in that order.
        if (!IsInstanceValid(this) || !IsInsideTree())
        {
            return;
        }

        // Presenter first, scene second: composing it resolves the content root through the engine
        // and throws by name when there is none, and a scene instantiated before that throw is a
        // node with no parent that nothing ever frees.
        var presenter = BootComposition.CreateBootPresenter(composed);
        var scene = GD.Load<PackedScene>(Boot.ScenePath);

        if (scene is null)
        {
            // Load answers null rather than throwing when the resource is missing or its import
            // cannot be read, so an unnamed null reference is all a caller gets unless it says so.
            GD.PushError($"The boot screen could not be loaded from '{Boot.ScenePath}'.");
            Render($"Failed — the boot screen is missing from this build ({Boot.ScenePath})");

            return;
        }

        var boot = scene.Instantiate<Boot>();

        // The graph goes with the presenter because the boot screen hands over in turn, to a screen
        // whose presenter does not exist yet — and by then the root is no longer the one handing.
        // Still ownership, not use: the root keeps the reference that makes the graph outlive every
        // screen built from it.
        boot.Drive(presenter, composed, _lifetime.Token);

        // The root's own layer sits above the default one, so it would draw over the screen it just
        // handed control to.
        GetNode<CanvasLayer>(UiLayerPath).Visible = false;

        AddChild(boot);
    }

    /// <summary>Puts the connection overlay up, if this build composed a connection to draw.</summary>
    /// <remarks>
    /// <para>
    /// 🔒 Parented to the root and to nothing else. What it draws is global — one connection, not one
    /// per screen — and the root is the only node whose lifetime is the application's, so this is the
    /// only parent that survives every handover. It is a sibling of the root's own <c>%Ui</c> layer
    /// rather than a child of it, which is the whole reason it is its own <see cref="CanvasLayer"/>:
    /// a child would have gone dark with the boot chrome on the very first handover.
    /// </para>
    /// <para>
    /// 🔒 <b>The null branch is the ordinary one, and it is not an absence.</b> A build composed over
    /// no wire seam has no connection to lose, so the factory answers null, nothing is instantiated
    /// and nothing about the connection is ever drawn — which is the specified rendering for a
    /// working connection. The other branch is live: the server arm composes the presenter and the
    /// root drives its ladder from <see cref="_Process"/>.
    /// </para>
    /// <para>
    /// 🔴 <b>Raised here rather than after the boot screen, so it can draw over the splash — which
    /// is deliberate, and which quietly enters a state nobody has specified.</b> Deliberate because
    /// a cold start is when a connection is most likely to be missing, the pill's own threshold
    /// already keeps a blink of failure off the screen, and nothing it draws can be tapped or
    /// blocked. Unspecified because what a player is shown when the FIRST connection of a launch is
    /// the one that fails is O32, still open — <see cref="Boot"/> names the same gap for the screen
    /// underneath. What is drawn during that window today is the mid-session reconnect pill, for
    /// want of anything authored; it is the least-wrong default rather than an answer, and the
    /// milestone that closes O32 should read this paragraph before assuming it was one.
    /// </para>
    /// </remarks>
    private void ShowConnectionOverlay(ComposedGodotClient composed)
    {
        if (AppRootComposition.CreateConnectionPresenter(composed) is not { } connection)
        {
            return;
        }

        var scene = GD.Load<PackedScene>(ConnectionOverlay.ScenePath);

        if (scene is null)
        {
            // Load answers null rather than throwing when the resource is missing or its import
            // cannot be read. Reported and then dropped: a missing overlay costs the player the
            // status pill, and taking the application down over it would be the blocking connection
            // error the overlay exists to avoid.
            GD.PushError(
                $"The connection overlay could not be loaded from '{ConnectionOverlay.ScenePath}', " +
                "so this build will draw none of the connection states.");

            return;
        }

        var overlay = scene.Instantiate<ConnectionOverlay>();

        // 🔴 The motion setting is named at the call site rather than left to its default, the way
        // the battle replay and the perk draft already name theirs: this is the first live caller
        // the overlay has ever had, and a reader who found it would otherwise have no way of seeing
        // that the pulse, the slide and the flash have a switch at all. False because there is
        // nowhere for a preference to have come from — this client has no settings screen — and
        // saying so here is what keeps that a stated absence rather than an oversight.
        overlay.Drive(connection, _lifetime.Token, reducedMotion: false);

        AddChild(overlay);
    }

    /// <summary>Writes a line of status into the scene, if the scene is still there to write it into.</summary>
    /// <remarks>
    /// ⚠️ Placeholder presentation. The root's three phases are text because the root is a seam,
    /// not a screen; what a player actually sees while the game starts is the boot screen's. Its
    /// size and colour repeat <c>Boot.tscn</c>'s status line by hand so the handover does not
    /// change type mid-launch — theme-kit debt owed to M8-03, not a scheme either scene invented —
    /// and the line count is capped, because a composition failure puts a whole exception message
    /// through this one label and the untrimmed text goes to the error log above.
    /// </remarks>
    private void Render(string status)
    {
        // The root can be freed outright while the host is still opening the profile — a shutdown
        // during start is the ordinary case on a handset — and the continuation still runs after
        // it, on the engine's context rather than on the node. Validity is checked before the tree
        // is, because asking a freed node whether it is in the tree is itself the crash.
        if (!IsInstanceValid(this) || !IsInsideTree())
        {
            return;
        }

        GetNode<Label>(StatusLabelPath).Text = status;
    }
}
