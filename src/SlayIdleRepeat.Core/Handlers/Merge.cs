using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Model.Gear;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Forge;
using SlayIdleRepeat.Core.Rules.Inventory;
using SlayIdleRepeat.Core.Rules.Luck;

namespace SlayIdleRepeat.Core.Handlers;

/// <summary>
/// <c>MERGE</c> — fuse identical items into one of the next band, charging Crowns and, where a slot
/// is filled with dust, Merge Dust.
/// </summary>
/// <remarks>
/// <para>
/// <b>Everything is checked before anything is charged, and the order is the order the player
/// reads.</b> The identities first, because naming an item they do not own is the only failure that
/// is about the request rather than the selection; then whether the selection is a legal fusion; then
/// whether they can pay for it. A handler that debited before the last of those would take Crowns for
/// a fusion it then refused.
/// </para>
/// <para>
/// <b>The output takes the first input's slot and identity.</b> Three items go in and one comes out,
/// so the other two are removed and the first is replaced where it stands. Removing all three and
/// filing the output afterwards is the same edit only when the stock has room: at capacity the three
/// opened slots reclaim from overflow first, and the fused item — the one the player just paid for —
/// would land in overflow itself.
/// </para>
/// <para>
/// ⚠️ <b>It emits no gear event.</b> The event set is closed at six and none of them reports an item
/// changing; a fusion therefore reaches a client as a currency movement and a new state hash. The
/// item it produces is not a grant either — no band was drawn and no counter moved — so reporting it
/// as one would put a fusion in the same analytics bucket as a chest.
/// </para>
/// </remarks>
internal static class Merge
{
    /// <summary>The attribution token the fusion's Crown price is logged under.</summary>
    private const string CrownReason = "merge_cost";

    /// <summary>The attribution token a dust-filled slot is logged under.</summary>
    private const string DustReason = "merge_dust_substitute";

    /// <summary>Fuses the named items.</summary>
    /// <param name="command">The inputs and whether dust fills a slot.</param>
    /// <param name="input">The slice, the content and the command's draw seed.</param>
    /// <returns>The currency movements, or the reason the fusion was refused.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    internal static HandlerResult Handle(MergeCommand command, HandlerInput input)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(input);

        var player = input.Player;
        var stock = player.Inventory;
        var items = new List<GearInstance>(command.InputItemIds.Count);

        foreach (var id in command.InputItemIds)
        {
            if (InventoryAccess.RejectionFor(stock.Availability(id)) is { } unavailable)
            {
                return HandlerResult.Reject(unavailable);
            }

            // Availability has already said the stock holds it and that it is not locked, so the
            // only way this is null is a container that disagrees with itself.
            items.Add(stock.Find(id)!);
        }

        var forge = ForgeTuning.Read(input.Context.Content);

        if (GearMerge.Refusal(items, command.DustSubstituted, forge) is { } refusal)
        {
            return HandlerResult.Reject(RejectionFor(refusal));
        }

        var inputBand = items[0].Rarity;

        // Refusal answers NO_HIGHER_RARITY when there is no band above the inputs, so by here there is.
        var outputBand = LuckService.MergeOutputBand(inputBand)!.Value;

        var crowns = forge.MergeCrownCost(outputBand);
        var dust = command.DustSubstituted ? forge.MergeDustSubstituteCost(inputBand) : 0L;

        if (player.BalanceOf(CurrencyId.CROWNS) < crowns ||
            player.BalanceOf(CurrencyId.MERGE_DUST) < dust)
        {
            return HandlerResult.Reject(RejectionReason.INSUFFICIENT_FUNDS);
        }

        var fused = GearMerge.Fuse(
            items, DropsTuning.Read(input.Context.Content), input.MetaDraws.Stream(RngStreams.Forge));

        var inventoryTuning = InventoryTuning.Read(input.Context.Content);

        for (var i = 1; i < items.Count; i++)
        {
            stock.Remove(items[i].InstanceId, inventoryTuning);
        }

        stock.Replace(fused);

        var events = new List<DomainEvent>(2)
        {
            player.MoveCurrency(CurrencyId.CROWNS, -crowns, CrownReason),
        };

        if (dust != 0)
        {
            events.Add(player.MoveCurrency(CurrencyId.MERGE_DUST, -dust, DustReason));
        }

        return HandlerResult.Accept(events);
    }

    /// <summary>
    /// The wire rejection a refusal maps onto. <b>All seven map onto the same one</b>, which is
    /// exactly why the rule answers a named refusal: the wire vocabulary cannot tell a mismatched
    /// band from a duplicated input, so the distinction has to survive somewhere else. Written as one
    /// answer rather than seven identical arms — a per-member arm list that never disagrees with
    /// itself only reads as though the mapping had branches.
    /// </summary>
    private static RejectionReason RejectionFor(MergeRefusal refusal) =>
        Enum.IsDefined(refusal)
            ? RejectionReason.ILLEGAL_STATE
            : throw new ArgumentOutOfRangeException(
                nameof(refusal),
                refusal,
                "That is not one of the ways a fusion can be refused. Answering a rejection here " +
                "would give a rule nobody wrote a reason nobody chose.");
}
