using Godot;
using SlayIdleRepeat.Client.Game.Presenters;

namespace SlayIdleRepeat.Client.Game.Scenes;

/// <summary>
/// One resource pill in the Home screen's top bar: an icon, a value, and a caption when the
/// resource has one.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>One scene for all three pills</b>, as the reference asks in as many words — Crowns, Energy
/// and Power differ by which glyph they carry, whether they caption, and what they open. Three
/// scenes would be three places to fix the day the pill's shape changes.
/// </para>
/// <para>
/// 🔒 <b>It decides nothing.</b> The number, its written form and the caption all arrive from
/// <see cref="HomePresenter"/>; what this file owns is the engine half — the glyph resource, the
/// count-up, the gain flash and the press. Its own WIDTH is
/// <see cref="HomeLayout.PillWidth(int, int, HomePillMetrics)"/>'s answer rather than a number
/// here, so the row's arithmetic is checked under a test runner and not by looking at a screenshot.
/// </para>
/// <para>
/// ⚠️ <b>Both animations are skippable and neither carries information of its own.</b> With motion
/// reduced the value is written straight in and the flash never happens, and the pill reads exactly
/// the same — the count-up is a flourish on a number that is already correct, and the gain accent
/// repeats what the number itself already says.
/// </para>
/// </remarks>
public partial class ResourcePill : Button
{
    /// <summary>Where this scene lives, for the screen that instances it.</summary>
    public const string ScenePath = "res://game/scenes/ResourcePill.tscn";

    private const string IconPath = "%Icon";
    private const string ValuePath = "%Value";
    private const string CaptionPath = "%Caption";

    /// <summary>The property a flash tweens back to rest along.</summary>
    private const string ModulateProperty = "modulate";

    /// <summary>
    /// Which resource this pill is. Exported, so the three instances differ by an authored value
    /// rather than by three near-identical scenes.
    /// </summary>
    /// <remarks>
    /// The glyph is <see cref="IconCatalogue"/>'s path for it and never a texture assigned here: the
    /// catalogue is the one table that pairs a resource with its file, and a scene binding its own
    /// would be a second pairing nothing checks.
    /// </remarks>
    [Export] public HudIcon Glyph { get; set; } = HudIcon.Crowns;

    /// <summary>How long a changed value takes to count up. The reference's short count-up.</summary>
    [Export] public double CountUpSeconds { get; set; } = 0.4;

    /// <summary>How long the gain accent stays on a value that rose.</summary>
    [Export] public double GainFlashSeconds { get; set; } = 0.6;

    /// <summary>How long a press is held before it counts as a hold rather than a tap.</summary>
    [Export] public double LongPressSeconds { get; set; } = 0.4;

    /// <summary>Raised when the pill is tapped, naming the resource it is about.</summary>
    /// <remarks>
    /// The pill knows which resource it is and nothing about what that resource's sheet looks like.
    /// The screen above decides where a tap goes.
    /// </remarks>
    public event Action<HudIcon>? ResourceTapped;

    /// <summary>Raised <c>true</c> when a press becomes a hold, and <c>false</c> when that hold ends.</summary>
    /// <remarks>
    /// 🔒 <b>Only the gesture is here.</b> What a hold MEANS to a number — that it is written in
    /// full rather than shortened — is <see cref="HomePresenter.FullValuesRevealed"/>'s answer, and
    /// it is one state for the whole screen rather than one per pill, because the gesture asks "how
    /// many, exactly?" of all of them. This half owns the single fact an engine event carries:
    /// whether the finger is still down.
    /// </remarks>
    public event Action<bool>? Held;

    private TextureRect? _icon;
    private Label? _value;
    private Label? _caption;
    private Tween? _countUp;
    private Tween? _flash;

    /// <summary>Whether a finger is currently down on this pill.</summary>
    private bool _down;

    /// <summary>Whether that press has already been reported as a hold.</summary>
    private bool _holding;

    /// <summary>
    /// Whether the release that just happened ended a hold.
    /// </summary>
    /// <remarks>
    /// 🔒 The engine emits <c>button_up</c> and then <c>pressed</c> for one release, so a hold
    /// that ends over the control would also open the resource sheet under the finger that was
    /// reading the exact figure — two answers to one gesture. Cleared on the next press down as
    /// well as when it is spent, so a release that lands off the control cannot swallow the tap
    /// after it.
    /// </remarks>
    private bool _releaseEndedAHold;

    /// <summary>How the amount is written. The presenter's rule, never this file's.</summary>
    private Func<long, string> _format = PlayerNumber.Full;

    /// <summary>The amount currently on the pill — where a count-up starts from.</summary>
    private long _shown;

    /// <summary>Whether anything has been drawn yet. A first value never counts up from zero.</summary>
    private bool _drawn;

    /// <summary>The value label's colour at rest, captured rather than written down.</summary>
    private Color _restTint;

    /// <inheritdoc/>
    public override void _Ready()
    {
        _icon = GetNode<TextureRect>(IconPath);
        _value = GetNode<Label>(ValuePath);
        _caption = GetNode<Label>(CaptionPath);

        _restTint = _value.Modulate;
        _icon.Texture = GD.Load<Texture2D>(IconCatalogue.PathOf(Glyph));

        Pressed += OnPressed;
        ButtonDown += OnDown;
        ButtonUp += OnUp;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// 🔒 <b>Hiding the screen ends the hold.</b> A pill held while another finger taps Start
    /// hands the screen over, and a hidden control never receives the release the gesture is waiting
    /// for — so the finger-down and the revealed figures would both survive the handover and come
    /// back with nobody holding anything. The visibility change is the one event that does arrive.
    /// </remarks>
    public override void _Notification(int what)
    {
        base._Notification(what);

        if (what == NotificationVisibilityChanged && _down && !IsVisibleInTree())
        {
            EndPress();
        }
    }

    /// <inheritdoc/>
    public override void _ExitTree()
    {
        Pressed -= OnPressed;
        ButtonDown -= OnDown;
        ButtonUp -= OnUp;

        EndPress();

        Stop(ref _countUp);
        Stop(ref _flash);
    }

    /// <summary>Draws one amount, counting up to it unless motion is reduced.</summary>
    /// <param name="amount">The number behind the value.</param>
    /// <param name="format">How the presenter writes that number.</param>
    /// <param name="caption">The caption, or empty when this pill has none right now.</param>
    /// <param name="metrics">What a pill is built from — this pill's own width comes out of it.</param>
    /// <param name="reducedMotion">Whether the count-up is skipped and the value written straight in.</param>
    /// <exception cref="ArgumentNullException"><paramref name="format"/> or <paramref name="caption"/> is null.</exception>
    public void Show(
        long amount,
        Func<long, string> format,
        string caption,
        HomePillMetrics metrics,
        bool reducedMotion)
    {
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(caption);
        ArgumentNullException.ThrowIfNull(metrics);

        _format = format;

        if (_caption is not null && IsInstanceValid(_caption))
        {
            _caption.Text = caption;

            // Hidden rather than blanked: an empty label still claims its width, and the Energy
            // pill's caption is gone at full rather than merely empty.
            _caption.Visible = caption.Length > 0;
        }

        CustomMinimumSize = new Vector2(
            HomeLayout.PillWidth(format(amount).Length, caption.Length, metrics),
            CustomMinimumSize.Y);

        Stop(ref _countUp);

        // A first draw is not a change, and a change nobody asked to see animated is not one either.
        if (reducedMotion || !_drawn || amount == _shown || CountUpSeconds <= 0d)
        {
            _shown = amount;
            _drawn = true;
            Write(amount);

            return;
        }

        var from = _shown;

        _shown = amount;
        _countUp = CreateTween();
        _countUp.TweenMethod(
            Callable.From<double>(Counting), (double)from, (double)amount, CountUpSeconds);

        // The exact amount at the end, whatever the last interpolated step happened to round to.
        _countUp.TweenCallback(Callable.From(() => Write(amount)));
    }

    /// <summary>Tints the value to an accent and lets it fade back to rest.</summary>
    /// <param name="accent">The theme accent to flash in. Never a colour this file decides.</param>
    /// <param name="reducedMotion">When true, nothing happens at all.</param>
    public void Flash(Color accent, bool reducedMotion)
    {
        if (reducedMotion || _value is null || !IsInstanceValid(_value) || GainFlashSeconds <= 0d)
        {
            return;
        }

        Stop(ref _flash);

        _value.Modulate = accent;
        _flash = CreateTween();
        _flash.TweenProperty(_value, ModulateProperty, _restTint, GainFlashSeconds);
    }

    private void Counting(double amount) => Write((long)amount);

    private void Write(long amount)
    {
        if (_value is not null && IsInstanceValid(_value))
        {
            _value.Text = _format(amount);
        }
    }

    private void OnPressed()
    {
        if (_releaseEndedAHold)
        {
            _releaseEndedAHold = false;

            return;
        }

        ResourceTapped?.Invoke(Glyph);
    }

    private void OnDown()
    {
        _down = true;
        _releaseEndedAHold = false;

        // The tree's timer rather than a node of this pill's own: it is one shot, it is created on
        // the press and it is gone after it, so a timer node would be a permanent child kept for a
        // gesture most players never make.
        var hold = GetTree()?.CreateTimer(LongPressSeconds);

        if (hold is null)
        {
            return;
        }

        hold.Timeout += OnHoldElapsed;
    }

    private void OnUp()
    {
        _releaseEndedAHold = _holding;

        EndPress();
    }

    /// <remarks>
    /// The flag is read FIRST, and it is cleared on teardown and on a hide as well as on release:
    /// this timer belongs to the tree and fires whether or not the pill that asked for it is still
    /// there, or still the one being pressed.
    /// </remarks>
    private void OnHoldElapsed()
    {
        if (!_down || _holding || !IsInstanceValid(this))
        {
            return;
        }

        _holding = true;
        Held?.Invoke(true);
    }

    /// <summary>Ends the press, and the hold with it when the press had become one.</summary>
    private void EndPress()
    {
        _down = false;

        if (!_holding)
        {
            return;
        }

        _holding = false;
        Held?.Invoke(false);
    }

    private static void Stop(ref Tween? tween)
    {
        if (tween is not null && IsInstanceValid(tween))
        {
            tween.Kill();
        }

        tween = null;
    }
}
