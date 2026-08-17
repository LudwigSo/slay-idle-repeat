using Godot;
using SlayIdleRepeat.Client.Game.Presenters;

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
/// <see cref="FailureScreenIsNotDesignedHere"/>. What a failed boot gets is the honest minimum: the
/// failure's identity on screen — bounded to the lines that fit inside the safe rect, so a detail
/// nobody sized cannot push itself off the bottom of the display — and the whole of it, untrimmed,
/// in the engine's error log.
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
    private const string FailureLabelPath = "%FailureLabel";

    private const string MarginLeftConstant = "margin_left";
    private const string MarginTopConstant = "margin_top";
    private const string MarginRightConstant = "margin_right";
    private const string MarginBottomConstant = "margin_bottom";

    /// <summary>
    /// The narrowest gap between the screen edge and anything drawn, in canvas units — roughly
    /// 16 dp across the supported density range. A resolved inset smaller than this is widened to
    /// it: a cutout-free edge is not a reason to put text against the glass.
    /// </summary>
    private const int DesignGutter = 48;

    /// <summary>
    /// The most of one axis a single resolved inset may take. A display server answering in a
    /// coordinate space this screen did not anticipate has to degrade to a wide margin, never to a
    /// content rect with no room left inside it to draw.
    /// </summary>
    private const float MaxInsetShare = 0.25f;

    private BootPresenter? _presenter;

    private CancellationToken _lifetime;

    private Label? _titleLabel;
    private Label? _statusLabel;
    private Label? _failureLabel;

    private string? _drawnTitle;
    private string? _drawnStatus;
    private BootFailure? _drawnFailure;

    /// <summary>
    /// Takes the presenter the composition root built, and the token the app shuts down through.
    /// </summary>
    /// <remarks>Called before the node enters the tree, so <c>_Ready</c> has something to draw.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="presenter"/> is null.</exception>
    public void Drive(BootPresenter presenter, CancellationToken lifetime)
    {
        ArgumentNullException.ThrowIfNull(presenter);

        _presenter = presenter;
        _lifetime = lifetime;
    }

    /// <inheritdoc/>
    public override void _Ready()
    {
        // Resolved once. Render runs every frame while the boot does, and a scene-unique lookup is
        // a string search of the owner's table each time it is asked.
        _titleLabel = GetNode<Label>(TitleLabelPath);
        _statusLabel = GetNode<Label>(StatusLabelPath);
        _failureLabel = GetNode<Label>(FailureLabelPath);

        ApplySafeArea();
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

    /// <summary>
    /// Resolves the real safe-area insets from the display server and applies them to the scene.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 This is the query the root scene's static margins stand in for. The display server answers
    /// in physical screen pixels while the scene is laid out in canvas units, so each inset is
    /// scaled by the ratio the stretch mode is already applying.
    /// </para>
    /// <para>
    /// ⚠️ A platform with no cutouts reports the whole window as safe, and a headless or
    /// not-yet-sized window reports nothing usable at all. The second case keeps the margins the
    /// scene was authored with rather than collapsing them to zero — an unanswered query is not a
    /// measurement of no inset.
    /// </para>
    /// </remarks>
    private void ApplySafeArea()
    {
        var window = DisplayServer.WindowGetSize();
        var safeArea = DisplayServer.GetDisplaySafeArea();
        var canvas = GetViewportRect().Size;

        if (window.X <= 0 || window.Y <= 0 || safeArea.Size.X <= 0 || safeArea.Size.Y <= 0 ||
            canvas.X <= 0 || canvas.Y <= 0)
        {
            return;
        }

        // The display server answers in SCREEN coordinates, and the window is only ever part of one
        // screen. Left unshifted, a window that does not sit at the desktop's origin resolves an
        // inset measured from somebody else's corner — on a second monitor, one wider than the whole
        // canvas.
        var origin = DisplayServer.WindowGetPosition();

        var horizontal = canvas.X / window.X;
        var vertical = canvas.Y / window.Y;

        var margins = GetNode<MarginContainer>(SafeAreaPath);

        margins.AddThemeConstantOverride(
            MarginLeftConstant, Inset((safeArea.Position.X - origin.X) * horizontal, canvas.X));
        margins.AddThemeConstantOverride(
            MarginTopConstant, Inset((safeArea.Position.Y - origin.Y) * vertical, canvas.Y));
        margins.AddThemeConstantOverride(
            MarginRightConstant,
            Inset((window.X - (safeArea.End.X - origin.X)) * horizontal, canvas.X));
        margins.AddThemeConstantOverride(
            MarginBottomConstant,
            Inset((window.Y - (safeArea.End.Y - origin.Y)) * vertical, canvas.Y));
    }

    /// <summary>
    /// One resolved inset in canvas units: never narrower than the design gutter, and never wide
    /// enough that the pair of them could close over the content between them.
    /// </summary>
    private static int Inset(float canvasUnits, float axis)
    {
        var ceiling = Mathf.Max(DesignGutter, Mathf.RoundToInt(axis * MaxInsetShare));

        return Math.Clamp(Mathf.RoundToInt(canvasUnits), DesignGutter, ceiling);
    }

    /// <remarks>
    /// <para>
    /// Nothing awaits this task, so its exceptions have nowhere to surface: the whole body is
    /// guarded, or a continuation that threw would leave a screen that silently stopped moving.
    /// </para>
    /// <para>
    /// 🔴 A finished boot stops here, and that is the second thing this file deliberately does not
    /// build. There is no screen to hand over to yet — Home is a later task's, as is whatever
    /// decides between Home and a resumed run — so <see cref="BootStage.Ready"/> is reported and
    /// drawn rather than navigated away from. Inventing a destination would put a screen on the
    /// only path every player takes, chosen by the task least equipped to choose it.
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
        }
        catch (Exception failure)
        {
            GD.PushError($"The boot screen stopped unexpectedly: {failure}");
        }
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
    /// label's text marshals a string into the engine whether or not it changed, and rendering a
    /// failure would build its line again on every one of them.
    /// </remarks>
    private void Render()
    {
        var presenter = _presenter;

        // Validity before tree membership: asking a freed node whether it is inside the tree is
        // itself the crash, and a shutdown during a slow start is the ordinary case on a handset.
        if (presenter is null || _titleLabel is null || _statusLabel is null ||
            _failureLabel is null || !IsInstanceValid(this) || !IsInsideTree())
        {
            return;
        }

        var title = presenter.Title;
        var status = presenter.StatusText;
        var failure = presenter.Failure;

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

        if (!ReferenceEquals(_drawnFailure, failure))
        {
            _drawnFailure = failure;
            _failureLabel.Visible = failure is not null;
            _failureLabel.Text = failure?.ToString() ?? string.Empty;
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
