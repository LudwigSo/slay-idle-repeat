using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Forge;
using SlayIdleRepeat.Core.Rules.Inventory;

namespace SlayIdleRepeat.Core.Handlers;

/// <summary>
/// <c>SALVAGE</c> — break items down for Merge Dust and a share of the stones their level cost.
/// </summary>
/// <remarks>
/// <para>
/// <b>All or nothing.</b> Every named item is checked before any of them is destroyed, so a batch
/// naming one locked item destroys none of the others. Salvage is the one operation in the forge
/// that cannot be undone, which is what makes a partial application worse than a refusal.
/// </para>
/// <para>
/// <b>A locked item refuses the whole batch, and that is the lock working.</b> Excluding it silently
/// and salvaging the rest would be a batch that did something other than what the player asked for,
/// on the operation where being surprised costs the most.
/// </para>
/// <para>
/// ⚠️ <b>The top band's Set Token is not paid.</b> Set Tokens are a non-wallet counter nothing on the
/// player holds and nothing spends, so there is no column to pay one into — the salvage rule records
/// the same absence, and the task that builds the token economy pays it.
/// </para>
/// </remarks>
internal static class Salvage
{
    /// <summary>The attribution token salvage dust is logged under.</summary>
    private const string DustReason = "salvage_dust";

    /// <summary>The attribution token the stone refund is logged under.</summary>
    private const string StoneReason = "salvage_stone_refund";

    /// <summary>Breaks the named items down.</summary>
    /// <param name="command">The items to salvage.</param>
    /// <param name="input">The slice and the content.</param>
    /// <returns>The currency movements, or the reason the batch was refused.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    internal static HandlerResult Handle(SalvageCommand command, HandlerInput input)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(input);

        if (command.ItemIds.Count == 0)
        {
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        var player = input.Player;
        var stock = player.Inventory;
        var forge = ForgeTuning.Read(input.Context.Content);

        var seen = new HashSet<GearInstanceId>(command.ItemIds.Count);
        var dust = 0L;
        var stones = 0L;

        foreach (var id in command.ItemIds)
        {
            // A repeat names one item twice and would be paid for twice while being destroyed once.
            if (!seen.Add(id))
            {
                return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
            }

            if (InventoryAccess.RejectionFor(stock.Availability(id)) is { } unavailable)
            {
                return HandlerResult.Reject(unavailable);
            }

            var payout = GearSalvage.Payout(stock.Find(id)!, forge);

            dust += payout.Dust;
            stones += payout.Stones;
        }

        var inventoryTuning = InventoryTuning.Read(input.Context.Content);

        foreach (var id in command.ItemIds)
        {
            stock.Remove(id, inventoryTuning);
        }

        var events = new List<DomainEvent>(2);

        if (dust != 0)
        {
            events.Add(player.MoveCurrency(CurrencyId.MERGE_DUST, dust, DustReason));
        }

        if (stones != 0)
        {
            events.Add(player.MoveCurrency(CurrencyId.ENHANCE_STONES, stones, StoneReason));
        }

        return HandlerResult.Accept(events);
    }
}
