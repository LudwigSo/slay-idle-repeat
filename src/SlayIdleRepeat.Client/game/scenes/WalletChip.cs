using Godot;
using SlayIdleRepeat.Client.Game.Presenters;

namespace SlayIdleRepeat.Client.Game.Scenes;

/// <summary>
/// One wallet chip in the Gear screen's top bar: a glyph and a figure the presenter has already
/// written.
/// </summary>
/// <remarks>
/// It decides nothing — not the number, not its written form. What it owns is the engine half: which
/// glyph file the exported resource resolves to, through <see cref="IconCatalogue"/>, and the two
/// nodes it writes into.
/// </remarks>
public partial class WalletChip : PanelContainer
{
    /// <summary>Where this scene lives, for the screen that instances it.</summary>
    public const string ScenePath = "res://game/scenes/WalletChip.tscn";

    private const string IconPath = "%Icon";
    private const string ValuePath = "%Value";

    /// <summary>Which wallet this chip is. Exported, so the three instances differ by an authored value.</summary>
    [Export] public HudIcon Glyph { get; set; } = HudIcon.Crowns;

    private Label? _value;

    /// <inheritdoc/>
    public override void _Ready()
    {
        GetNode<TextureRect>(IconPath).Texture = GD.Load<Texture2D>(IconCatalogue.PathOf(Glyph));
        _value = GetNode<Label>(ValuePath);
    }

    /// <summary>Writes the figure, as the presenter wrote it.</summary>
    /// <param name="text">The wallet's figure.</param>
    public void Show(string text)
    {
        if (_value is not null && IsInstanceValid(_value))
        {
            _value.Text = text;
        }
    }
}
