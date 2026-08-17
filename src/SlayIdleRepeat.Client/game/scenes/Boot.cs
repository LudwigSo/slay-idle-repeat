using Godot;
using SlayIdleRepeat.Client.Composition;
using SlayIdleRepeat.Client.Game.Presenters;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Client.Game.Scenes;

/// <summary>
/// S01 — the splash and loading screen: a driving adapter over <see cref="BootPresenter"/>.
/// </summary>
/// <remarks>
/// <para>
/// It renders the stage the presenter reports and nothing else. No rules, no ports, no adapters:
/// the application root composes the presenter and hands it over, and this half owns only the
/// things that genuinely need the engine — the safe-area query, the drawn ground, the error log and
/// the engine-level cold-start reading.
/// </para>
/// <para>
/// ⚠️ Every type size, colour and gap in <c>Boot.tscn</c> is a per-node override, because the
/// shared theme resource and the two display faces it will carry do not exist yet — they are
/// M8-03's, and these overrides are debt owed to it rather than a naming scheme of this screen's
/// own. <c>AppRoot.tscn</c>'s status line repeats the same two values by hand for that reason, and
/// both move together when the theme kit lands. The sizes were chosen against the engine's default
/// font and the longest translated string, so they have to be
/// re-checked — not merely re-applied — when the real faces land. The layout itself is structural:
/// containers and stretch ratios, so it holds its proportions across the whole supported aspect
/// range without an override taking part.
/// </para>
/// <para>
/// 🔴 <b>The player-facing failure SCREEN is deliberately not built here</b> — see
/// <see cref="FailureScreenIsNotDesignedHere"/>. What a failed boot shows a player is the one
/// localised failure line the status label already carries, and nothing else. The failure's full
/// identity — its kind, its stage and the exception behind it — goes to the engine's error log,
/// which is where the person who can act on it reads. Putting that identity on the display instead
/// would put untranslated type names and a filesystem path in front of somebody who cannot use
/// either, under a sentence that had just been translated for them.
/// </para>
/// </remarks>
public partial class Boot : Control
{
    /// <summary>Where this scene lives, for the root that instantiates it.</summary>
    public const string ScenePath = "res://game/scenes/Boot.tscn";

    /// <summary>
    /// 🔴 Deliberately unbuilt, and named so it can be found. The maintenance, forced-update and
    /// first-boot-failure states — and the retry ladder that goes with them — are an open decision
    /// (O32) owned by a later milestone. Designing any of them here would make an unbuilt flow look
    /// shipped, so this string is all that stands where that screen will go, and it is logged with
    /// every failure rather than silently held.
    /// </summary>
    private const string FailureScreenIsNotDesignedHere =
        "O32 is open: no maintenance dialog, no retry ladder and no forced-update prompt is designed " +
        "on this screen. The failure above is reported, not handled.";

    /// <summary>
    /// The one line a headless run's cold-start measurement is read off. Distinctive on purpose:
    /// a measured number has to be greppable out of an engine log full of everything else.
    /// </summary>
    private const string ColdStartMarker = "SIR_BOOT_COLDSTART";

    private const string SafeAreaPath = "%SafeArea";
    private const string TitleLabelPath = "%TitleLabel";
    private const string StatusLabelPath = "%StatusLabel";

    private BootPresenter? _presenter;

    /// <summary>
    /// The composed graph, carried through rather than used.
    /// </summary>
    /// <remarks>
    /// 🔒 This screen reads nothing out of it. It holds it because the screen it hands over to
    /// needs a presenter built from the graph the application root already owns, and composing a
    /// second graph here would mean a second cache over the same directory. Ownership stays the
    /// root's; this is the handover carrying it one hop further down the only path there is.
    /// </remarks>
    private ComposedGodotClient? _composed;

    private CancellationToken _lifetime;

    private Label? _titleLabel;
    private Label? _statusLabel;

    private string? _drawnTitle;
    private string? _drawnStatus;

    /// <summary>
    /// Takes the presenter the composition root built, the graph it was built from, and the token
    /// the app shuts down through.
    /// </summary>
    /// <remarks>
    /// Called before the node enters the tree, so <c>_Ready</c> has something to draw. The graph
    /// comes with the presenter because a finished boot hands over to a screen whose presenter does
    /// not exist yet, and the root that owns the graph is no longer the one doing the handing.
    /// </remarks>
    /// <param name="presenter">Drives this screen.</param>
    /// <param name="composed">The graph the application root built and holds.</param>
    /// <param name="lifetime">Cancelled when the application shuts down.</param>
    /// <exception cref="ArgumentNullException"><paramref name="presenter"/> or <paramref name="composed"/> is null.</exception>
    public void Drive(BootPresenter presenter, ComposedGodotClient composed, CancellationToken lifetime)
    {
        ArgumentNullException.ThrowIfNull(presenter);
        ArgumentNullException.ThrowIfNull(composed);

        _presenter = presenter;
        _composed = composed;
        _lifetime = lifetime;
    }

    /// <inheritdoc/>
    public override void _Ready()
    {
        // Resolved once. Render runs every frame while the boot does, and a scene-unique lookup is
        // a string search of the owner's table each time it is asked.
        _titleLabel = GetNode<Label>(TitleLabelPath);
        _statusLabel = GetNode<Label>(StatusLabelPath);

        SafeAreaInsets.ApplyTo(GetNode<MarginContainer>(SafeAreaPath), GetViewportRect().Size);
        Render();

        _ = RunAsync();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The stages between the first frame and the last are the ones a player spends the boot looking
    /// at, and they pass between two awaits rather than at one. Redrawing per frame while the boot
    /// runs is what makes them visible; it stops the moment the boot does.
    /// </remarks>
    public override void _Process(double delta) => Render();

    /// <remarks>
    /// <para>
    /// Nothing awaits this task, so its exceptions have nowhere to surface: the whole body is
    /// guarded, or a continuation that threw would leave a screen that silently stopped moving.
    /// </para>
    /// <para>
    /// A finished boot hands over to <see cref="Home"/>, which is the seam this file named while
    /// Home was still a later task's. A boot that FAILED hands over to nothing: the localised
    /// failure line stays on screen, because the alternative is a home screen drawn over a profile
    /// that never opened. The decision between Home and a resumed run is Home's own, made from the
    /// stored state and nothing this screen carries forward.
    /// </para>
    /// </remarks>
    private async Task RunAsync()
    {
        try
        {
            var presenter = _presenter;

            if (presenter is null)
            {
                StopRedrawing();

                GD.PushError(
                    "The boot screen entered the tree with no presenter. Only the application root " +
                    "may instantiate it, and it must call Drive before adding it to the tree.");

                return;
            }

            // No ConfigureAwait(false): the continuation writes to nodes, and only the thread the
            // engine runs the scene tree on may do that.
            await presenter.StartAsync(_lifetime);

            StopRedrawing();
            Render();
            Report(presenter);

            if (presenter.Stage == BootStage.Ready && presenter.PlayerId is { } player)
            {
                ShowHome(player);
            }
        }
        catch (Exception failure)
        {
            GD.PushError($"The boot screen stopped unexpectedly: {failure}");
        }
    }

    /// <summary>Puts the home screen beside this one and stands down.</summary>
    /// <remarks>
    /// <para>
    /// Presenter first, scene second — the same order and the same reasons the application root
    /// uses for this screen: a scene instantiated before a throw is a node with no parent that
    /// nothing ever frees, and <c>Load</c> answers null rather than throwing when a resource is
    /// missing, so an unnamed null reference is all a caller gets unless it says so.
    /// </para>
    /// <para>
    /// Home is added to this screen's own parent rather than to this screen, and this screen is
    /// hidden. A child would be drawn inside a ground this screen still owns, and the root above
    /// holds the graph both screens were built from either way.
    /// </para>
    /// </remarks>
    private void ShowHome(PlayerId player)
    {
        // This runs in a continuation, so the screen may have been freed or pulled out of the tree
        // while the boot was running. Validity before tree membership, for the same reason Render
        // checks them in that order.
        if (!IsInstanceValid(this) || !IsInsideTree() || _composed is not { } composed)
        {
            return;
        }

        var parent = GetParent();

        if (parent is null)
        {
            GD.PushError("The boot screen has no parent to hand the home screen to.");

            return;
        }

        var screen = HomeComposition.CreateHomeScreen(composed, player);
        var scene = GD.Load<PackedScene>(Home.ScenePath);

        if (scene is null)
        {
            GD.PushError($"The home screen could not be loaded from '{Home.ScenePath}'.");

            return;
        }

        var home = scene.Instantiate<Home>();

        home.Drive(screen.Home, screen.ChapterSelect, _lifetime);

        Visible = false;

        parent.AddChild(home);
    }

    /// <summary>Stops the per-frame redraw, if there is still a node left to stop it on.</summary>
    private void StopRedrawing()
    {
        if (IsInstanceValid(this))
        {
            SetProcess(false);
        }
    }

    /// <summary>Writes the presenter's state into the scene, if the scene is still there to write into.</summary>
    /// <remarks>
    /// Every write is compared against what was last drawn, because this runs per frame: setting a
    /// label's text marshals a string into the engine whether or not it changed. The failure is not
    /// among the things written — the status line already says, in the player's language, that the
    /// game could not start, and the identity behind it is logged rather than displayed.
    /// </remarks>
    private void Render()
    {
        var presenter = _presenter;

        // Validity before tree membership: asking a freed node whether it is inside the tree is
        // itself the crash, and a shutdown during a slow start is the ordinary case on a handset.
        if (presenter is null || _titleLabel is null || _statusLabel is null ||
            !IsInstanceValid(this) || !IsInsideTree())
        {
            return;
        }

        var title = presenter.Title;
        var status = presenter.StatusText;

        if (!string.Equals(_drawnTitle, title, StringComparison.Ordinal))
        {
            _drawnTitle = title;
            _titleLabel.Text = title;
        }

        if (!string.Equals(_drawnStatus, status, StringComparison.Ordinal))
        {
            _drawnStatus = status;
            _statusLabel.Text = status;
        }
    }

    /// <summary>
    /// Prints the boot's two measurements on one greppable line, and pushes a failure to the error
    /// log where a developer will actually meet it.
    /// </summary>
    /// <remarks>
    /// Two numbers rather than one, because they answer different questions: the presenter's span
    /// covers the work this screen did, while the engine's tick count runs from engine start and so
    /// includes the window opening, the assemblies loading and the root composing. Only the second
    /// is comparable to a cold-start budget — and even that one covers a strict subset of the budget
    /// as written, since it stops before authentication, which does not exist yet.
    /// </remarks>
    private static void Report(BootPresenter presenter)
    {
        var atlas = presenter.Atlas;

        GD.Print(
            $"{ColdStartMarker} engine_ms={Time.GetTicksMsec()} " +
            $"boot_ms={(long)presenter.Elapsed.TotalMilliseconds} " +
            $"stage={presenter.Stage} " +
            $"atlas={(atlas is null ? "none" : atlas.IsAvailable ? "loaded" : "absent")} " +
            $"atlas_count={atlas?.AtlasCount ?? 0} placements={atlas?.PlacementCount ?? 0} " +
            $"atlas_detail=\"{atlas?.Detail ?? "no atlas result was recorded — the stage either did " +
                "not run or its read threw"}\"");

        if (presenter.Failure is { } failure)
        {
            GD.PushError($"Boot did not complete — {failure} · {FailureScreenIsNotDesignedHere}");
        }
    }
}
