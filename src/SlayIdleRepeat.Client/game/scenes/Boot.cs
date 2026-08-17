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
/// 🔴 <b>The player-facing failure SCREEN is deliberately not built here</b> — see
/// <see cref="FailureScreenIsNotDesignedHere"/>. What a failed boot gets is the honest minimum: the
/// failure's full identity on screen and the same identity in the engine's error log.
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

    private BootPresenter? _presenter;

    private CancellationToken _lifetime;

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

        if (window.X <= 0 || window.Y <= 0 || safeArea.Size.X <= 0 || safeArea.Size.Y <= 0)
        {
            return;
        }

        var horizontal = canvas.X / window.X;
        var vertical = canvas.Y / window.Y;

        var margins = GetNode<MarginContainer>(SafeAreaPath);

        margins.AddThemeConstantOverride(MarginLeftConstant, Inset(safeArea.Position.X * horizontal));
        margins.AddThemeConstantOverride(MarginTopConstant, Inset(safeArea.Position.Y * vertical));
        margins.AddThemeConstantOverride(
            MarginRightConstant, Inset((window.X - safeArea.End.X) * horizontal));
        margins.AddThemeConstantOverride(
            MarginBottomConstant, Inset((window.Y - safeArea.End.Y) * vertical));
    }

    /// <summary>One resolved inset in canvas units, never narrower than the design gutter.</summary>
    private static int Inset(float canvasUnits) => Mathf.Max(DesignGutter, Mathf.RoundToInt(canvasUnits));

    private async Task RunAsync()
    {
        var presenter = _presenter;

        if (presenter is null)
        {
            SetProcess(false);

            GD.PushError(
                "The boot screen entered the tree with no presenter. Only the application root may " +
                "instantiate it, and it must call Drive before adding it to the tree.");

            return;
        }

        // No ConfigureAwait(false): the continuation writes to nodes, and only the thread the engine
        // runs the scene tree on may do that.
        await presenter.StartAsync(_lifetime);

        SetProcess(false);
        Render();
        Report(presenter);
    }

    /// <summary>Writes the presenter's state into the scene, if the scene is still there to write into.</summary>
    private void Render()
    {
        var presenter = _presenter;

        // Validity before tree membership: asking a freed node whether it is inside the tree is
        // itself the crash, and a shutdown during a slow start is the ordinary case on a handset.
        if (presenter is null || !IsInstanceValid(this) || !IsInsideTree())
        {
            return;
        }

        GetNode<Label>(TitleLabelPath).Text = presenter.Title;
        GetNode<Label>(StatusLabelPath).Text = presenter.StatusText;

        var failureLabel = GetNode<Label>(FailureLabelPath);
        var failure = presenter.Failure;

        failureLabel.Visible = failure is not null;
        failureLabel.Text = failure?.ToString() ?? string.Empty;
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
