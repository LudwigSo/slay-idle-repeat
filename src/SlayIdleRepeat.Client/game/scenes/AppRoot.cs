using Godot;
using SlayIdleRepeat.Client.Game.Presenters;

namespace SlayIdleRepeat.Client.Game.Scenes;

/// <summary>
/// The root scene: a driving adapter over <see cref="AppRootPresenter"/>.
/// </summary>
/// <remarks>
/// <para>
/// Scenes are the outermost ring, not the foundation. This one renders what the presenter
/// says and forwards what the player does; it holds no rules, no ports and no state of its
/// own, and it is the only half of the pair that is allowed to name the engine.
/// </para>
/// <para>
/// It is also the negative control for the presenter boundary rule: a scene script that
/// names the engine on purpose, so a rule finding nothing to object to here would be a rule
/// looking in the wrong place.
/// </para>
/// </remarks>
public partial class AppRoot : Node
{
    /// <inheritdoc/>
    public override void _Ready() => throw new NotImplementedException();
}
