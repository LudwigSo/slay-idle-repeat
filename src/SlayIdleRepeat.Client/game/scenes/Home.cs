using System.Globalization;
using Godot;
using SlayIdleRepeat.Client.Game.Presenters;

namespace SlayIdleRepeat.Client.Game.Scenes;

/// <summary>
/// S03 — the home screen: a driving adapter over <see cref="HomePresenter"/>.
/// </summary>
/// <remarks>
/// <para>
/// It renders what the presenter says and forwards one press. No rules, no ports, no adapters: the
/// boot screen composes both presenters and hands them over, and this half owns only the things
/// that genuinely need the engine — the safe-area query, the drawn ground, the button, and the
/// handover to the picker.
/// </para>
/// <para>
/// 🔒 <b>Minimal, and the emptiness is the design.</b> A name, a Legend Level, the two Energy
/// amounts the profile literally carries, and one primary action. There is no Energy denominator,
/// no regeneration countdown, no Legend-XP bar and no run cost, because the presenter exposes none
/// of them and could not without copying a formula the rules already own. The hero diorama, daily
/// quests, ad widgets, chest pity, event and guild cards, the inbox, the account-link banner and
/// the bottom navigation belong to later milestones and are absent rather than stubbed.
/// </para>
/// <para>
/// ⚠️ Every type size, colour and gap in <c>Home.tscn</c> is a per-node override, because the
/// shared theme resource and the display faces it will carry do not exist yet — they are M8-03's,
/// and these overrides are debt owed to it rather than a naming scheme of this screen's own. The
/// sizes were chosen against the engine's default font, so they have to be re-checked — not merely
/// re-applied — when the real faces land. The layout itself is structural: containers and stretch
/// ratios, so it holds its proportions across the whole supported aspect range without an override
/// taking part. There is no art here at all, placeholder or otherwise.
/// </para>
/// <para>
/// 🔴 <b>Continuing a run has nowhere to go</b> — see <see cref="TheRunScreenIsNotBuiltHere"/>.
/// Starting one does: the picker is a screen this task built, so the primary action reaches it.
/// </para>
/// </remarks>
public partial class Home : Control
{
    /// <summary>Where this scene lives, for the screen that instantiates it.</summary>
    public const string ScenePath = "res://game/scenes/Home.tscn";

    /// <summary>
    /// 🔴 Deliberately unbuilt, and named so it can be found. S05 — the board a run is actually
    /// played on — is a later task's and does not exist in this build, so an open run has no screen
    /// to resume onto. The run is named and reported rather than silently swallowed, and nothing is
    /// navigated to: inventing a destination would put a screen on the only path a returning player
    /// takes, chosen by the task least equipped to choose it.
    /// </summary>
    private const string TheRunScreenIsNotBuiltHere =
        "S05, the board screen a run is played on, is not built in this milestone: there is no " +
        "scene to resume an open run onto. The run above is reported, not resumed.";

    /// <summary>
    /// The one line a headless run's screen state is read off. Distinctive on purpose: a decision
    /// reached against real content has to be greppable out of an engine log full of everything
    /// else, the way the boot's cold-start measurement already is.
    /// </summary>
    private const string HomeMarker = "SIR_HOME_READY";

    private const string SafeAreaPath = "%SafeArea";
    private const string HeaderPath = "%Header";
    private const string EnergyPath = "%Energy";
    private const string DisplayNameLabelPath = "%DisplayNameLabel";
    private const string LegendLevelLabelPath = "%LegendLevelLabel";
    private const string LegendLevelValuePath = "%LegendLevelValue";
    private const string EnergyLabelPath = "%EnergyLabel";
    private const string EnergyValuePath = "%EnergyValue";
    private const string EnergyReserveLabelPath = "%EnergyReserveLabel";
    private const string EnergyReserveValuePath = "%EnergyReserveValue";
    private const string StatusLabelPath = "%StatusLabel";
    private const string ActionButtonPath = "%ActionButton";

    private HomePresenter? _presenter;
    private ChapterSelectPresenter? _picker;

    private CancellationToken _lifetime;

    private Control? _header;
    private Control? _energy;
    private Label? _displayNameLabel;
    private Label? _legendLevelLabel;
    private Label? _legendLevelValue;
    private Label? _energyLabel;
    private Label? _energyValue;
    private Label? _energyReserveLabel;
    private Label? _energyReserveValue;
    private Label? _statusLabel;
    private Button? _actionButton;

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
    /// <param name="lifetime">Cancelled when the application shuts down.</param>
    /// <exception cref="ArgumentNullException">Either presenter is null.</exception>
    public void Drive(HomePresenter presenter, ChapterSelectPresenter picker, CancellationToken lifetime)
    {
        ArgumentNullException.ThrowIfNull(presenter);
        ArgumentNullException.ThrowIfNull(picker);

        _presenter = presenter;
        _picker = picker;
        _lifetime = lifetime;
    }

    /// <inheritdoc/>
    public override void _Ready()
    {
        // Resolved once. A scene-unique lookup is a string search of the owner's table each time it
        // is asked, and this screen redraws whenever the read behind it moves.
        _header = GetNode<Control>(HeaderPath);
        _energy = GetNode<Control>(EnergyPath);
        _displayNameLabel = GetNode<Label>(DisplayNameLabelPath);
        _legendLevelLabel = GetNode<Label>(LegendLevelLabelPath);
        _legendLevelValue = GetNode<Label>(LegendLevelValuePath);
        _energyLabel = GetNode<Label>(EnergyLabelPath);
        _energyValue = GetNode<Label>(EnergyValuePath);
        _energyReserveLabel = GetNode<Label>(EnergyReserveLabelPath);
        _energyReserveValue = GetNode<Label>(EnergyReserveValuePath);
        _statusLabel = GetNode<Label>(StatusLabelPath);
        _actionButton = GetNode<Button>(ActionButtonPath);

        _actionButton.Pressed += OnActionPressed;

        SafeAreaInsets.ApplyTo(GetNode<MarginContainer>(SafeAreaPath), GetViewportRect().Size);
        Render();

        _ = StartAsync();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The matching half of the subscription in <c>_Ready</c>. The button is a child and dies with
    /// this node either way, but a handler left connected across a scene that is merely detached
    /// and re-added would fire twice, and once is the whole contract of a primary action.
    /// </remarks>
    public override void _ExitTree()
    {
        if (_actionButton is not null)
        {
            _actionButton.Pressed -= OnActionPressed;
        }
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
        // Every node this writes to is checked, not just the one: a scene-unique name that no
        // longer resolves leaves a null behind, and a null-forgiving operator over it would turn a
        // renamed node into a crash here instead of a blank label.
        if (presenter is null || !IsInstanceValid(this) || !IsInsideTree() ||
            _header is null || _energy is null ||
            _displayNameLabel is null || _legendLevelLabel is null || _legendLevelValue is null ||
            _energyLabel is null || _energyValue is null || _energyReserveLabel is null ||
            _energyReserveValue is null || _statusLabel is null || _actionButton is null)
        {
            return;
        }

        // The one predicate the whole screen turns on: whether the read has produced a profile
        // there is anything to say about.
        var carried = presenter.Decision is HomeContinueDecision.StartNewRun or
                                             HomeContinueDecision.ContinueRun;

        // 🔒 The profile's numbers are drawn only when there ARE numbers. Before the read answers,
        // and in the two states where it never will, the name is empty and the Legend Level, the
        // Energy and the Reserve are all still the zero an unset int carries — and "Energy 0" told
        // to a player who has plenty is not a placeholder, it is a plausible value in a hole, which
        // is the one thing this codebase refuses to put on a screen anywhere else. The block leaves
        // instead, the status line below says which of the three states this is, and nothing is
        // invented. Two of those states never end, so this is not a flicker on the way to the
        // truth: it is what the screen looks like for as long as it is up.
        _header.Visible = carried;
        _energy.Visible = carried;

        _displayNameLabel.Text = presenter.DisplayName;
        _legendLevelLabel.Text = presenter.LegendLevelLabel;
        _legendLevelValue.Text = presenter.LegendLevel.ToString(CultureInfo.InvariantCulture);
        _energyLabel.Text = presenter.EnergyLabel;
        _energyValue.Text = presenter.Energy.ToString(CultureInfo.InvariantCulture);
        _energyReserveLabel.Text = presenter.EnergyReserveLabel;
        _energyReserveValue.Text = presenter.EnergyReserve.ToString(CultureInfo.InvariantCulture);
        // Hidden rather than blanked once there is nothing left to say, which is the same thing the
        // picker does with the same line and for the same reason: an empty label still claims a
        // full line of height, so a blank one is a sentence a player can see room for and cannot
        // read. Hiding it also hands the space back to the frame above the primary action.
        _statusLabel.Text = presenter.StatusText;
        _statusLabel.Visible = _statusLabel.Text.Length > 0;

        _actionButton.Text = presenter.ActionText;

        // A read that has not answered, or that answered with no profile, leaves an action with
        // nothing to do. Disabled rather than hidden: a primary action that vanishes reads as a
        // screen that lost its purpose, while a disabled one under the status line reads as a
        // screen waiting, which is what it is.
        _actionButton.Disabled = !carried;
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

        if (presenter.Decision == HomeContinueDecision.StartNewRun)
        {
            ShowChapterSelect();

            return;
        }

        if (presenter.Decision == HomeContinueDecision.ContinueRun)
        {
            GD.PushError(
                $"Continue was taken for run {presenter.ContinuableRun} · {TheRunScreenIsNotBuiltHere}");
        }
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
    /// 🔴 <b>The handover is ONE-WAY, and hiding instead of freeing is only safe because of it.</b>
    /// Every screen this build opens stays in the tree for the life of the application: three of
    /// them by the time the picker is up, two hidden. That is bounded at three and inert — a hidden
    /// <c>Control</c> takes no input, so the button above cannot be reached again, and neither
    /// screen draws or processes. What it is NOT is reusable. This method instantiates
    /// unconditionally, so the first back path that returns a player here and lets them press START
    /// again adds a second picker beside the first, with its own presenter and its own read, and
    /// one more on every traversal after that. The navigation stack that introduces a back path
    /// therefore owes this pair a free-or-reuse decision; it does not inherit one, and no comment
    /// here substitutes for making it.
    /// </para>
    /// </remarks>
    private void ShowChapterSelect()
    {
        if (!IsInstanceValid(this) || !IsInsideTree() || _picker is not { } picker)
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

        picked.Drive(picker, _lifetime);

        Visible = false;

        parent.AddChild(picked);
    }

    /// <summary>
    /// Prints, on one greppable line, what this screen resolved against the content the build
    /// actually shipped — and what the picker it prepared resolved with it.
    /// </summary>
    /// <remarks>
    /// One line rather than two, because a launch produces exactly one of each and the interesting
    /// claim spans both: that composition, the content load, the profile read and the gating
    /// evaluation all ran, in the engine, against real authored data. The picker's half is
    /// described by the picker's own file so the format lives beside the screen it is about.
    /// </remarks>
    private static void Report(HomePresenter home, ChapterSelectPresenter picker)
    {
        GD.Print(
            $"{HomeMarker} decision={home.Decision} legend_level={home.LegendLevel} " +
            $"energy={home.Energy} reserve={home.EnergyReserve} " +
            $"chapters={picker.Chapters.Count} picker_stage={picker.Stage} " +
            $"verdicts=[{ChapterSelect.Describe(picker)}]");
    }
}
