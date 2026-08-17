using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Model.Gear;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Forge;
using SlayIdleRepeat.Core.Rules.Inventory;

namespace SlayIdleRepeat.Core.Handlers;

/// <summary>
/// <c>ENHANCE</c> — one attempt at the next level, paid for in Enhance Stones whether or not it lands.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>A locked item can be enhanced, and that is not an oversight.</b> The lock excludes an item
/// from auto-salvage and from merge selection — the two operations that consume it. Enhancement
/// consumes nothing but stones and can neither destroy nor downgrade the item, so refusing it here
/// would make the lock mean something the design set does not say it means: a player who has locked
/// their best weapon precisely because it is their best would be unable to improve it.
/// </para>
/// <para>
/// ⚠️ <b>No lucky bonus is applied, and the zero is deliberate rather than missing.</b> The rewarded
/// ad grants its bonus through the ad-reward claim path against a server-side callback record, and
/// that path is not built; the subscription's charges are defined as byte-for-byte what a full
/// ad-watcher gets, so granting them alone would make the subscription strictly better than the free
/// route — which is the one thing the equivalence rule exists to forbid. The rate rule takes the
/// bonus as an argument and is tested with one; the handler passes none until both halves can land
/// together.
/// </para>
/// </remarks>
internal static class Enhance
{
    /// <summary>The attribution token a <em>successful</em> attempt's stone cost is logged under.</summary>
    /// <remarks>
    /// 🔒 <b>Two tokens rather than one, and that is the whole reason the outcome is not discarded.</b>
    /// The stones are spent either way, so a single token would make an attempt that bought a level
    /// and an attempt that bought nothing the same row of `21` §8.3's attribution report — and the
    /// enhancement success rate, which `24` §4.6's mercy exists to protect, would be unanswerable
    /// from the event log. The item itself carries the level and the failure counter, but only as
    /// they stand <em>now</em>: a stock read after the fact cannot say which command moved them, and
    /// `30` §12.7 forbids rebuilding what was never written down.
    /// </remarks>
    private const string StoneReasonOnSuccess = "enhance_cost_success";

    /// <summary>The attribution token a <em>failed</em> attempt's stone cost is logged under.</summary>
    /// <remarks>See <see cref="StoneReasonOnSuccess"/> for why the outcome is spelled into the token.</remarks>
    private const string StoneReasonOnFailure = "enhance_cost_failure";

    /// <summary>Takes one enhancement attempt.</summary>
    /// <param name="command">The item to enhance.</param>
    /// <param name="input">The slice, the content and the command's draw seed.</param>
    /// <returns>The stone movement, or the reason the attempt was refused.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    internal static HandlerResult Handle(EnhanceCommand command, HandlerInput input)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(input);

        var player = input.Player;
        var stock = player.Inventory;

        // The lock arm of the availability vocabulary is passed over on purpose — see the type's
        // remarks. Every other arm is the container's answer, unchanged.
        var availability = stock.Availability(command.ItemId);

        if (availability != ItemAvailability.LOCKED &&
            InventoryAccess.RejectionFor(availability) is { } unavailable)
        {
            return HandlerResult.Reject(unavailable);
        }

        var item = stock.Find(command.ItemId)!;
        var forge = ForgeTuning.Read(input.Context.Content);

        if (GearEnhancement.IsAtCeiling(item.EnhanceLevel, forge))
        {
            return HandlerResult.Reject(RejectionReason.CAP_REACHED);
        }

        var stones = forge.EnhanceStoneCost(item.EnhanceLevel + 1);

        if (player.BalanceOf(CurrencyId.ENHANCE_STONES) < stones)
        {
            return HandlerResult.Reject(RejectionReason.INSUFFICIENT_FUNDS);
        }

        var mercy = LuckTuning.Read(input.Context.Content).Enhance;

        // ⚠️ The RATE is deliberately the one part of the answer that is not carried out of the
        // handler. No event in today's vocabulary can hold a double, and unlike the outcome it is
        // recomputable without one: GearEnhancement.EffectiveRate is a pure function of the level
        // and the failure counter the item carried going in, both of which the previous accepted
        // command wrote down. The outcome is not — see StoneReasonOnSuccess.
        var (enhanced, succeeded, _) = GearEnhancement.Attempt(
            item,
            GearEnhancement.NoLuckyBonus,
            forge,
            mercy,
            input.MetaDraws.Stream(RngStreams.Forge));

        stock.Replace(enhanced);

        return HandlerResult.Accept(
            player.MoveCurrency(
                CurrencyId.ENHANCE_STONES,
                -stones,
                succeeded ? StoneReasonOnSuccess : StoneReasonOnFailure));
    }
}
