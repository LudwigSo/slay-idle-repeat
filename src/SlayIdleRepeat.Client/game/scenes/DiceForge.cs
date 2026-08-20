using Godot;
using SlayIdleRepeat.Client.Game.Presenters;

namespace SlayIdleRepeat.Client.Game.Scenes;

/// <summary>
/// The Dice Forge tile: pick a face, pick an upgrade, forge. A driving adapter over
/// <see cref="DiceForgePresenter"/>.
/// </summary>
/// <remarks>
/// <para>
/// It renders what the presenter says and forwards presses. No rules, no ports, no adapters, and no
/// decision about what may be pressed — including which upgrades exist, which is the presenter's
/// reading of the rules layer's own menu.
/// </para>
/// <para>
/// 🔒 <b>Two toggle lists and one commit, rather than a grid of thirty buttons.</b> A face and an
/// option are two independent choices and the command carries both, so the screen holds a selection
/// of each and forges when the player says. The alternative — a button per (face, option) pair — is
/// eighteen controls on a handset for a decision with six and three sides.
/// </para>
/// <para>
/// ⚠️ The pip count the "raise the pips" option needs is not asked for: the screen sends the highest
/// legal one, which is the only value that is unambiguously an upgrade. Choosing between "make this
/// 1 a 3" and "make it a 6" is a second decision the design does not describe, and inventing a
/// stepper for it would be inventing the decision.
/// </para>
/// <para>
/// ⚠️ Every type size and colour here is a per-node override, because the shared theme resource does
/// not exist yet — M8-03's, and to be re-checked rather than re-applied.
/// </para>
/// </remarks>
public partial class DiceForge : Control
{
    /// <summary>Where this scene lives, for the screen that instantiates it.</summary>
    public const string ScenePath = "res://game/scenes/DiceForge.tscn";

    /// <summary>Where one choice row's scene lives.</summary>
    private const string ChoiceRowScenePath = "res://game/scenes/DiceForgeChoiceRow.tscn";

    /// <summary>The one line a headless run's screen state is read off.</summary>
    private const string DiceForgeMarker = "SIR_DICE_FORGE_READY";

    /// <summary>
    /// The pip count the higher-pip option is sent with — the die's top face, and the only value
    /// that is legal from every source below it.
    /// </summary>
    private const int HighestPip = 6;

    private const string SafeAreaPath = "%SafeArea";
    private const string TitleLabelPath = "%TitleLabel";
    private const string FaceListPath = "%FaceList";
    private const string OptionListPath = "%OptionList";
    private const string WithheldLabelPath = "%WithheldLabel";
    private const string StatusLabelPath = "%StatusLabel";
    private const string RejectionLabelPath = "%RejectionLabel";
    private const string ForgeButtonPath = "%ForgeButton";

    /// <summary>A control with something to do.</summary>
    private static readonly Color LiveColour = new(0.93f, 0.93f, 0.96f);

    /// <summary>And one with nothing to do — the same quiet grey every caption is drawn in.</summary>
    private static readonly Color UnavailableColour = new(0.66f, 0.67f, 0.73f);

    private DiceForgePresenter? _presenter;
    private Board? _board;
    private CancellationToken _lifetime;

    private Label? _titleLabel;
    private VBoxContainer? _faceList;
    private VBoxContainer? _optionList;
    private Label? _withheldLabel;
    private Label? _statusLabel;
    private Label? _rejectionLabel;
    private Button? _forgeButton;

    /// <summary>The face the player has selected, or <c>null</c> before they have chosen one.</summary>
    private int? _face;

    /// <summary>The option they have selected, or <c>null</c>.</summary>
    private int? _option;

    /// <summary>What the lists currently drawn were built from, so a redraw does not rebuild them.</summary>
    private IReadOnlyList<DiceForgeFaceRow> _drawnFaces = Array.Empty<DiceForgeFaceRow>();

    /// <summary>Whether a submission is in flight, so a second press cannot start another.</summary>
    private bool _busy;

    /// <summary>Takes the presenter, the board to hand back to, and the app's shutdown token.</summary>
    /// <param name="presenter">Drives this screen.</param>
    /// <param name="board">The board the tile was entered from, returned to once it is resolved.</param>
    /// <param name="lifetime">Cancelled when the application shuts down.</param>
    /// <exception cref="ArgumentNullException">The presenter or the board is null.</exception>
    public void Drive(DiceForgePresenter presenter, Board board, CancellationToken lifetime)
    {
        ArgumentNullException.ThrowIfNull(presenter);
        ArgumentNullException.ThrowIfNull(board);

        _presenter = presenter;
        _board = board;
        _lifetime = lifetime;
    }

    /// <inheritdoc/>
    public override void _Ready()
    {
        _titleLabel = GetNode<Label>(TitleLabelPath);
        _faceList = GetNode<VBoxContainer>(FaceListPath);
        _optionList = GetNode<VBoxContainer>(OptionListPath);
        _withheldLabel = GetNode<Label>(WithheldLabelPath);
        _statusLabel = GetNode<Label>(StatusLabelPath);
        _rejectionLabel = GetNode<Label>(RejectionLabelPath);
        _forgeButton = GetNode<Button>(ForgeButtonPath);

        _forgeButton.Pressed += OnForgePressed;

        // Painted because a Button draws its text by draw mode, and the disabled mode this control
        // spends every round trip in has an engine default of half-transparent grey that no override
        // of font_color reaches.
        ButtonTextColours.ApplyTo(_forgeButton, LiveColour, UnavailableColour);

        SafeAreaInsets.ApplyTo(GetNode<MarginContainer>(SafeAreaPath), GetViewportRect().Size);

        Render();

        _ = StartAsync();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The matching half of the subscription in <c>_Ready</c>. The control is a child and dies with
    /// this node either way, but a handler left connected across a scene that is merely detached and
    /// re-added would fire twice — and once is the whole contract of forging a face.
    /// </remarks>
    public override void _ExitTree()
    {
        if (_forgeButton is not null)
        {
            _forgeButton.Pressed -= OnForgePressed;
        }
    }

    /// <remarks>
    /// Nothing awaits this task, so its exceptions have nowhere to surface: the whole body is
    /// guarded, or a continuation that threw would leave a forge that silently stopped moving.
    /// </remarks>
    private async Task StartAsync()
    {
        try
        {
            if (_presenter is not { } presenter)
            {
                GD.PushError(
                    "The dice forge entered the tree with no presenter. Only a screen that already " +
                    "has a run may instantiate it, and it must call Drive before adding it.");

                return;
            }

            // No ConfigureAwait(false): the continuation writes to nodes, and only the thread the
            // engine runs the scene tree on may do that.
            await presenter.StartAsync(_lifetime);

            Render();
            Report(presenter);
            LeaveIfTheTileHasCleared();
        }
        catch (Exception failure)
        {
            GD.PushError($"The dice forge stopped unexpectedly: {failure}");
        }
    }

    /// <summary>Writes the presenter's state into the scene, if the scene is still there to write into.</summary>
    private void Render()
    {
        var presenter = _presenter;

        // Validity before tree membership: asking a freed node whether it is inside the tree is
        // itself the crash, and a shutdown during a slow command is the ordinary case on a handset.
        if (presenter is null || !IsInstanceValid(this) || !IsInsideTree() ||
            _titleLabel is null || _faceList is null || _optionList is null ||
            _withheldLabel is null || _statusLabel is null || _rejectionLabel is null ||
            _forgeButton is null)
        {
            return;
        }

        var open = presenter.Stage == DiceForgeStage.Ready;

        _titleLabel.Text = presenter.Title;
        _titleLabel.Visible = open;

        DrawChoices(presenter, open);

        _withheldLabel.Text = presenter.WithheldText;
        _withheldLabel.Visible = open;

        _forgeButton.Text = presenter.UpgradeText;
        _forgeButton.Visible = open;
        _forgeButton.Disabled = _busy || !open || _face is null || _option is null;

        // Hidden rather than blanked once they have nothing to say: an empty label still claims a
        // full line of height. They are two lines because they answer two different questions —
        // what state the screen is in, and what the game said about the last command.
        _statusLabel.Text = presenter.StatusText;
        _statusLabel.Visible = _statusLabel.Text.Length > 0;

        _rejectionLabel.Text = presenter.RejectionText;
        _rejectionLabel.Visible = _rejectionLabel.Text.Length > 0;
    }

    /// <summary>Rebuilds the two lists when the die changed, and re-states the selection when it did not.</summary>
    private void DrawChoices(DiceForgePresenter presenter, bool open)
    {
        if (_faceList is null || _optionList is null)
        {
            return;
        }

        _faceList.Visible = open;
        _optionList.Visible = open;

        if (!_drawnFaces.SequenceEqual(presenter.Faces))
        {
            Rebuild(presenter);
        }

        Restate(_faceList, _face);
        Restate(_optionList, _option);
    }

    /// <summary>Puts every row's pressed state and availability back where the selection says.</summary>
    private void Restate(VBoxContainer list, int? selected)
    {
        for (var index = 0; index < list.GetChildCount(); index++)
        {
            if (list.GetChild(index) is not Button row)
            {
                continue;
            }

            // Only the disabled flag is touched for an unupgradeable face: its caption and its own
            // toggle state were set when it was built, and rewriting the caption on every render
            // would undo the selection the player is looking at.
            row.ButtonPressed = selected is { } chosen && Index(row) == chosen;
            row.Disabled = _busy || row.HasMeta(UnupgradeableMeta);
        }
    }

    /// <summary>The index a row carries — its face number, or its option position.</summary>
    private static int Index(Button row) => (int)row.GetMeta(IndexMeta);

    private const string IndexMeta = "sir_index";
    private const string UnupgradeableMeta = "sir_unupgradeable";

    /// <summary>Instantiates one row per face and one per option, each wired to its own index.</summary>
    private void Rebuild(DiceForgePresenter presenter)
    {
        if (_faceList is null || _optionList is null ||
            GD.Load<PackedScene>(ChoiceRowScenePath) is not { } rowScene)
        {
            GD.PushError($"The forge choice row could not be loaded from '{ChoiceRowScenePath}'.");

            return;
        }

        Clear(_faceList);
        Clear(_optionList);

        _drawnFaces = presenter.Faces;
        _face = null;
        _option = null;

        foreach (var face in presenter.Faces)
        {
            var row = rowScene.Instantiate<Button>();

            row.Text = presenter.FaceLabel + " " + face.FaceIndex + ":  " + face.Current;
            row.SetMeta(IndexMeta, face.FaceIndex);

            if (!face.Upgradeable)
            {
                // Marked rather than skipped: a die drawn with four faces is a worse lie than one
                // with two rows the player can see and cannot press.
                row.SetMeta(UnupgradeableMeta, true);
            }

            var faceIndex = face.FaceIndex;

            // Captured by value: the loop variable would otherwise be shared by every handler.
            row.Pressed += () => OnFaceSelected(faceIndex);
            ButtonTextColours.ApplyTo(row, LiveColour, UnavailableColour);

            _faceList.AddChild(row);
        }

        foreach (var option in presenter.Options)
        {
            var row = rowScene.Instantiate<Button>();

            row.Text = option.Name;
            row.SetMeta(IndexMeta, option.OptionIndex);

            var optionIndex = option.OptionIndex;

            row.Pressed += () => OnOptionSelected(optionIndex);
            ButtonTextColours.ApplyTo(row, LiveColour, UnavailableColour);

            _optionList.AddChild(row);
        }
    }

    /// <summary>Empties a container now, rather than at the end of the frame.</summary>
    /// <remarks>
    /// <c>QueueFree</c> alone defers removal, so a child freed and a child added in the same frame
    /// are both in the tree and both laid out until it ends.
    /// </remarks>
    private static void Clear(Node container)
    {
        foreach (var child in container.GetChildren())
        {
            container.RemoveChild(child);
            child.QueueFree();
        }
    }

    private void OnFaceSelected(int faceIndex)
    {
        _face = faceIndex;
        Render();
    }

    private void OnOptionSelected(int optionIndex)
    {
        _option = optionIndex;
        Render();
    }

    private void OnForgePressed()
    {
        if (_face is not { } face || _option is not { } option)
        {
            return;
        }

        _ = SubmitAsync(presenter => presenter.ForgeAsync(face, option, HighestPip, _lifetime));
    }

    /// <remarks>
    /// Every control is taken out of use for the whole round trip and put back once, on one path. A
    /// second press landing mid-flight would submit a command against a run the first has already
    /// moved — and on a forge that is a second face upgraded from a single tap.
    /// </remarks>
    private async Task SubmitAsync(Func<DiceForgePresenter, Task<DiceForgeSubmission>> submit)
    {
        if (_busy || _presenter is not { } presenter)
        {
            return;
        }

        // Latched before the await, not after it: taken afterwards, a second press arriving while
        // the first is in flight finds it unset and submits again.
        _busy = true;
        Render();

        try
        {
            await submit(presenter);

            Report(presenter);
        }
        catch (Exception failure)
        {
            GD.PushError($"A dice forge command failed: {failure}");
        }
        finally
        {
            _busy = false;
            Render();
            LeaveIfTheTileHasCleared();
        }
    }

    /// <summary>Hands back to the board once the run no longer stands on the forge tile.</summary>
    /// <remarks>
    /// Read off the run the command answered with, never off which command was pressed: a screen
    /// that left because it had submitted an upgrade would leave on one the rules layer had refused.
    /// </remarks>
    private void LeaveIfTheTileHasCleared()
    {
        if (_presenter is not { Stage: DiceForgeStage.NotAtAForge } || _board is not { } board ||
            !IsInstanceValid(this) || !IsInsideTree())
        {
            return;
        }

        RunDecisionHandover.Return(this, board);
    }

    /// <summary>Prints, on one greppable line, what this screen resolved against the run the build shipped.</summary>
    private static void Report(DiceForgePresenter presenter) =>
        GD.Print(
            $"{DiceForgeMarker} stage={presenter.Stage} faces={presenter.Faces.Count} " +
            $"upgradeable={presenter.Faces.Count(face => face.Upgradeable)} " +
            $"options={presenter.Options.Count} host_faulted={presenter.HostFaulted} " +
            $"rejection={presenter.RulesRejection?.ToString() ?? "none"}");
}
