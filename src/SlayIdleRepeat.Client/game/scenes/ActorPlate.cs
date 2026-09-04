using System.Globalization;
using Godot;
using SlayIdleRepeat.Client.Game.Presenters;

namespace SlayIdleRepeat.Client.Game.Scenes;

/// <summary>
/// The plate over one actor's head: its name, its health bar and readout, and its status chips.
/// </summary>
/// <remarks>
/// A canvas control rather than a 3D label, placed by the replay every frame at the actor's
/// unprojected anchor, so it keeps the interface's own text rendering and reflows with it. Every
/// size on it is authored in <c>ActorPlate.tscn</c>; a status chip is a copy of the hidden
/// template there, so the chip's square is a number in the scene rather than in this file.
/// </remarks>
public partial class ActorPlate : VBoxContainer
{
    /// <summary>Where this scene lives, for the replay that instantiates it.</summary>
    public const string ScenePath = "res://game/scenes/ActorPlate.tscn";

    private const string NamePath = "%Name";
    private const string HpBarPath = "%HpBar";
    private const string HpTextPath = "%HpText";
    private const string StatusesPath = "%Statuses";
    private const string ChipTemplatePath = "%ChipTemplate";

    /// <summary>The label inside a chip that carries its stack count.</summary>
    private const string StacksPath = "Stacks";

    /// <summary>The theme entry the bar's filled part is drawn from — the fill alone, never the track.</summary>
    private const string BarFillStyle = "fill";

    /// <summary>Separates a current health value from the value it started at.</summary>
    private const string OverSeparator = " / ";

    /// <summary>Introduces how many of a status are stacked, when more than one is.</summary>
    private const string StackPrefix = "×";

    /// <summary>Introduces a status the build has no icon for, which is then known by number alone.</summary>
    private const string StatusPrefix = "#";

    /// <summary>What stands beside an actor whose health the log never fixes.</summary>
    private const string UnknownValue = "—";

    private Label? _name;
    private ProgressBar? _bar;
    private Label? _hpText;
    private HBoxContainer? _statuses;
    private TextureRect? _chipTemplate;

    private readonly List<TextureRect> _chips = [];

    private double? _maximum;

    /// <inheritdoc/>
    public override void _Ready()
    {
        _name = GetNodeOrNull<Label>(NamePath);
        _bar = GetNodeOrNull<ProgressBar>(HpBarPath);
        _hpText = GetNodeOrNull<Label>(HpTextPath);
        _statuses = GetNodeOrNull<HBoxContainer>(StatusesPath);
        _chipTemplate = GetNodeOrNull<TextureRect>(ChipTemplatePath);

        if (_name is null || _bar is null || _hpText is null || _statuses is null || _chipTemplate is null)
        {
            GD.PushError(
                $"An actor plate is missing one of '{NamePath}', '{HpBarPath}', '{HpTextPath}', " +
                $"'{StatusesPath}' or '{ChipTemplatePath}', so the actor it stands over is drawn " +
                "without a name, a bar or its statuses.");
        }
    }

    /// <summary>Captions the plate and scales its bar, once, off what the log fixes about the actor.</summary>
    /// <param name="caption">The actor's name.</param>
    /// <param name="maxHp">The bar's denominator, or null when the log spawned no actor on this slot.</param>
    /// <param name="startingHp">Where the bar opens, or null with the same meaning.</param>
    /// <param name="tint">The side's own colour, painted onto the bar's fill.</param>
    public void Bind(string caption, double? maxHp, double? startingHp, Color tint)
    {
        if (_name is null || _bar is null || _hpText is null)
        {
            return;
        }

        _name.Text = caption;
        _maximum = maxHp;

        // The maximum is the SCALE and the starting health is the FILL: a run carries its health
        // between fights, so a wounded hero opens part-way along a full-length bar.
        _bar.MaxValue = Math.Max(maxHp ?? 0d, 1d);
        _bar.Value = startingHp ?? 0d;
        _bar.Visible = maxHp is not null;
        _bar.AddThemeStyleboxOverride(BarFillStyle, new StyleBoxFlat { BgColor = tint });

        _hpText.Text = maxHp is { } maximum && startingHp is { } start
            ? Readout(start, maximum)
            : UnknownValue;
    }

    /// <summary>Puts the bar and its readout on the value the playhead has walked the actor to.</summary>
    /// <param name="health">What the actor's health now stands at.</param>
    public void Spend(double health)
    {
        if (_bar is null || _hpText is null || _maximum is not { } maximum)
        {
            return;
        }

        _bar.Value = health;
        _hpText.Text = Readout(health, maximum);
    }

    /// <summary>Takes every status chip off the plate.</summary>
    public void ClearStatuses()
    {
        foreach (var chip in _chips)
        {
            _statuses?.RemoveChild(chip);
            chip.QueueFree();
        }

        _chips.Clear();
    }

    /// <summary>Adds one status chip: its icon, and its stack count when there is more than one.</summary>
    /// <param name="icon">The status's icon, or null for a status the build has no icon for.</param>
    /// <param name="statusId">The log's own number for the status, written on a chip with no icon.</param>
    /// <param name="stacks">How many are stacked.</param>
    public void AddStatus(Texture2D? icon, ushort statusId, int stacks)
    {
        if (_statuses is null || _chipTemplate is null ||
            _chipTemplate.Duplicate() is not TextureRect chip)
        {
            return;
        }

        chip.Texture = icon;
        chip.Visible = true;

        if (chip.GetNodeOrNull<Label>(StacksPath) is { } label)
        {
            var count = stacks > 1 ? StackPrefix + stacks.ToString(CultureInfo.InvariantCulture) : "";

            label.Text = icon is null
                ? StatusPrefix + statusId.ToString(CultureInfo.InvariantCulture) + count
                : count;
        }

        _statuses.AddChild(chip);
        _chips.Add(chip);
    }

    /// <remarks>Through <see cref="PlayerNumber"/>, like every other number a player reads, so a bar past ten thousand shortens the way a result does.</remarks>
    private static string Readout(double current, double maximum) =>
        PlayerNumber.Abbreviated((long)Math.Round(current)) +
        OverSeparator +
        PlayerNumber.Abbreviated((long)Math.Round(maximum));
}
