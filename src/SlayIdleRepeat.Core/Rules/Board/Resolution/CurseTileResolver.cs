using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;

namespace SlayIdleRepeat.Core.Rules.Board.Resolution;

/// <summary>
/// 🔒 `03` §2 / `19` Part E — <c>TILE_CURSE</c>: draw one chapter-eligible curse and pay its
/// authored paired reward.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️⚠️ <b>THIS RESOLVER DOES NOT APPLY THE CURSE. It pays the reward and stops.</b> That is the
/// single most important thing to know about it, and it is a scope boundary rather than a bug. `19`
/// Part E's curses are a <em>rules engine</em> — no stacking, the paired-reward payout, the
/// <c>AD_SKIP_CURSE</c> hook and mount immunity — and that engine is M3-11's, tracked by
/// <c>GapRegister</c>'s <c>Curses</c> entry, keyed on a <c>CurseDefinition</c> type that must not yet
/// exist. <c>Run</c> therefore holds no curse list, and this task deliberately does not add one:
/// a held-curse list authored now would freeze the curse's shape under all four of those unwritten
/// rules (steering <b>S6</b>).
/// </para>
/// <para>
/// 🔒 <b>What that means concretely for a player today.</b> Landing on a curse tile is
/// <em>strictly good</em>: they take the Gold or the Enhance Stones and suffer none of the debuff.
/// That is a known, temporary imbalance owned by M3-11, not an economy this task is claiming is
/// correct. The alternative — paying nothing either — would be worse: it would make the tile
/// mechanically inert and hide the gap behind a tile that appears to work.
/// </para>
/// <para>
/// 🔒 <b>The draw happens on the <c>drops</c> stream, and that is a recorded assumption.</b>
/// `14` §8.1's registry is closed and authors no curse stream; <c>drops</c> is the closest existing
/// semantic fit, and adding a registry row would be a protocol change (`14` §2.3 puts stream names on
/// the wire) this task is not authorised to make.
/// </para>
/// </remarks>
internal static class CurseTileResolver
{
    /// <inheritdoc cref="TreasureResolver.Reason"/>
    internal const string Reason = "curse_tile_reward";

    /// <summary>Resolves a curse tile: draws an eligible curse and pays its `19` Part E reward.</summary>
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

        // 🔒 Only the rows this task can honestly resolve: eligible for the chapter AND carrying a
        // reward CurseRewards has a named row for. The second filter is not a convenience — a curse
        // whose reward is "+12% ATK" or "+1 gear drop per Elite" has no system to pay it, so drawing
        // one would mean applying neither the debuff nor the reward, i.e. a tile that did nothing at
        // all while looking like it resolved.
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
