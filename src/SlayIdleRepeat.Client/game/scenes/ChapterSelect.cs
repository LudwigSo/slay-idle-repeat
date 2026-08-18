using System.Globalization;
using Godot;
using SlayIdleRepeat.Client.Composition;
using SlayIdleRepeat.Client.Game.Presenters;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Client.Game.Scenes;

/// <summary>
/// S04 — the chapter picker: a driving adapter over <see cref="ChapterSelectPresenter"/>.
/// </summary>
/// <remarks>
/// <para>
/// It renders what the presenter says and forwards two choices and a confirm. No rules, no ports,
/// no adapters, and above all no gating of its own: which chapter and tier a player may take is the
/// presenter's answer, read out of authored tuning, and this half only draws it.
/// </para>
/// <para>
/// 🔒 <b>The four refusals are drawn four different ways, and that is the point of the screen.</b>
/// A chapter the content set never authored has no row at all — a chapter nobody has written is not
/// something to work towards, so it is absent rather than greyed. A chapter blocked on a clear
/// carries a line naming which chapter on which tier. One blocked on Legend Level carries a line
/// naming the level demanded and the level reached. One blocked on both carries both lines at once.
/// A single grey button for all four would tell a player nothing they could act on, and it is what
/// this layout exists to avoid.
/// </para>
/// <para>
/// ⚠️ <b>A pair whose requirements are not yet known is drawn as unknown, never as locked.</b>
/// Until the profile read answers, every row is non-interactive and the whole list is dimmed — but
/// it keeps the selectable colour and carries no requirement line, because a refusal with no
/// requirement under it is exactly the "this is disabled" the paragraph above rules out. The dimmed
/// list is the screen saying it is still reading, not the screen saying no.
/// </para>
/// <para>
/// 🔒 <b>And the dimming never has to carry that on its own.</b> A status line above the confirm
/// says in words which of the three unread states this is, because opacity is identical in all of
/// them and two of them never end. It goes away the moment the list becomes the answer. It is one
/// sentence and nothing else: no retry, no reload, no panel — the failure screen and its ladder are
/// a later milestone's, and half of one built here would make an unbuilt flow look shipped.
/// </para>
/// <para>
/// 🔒 <b>And the same line answers the confirm, because that press is otherwise silent too.</b>
/// The control is taken out of use on the press and comes back on every outcome but one, so a run
/// still being started, a run that started and a run the rules layer refused are the same flicker of
/// the same button — and the refusal, which is the one the player can act on, reached only the log.
/// The line says which of the three it was and nothing more; it offers no retry, because the confirm
/// itself is the retry and comes back live.
/// </para>
/// <para>
/// 🔒 <b>And the redraw that gives the control back is drawn from state re-read after the refusal,
/// not from the state the refused ladder was drawn from.</b> The rules layer enforces the same
/// ladder now, so a refusal on a pair this screen offered means this screen's copy of the player's
/// progress had already moved. The presenter re-reads before it answers, so the row redraws as
/// blocked with its requirement lines and the confirm goes dead on that pair — otherwise the live
/// control, the unchanged rows and a line ending "Try again" would together invite a press that
/// could not succeed, for as long as the player was willing to make it.
/// </para>
/// <para>
/// ⚠️ Every type size, colour and gap in <c>ChapterSelect.tscn</c> and <c>ChapterRow.tscn</c> is a
/// per-node override, because the shared theme resource and the display faces it will carry do not
/// exist yet — they are M8-03's, and these overrides are debt owed to it rather than a naming
/// scheme of this screen's own. The tier toggles carry theirs in this file instead, because the set
/// is built from the enum and has no node in a scene to hold them; they are the same debt in a
/// different place, and the sizes were chosen against the engine's default font, so all of them have
/// to be re-checked rather than merely re-applied when the real faces land. The layout itself is
/// structural: containers and stretch ratios, so it holds its proportions across the whole supported
/// aspect range without an override taking part. There is no art here at all, placeholder or
/// otherwise.
/// </para>
/// <para>
/// A confirmed run opens onto the board it is played on, unless the accepted command named no run
/// — see <see cref="TheStartedRunWasNotNamed"/>.
/// </para>
/// <para>
/// ⚠️ <b>Only an accepted run takes the confirm out of use.</b> A run the rules layer refused is a
/// run that never started, so the control comes back and the choice can be made again. The latch
/// below exists because there is nowhere to navigate to afterwards; latching on a refusal would
/// strand the player on a screen whose one action had been spent for nothing.
/// </para>
/// <para>
/// 🔒 There is deliberately no par-power readout, no expected-power number and no power warning.
/// The power model belongs to the row that owns it, and a comparison drawn here would be a second
/// answer to a question that already has one owner.
/// </para>
/// </remarks>
public partial class ChapterSelect : Control
{
    /// <summary>Where this scene lives, for the screen that instantiates it.</summary>
    public const string ScenePath = "res://game/scenes/ChapterSelect.tscn";

    /// <summary>
    /// ⚠️ Named so an accepted run that cannot be entered is still reported. S05 exists now, so a
    /// confirmed chapter does open onto the board — but only when the accepted command answered
    /// with the run it created. A run that was started and not named is a state nothing should
    /// paper over: a board opened without one would read whichever run the player happens to be in.
    /// </summary>
    private const string TheStartedRunWasNotNamed =
        "START_RUN was accepted but the state it answered with carried no run, so there is nothing " +
        "to open the board on. The run above is reported, not entered.";

    /// <summary>Where the per-chapter row lives, instantiated once per authored chapter.</summary>
    private const string ChapterRowScenePath = "res://game/scenes/ChapterRow.tscn";

    private const string SafeAreaPath = "%SafeArea";
    private const string TitleLabelPath = "%TitleLabel";
    private const string TierPickerPath = "%TierPicker";
    private const string ChapterListPath = "%ChapterList";
    private const string StatusLabelPath = "%StatusLabel";
    private const string ConfirmButtonPath = "%ConfirmButton";

    private const string RowNameButtonPath = "NameButton";
    private const string RowRequiresClearPath = "RequiresClear";
    private const string RowRequiresLegendLevelPath = "RequiresLegendLevel";

    /// <summary>Separates the two halves of one requirement line: a caption, then its payload.</summary>
    private const string CaptionSeparator = " ";

    /// <summary>Separates a chapter from the tier it must be cleared on.</summary>
    private const string ChapterTierSeparator = " · ";

    /// <summary>Separates the level demanded from the level reached.</summary>
    private const char LevelSeparator = '/';

    /// <summary>Joins two clears, or two levels, that landed in the same line.</summary>
    /// <remarks>
    /// The shipped ladder authors at most one of each kind per rung, so this normally joins
    /// nothing. It exists because the presenter's answer is a list rather than a pair, and a
    /// renderer that drew only the first would be the truncation the list shape exists to prevent.
    /// </remarks>
    private const string RequirementJoin = ", ";

    /// <summary>Joins the verdicts of one report line.</summary>
    private const string VerdictJoin = "; ";

    /// <summary>The lowest chapter number the campaign has.</summary>
    private const int FirstChapterId = 1;

    /// <summary>What a still-reading list is drawn at, so it reads as pending rather than refused.</summary>
    private const float UnknownListOpacity = 0.55f;

    private const string FontOutlineColourOverride = "font_outline_color";
    private const string OutlineSizeConstant = "outline_size";
    private const string FontSizeOverride = "font_size";

    /// <summary>
    /// How much a chosen control's glyphs are thickened, in canvas units — the whole visible signal
    /// that this chapter, and this tier, are the ones a confirm would start.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>Weight, because the engine's own pressed state is not a signal at all.</b> Measured
    /// off a real button in a headless run rather than assumed: the default theme fills a pressed
    /// button with <c>(0, 0, 0, 0.6)</c> and an unpressed one with <c>(0.1, 0.1, 0.1, 0.6)</c>.
    /// Composited over this screen's ground those are 0.028 and 0.088, which is <b>1.11:1</b>
    /// against each other and <b>1.07:1</b> between the chosen control and the ground it sits on —
    /// so the selected control does not merely look similar to the others, it disappears, and reads
    /// as a hole where the unselected ones read as buttons. Both of the two choices this screen
    /// exists to take were drawn that way.
    /// </para>
    /// <para>
    /// 🔒 And weight rather than a brighter or a differently-hued text: a blocked row already spends
    /// a hue AND a luminance step on <see cref="BlockedColour"/>, and a second colour meaning
    /// "chosen" would land between the two that already mean "open" and "refused". Thickness is a
    /// free channel — it survives every colour vision deficiency, it survives greyscale, and it adds
    /// nothing to a palette that has no theme resource to hold it. It is still a per-node override
    /// and still M8-03's to take away.
    /// </para>
    /// </remarks>
    private const int SelectedOutlineSize = 3;

    /// <summary>An unchosen control's glyphs, at the face's own weight.</summary>
    private const int UnselectedOutlineSize = 0;

    /// <summary>
    /// The least of one axis a control a thumb lands on may take, in canvas units — the height the
    /// chapter rows are already authored at, and the smallest target the screen offers anywhere.
    /// </summary>
    private const int TouchTargetHeight = 144;

    /// <summary>The size a chapter row is typed at, so a tier toggle reads as its peer rather than as a caption.</summary>
    private const int BodyFontSize = 48;

    /// <summary>A row whose requirements are met, or are not yet known.</summary>
    private static readonly Color LiveColour = new(0.93f, 0.93f, 0.96f);

    /// <summary>A row the ladder refuses, and the requirement lines under it.</summary>
    private static readonly Color BlockedColour = new(0.78f, 0.62f, 0.40f);

    /// <summary>
    /// The confirm while there is nothing for it to start — the palette's quiet secondary, the same
    /// grey the status line above it is drawn in.
    /// </summary>
    /// <remarks>
    /// 🔒 Deliberately not <see cref="BlockedColour"/>. Amber means the ladder refused something on
    /// this screen, and a confirm that is waiting for a read, waiting for a choice, or waiting for a
    /// submission it already sent has been refused nothing. It is unavailable, which is quieter than
    /// live and is not the same statement as no.
    /// </remarks>
    private static readonly Color UnavailableColour = new(0.66f, 0.67f, 0.73f);

    private readonly List<TierChoice> _tiers = [];
    private readonly List<ChapterRow> _rows = [];

    private ChapterSelectPresenter? _presenter;

    private CancellationToken _lifetime;

    private Label? _titleLabel;
    private BoxContainer? _tierPicker;
    private BoxContainer? _chapterList;
    private Label? _statusLabel;
    private Button? _confirmButton;

    private Func<RunId, ComposedBoardScreen>? _board;

    /// <summary>
    /// True once a run has been started, so this screen's one action cannot be taken twice.
    /// </summary>
    /// <remarks>
    /// It was latched because there was nowhere to go; it stays latched now that there is, because
    /// the handover is one-way and this screen remains in the tree behind the board.
    /// </remarks>
    private bool _submitted;

    /// <summary>True from the press until the host has answered it.</summary>
    private bool _confirming;

    /// <summary>Takes the presenter the composition root built, and the app's shutdown token.</summary>
    /// <param name="presenter">Drives this screen.</param>
    /// <param name="board">
    /// Builds the board for the run a confirm starts. A factory rather than a presenter, because
    /// the run does not exist until <c>START_RUN</c> has been accepted.
    /// </param>
    /// <param name="lifetime">Cancelled when the application shuts down.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public void Drive(
        ChapterSelectPresenter presenter,
        Func<RunId, ComposedBoardScreen> board,
        CancellationToken lifetime)
    {
        ArgumentNullException.ThrowIfNull(presenter);
        ArgumentNullException.ThrowIfNull(board);

        _presenter = presenter;
        _board = board;
        _lifetime = lifetime;
    }

    /// <summary>
    /// Describes what one picker makes of every chapter the campaign currently reaches, on one
    /// line.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The range is every authored chapter plus the one after it, rather than a hand-picked list of
    /// probes: the extra chapter is where the campaign currently ends, so the line shows the
    /// boundary between what is written and what is not without anybody maintaining a census of it.
    /// </para>
    /// <para>
    /// Every verdict carries its payload, because the whole claim worth reporting is that the four
    /// refusals stayed apart — a line of four identical <c>Blocked</c>s would be the collapse this
    /// screen exists to prevent, printed.
    /// </para>
    /// </remarks>
    /// <param name="presenter">The picker to describe.</param>
    /// <exception cref="ArgumentNullException"><paramref name="presenter"/> is null.</exception>
    public static string Describe(ChapterSelectPresenter presenter)
    {
        ArgumentNullException.ThrowIfNull(presenter);

        var beyondCampaign = presenter.Chapters.Count == 0
            ? FirstChapterId
            : presenter.Chapters[^1].ChapterId + 1;

        var verdicts =
            from chapterId in Enumerable.Range(FirstChapterId, beyondCampaign - FirstChapterId + 1)
            from tier in Enum.GetValues<DifficultyTier>()
            let availability = presenter.Availability(chapterId, tier)
            select $"{chapterId}:{tier}={availability.Lookup}{DescribeUnmet(availability.Unmet)}";

        return string.Join(VerdictJoin, verdicts);
    }

    /// <inheritdoc/>
    public override void _Ready()
    {
        // Resolved once. A scene-unique lookup is a string search of the owner's table each time it
        // is asked, and this screen redraws on every tier press.
        _titleLabel = GetNode<Label>(TitleLabelPath);
        _tierPicker = GetNode<BoxContainer>(TierPickerPath);
        _chapterList = GetNode<BoxContainer>(ChapterListPath);
        _statusLabel = GetNode<Label>(StatusLabelPath);
        _confirmButton = GetNode<Button>(ConfirmButtonPath);

        _confirmButton.Pressed += OnConfirmPressed;

        // Painted once rather than on every redraw: unlike the two choice families below, this
        // control's colour is decided entirely by the state the engine is drawing it in, so there is
        // nothing for a later pass to recompute. It is painted at all because the primary action was
        // the one control on this screen still drawing at the engine's own font colours — and the
        // state it spends most of its life in, disabled, is the one whose default is furthest from
        // anything this screen chose.
        ButtonTextColours.ApplyTo(_confirmButton, LiveColour, UnavailableColour);

        BuildTiers();
        BuildChapters();

        SafeAreaInsets.ApplyTo(GetNode<MarginContainer>(SafeAreaPath), GetViewportRect().Size);
        Render();

        _ = StartAsync();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The matching half of every subscription made above. The buttons are children and die with
    /// this node either way; a handler left connected across a scene that is merely detached and
    /// re-added would fire twice, and a confirm that submits twice starts two runs.
    /// </remarks>
    public override void _ExitTree()
    {
        if (_confirmButton is not null)
        {
            _confirmButton.Pressed -= OnConfirmPressed;
        }

        foreach (var tier in _tiers)
        {
            tier.Button.Pressed -= tier.OnPressed;
        }

        foreach (var row in _rows)
        {
            row.Name.Pressed -= row.OnPressed;
        }
    }

    /// <summary>One requirement list, rendered as the report line carries it.</summary>
    private static string DescribeUnmet(IReadOnlyList<ChapterTierRequirement> unmet)
    {
        if (unmet.Count == 0)
        {
            return "";
        }

        var parts = unmet.Select(requirement => requirement switch
        {
            ClearRequirement clear => $"Clear {clear.ChapterId}:{clear.Tier}",
            LegendLevelRequirement level => $"LegendLevel {level.Required}{LevelSeparator}{level.Actual}",
            _ => requirement.GetType().Name,
        });

        return $"({string.Join(RequirementJoin, parts)})";
    }

    /// <summary>The tier picker: one toggle per tier the game defines, in the enum's own order.</summary>
    /// <remarks>
    /// Built from the enum rather than authored as three nodes, so a tier added to the game brings
    /// its control with it — the same reason the presenter keys the authored ladder by the tier's
    /// own name instead of keeping a table beside it.
    /// </remarks>
    private void BuildTiers()
    {
        var group = new ButtonGroup();

        foreach (var tier in Enum.GetValues<DifficultyTier>())
        {
            var choice = new TierChoice(tier, NewToggle(group), Render);

            choice.Button.Pressed += choice.OnPressed;

            _tiers.Add(choice);
            _tierPicker!.AddChild(choice.Button);
        }

        if (_tiers.Count > 0)
        {
            _tiers[0].Button.ButtonPressed = true;
        }
    }

    /// <summary>One row per authored chapter, and none at all for a chapter nobody wrote.</summary>
    private void BuildChapters()
    {
        var presenter = _presenter;
        var template = GD.Load<PackedScene>(ChapterRowScenePath);

        if (presenter is null || template is null)
        {
            // Load answers null rather than throwing when the resource is missing or its import
            // cannot be read, so an unnamed null reference is all a caller gets unless it says so.
            GD.PushError(
                $"The chapter picker cannot draw its list: presenter={presenter is not null}, " +
                $"row template loaded from '{ChapterRowScenePath}'={template is not null}.");

            return;
        }

        var group = new ButtonGroup();

        foreach (var chapter in presenter.Chapters)
        {
            var root = template.Instantiate<Control>();
            var row = new ChapterRow(
                chapter.ChapterId,
                root,
                Adopt(root.GetNode<Button>(RowNameButtonPath), group),
                root.GetNode<Label>(RowRequiresClearPath),
                root.GetNode<Label>(RowRequiresLegendLevelPath),
                Render);

            row.Name.Text = chapter.DisplayName;
            row.Name.Pressed += row.OnPressed;

            _rows.Add(row);
            _chapterList!.AddChild(root);
        }
    }

    /// <summary>One tier's toggle, sized and typed to match the rows it filters.</summary>
    /// <remarks>
    /// A control built in code inherits the engine's default face and its default height, which is a
    /// caption-sized target on a handset canvas. The tier is one of the two choices this screen
    /// exists to take, so it is given the chapter rows' own touch height and body size — still
    /// short of the confirm below it, which stays the largest thing here. The text wraps rather than
    /// widening: three toggles share one row, and a translation that outgrew its third would push
    /// the row past the safe area instead of growing downwards.
    /// </remarks>
    private static Button NewToggle(ButtonGroup group)
    {
        var button = new Button
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, TouchTargetHeight),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };

        button.AddThemeFontSizeOverride(FontSizeOverride, BodyFontSize);

        return Adopt(button, group);
    }

    /// <summary>Makes a button one of a radio set, so the engine holds the selection.</summary>
    /// <remarks>
    /// 🔒 The selection is deliberately not mirrored into a field here. A scene holds no state of
    /// its own, and a toggle group already IS the state — a copy beside it is a second answer to
    /// "what is selected" that can disagree with the thing the player is looking at.
    /// </remarks>
    private static Button Adopt(Button button, ButtonGroup group)
    {
        button.ToggleMode = true;
        button.ButtonGroup = group;

        return button;
    }

    /// <remarks>
    /// Nothing awaits this task, so its exceptions have nowhere to surface: the whole body is
    /// guarded, or a continuation that threw would leave a screen that silently stopped moving.
    /// </remarks>
    private async Task StartAsync()
    {
        try
        {
            if (_presenter is not { } presenter)
            {
                GD.PushError(
                    "The chapter picker entered the tree with no presenter. Only the home screen " +
                    "may instantiate it, and it must call Drive before adding it to the tree.");

                return;
            }

            // The screen this one opens from starts the same presenter before the tap, so the read
            // has usually already answered. Repeating it would buy nothing and could cost
            // everything: StartAsync settles the stage from whatever the LAST read did, so a second
            // read that failed would demote a picker that knows its gating back to knowing nothing
            // and dim a list the player is already looking at. A stage short of Ready still reads,
            // because then there is something left to find out.
            if (presenter.Stage != ChapterSelectStage.Ready)
            {
                // No ConfigureAwait(false): the continuation writes to nodes, and only the thread
                // the engine runs the scene tree on may do that.
                await presenter.StartAsync(_lifetime);
            }

            Render();
        }
        catch (Exception failure)
        {
            GD.PushError($"The chapter picker stopped unexpectedly: {failure}");
        }
    }

    /// <remarks>
    /// The control is taken out of use here, on the press, rather than when the host answers.
    /// Nothing waits for the task this starts, so the button stays live for every frame the host
    /// takes — and two presses inside that window are two START_RUN commands, of which the rules
    /// layer refuses the second for a reason this screen has no way to show.
    /// </remarks>
    private void OnConfirmPressed()
    {
        if (_submitted || _confirming || _presenter is null ||
            SelectedChapter() is not { } chapterId || SelectedTier() is not { } tier)
        {
            return;
        }

        _confirming = true;

        Render();

        _ = ConfirmAsync(chapterId, tier);
    }

    /// <summary>Opens the board on the run the accepted confirm created.</summary>
    /// <remarks>
    /// Reported either way, because a confirm that reached the rules layer is the one moment on
    /// this screen worth being able to find in a log — and because the run it names is what the
    /// board is then about. 🔒 Every way of NOT reaching the board says so out loud: the confirm has
    /// already been latched by the time this runs, so a silent return leaves a screen whose one
    /// action is spent and whose run is playing somewhere the player cannot see.
    /// </remarks>
    private void EnterStartedRun(ChapterSelectPresenter presenter, int chapterId, DifficultyTier tier)
    {
        if (presenter.StartedRun is not { } run)
        {
            GD.PushError(
                $"START_RUN was accepted for chapter {chapterId} on {tier} · {TheStartedRunWasNotNamed}");

            return;
        }

        GD.Print($"START_RUN accepted for chapter {chapterId} on {tier}, entering run {run}.");

        if (!IsInstanceValid(this) || !IsInsideTree())
        {
            // The screen went away while the command was in flight — an ordinary shutdown, and the
            // one case here that is not a defect.
            return;
        }

        if (_board is not { } board)
        {
            GD.PushError(
                $"Run {run} was started and this screen has no way to build its board, so the " +
                "confirm is spent and the run is unreachable. Only a screen that was handed the " +
                "board factory may instantiate the picker.");

            return;
        }

        BoardHandover.Show(this, board(run), _lifetime);
    }

    /// <remarks>
    /// Nothing awaits this task either, so the whole body stays inside the guard — including the
    /// redraw, which is what gives the control back.
    /// </remarks>
    private async Task ConfirmAsync(int chapterId, DifficultyTier tier)
    {
        try
        {
            if (_presenter is { } presenter)
            {
                var submission = await presenter.ConfirmAsync(chapterId, tier, _lifetime);

                if (submission == ChapterSelectSubmission.Submitted)
                {
                    // Latched rather than re-enabled, and only here: the handover is one-way and
                    // leaves this screen in the tree, so a second press would start a second run.
                    // Every other verdict leaves the flag alone and the redraw below gives the
                    // control back.
                    _submitted = true;

                    EnterStartedRun(presenter, chapterId, tier);
                }
                else
                {
                    GD.PushWarning(
                        $"No run was started for chapter {chapterId} on {tier}: " +
                        $"{submission}{DescribeRejection(presenter)}. The confirm stays live.");
                }
            }

            _confirming = false;

            Render();
        }
        catch (Exception failure)
        {
            // Cleared on this way out too, and not through a finally, so that the redraw that acts
            // on it stays inside the guard: a confirm left marked in flight would be a control dead
            // for the rest of the screen's life with nothing left running to re-enable it.
            _confirming = false;

            GD.PushError($"The chapter picker could not confirm a run: {failure}");

            Render();
        }
    }

    /// <summary>The rules layer's own reason for a refusal, or nothing when it made none.</summary>
    /// <remarks>
    /// Named rather than summarised: a run already open, a chapter id below one, an undefined tier
    /// and a ladder rung the player has not reached are four different defects, and a line saying
    /// only that the command failed would leave whoever reads it unable to tell a player mid-run
    /// from a build sending nonsense from a screen that had gone stale.
    /// </remarks>
    private static string DescribeRejection(ChapterSelectPresenter presenter) =>
        presenter.RulesRejection is { } rejection ? $" ({rejection})" : "";

    /// <summary>Writes the presenter's state into the scene, if the scene is still there to write into.</summary>
    private void Render()
    {
        var presenter = _presenter;

        // Validity before tree membership: asking a freed node whether it is inside the tree is
        // itself the crash, and a shutdown during a slow read is the ordinary case on a handset.
        if (presenter is null || _titleLabel is null || _chapterList is null ||
            _statusLabel is null || _confirmButton is null ||
            !IsInstanceValid(this) || !IsInsideTree())
        {
            return;
        }

        _titleLabel.Text = presenter.Title;
        _confirmButton.Text = presenter.ConfirmText;

        // The line that says why the list below is dimmed — and, once the list IS the answer, what
        // the last press of the confirm did. One label rather than two, because the two can never be
        // needed at once: nothing short of a Ready stage can reach the confirm at all. The read's own
        // sentence wins where they would otherwise overlap, since a screen that cannot say which
        // chapters are open has nothing useful to add about a run.
        //
        // Hidden rather than blanked when there is nothing to say, for the same reason the
        // requirement lines are: a blank line of the right height is a sentence the player can see
        // room for and cannot read.
        var status = presenter.StatusText;

        if (status.Length == 0)
        {
            // In flight is drawn from this screen's own flag rather than from the presenter: the
            // control is taken out of use on the press, before the presenter is called at all.
            status = _confirming ? presenter.StartingStatus : presenter.ConfirmStatusText;
        }

        _statusLabel.Text = status;
        _statusLabel.Visible = status.Length > 0;

        // Repainted every pass rather than on the toggle that changed, because a button group tells
        // the buttons it deselects nothing: only the pressed one raises a signal, so the one that
        // just stopped being chosen would keep the chosen weight if this drew a single control.
        foreach (var tier in _tiers)
        {
            tier.Button.Text = presenter.TierName(tier.Tier);

            Paint(tier.Button, LiveColour, tier.Button.ButtonPressed);
        }

        // The whole list dims while the gating is unknown, which is what keeps a row that carries
        // no requirement line from reading as a refusal that gave no reason.
        _chapterList.Modulate = new Color(
            1f, 1f, 1f, presenter.Stage == ChapterSelectStage.Ready ? 1f : UnknownListOpacity);

        // No tier chosen means no pair to ask about, and asking anyway would put a value the game
        // does not define through a lookup that would answer for it. The rows keep what they last
        // drew; the confirm does not.
        if (SelectedTier() is not { } chosenTier)
        {
            _confirmButton.Disabled = true;

            return;
        }

        var selectable = false;

        foreach (var row in _rows)
        {
            var availability = presenter.Availability(row.ChapterId, chosenTier);

            RenderRow(presenter, row, availability);

            selectable |= availability.Lookup == ChapterTierLookup.Selectable &&
                          row.Name.ButtonPressed;
        }

        _confirmButton.Disabled = _submitted || _confirming || !selectable;
    }

    /// <summary>Draws one chapter at the chosen tier, with its refusal told apart from the others.</summary>
    private static void RenderRow(
        ChapterSelectPresenter presenter, ChapterRow row, ChapterTierAvailability availability)
    {
        // A chapter the content set does not author has nothing to say and no requirement to work
        // towards, so the row leaves rather than greying out. Reachable only if a listed chapter
        // ever stopped being authored, which is why it is handled instead of defaulted into a lock.
        row.Root.Visible = availability.Lookup != ChapterTierLookup.NotAuthored;

        row.Name.Disabled = availability.Lookup != ChapterTierLookup.Selectable;

        // An unknown pair keeps the live colour on purpose: the only thing the screen may say
        // before the read answers is that it has not answered, and the dimmed list already says it.
        var colour = availability.Lookup == ChapterTierLookup.Blocked ? BlockedColour : LiveColour;

        Paint(row.Name, colour, row.Name.ButtonPressed);

        Line(row.RequiresClear, presenter.RequiresClearCaption, availability.Unmet
            .OfType<ClearRequirement>()
            .Select(clear => $"{clear.ChapterId}{ChapterTierSeparator}{presenter.TierName(clear.Tier)}"));

        Line(row.RequiresLegendLevel, presenter.RequiresLegendLevelCaption, availability.Unmet
            .OfType<LegendLevelRequirement>()
            .Select(level => string.Create(
                CultureInfo.InvariantCulture,
                $"{level.Required}{LevelSeparator}{level.Actual}")));
    }

    /// <summary>
    /// Gives one button its colour in every state it can draw text in, and the weight that says
    /// whether it is the chosen one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every state, because the engine picks the font colour by draw mode and none of the other
    /// modes falls back to <c>font_color</c> — see <see cref="ButtonTextColours"/>, which owns that
    /// list of states for every button on both screens so that no control can silently keep an
    /// engine default. One colour for all of them here: a refused row stays amber whether the finger
    /// is on it or not, and being unusable is not the news a chapter row carries.
    /// </para>
    /// <para>
    /// The outline colour matches the text rather than contrasting with it: the point is a thicker
    /// glyph, not a rimmed one.
    /// </para>
    /// </remarks>
    /// <param name="button">The control to draw.</param>
    /// <param name="colour">What its text says about itself — open, refused, or not yet known.</param>
    /// <param name="selected">Whether it is the one a confirm would act on.</param>
    private static void Paint(Button button, Color colour, bool selected)
    {
        ButtonTextColours.ApplyTo(button, colour);

        button.AddThemeColorOverride(FontOutlineColourOverride, colour);

        button.AddThemeConstantOverride(
            OutlineSizeConstant, selected ? SelectedOutlineSize : UnselectedOutlineSize);
    }

    /// <summary>
    /// One requirement line: its caption and every payload of that kind, or no line at all.
    /// </summary>
    /// <remarks>
    /// Hidden rather than blanked when nothing is unmet, so the gap between a met requirement and
    /// an unmet one is structural. A blank line of the right height is a refusal a player cannot
    /// read but can still see room for.
    /// </remarks>
    private static void Line(Label label, string caption, IEnumerable<string> payloads)
    {
        var joined = string.Join(RequirementJoin, payloads);

        label.Visible = joined.Length > 0;
        label.Text = label.Visible ? caption + CaptionSeparator + joined : "";
    }

    private DifficultyTier? SelectedTier() =>
        _tiers.FirstOrDefault(choice => choice.Button.ButtonPressed)?.Tier;

    private int? SelectedChapter() =>
        _rows.FirstOrDefault(row => row.Name.ButtonPressed)?.ChapterId;

    /// <summary>One tier's toggle, paired with the tier it stands for.</summary>
    /// <remarks>
    /// The handler is held as a field so the subscription made when the control is built has a
    /// matching removal when the screen leaves the tree — a lambda subscribed inline cannot be
    /// unsubscribed, because there is nothing left to name it by.
    /// </remarks>
    private sealed class TierChoice(DifficultyTier tier, Button button, Action redraw)
    {
        public DifficultyTier Tier { get; } = tier;

        public Button Button { get; } = button;

        public Action OnPressed { get; } = redraw;
    }

    /// <summary>One chapter's row: the control that selects it and the two requirement lines.</summary>
    /// <remarks>
    /// Two labels rather than a list built at render time, because there are exactly two KINDS of
    /// requirement the presenter can report and exactly two captions it exposes for them. A pair of
    /// fixed lines keeps the whole row authored in the scene file, where the theme debt already is.
    /// </remarks>
    private sealed class ChapterRow(
        int chapterId, Control root, Button name, Label requiresClear, Label requiresLegendLevel, Action redraw)
    {
        public int ChapterId { get; } = chapterId;

        public Control Root { get; } = root;

        public Button Name { get; } = name;

        public Label RequiresClear { get; } = requiresClear;

        public Label RequiresLegendLevel { get; } = requiresLegendLevel;

        public Action OnPressed { get; } = redraw;
    }
}
