using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;

namespace SlayIdleRepeat.Core.Rules.Board.Resolution;

/// <summary><c>TILE_CURSE</c>: draw one chapter-eligible curse and pay its authored paired reward.</summary>
/// <remarks>
/// <para>
/// This resolver does not apply the curse — it pays the reward and stops. Curses are a full rules
/// engine (stacking, the paired-reward payout, an ad-skip hook, mount immunity) that does not exist
/// in this codebase yet, and <c>Run</c> holds no curse list. Authoring a held-curse list now would
/// freeze the curse's shape under all of those unwritten rules.
/// </para>
/// <para>
/// What that means for a player today: landing on a curse tile is strictly good — they take the
/// Gold or Enhance Stones and suffer no debuff. That is a known, temporary imbalance rather than
/// this tile's real economy; paying nothing instead would be worse, since it would hide the gap
/// behind a tile that looks like it works.
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

        return new DomainEvent[]
        {
            currency == CurrencyId.GOLD
                ? input.Run.MoveCurrency(currency, amount, Reason)
                : input.Player.MoveCurrency(currency, amount, Reason),
        };
    }
}
