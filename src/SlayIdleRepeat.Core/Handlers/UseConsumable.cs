using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Handlers;

/// <summary>
/// The <c>USE_CONSUMABLE</c> handler: spends one held consumable from the run's pouch (`03` §7.1).
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Board-only, and the gate is the pending tile.</b> `03` §7.1 makes use legal "only in the
/// <c>AWAIT_ROLL</c> state — never while moving, never during a prompt, never in battle". This
/// handler's reading of that state is: no tile pending, no fork pending. The battle half is already
/// covered twice over — <c>GameRules.Execute</c> refuses every command but
/// <c>CONFIRM_BATTLE_RESULT</c> and <c>ABANDON_RUN</c> while a fight is open — and the draft half by
/// its own gate, so what is left for this handler is the board's own two.
/// </para>
/// <para>
/// Only the two HELD consumables can be used: the Reroll Token and the Draft Token convert to their
/// charge at the till and are never in the pouch, so naming one here is a request to spend something
/// the run does not hold, which is the same refusal as naming a Draught it has none of.
/// </para>
/// <para>
/// 🔒 <b>The Draught is refused at full HP</b> — `03` §7.1's "disabled at full HP — a draught can
/// never be wasted by a mis-tap" — and the rope is refused when one is already armed, because
/// `03` §7.1 permits only one armed at a time and arming a second would silently destroy it.
/// </para>
/// </remarks>
internal static class UseConsumable
{
    /// <summary>Applies <c>USE_CONSUMABLE</c>.</summary>
    /// <param name="command">Which consumable to use.</param>
    /// <param name="input">The cloned, already-caught-up, in-run slice.</param>
    /// <returns>
    /// <see cref="RejectionReason.ILLEGAL_STATE"/> when the run is not on the board between rolls,
    /// the id is not one of the two held consumables, the run holds none, or the use would be wasted;
    /// otherwise accepted, with the consumable spent.
    /// </returns>
    internal static HandlerResult Handle(UseConsumableCommand command, HandlerInput input)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(input);

        var run = input.Run;

        if (run.HasPendingTile || run.PendingFork is not null)
        {
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        if (!Consumables.IsHeld(command.ConsumableId) || run.ConsumableCount(command.ConsumableId) == 0)
        {
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        // The waste checks come before anything is spent, so a refused use costs the player nothing.
        switch (command.ConsumableId)
        {
            case Consumables.HealthDraught when run.CurrentHp >= run.MaxHp:
            case Consumables.EscapeRope when run.EscapeRopeArmed:
                return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);

            default:
                break;
        }

        // Spent first: every remaining branch succeeds, and taking the item before applying it is
        // what stops a throw mid-apply leaving the run holding a consumable it has already used.
        run.ConsumeOne(command.ConsumableId);

        switch (command.ConsumableId)
        {
            case Consumables.HealthDraught:
                Drink(input);
                break;

            case Consumables.EscapeRope:
                run.ArmEscapeRope();
                break;

            default:
                throw new InvalidOperationException(
                    "'" + command.ConsumableId + "' passed Consumables.IsHeld and reached the apply " +
                    "switch with no branch. A third held consumable was authored without a use — the " +
                    "run has already been charged for it at this point, which is why this throws " +
                    "rather than accepting a spend that did nothing.");
        }

        return HandlerResult.Accept();
    }

    /// <summary>The share of Max HP a Health Draught restores.</summary>
    /// <remarks>
    /// 📐 `03` §7.1 authors the Draught's heal as <b>30% Max HP</b> and marks it TUNABLE, but
    /// <c>currencies.json</c> has no key for it — the consumable block authors prices only. Steering
    /// S6 forbids inventing the key, and it equally forbids quietly borrowing the campfire's 40% or
    /// the shop Heal's 35%, which are different numbers for different things. The document's own
    /// printed value is transcribed here, greppable, and the day the key is authored this constant is
    /// what the reader replaces.
    /// </remarks>
    internal const double HealthDraughtHealPctMaxHp = 0.30;

    /// <summary>Drinks a Health Draught. Overheal is clamped by the rule that computes it.</summary>
    private static void Drink(HandlerInput input)
    {
        var run = input.Run;
        var healed = (int)Math.Round(run.MaxHp * HealthDraughtHealPctMaxHp, MidpointRounding.ToEven);

        run.SetHitPoints(Math.Min(run.MaxHp, run.CurrentHp + healed), run.MaxHp);
    }
}
