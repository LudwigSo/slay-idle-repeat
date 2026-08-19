using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Perks;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Rules.Economy;

namespace SlayIdleRepeat.Core.Handlers;

/// <summary>
/// The <c>SHOP_BUY</c> handler: buys one of the in-run shop's four slots, paid in run-local Gold.
/// </summary>
/// <remarks>
/// <para>
/// The order is: legality (a shop is open, the index names a slot of it, that slot is not already
/// bought and has something to sell), then affordability, then the grant, then the charge and the
/// purchase mark. Nothing is spent before every refusal has had its say, so a rejected purchase
/// leaves the run byte-identical.
/// </para>
/// <para>
/// 🔒 <b>A bought slot is marked, not removed.</b> The offer is derived from a stream position and
/// re-derived on every read (see <see cref="RunShopOffer"/>), so it cannot be edited — what changes is
/// the run's purchase mask. That is also what makes buying the same slot twice a refusal rather than
/// a second charge.
/// </para>
/// <para>
/// Each slot grants what `03` §7 says it does: a perk enters the run's drafted perks at Tier I (or
/// one tier up, if the run already owns it), a consumable is held or converted to its charge at the
/// till (`03` §7.1), a run buff joins the additive run-buff list, and the Heal restores a share of
/// Max HP. The Heal is refused at full HP, on the Health Draught's own rule and for its reason: a
/// heal that can be bought for nothing is a mis-tap that costs the player Gold.
/// </para>
/// </remarks>
internal static class ShopBuy
{
    /// <summary>Income-attribution token the shop's charge is logged under.</summary>
    internal const string SpendReason = "shop_buy";

    /// <summary>Applies <c>SHOP_BUY</c>.</summary>
    /// <param name="command">Which of the four slots to buy.</param>
    /// <param name="input">The cloned, already-caught-up, in-run slice.</param>
    /// <returns>
    /// <see cref="RejectionReason.ILLEGAL_STATE"/> when no shop is open, the index names no slot, the
    /// slot is already bought, the slot has nothing to sell, or the grant would be wasted (a Heal at
    /// full HP, a held consumable at the pouch cap); <see cref="RejectionReason.INSUFFICIENT_FUNDS"/>
    /// when the run cannot pay; otherwise accepted, with the charge's <c>CurrencyChanged</c>.
    /// </returns>
    internal static HandlerResult Handle(ShopBuyCommand command, HandlerInput input)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(input);

        var run = input.Run;

        if (!run.HasPendingTile ||
            (TileKind)run.PendingTileKindValue != TileKind.Shop ||
            run.ShopOfferDraw is not { } drawPosition)
        {
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        var offer = RunShopOffer.DrawAt(run, input.Context.Content, drawPosition);

        if (command.ShopSlotIndex < 0 || command.ShopSlotIndex >= offer.Count)
        {
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        if (run.IsShopSlotPurchased(command.ShopSlotIndex))
        {
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        var slot = offer[command.ShopSlotIndex];

        // A slot whose pool had nothing left to offer. It still occupies its index and still spent
        // its draw — see RunShopOffer's remarks — so it is refused here rather than renumbering the
        // offer, which would move every later slot under the player's finger.
        if (slot.Kind != ShopItemKind.HEAL && slot.ItemId is null)
        {
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        if (!CanBenefit(input, slot))
        {
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        if (run.BalanceOf(CurrencyId.GOLD) < slot.Price)
        {
            return HandlerResult.Reject(RejectionReason.INSUFFICIENT_FUNDS);
        }

        Grant(input, slot);

        var events = new List<DomainEvent>(1);

        if (slot.Price != 0)
        {
            events.Add(run.MoveCurrency(CurrencyId.GOLD, -slot.Price, SpendReason));
        }

        run.MarkShopSlotPurchased(command.ShopSlotIndex);

        return HandlerResult.Accept(events);
    }

    /// <summary>
    /// Whether the purchase would actually do something for this run.
    /// </summary>
    /// <remarks>
    /// 🔒 `03` §7.1 states this rule three times over — the Heal and the Draught are "disabled at
    /// full HP … can never be wasted by a mis-tap", the token slots "grey out" at their caps, and a
    /// purchase that would exceed the held cap "is unavailable". A shop that took the Gold anyway
    /// and granted nothing is the exact failure all three sentences are written against, so the
    /// checks live together here rather than one per grant branch.
    /// </remarks>
    private static bool CanBenefit(HandlerInput input, RunShopSlot slot)
    {
        var run = input.Run;

        return slot.Kind switch
        {
            ShopItemKind.HEAL => run.CurrentHp < run.MaxHp,
            ShopItemKind.CONSUMABLE => CanTakeConsumable(run, slot.ItemId!),

            // A perk slot only ever offers a row the run can still take (RunShopOffer filters owned-at-
            // top-tier rows out), and a run buff and the Heal always do something.
            _ => true,
        };
    }

    /// <summary>
    /// Whether one more of this consumable fits: the pouch cap for a held one, and nothing at all to
    /// check for the two that convert at the till.
    /// </summary>
    /// <remarks>
    /// ⚠️ The two token consumables are accepted unconditionally, and `03` §7.1's "greys out when
    /// charges are at the max stored" is therefore NOT enforced. It cannot be honestly enforced
    /// here: the stored-charge cap is counted against a total that includes talent and perk bonuses,
    /// neither of which exists, so a cap check today would refuse a purchase against a number that
    /// is not yet the real one. The grant itself is capped where it is spent, in
    /// <c>Rules.Dice.RerollEconomy</c>, so the charge cannot exceed the ceiling — what a player can
    /// still do is buy a token that grants them nothing.
    /// </remarks>
    private static bool CanTakeConsumable(Model.Run run, string consumableId) =>
        !Consumables.IsHeld(consumableId) || run.HeldConsumableCount < Consumables.HeldCap;

    /// <summary>Applies what the slot sells. Reached only once every refusal has passed.</summary>
    private static void Grant(HandlerInput input, RunShopSlot slot)
    {
        var run = input.Run;

        switch (slot.Kind)
        {
            case ShopItemKind.PERK:
                // One tier up from whatever the run owns — 0 for an unowned perk, which makes this
                // a fresh grant at Tier I. The same seam PICK_PERK uses, so a perk bought and a perk
                // drafted are the same holding rather than two.
                run.UpsertPerkTier(slot.ItemId!, run.DraftedPerks.TierOf(slot.ItemId!) + 1);
                break;

            case ShopItemKind.CONSUMABLE:
                GrantConsumable(run, slot.ItemId!);
                break;

            case ShopItemKind.RUN_BUFF:
                run.AddRunBuff(slot.ItemId!);
                break;

            case ShopItemKind.HEAL:
                Heal(input);
                break;

            default:
                throw new InvalidOperationException(
                    "03 §7 authors exactly four shop slot kinds and this offer produced a '" +
                    slot.Kind + "'. RunShopOffer builds every slot, so a kind arriving here that no " +
                    "branch names is a fifth pool added there without a grant here — a defect, not a " +
                    "player request.");
        }
    }

    /// <summary>`03` §7.1's purchase column: two consumables are held, two convert at the till.</summary>
    private static void GrantConsumable(Model.Run run, string consumableId)
    {
        switch (consumableId)
        {
            case Consumables.RerollToken:
                run.GrantRerollCharges(1);
                break;

            case Consumables.DraftToken:
                run.GrantFreeDraftRerolls(1);
                break;

            default:
                run.AddConsumable(consumableId, 1);
                break;
        }
    }

    /// <summary>Slot 4's heal: restore <c>#/shopTile/healPctMaxHp</c> of Max HP.</summary>
    /// <remarks>
    /// Overheal is clamped by the rule that computes it, as everywhere else in this codebase:
    /// <c>Run.SetHitPoints</c> refuses a current above the maximum rather than trimming it silently,
    /// so a heal that over-delivered could not look correct.
    /// </remarks>
    private static void Heal(HandlerInput input)
    {
        var run = input.Run;
        var share = ShopTuning.Read(input.Context.Content).HealPctMaxHp;
        var healed = (int)Math.Round(run.MaxHp * share, MidpointRounding.ToEven);

        run.SetHitPoints(Math.Min(run.MaxHp, run.CurrentHp + healed), run.MaxHp);
    }
}
