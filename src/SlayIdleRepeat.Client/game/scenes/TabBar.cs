using Godot;
using SlayIdleRepeat.Application.Services;

namespace SlayIdleRepeat.Client.Game.Scenes;

/// <summary>
/// The Home screen's bottom navigation: five tabs, each a button, each able to carry a dot.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>The bar is this scene; the TABS are the screen's.</b> Which five destinations exist is a
/// fact about the game rather than about a strip of buttons, so the buttons are declared in
/// <c>Home.tscn</c> as this node's children and <c>HomeSceneRuleTests</c> reads them there. A bar
/// that authored its own five would put the one list nothing checks inside the one file nothing
/// reads.
/// </para>
/// <para>
/// 🔒 <b>A tab is matched to its destination by its own NAME.</b> The node names are the
/// <see cref="HomeTab"/> members, which is the fact the scene rule already pins — so a sixth button
/// named after nothing is simply not wired, rather than silently taking another tab's place.
/// </para>
/// <para>
/// ⚠️ Dots, never numbers: the reference is explicit, and this build records nothing that could
/// feed a count anyway.
/// </para>
/// </remarks>
public partial class TabBar : HBoxContainer
{
    /// <summary>Where this scene lives, for the screen that instances it.</summary>
    public const string ScenePath = "res://game/scenes/TabBar.tscn";

    /// <summary>What each tab button calls the dot it carries.</summary>
    private const string DotName = "Dot";

    /// <summary>Raised when a tab is pressed, naming the destination it stands for.</summary>
    public event Action<HomeTab>? TabSelected;

    private readonly List<TabEntry> _tabs = [];

    /// <inheritdoc/>
    public override void _Ready()
    {
        foreach (var child in GetChildren())
        {
            if (child is not Button button || !Enum.TryParse<HomeTab>(button.Name, out var tab))
            {
                continue;
            }

            // Held rather than written inline, so the same delegate can be taken off again: a
            // handler left connected across a bar that is detached and re-added fires twice, and
            // once is the whole contract of a navigation control.
            var entry = new TabEntry(tab, button, () => TabSelected?.Invoke(tab));

            button.Pressed += entry.Handler;
            _tabs.Add(entry);
        }
    }

    /// <inheritdoc/>
    public override void _ExitTree()
    {
        foreach (var entry in _tabs)
        {
            if (IsInstanceValid(entry.Button))
            {
                entry.Button.Pressed -= entry.Handler;
            }
        }

        _tabs.Clear();
    }

    /// <summary>Writes every tab's caption, its dot and which one is the screen the player is on.</summary>
    /// <param name="caption">What each tab is called, resolved.</param>
    /// <param name="badged">Whether each tab is carrying a dot.</param>
    /// <param name="dotColour">The accent a dot is drawn in — the theme's, never this file's.</param>
    /// <param name="selected">The tab whose screen is on the page.</param>
    /// <exception cref="ArgumentNullException">A delegate is null.</exception>
    public void Show(
        Func<HomeTab, string> caption, Func<HomeTab, bool> badged, Color dotColour, HomeTab selected)
    {
        ArgumentNullException.ThrowIfNull(caption);
        ArgumentNullException.ThrowIfNull(badged);

        foreach (var entry in _tabs)
        {
            if (!IsInstanceValid(entry.Button))
            {
                continue;
            }

            entry.Button.Text = caption(entry.Tab);
            entry.Button.ButtonPressed = entry.Tab == selected;

            if (entry.Button.GetNodeOrNull<Control>(DotName) is { } dot)
            {
                dot.Visible = badged(entry.Tab);
                dot.SelfModulate = dotColour;
            }
        }
    }

    /// <summary>One wired tab: which destination it is, its control, and the handler it holds.</summary>
    private sealed record TabEntry(HomeTab Tab, Button Button, Action Handler);
}
