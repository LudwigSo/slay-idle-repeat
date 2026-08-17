using Godot;
using SlayIdleRepeat.Client.Composition;
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
/// 🔒 The composition/lifecycle seam and nothing else. There is no splash here, no session
/// handshake, no atlas load and no cold-start budget — the boot screen owns all four, and a
/// root that grew them would be the boot screen under another name. What the root does own is
/// the handover: once the graph is composed it puts <see cref="Boot"/> on screen and stops
/// drawing.
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
public partial class AppRoot : Node
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
    /// still reads nothing out of it but the host it gives its presenter.
    /// </remarks>
    private ComposedGodotClient? _composed;

    private AppRootPresenter? _presenter;

    /// <inheritdoc/>
    public override void _Ready()
    {
        Render("Composing…");

        _ = ComposeAndStartAsync();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Cancelled and deliberately not disposed. The token is still held by an open profile call
    /// at exactly this moment, and reading it from a disposed source throws — so disposing here
    /// would trade a leak of one wait handle for a crash on the way out.
    /// </remarks>
    public override void _ExitTree() => _lifetime.Cancel();

    /// <summary>
    /// Runs the composition root once, hands the host to the presenter, and shows where it got to.
    /// </summary>
    /// <remarks>
    /// The graph is composed once, here, and kept. What the root does NOT do is read it: the
    /// presenter gets the host and the root touches nothing else in it. That is the line the
    /// split actually draws — a scene may not use a port, and something has to own one.
    /// </remarks>
    private async Task ComposeAndStartAsync()
    {
        try
        {
            _composed = GodotClientComposition.ComposeLocalHost(GodotClientComposition.BuildCapabilities(this));

            _presenter = new AppRootPresenter(_composed.Client.GameHost);

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
