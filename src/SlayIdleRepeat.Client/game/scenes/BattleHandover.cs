using Godot;
using SlayIdleRepeat.Client.Composition;

namespace SlayIdleRepeat.Client.Game.Scenes;

/// <summary>
/// The handover onto the battle replay, and the way back off it.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>This is the first handover in the build that comes back, and the free-or-reuse decision
/// <see cref="BoardHandover"/> says the introducing task owes is made here.</b> The rule it inherits
/// is that every earlier handover is one-way: it instantiates unconditionally, hides the outgoing
/// screen, and never frees anything — which is safe exactly as long as nobody returns. A battle
/// returns, and it returns to the same board on every fight of a run.
/// </para>
/// <para>
/// 🔒 <b>The board is REUSED and the replay FREES ITSELF.</b> Both halves are decided by what each
/// screen is: the board is one long-lived screen a run stands on for its whole length, so
/// re-instantiating it per fight would add a board and a presenter and a fresh read to the tree for
/// every enemy in a chapter, and the twentieth fight of a run would be played on the twentieth board.
/// The replay is the opposite — it is about one fight, its presenter holds that fight's log and its
/// playhead, and there is nothing in it a second fight could reuse. Keeping it hidden would leave one
/// dead log in memory per battle for the life of the application.
/// </para>
/// <para>
/// 🔒 <b>The board is re-READ, not merely un-hidden.</b> A replay that reached its end submitted the
/// confirmation that closes the battle, so the run behind the hidden board is a different row from
/// the one it drew: the phase has moved, the HP has moved, and a won fight has opened a draft. Making
/// the screen visible without reading again would put a pre-battle board in front of a post-battle
/// run, which reads as though the fight had not happened.
/// </para>
/// <para>
/// 🔒 <b>The free is deferred, and that is not a detail.</b> <see cref="Return"/> is reached from the
/// replay's own handler, so freeing outright would destroy the object the call is running on;
/// <c>QueueFree</c> lets the frame finish first. It is also why the board is shown BEFORE the replay
/// is queued — the order the other way round leaves one frame with nothing visible on it.
/// </para>
/// </remarks>
public static class BattleHandover
{
    /// <summary>Puts the replay of the run's open battle beside the board and hides the board.</summary>
    /// <remarks>
    /// The replay is added to the board's own parent rather than to the board, matching every other
    /// handover in this build: a child would be drawn inside a ground the board still owns, and the
    /// board has to survive the fight to be handed back to.
    /// </remarks>
    /// <param name="from">The board handing over, which is hidden on success and returned to later.</param>
    /// <param name="screen">The composed replay, already built for the battle being entered.</param>
    /// <param name="lifetime">Cancelled when the application shuts down.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public static void Show(Board from, ComposedBattleScreen screen, CancellationToken lifetime)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(screen);

        var parent = from.GetParent();

        if (parent is null)
        {
            GD.PushError("The board has no parent to hand the battle replay to.");

            return;
        }

        var packed = GD.Load<PackedScene>(BattleReplay.ScenePath);

        if (packed is null)
        {
            // Load answers null rather than throwing when the resource is missing or its import
            // cannot be read, so an unnamed null reference is all a caller gets unless it says so.
            GD.PushError($"The battle replay could not be loaded from '{BattleReplay.ScenePath}'.");

            return;
        }

        var replay = packed.Instantiate<BattleReplay>();

        replay.Drive(screen.Battle, screen.ReducedMotion, from, lifetime);

        from.Visible = false;

        parent.AddChild(replay);
    }

    /// <summary>Hands control back to the board the fight was entered from, and frees the replay.</summary>
    /// <remarks>
    /// A board that has been freed underneath a running replay — a shutdown mid-fight is the ordinary
    /// way that happens on a handset — is named rather than written to, and the replay is freed
    /// regardless: a screen with nowhere to return to is still a screen that has finished.
    /// </remarks>
    /// <param name="replay">The replay standing down, which is queued for freeing.</param>
    /// <param name="to">The board the fight was entered from, which is shown and read again.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public static void Return(BattleReplay replay, Board to)
    {
        ArgumentNullException.ThrowIfNull(replay);
        ArgumentNullException.ThrowIfNull(to);

        if (GodotObject.IsInstanceValid(to))
        {
            to.Resume();
        }
        else
        {
            GD.PushError("The battle replay finished and the board it was entered from is gone.");
        }

        replay.QueueFree();
    }
}
