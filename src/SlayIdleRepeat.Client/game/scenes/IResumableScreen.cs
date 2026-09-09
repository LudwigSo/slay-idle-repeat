namespace SlayIdleRepeat.Client.Game.Scenes;

/// <summary>
/// A screen that is shown again by being <em>resumed</em> — re-read as well as un-hidden — when a
/// screen it opened hands back to it.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>A capability, not a type.</b> <see cref="InventoryHandover"/> takes any <c>Node3D</c> as the
/// screen to return to, because the Gear screen is reachable from more than one place; but the screen
/// it returns to has usually changed underneath — a loadout swapped, a wallet spent — and a screen
/// merely shown again would draw the row it read on the way in. A type test on <c>Home</c> would have
/// done the job for Home and silently not done it for the next screen that opens the bag. Implementing
/// this is how a screen says "read me again when they come back".
/// </para>
/// <para>
/// Every implementer's <see cref="Resume"/> is also its own <c>ScreenStage.Show</c>: the handover calls
/// one or the other, never both.
/// </para>
/// </remarks>
public interface IResumableScreen
{
    /// <summary>Shows the screen again and reads afresh whatever it draws from.</summary>
    void Resume();
}
