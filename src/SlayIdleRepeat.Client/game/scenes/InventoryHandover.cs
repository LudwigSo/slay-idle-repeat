using Godot;
using SlayIdleRepeat.Client.Composition;

namespace SlayIdleRepeat.Client.Game.Scenes;

/// <summary>
/// The handover onto the Inventory screen, and the way back off it.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 The screen it came from is reused and the inventory frees itself, which is the same shape
/// <see cref="RunDecisionHandover"/> states for the three run-decision screens. The reason is the same
/// too: the screen behind holds a presenter mid-read, and rebuilding it would re-issue that read and
/// lose whatever the player was looking at.
/// </para>
/// <para>
/// ⚠️ It takes a <c>Node3D</c> rather than a named screen type, because S16 is reachable from more than
/// one place — Home between runs today, and <c>13</c> §1.1's Hero screen when M9 lands. Naming one caller
/// would make the second one a change to this file. <c>Node3D</c> is what a screen IS since the 3D
/// conversion, and it is the loosest thing <see cref="ScreenStage"/> can show and hide.
/// </para>
/// </remarks>
public static class InventoryHandover
{
    /// <summary>Puts the inventory in front of the screen that opened it.</summary>
    /// <param name="from">The screen handing over, hidden on success and returned to later.</param>
    /// <param name="screen">The composed inventory.</param>
    /// <param name="lifetime">Cancelled when the application shuts down.</param>
    /// <returns>True when the screen is in the tree and the one behind it is hidden.</returns>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public static bool Show(Node3D from, ComposedInventoryScreen screen, CancellationToken lifetime)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(screen);

        if (GD.Load<PackedScene>(Inventory.ScenePath) is not { } scene ||
            scene.Instantiate() is not Inventory inventory)
        {
            GD.PushError(
                "The Inventory scene did not load, so the screen was not opened and the one behind it " +
                "stays visible. A player cannot equip what they cannot reach, so this is a defect.");

            return false;
        }

        inventory.Drive(screen.Inventory, from, lifetime, screen.ReducedMotion);

        from.GetParent().AddChild(inventory);
        ScreenStage.Hide(from);

        return true;
    }

    /// <summary>Hands control back to the screen the inventory was entered from, and frees it.</summary>
    /// <param name="screen">The inventory standing down, which is queued for freeing.</param>
    /// <param name="to">The screen it was entered from, which is shown again.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public static void Return(Inventory screen, Node3D to)
    {
        ArgumentNullException.ThrowIfNull(screen);
        ArgumentNullException.ThrowIfNull(to);

        // 🔒 A screen that can be resumed is RESUMED rather than merely shown, and the difference is the
        // read. The Gear screen exists to change the loadout, and Home's power pill and hero caption are
        // drawn from the profile row as it stood when Home last read it — so a player who equips a better
        // blade and comes back would find the pill still quoting the old figure. Resume re-reads; Show
        // would not. Asked of the capability rather than of the type, so the next screen that opens the
        // bag gets the same treatment by implementing it, not by editing this file.
        if (to is IResumableScreen resumable)
        {
            resumable.Resume();
        }
        else
        {
            ScreenStage.Show(to);
        }

        screen.GetParent()?.RemoveChild(screen);
        screen.QueueFree();
    }
}
