using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;

namespace SlayIdleRepeat.Core.Rules.Economy;

/// <summary>
/// The four slots a pending shop is offering, projected read-only so the Shop screen can draw them
/// before <c>SHOP_BUY</c> charges for one.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>This must agree with the handler, and shares its derivation rather than restating it.</b>
/// The rows come off the run's own recorded offer position through the same
/// <see cref="RunShopOffer"/> the purchase reads — a screen showing one offer while the command
/// charged for another would be the shop lying about what it sells, and it is the exact failure a
/// second copy of derived data produces.
/// </para>
/// <para>
/// 🔒 Read-only: projecting mutates no <c>Run</c> and moves no stream position. The draws it walks
/// are ones the run has ALREADY spent — the visit spent them when it stocked.
/// </para>
/// <para>
/// <see cref="ShopSlotRow.Affordable"/> is answered here rather than left to the screen because the
/// price is this layer's and the run's Gold is beside it; a screen deciding affordability would be a
/// second reading of a comparison the handler also makes, and the two would disagree the day a
/// modifier moved a price.
/// </para>
/// </remarks>
public sealed class ShopView
{
    private ShopView(IReadOnlyList<ShopSlotRow> slots, long gold, int refreshesUsedThisVisit)
    {
        Slots = slots;
        Gold = gold;
        RefreshesUsedThisVisit = refreshesUsedThisVisit;
    }

    /// <summary>The offer, in slot order.</summary>
    public IReadOnlyList<ShopSlotRow> Slots { get; }

    /// <summary>The run-local Gold the prices are against.</summary>
    public long Gold { get; }

    /// <summary>How many refreshes this visit has already spent.</summary>
    public int RefreshesUsedThisVisit { get; }

    /// <summary>
    /// Projects the shop <paramref name="run"/> is standing at, or <c>null</c> when it is not
    /// standing at one with an offer stocked.
    /// </summary>
    /// <param name="run">The run whose shop is being drawn.</param>
    /// <param name="content">The loaded content set, read for the pools and the price tables.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    /// <exception cref="MissingContentException"><paramref name="content"/> authors no shop block.</exception>
    public static ShopView? Project(RunSnapshot run, ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(content);

        // Both, and neither implies the other: a run can stand on a shop tile it has not stocked yet
        // (RESOLVE_TILE has not landed), and a stocked offer belongs to no other tile.
        if (run.PendingTileKind != (int)TileKind.Shop || run.ShopOfferDraw is not { } drawPosition)
        {
            return null;
        }

        var drawn = RunShopOffer.DrawAt(RunShopContext.Of(run), content, drawPosition);
        var rows = new ShopSlotRow[drawn.Count];

        for (var slot = 0; slot < rows.Length; slot++)
        {
            var purchased = (run.ShopSlotsPurchased & (1 << slot)) != 0;

            rows[slot] = new ShopSlotRow(
                slot,
                drawn[slot].Kind.ToString(),
                drawn[slot].ItemId,
                drawn[slot].Rarity?.ToString(),
                drawn[slot].Price,
                Purchased: purchased,
                Affordable: run.Gold >= drawn[slot].Price);
        }

        return new ShopView(Array.AsReadOnly(rows), run.Gold, run.ShopRefreshesUsedThisVisit);
    }
}

/// <summary>One slot of a shop's offer, as a screen draws it.</summary>
/// <param name="SlotIndex">The index <c>SHOP_BUY</c> names to buy this slot.</param>
/// <param name="Kind">Which of `03` §7's four pools it drew from, as its own token.</param>
/// <param name="ItemId">
/// The id inside that pool — a perk id, a consumable id, a run buff id — or <c>null</c> for the
/// Heal, which sells a fixed effect rather than a catalogue row, and for a pool that had nothing
/// left to offer.
/// </param>
/// <param name="Rarity">The perk's rarity token, for slot 1. <c>null</c> for every other slot.</param>
/// <param name="Price">What it costs in run-local Gold, with the run's own price modifiers applied.</param>
/// <param name="Purchased">Whether this visit has already bought it.</param>
/// <param name="Affordable">Whether the run's Gold covers the price.</param>
/// <remarks>
/// The tokens are strings rather than the enums behind them because those enums are <c>internal</c>
/// to <c>Core</c> — the same reason the pending tile crosses to a screen as a number. A screen turns
/// them into a localisation key; nothing outside branches on them.
/// </remarks>
public readonly record struct ShopSlotRow(
    int SlotIndex,
    string Kind,
    string? ItemId,
    string? Rarity,
    long Price,
    bool Purchased,
    bool Affordable);
