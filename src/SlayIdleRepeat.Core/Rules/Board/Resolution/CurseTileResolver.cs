using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Economy;

namespace SlayIdleRepeat.Core.Rules.Board.Resolution;

/// <summary><c>TILE_CURSE</c>: draw one chapter-eligible curse and pay its authored paired reward.</summary>
/// <remarks>
/// <para>
/// 🔒 <b>The curse is applied AND the reward paid.</b> Landing here used to be strictly good — the
/// player took the Gold or the Enhance Stones and suffered no debuff at all, because the run had
/// nowhere to hold a curse. It does now, and six of `19` Part E's twelve carry a real stat penalty
/// through <c>Rules.Effects.CurseEffectSource</c> while a seventh (<c>CUR_SLIPPERY</c>) is honoured
/// by the board. <see cref="Content.CurseEffects.UnappliedReason"/> names, per curse, what the other
/// five still cannot do and why — those are still APPLIED to the run, so a Shrine can cleanse them
/// and a screen can name them; what does not happen is the debuff.
/// </para>
/// <para>
/// 🔒 <b>No stacking, and a re-draw of a curse already carried still pays.</b> `19` Part E gives
/// curses no stacking, so <c>Run.ApplyCurse</c> refuses the duplicate — and the reward is paid
/// anyway. The alternative would be a tile that sometimes does nothing at all, decided by a draw the
/// player cannot see or influence; paying is the reading that keeps the tile's bargain honest even
/// when the debuff half is a no-op.
/// </para>
/// <para>
/// The draw happens on the <c>drops</c> stream — the closest existing semantic fit; there is no
/// dedicated curse stream.
/// </para>
/// </remarks>
internal static class CurseTileResolver
{
    /// <inheritdoc cref="TreasureResolver.Reason"/>
    internal const string Reason = "curse_tile_reward";

    /// <summary>Resolves a curse tile: draws an eligible curse and pays its authored reward.</summary>
    /// <param name="input">The cloned, already-caught-up, in-run slice.</param>
    /// <returns>
    /// The single <see cref="CurrencyChanged"/> the drawn curse's reward pays — on the <c>Run</c> for
    /// <c>GOLD</c>, on the <c>Player</c> for a wallet currency.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// The run's chapter has no eligible curse, or has one whose reward <see cref="CurseRewards"/>
    /// cannot pay. Both are content defects rather than player inputs — see the messages.
    /// </exception>
    internal static IReadOnlyList<DomainEvent> Resolve(HandlerInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var chapterId = input.Run.ChapterId;
        var catalogue = CurseTuning.Read(input.Context.Content);

        // Only the rows this task can honestly resolve: eligible for the chapter AND carrying a
        // reward CurseRewards has a named row for — a curse with no payable reward would mean
        // applying neither the debuff nor the reward, i.e. a tile that did nothing while looking
        // like it resolved.
        var eligible = new List<CurseTileRow>(CurseRewards.PayableIds.Count);

        foreach (var row in catalogue.AvailableFrom(chapterId))
        {
            if (CurseRewards.IsPayable(row.Id))
            {
                eligible.Add(row);
            }
        }

        if (eligible.Count == 0)
        {
            throw new InvalidOperationException(
                "No curse in content/curses/curses.json is both available from chapter " +
                chapterId.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                " and carries a reward CurseRewards can pay. 19 Part E opens four curses at chapter " +
                "1 (" + string.Join(", ", CurseRewards.PayableIds) + ") and every later chapter " +
                "includes them, so an empty set here means the catalogue lost those rows.");
        }

        var drawn = eligible[input.Rng.Stream(RngStreams.Drops).Range(0, eligible.Count)];
        var (currency, amount) = CurseRewards.For(drawn.Id);

        // The debuff first, then the payment — so a Gold reward is scaled by every Gold modifier the
        // run holds INCLUDING one this very curse just applied. CUR_TITHE ("20% of all Gold gained is
        // lost") pays in Gold, and a tithe that spared its own reward would be the one gain in the
        // run it did not touch.
        input.Run.ApplyCurse(drawn.Id);

        var paid = currency == CurrencyId.GOLD
            ? RunModifierTotals.ScaleGoldIncome(input.Run, input.Context.Content, amount)
            : amount;

        return paid == 0
            ? Array.Empty<DomainEvent>()
            : new DomainEvent[]
            {
                currency == CurrencyId.GOLD
                    ? input.Run.MoveCurrency(currency, paid, Reason)
                    : input.Player.MoveCurrency(currency, paid, Reason),
            };
    }
}
